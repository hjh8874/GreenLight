using System;
using System.Collections.Generic;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Sim
{
    public enum CarState
    {
        ParkedHome,
        Outbound,
        ParkedWork,
        Inbound,
        Inactive
    }
    public enum RetireReason { None, HomeLost, WorkLost }

    public sealed class CommuteCar
    {
        public Vector2Int Home, Work;
        public int RouteIndex, WorkSlot, HomeSlot;
        public float DepartHomeHour, DepartWorkHour;
        // 이 차의 퇴근창 [EveningStartHour, EveningEndHour). Start > End 면 자정을 넘는다.
        // 판정 기준을 전역 창이 아니라 차 개별 값으로 두는 이유 = 유형별 근무시간(야간조) 대비.
        public float EveningStartHour, EveningEndHour;
        public CarState State;
        public float Distance;
        // 리빌드(건설) 생존 차의 현재 월드 타일. 인덱스는 재배열되지만 이 인스턴스는
        // sticky 매칭으로 유지되므로, 진행도를 차에 실어 리빌드를 건너보낸다.
        public Vector2Int ResumeTile;
        public bool HasResume;
        // 건물 소멸은 주행 중인 차를 즉시 삭제하지 않는다. CarSim이 구 짝/경로를
        // carry-over하는 동안 이 사유를 보존하고, 안전한 주차 경계에서만 제거한다.
        public RetireReason RetireReason;
        // 리빌드로 새로 생긴 배정은 생성 시각이 이미 지난 출근 창을 소급하지 않는다.
        // 다음 날 출발 시각 이전 구간을 관측한 뒤에만 정상 출발 전이를 허용한다.
        public bool AwaitingNextWave;
        public VehicleTripPurpose RoutinePurpose { get; private set; } =
            VehicleTripPurpose.Commute;
        public bool IsTransient { get; private set; }
        public bool SpecialTripReserved { get; private set; }
        public bool IsVisible { get; private set; } = true;

        internal void ConfigureTransient(Vector2Int origin)
        {
            Home = origin;
            Work = origin;
            RouteIndex = -1;
            WorkSlot = 0;
            HomeSlot = 0;
            State = CarState.Outbound;
            Distance = 0f;
            ResumeTile = default;
            HasResume = false;
            RetireReason = RetireReason.None;
            AwaitingNextWave = false;
            RoutinePurpose = VehicleTripPurpose.SpecialBuildingVisit;
            IsTransient = true;
            SpecialTripReserved = false;
            IsVisible = true;
        }

        internal void ReleaseTransient()
        {
            State = CarState.Inactive;
            Distance = 0f;
            RouteIndex = -1;
            ResumeTile = default;
            HasResume = false;
            SpecialTripReserved = false;
            IsVisible = false;
        }

        public void ApplyViewVisibility(bool isVisible)
        {
            IsVisible = isVisible;
        }

        internal void SetRoutinePurpose(VehicleTripPurpose purpose)
        {
            RoutinePurpose = purpose;
        }

        internal void SetSpecialTripReservation(bool isReserved)
        {
            SpecialTripReserved = isReserved;
            IsVisible = !isReserved;
        }
    }

    // 하루 주기 통근 안무. 세이브 불필요 — 로드/큰 시각 점프 시 SnapToHour로 주차 상태에
    // 조대 수렴한다(이동 중 연출은 복원하지 않음 — 스펙 정정 2026-07-16 외부 리뷰 #4).
    // 수요 순서 = route order(DemandMap 순서, 결정적). 같은 집의 후순위 수요는 homeSlots 초과 시 탈락.
    // 이동 거리 적분·도착 판정은 뷰 소유(NotifyArrived로 통지). 판단 없음(안무만).
    public sealed class CommuteScheduler
    {
        readonly List<CommuteCar> _cars = new(96);
        readonly List<CommuteCar> _newCars = new(32);   // 직전 Rebuild에서 신규 생성된 차 — SnapNewToHour 대상

        public IReadOnlyList<CommuteCar> Cars => _cars;
        public int ActiveCount
        {
            get
            {
                int count = 0;
                for (int index = 0; index < _cars.Count; index++)
                {
                    CommuteCar car = _cars[index];
                    if (car.State != CarState.Inactive &&
                        !car.SpecialTripReserved)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        // sticky 리빌드(라이브 QA A — Sim PR#92 sticky 배정과 같은 철학): (Home, Work) 짝이
        // 새 목록에도 있으면 차 객체를 보존한다(State·Distance·DepartHour 유지, RouteIndex만 갱신).
        // 무관한 건물 건설/해체가 이동·주차 중인 차를 리셋하지 않게 하는 핵심.
        // 슬롯 유일성 불변식: (Work, WorkSlot)/(Home, HomeSlot)은 점유 셋이 절대 보장.
        public void Rebuild(IReadOnlyList<Vector2Int> sources, IReadOnlyList<Vector2Int> sinks,
            Func<Vector2Int, int> workCapacityFor, int homeSlots, int maxCars,
            float morningStart, float morningEnd, float eveningStart, float eveningEnd,
            bool deferNewAssignments = false,
            IReadOnlyList<VehicleTripPurpose> purposes = null,
            int transientStorageCapacity = 0)
        {
            if (workCapacityFor == null)
                throw new ArgumentNullException(nameof(workCapacityFor));

            var activeTransients = new List<CommuteCar>();
            int reservedRoutineCount = 0;
            var survivors = new Dictionary<(Vector2Int, Vector2Int), Queue<CommuteCar>>(_cars.Count);
            for (int i = 0; i < _cars.Count; i++)
            {
                if (_cars[i].IsTransient)
                {
                    if (_cars[i].State != CarState.Inactive)
                    {
                        activeTransients.Add(_cars[i]);
                    }

                    continue;
                }

                if (_cars[i].SpecialTripReserved)
                {
                    reservedRoutineCount++;
                }

                var key = (_cars[i].Home, _cars[i].Work);
                if (!survivors.TryGetValue(key, out Queue<CommuteCar> q)) survivors[key] = q = new Queue<CommuteCar>();
                q.Enqueue(_cars[i]);
            }

            _cars.Clear();
            _newCars.Clear();

            int count = Mathf.Min(sources.Count, sinks.Count);
            var matched = new CommuteCar[count];
            var workUsed = new Dictionary<Vector2Int, HashSet<int>>(32);
            var homeUsed = new Dictionary<Vector2Int, HashSet<int>>(64);

            // 1차: 생존 짝 매칭 + 기존 슬롯 선점(신규 차가 생존 차의 칸을 뺏지 못하게).
            // 생존 차끼리의 슬롯 충돌은 이전 유일성 불변식상 불가능.
            for (int i = 0; i < count; i++)
            {
                if (!survivors.TryGetValue((sources[i], sinks[i]), out Queue<CommuteCar> q) || q.Count == 0) continue;
                CommuteCar car = q.Dequeue();
                matched[i] = car;
                int workCapacity = SafeCapacity(
                    workCapacityFor,
                    car.Work
                );
                if (car.WorkSlot < workCapacity) Occupy(workUsed, car.Work, car.WorkSlot);
                if (car.HomeSlot < homeSlots) Occupy(homeUsed, car.Home, car.HomeSlot);
            }

            // 2차: route 순서로 확정. 생존 차는 슬롯 유지(정원 축소로 밀려나면 재배정, 빈 칸 없으면
            // 그날 통근 제외), 신규 짝은 빈 칸 배정(ParkedHome — 스냅은 SnapNewToHour가 신규만).
            // A transient backed by a hidden routine owner does not consume
            // an additional active slot. Only ownerless transients reduce the
            // routine limit; transient objects use the separate storage budget.
            int unbackedTransientCount = Math.Max(
                0,
                activeTransients.Count - reservedRoutineCount);
            int routineLimit = Math.Max(
                0,
                maxCars - unbackedTransientCount);
            for (int i = 0; i < count && _cars.Count < routineLimit; i++)
            {
                CommuteCar car = matched[i];
                int workCapacity = SafeCapacity(
                    workCapacityFor,
                    sinks[i]
                );
                if (car != null)
                {
                    if (car.WorkSlot >= workCapacity)
                    {
                        if (!TryTakeSlot(workUsed, car.Work, workCapacity, out int ws)) continue;
                        car.WorkSlot = ws;
                    }
                    if (car.HomeSlot >= homeSlots)
                    {
                        if (!TryTakeSlot(homeUsed, car.Home, homeSlots, out int hs)) continue;
                        car.HomeSlot = hs;
                    }
                    car.RouteIndex = i;
                    car.SetRoutinePurpose(PurposeAt(purposes, i));
                    car.ApplyViewVisibility(!car.SpecialTripReserved);
                    _cars.Add(car);
                    continue;
                }

                if (!TryTakeSlot(workUsed, sinks[i], workCapacity, out int workSlot)) continue;   // 정원 초과 = 그날 통근 안 함
                if (!TryTakeSlot(homeUsed, sources[i], homeSlots, out int homeSlot))
                {
                    workUsed[sinks[i]].Remove(workSlot);   // 짝 성립 실패 — 선점한 회사 칸 반납
                    continue;
                }

                var fresh = new CommuteCar
                {
                    Home = sources[i], Work = sinks[i], RouteIndex = i,
                    WorkSlot = workSlot, HomeSlot = homeSlot,
                    DepartHomeHour = StaggerHour(sources[i], morningStart, morningEnd),
                    DepartWorkHour = StaggerHour(sources[i], eveningStart, eveningEnd),
                    EveningStartHour = eveningStart,
                    EveningEndHour = eveningEnd,
                    State = CarState.ParkedHome,
                    AwaitingNextWave = deferNewAssignments,
                };
                fresh.SetRoutinePurpose(PurposeAt(purposes, i));
                _cars.Add(fresh);
                _newCars.Add(fresh);
            }

            int safeTransientCapacity = Math.Max(
                0,
                transientStorageCapacity);
            int storageLimit = maxCars > int.MaxValue - safeTransientCapacity
                ? int.MaxValue
                : maxCars + safeTransientCapacity;
            for (int index = 0;
                 index < activeTransients.Count &&
                 _cars.Count < storageLimit;
                 index++)
            {
                _cars.Add(activeTransients[index]);
            }
        }

        private static VehicleTripPurpose PurposeAt(
            IReadOnlyList<VehicleTripPurpose> purposes,
            int index)
        {
            return purposes != null && index >= 0 && index < purposes.Count
                ? purposes[index]
                : VehicleTripPurpose.Commute;
        }

        static int SafeCapacity(
            Func<Vector2Int, int> capacityFor,
            Vector2Int tile
        ) => Mathf.Max(0, capacityFor(tile));

        static void Occupy(Dictionary<Vector2Int, HashSet<int>> used, Vector2Int key, int slot)
        {
            if (!used.TryGetValue(key, out HashSet<int> set)) used[key] = set = new HashSet<int>();
            set.Add(slot);
        }

        static bool TryTakeSlot(Dictionary<Vector2Int, HashSet<int>> used, Vector2Int key, int capacity, out int slot)
        {
            used.TryGetValue(key, out HashSet<int> set);
            for (int s = 0; s < capacity; s++)
            {
                if (set != null && set.Contains(s)) continue;
                if (set == null) { set = new HashSet<int>(); used[key] = set; }
                set.Add(s);
                slot = s;
                return true;
            }
            slot = -1;
            return false;
        }

        public void UpdateDepartures(float hour)
        {
            for (int i = 0; i < _cars.Count; i++)
            {
                CommuteCar car = _cars[i];
                if (car.IsTransient || car.SpecialTripReserved)
                {
                    continue;
                }
                if (car.State == CarState.ParkedHome && car.AwaitingNextWave)
                {
                    // 창 밖(=다음 파도 이전)을 한 번 관측하면 해제. 자정을 넘는 창도 성립한다.
                    if (!CommuteWindow.InWindow(hour, car.DepartHomeHour, car.EveningEndHour))
                        car.AwaitingNextWave = false;
                    else
                        continue;
                }
                // 출근 = [개인 출근 시각, 퇴근창 끝) · 퇴근 = [개인 퇴근 시각, 다음 개인 출근 시각).
                // 트리거가 개인 시각이라 스태거가 유지되고, 자정을 넘는 근무도 단순 비교 없이 성립한다.
                if (car.State == CarState.ParkedHome
                    && CommuteWindow.InWindow(hour, car.DepartHomeHour, car.EveningEndHour))
                { car.State = CarState.Outbound; car.Distance = 0f; }
                else if (car.State == CarState.ParkedWork
                    && (CommuteWindow.InWindow(hour, car.DepartWorkHour, car.DepartHomeHour)
                        || car.RetireReason == RetireReason.WorkLost))
                { car.State = CarState.Inbound; car.Distance = 0f; }
            }
        }

        public void NotifyArrived(CommuteCar car)
        {
            if (car.State == CarState.Outbound) car.State = CarState.ParkedWork;
            else if (car.State == CarState.Inbound) car.State = CarState.ParkedHome;
            car.Distance = 0f;
        }

        // 로드/배속 점프 후 조대 수렴: 이동 연출은 버리고 주차 상태로 스냅(전 차).
        public void SnapToHour(float hour)
        {
            for (int i = 0; i < _cars.Count; i++)
            {
                if (_cars[i].IsTransient || _cars[i].SpecialTripReserved)
                {
                    continue;
                }

                SnapCar(_cars[i], hour);
            }
        }

        // sticky 리빌드 직후: 신규 차만 현재 시각으로 수렴(생존 차 상태 보존 — 전체 스냅 금지, QA A).
        public void SnapNewToHour(float hour)
        {
            for (int i = 0; i < _newCars.Count; i++)
            {
                if (_newCars[i].IsTransient || _newCars[i].SpecialTripReserved)
                {
                    continue;
                }

                SnapCar(_newCars[i], hour);
            }
            _newCars.Clear();
        }

        // 단일 차 스냅 — 뷰가 경로 소실 차를 개별 수렴시킬 때도 사용(순간이동 대신 주차 재배치).
        // 정책(기획 결정 2026-07-17 환): 저녁 창[eveningStart, eveningEnd) 안만 ParkedWork(퇴근이 자연),
        // 그 외(새벽·아침·낮·밤 전부) = ParkedHome. 낮 로드 = 전원 지각 출근(즉시 파도) 의도 —
        // UpdateDepartures가 다음 틱에 곧바로 Outbound로 전이시킨다. 첫 움직임은 항상 출근이어야 한다.
        public void SnapCar(CommuteCar car, float hour)
        {
            if (car.AwaitingNextWave)
            {
                car.State = CarState.ParkedHome;
            }
            else
            {
                bool inEveningWindow = CommuteWindow.InWindow(
                    hour, car.EveningStartHour, car.EveningEndHour);
                car.State = inEveningWindow ? CarState.ParkedWork : CarState.ParkedHome;
            }
            car.Distance = 0f;
        }

        // 전체 폐기(세이브 복원 등) — 다음 Rebuild는 sticky 없이 전원 신규(=SnapNewToHour 전체 수렴).
        public void Clear()
        {
            _cars.Clear();
            _newCars.Clear();
        }

        internal CommuteCar AcquireTransient(
            Vector2Int origin,
            int maxCars)
        {
            for (int index = 0; index < _cars.Count; index++)
            {
                CommuteCar candidate = _cars[index];
                if (!candidate.IsTransient ||
                    candidate.State != CarState.Inactive)
                {
                    continue;
                }

                candidate.ConfigureTransient(origin);
                return candidate;
            }

            if (_cars.Count >= Math.Max(1, maxCars))
            {
                return null;
            }

            var created = new CommuteCar();
            created.ConfigureTransient(origin);
            _cars.Add(created);
            return created;
        }

        internal void ReleaseTransient(CommuteCar car)
        {
            if (car == null || !car.IsTransient)
            {
                return;
            }

            car.ReleaseTransient();
        }

        public static float StaggerHour(Vector2Int home, float windowStart, float windowEnd)
        {
            uint h = (uint)(home.x * 73856093) ^ (uint)(home.y * 19349663);
            float t = (h % 10000u) / 10000f;
            return Mathf.Lerp(windowStart, windowEnd - 0.01f, t);
        }
    }
}
