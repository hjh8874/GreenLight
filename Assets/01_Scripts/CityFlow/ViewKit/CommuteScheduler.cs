using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.ViewKit
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
        float _morningEnd, _eveningEnd;

        public IReadOnlyList<CommuteCar> Cars => _cars;

        public void Rebuild(IReadOnlyList<Vector2Int> sources, IReadOnlyList<Vector2Int> sinks,
            int officeSlots, int homeSlots, int maxCars,
            float morningStart, float morningEnd, float eveningStart, float eveningEnd)
        {
            _cars.Clear();
            _morningEnd = morningEnd; _eveningEnd = eveningEnd;
            var slotUsed = new Dictionary<Vector2Int, int>(32);   // work → 점유 칸 수
            var homeUsed = new Dictionary<Vector2Int, int>(64);
            for (int i = 0; i < sources.Count && _cars.Count < maxCars; i++)
            {
                slotUsed.TryGetValue(sinks[i], out int used);
                if (used >= officeSlots) continue;               // 정원 초과 = 그날 통근 안 함
                homeUsed.TryGetValue(sources[i], out int hUsed);
                if (hUsed >= homeSlots) continue;
                slotUsed[sinks[i]] = used + 1;
                homeUsed[sources[i]] = hUsed + 1;
                _cars.Add(new CommuteCar
                {
                    Home = sources[i], Work = sinks[i], RouteIndex = i,
                    WorkSlot = used, HomeSlot = hUsed,
                    DepartHomeHour = StaggerHour(sources[i], morningStart, morningEnd),
                    DepartWorkHour = StaggerHour(sources[i], eveningStart, eveningEnd),
                    State = CarState.ParkedHome,
                });
            }
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

        // 로드/배속 점프 후 조대 수렴: 이동 연출은 버리고 주차 상태로 스냅.
        public void SnapToHour(float hour)
        {
            for (int i = 0; i < _cars.Count; i++)
            {
                CommuteCar car = _cars[i];
                bool atWork = hour >= car.DepartHomeHour && hour < car.DepartWorkHour;
                car.State = atWork ? CarState.ParkedWork : CarState.ParkedHome;
                car.Distance = 0f;
            }
        }

        public static float StaggerHour(Vector2Int home, float windowStart, float windowEnd)
        {
            uint h = (uint)(home.x * 73856093) ^ (uint)(home.y * 19349663);
            float t = (h % 10000u) / 10000f;
            return Mathf.Lerp(windowStart, windowEnd - 0.01f, t);
        }
    }
}
