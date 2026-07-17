using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Sim
{
    public enum CarState { ParkedHome, Outbound, ParkedWork, Inbound }

    public sealed class CommuteCar
    {
        public Vector2Int Home, Work;
        public int RouteIndex, WorkSlot, HomeSlot;
        public float DepartHomeHour, DepartWorkHour;
        public CarState State;
        public float Distance;
    }

    // 하루 주기 통근 안무. 세이브 불필요 — 로드/큰 시각 점프 시 SnapToHour로 주차 상태에
    // 조대 수렴한다(이동 중 연출은 복원하지 않음 — 스펙 정정 2026-07-16 외부 리뷰 #4).
    // 수요 순서 = route order(DemandMap 순서, 결정적). 같은 집의 후순위 수요는 homeSlots 초과 시 탈락.
    // 이동 거리 적분·도착 판정은 뷰 소유(NotifyArrived로 통지). 판단 없음(안무만).
    public sealed class CommuteScheduler
    {
        readonly List<CommuteCar> _cars = new(96);
        readonly List<CommuteCar> _newCars = new(32);   // 직전 Rebuild에서 신규 생성된 차 — SnapNewToHour 대상
        float _morningEnd, _eveningStart, _eveningEnd;

        public IReadOnlyList<CommuteCar> Cars => _cars;

        // sticky 리빌드(라이브 QA A — Sim PR#92 sticky 배정과 같은 철학): (Home, Work) 짝이
        // 새 목록에도 있으면 차 객체를 보존한다(State·Distance·DepartHour 유지, RouteIndex만 갱신).
        // 무관한 건물 건설/해체가 이동·주차 중인 차를 리셋하지 않게 하는 핵심.
        // 슬롯 유일성 불변식: (Work, WorkSlot)/(Home, HomeSlot)은 점유 셋이 절대 보장.
        public void Rebuild(IReadOnlyList<Vector2Int> sources, IReadOnlyList<Vector2Int> sinks,
            int officeSlots, int homeSlots, int maxCars,
            float morningStart, float morningEnd, float eveningStart, float eveningEnd)
        {
            _morningEnd = morningEnd; _eveningStart = eveningStart; _eveningEnd = eveningEnd;

            var survivors = new Dictionary<(Vector2Int, Vector2Int), Queue<CommuteCar>>(_cars.Count);
            for (int i = 0; i < _cars.Count; i++)
            {
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
                if (car.WorkSlot < officeSlots) Occupy(workUsed, car.Work, car.WorkSlot);
                if (car.HomeSlot < homeSlots) Occupy(homeUsed, car.Home, car.HomeSlot);
            }

            // 2차: route 순서로 확정. 생존 차는 슬롯 유지(정원 축소로 밀려나면 재배정, 빈 칸 없으면
            // 그날 통근 제외), 신규 짝은 빈 칸 배정(ParkedHome — 스냅은 SnapNewToHour가 신규만).
            for (int i = 0; i < count && _cars.Count < maxCars; i++)
            {
                CommuteCar car = matched[i];
                if (car != null)
                {
                    if (car.WorkSlot >= officeSlots)
                    {
                        if (!TryTakeSlot(workUsed, car.Work, officeSlots, out int ws)) continue;
                        car.WorkSlot = ws;
                    }
                    if (car.HomeSlot >= homeSlots)
                    {
                        if (!TryTakeSlot(homeUsed, car.Home, homeSlots, out int hs)) continue;
                        car.HomeSlot = hs;
                    }
                    car.RouteIndex = i;
                    _cars.Add(car);
                    continue;
                }

                if (!TryTakeSlot(workUsed, sinks[i], officeSlots, out int workSlot)) continue;   // 정원 초과 = 그날 통근 안 함
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
                    State = CarState.ParkedHome,
                };
                _cars.Add(fresh);
                _newCars.Add(fresh);
            }
        }

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
                if (car.State == CarState.ParkedHome
                    && hour >= car.DepartHomeHour && hour < _eveningEnd)
                { car.State = CarState.Outbound; car.Distance = 0f; }
                else if (car.State == CarState.ParkedWork && hour >= car.DepartWorkHour)
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
                SnapCar(_cars[i], hour);
            }
        }

        // sticky 리빌드 직후: 신규 차만 현재 시각으로 수렴(생존 차 상태 보존 — 전체 스냅 금지, QA A).
        public void SnapNewToHour(float hour)
        {
            for (int i = 0; i < _newCars.Count; i++)
            {
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
            bool inEveningWindow = hour >= _eveningStart && hour < _eveningEnd;
            car.State = inEveningWindow ? CarState.ParkedWork : CarState.ParkedHome;
            car.Distance = 0f;
        }

        // 전체 폐기(세이브 복원 등) — 다음 Rebuild는 sticky 없이 전원 신규(=SnapNewToHour 전체 수렴).
        public void Clear()
        {
            _cars.Clear();
            _newCars.Clear();
        }

        public static float StaggerHour(Vector2Int home, float windowStart, float windowEnd)
        {
            uint h = (uint)(home.x * 73856093) ^ (uint)(home.y * 19349663);
            float t = (h % 10000u) / 10000f;
            return Mathf.Lerp(windowStart, windowEnd - 0.01f, t);
        }
    }
}
