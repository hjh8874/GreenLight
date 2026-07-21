using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using CityFlow.ViewKit;

namespace CityFlow.Sim.Tests
{
    public class CommuteSchedulerTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static CommuteScheduler Build(int homes, int officeSlots, int maxCars = 96)
        {
            var sources = new List<Vector2Int>();
            var sinks = new List<Vector2Int>();
            for (int i = 0; i < homes; i++) { sources.Add(V(i, 0)); sinks.Add(V(50, 50)); }
            var s = new CommuteScheduler();
            s.Rebuild(sources, sinks, officeSlots, homeSlots: 1, maxCars,
                morningStart: 6f, morningEnd: 10f, eveningStart: 17f, eveningEnd: 21f);
            return s;
        }

        // ★겹침 금지 회귀: 어느 구성에서도 (Work, WorkSlot) 중복 불가 + 정원 = min(배정, 칸수).
        [Test]
        public void SlotUniqueness_NeverTwoCarsSameSlot()
        {
            var s = Build(homes: 10, officeSlots: 4);
            Assert.AreEqual(4, s.Cars.Count, "정원 = min(10, 4)");
            var seen = new HashSet<(Vector2Int, int)>();
            foreach (var car in s.Cars)
                Assert.IsTrue(seen.Add((car.Work, car.WorkSlot)), $"슬롯 중복: {car.Work}#{car.WorkSlot}");
        }

        // 스태거 결정론: 같은 집 = 항상 같은 출발 시각, 창 안.
        [Test]
        public void Stagger_DeterministicAndInWindow()
        {
            float a = CommuteScheduler.StaggerHour(V(3, 7), 6f, 10f);
            float b = CommuteScheduler.StaggerHour(V(3, 7), 6f, 10f);
            Assert.AreEqual(a, b);
            Assert.GreaterOrEqual(a, 6f);
            Assert.Less(a, 10f);
            Assert.AreNotEqual(a, CommuteScheduler.StaggerHour(V(4, 7), 6f, 10f), "집마다 분산");
        }

        // 상태 전이: 출근 시각 전 ParkedHome → 지나면 Outbound → 도착 통지로 ParkedWork.
        [Test]
        public void Departure_ThenArrival_Transitions()
        {
            var s = Build(homes: 1, officeSlots: 4);
            var car = s.Cars[0];
            s.UpdateDepartures(car.DepartHomeHour - 0.1f);
            Assert.AreEqual(CarState.ParkedHome, car.State);
            s.UpdateDepartures(car.DepartHomeHour + 0.1f);
            Assert.AreEqual(CarState.Outbound, car.State);
            s.NotifyArrived(car);
            Assert.AreEqual(CarState.ParkedWork, car.State);
            s.UpdateDepartures(car.DepartWorkHour + 0.1f);
            Assert.AreEqual(CarState.Inbound, car.State);
            s.NotifyArrived(car);
            Assert.AreEqual(CarState.ParkedHome, car.State);
        }

        // 시각 점프 수렴: 로드 직후 한낮이면 전원 ParkedWork, 자정이면 전원 ParkedHome.
        [Test]
        public void SnapToHour_Converges()
        {
            var s = Build(homes: 5, officeSlots: 8);
            s.SnapToHour(13f);
            foreach (var car in s.Cars) Assert.AreEqual(CarState.ParkedWork, car.State);
            s.SnapToHour(2f);
            foreach (var car in s.Cars) Assert.AreEqual(CarState.ParkedHome, car.State);
        }

        // maxCars 상한: 초과분은 생성 안 함(그날 통근 안 함 — 풀 상한 흡수).
        [Test]
        public void MaxCars_CapsPopulation()
        {
            var s = Build(homes: 20, officeSlots: 20, maxCars: 12);
            Assert.AreEqual(12, s.Cars.Count);
        }

        // 같은 집에 수요 2건(Office+School — DemandMap이 실제로 이렇게 생성):
        // homeSlots=1이면 선순위 수요만 차가 되고(결정적 탈락), homeSlots=2면 2대(HomeSlot 0/1).
        [Test]
        public void SameHomeTwoDemands_HomeSlotsGovernDeterministically()
        {
            var sources = new List<Vector2Int> { V(0, 0), V(0, 0) };      // 같은 집
            var sinks = new List<Vector2Int> { V(50, 50), V(60, 60) };    // Office, School
            var one = new CommuteScheduler();
            one.Rebuild(sources, sinks, officeSlots: 4, homeSlots: 1, maxCars: 96,
                morningStart: 6f, morningEnd: 10f, eveningStart: 17f, eveningEnd: 21f);
            Assert.AreEqual(1, one.Cars.Count, "homeSlots=1 → 선순위(route order) 수요만");
            Assert.AreEqual(V(50, 50), one.Cars[0].Work);

            var two = new CommuteScheduler();
            two.Rebuild(sources, sinks, officeSlots: 4, homeSlots: 2, maxCars: 96,
                morningStart: 6f, morningEnd: 10f, eveningStart: 17f, eveningEnd: 21f);
            Assert.AreEqual(2, two.Cars.Count, "homeSlots=2 → 집당 2대");
            Assert.AreEqual(0, two.Cars[0].HomeSlot);
            Assert.AreEqual(1, two.Cars[1].HomeSlot);
        }

        // sticky 리빌드: 동일 목록 재빌드는 이동 중 차의 상태·거리를 보존한다.
        [Test]
        public void Rebuild_SameLists_PreservesMovingCarState()
        {
            var s = Build(homes: 3, officeSlots: 4);
            var car = s.Cars[1];
            car.State = CarState.Outbound; car.Distance = 3.5f;
            var sources = new List<Vector2Int> { V(0, 0), V(1, 0), V(2, 0) };
            var sinks = new List<Vector2Int> { V(50, 50), V(50, 50), V(50, 50) };
            s.Rebuild(sources, sinks, 4, 1, 96, 6f, 10f, 17f, 21f);
            var same = s.Cars.First(c => c.Home == V(1, 0));
            Assert.AreEqual(CarState.Outbound, same.State, "생존 짝은 상태 보존");
            Assert.AreEqual(3.5f, same.Distance, 1e-4f);
        }

        // sticky 리빌드: 새 집 추가가 기존 차를 리셋하지 않고, 슬롯 유일성도 유지된다.
        [Test]
        public void Rebuild_AddedHome_DoesNotResetOthers_SlotsStayUnique()
        {
            var s = Build(homes: 3, officeSlots: 8);
            s.Cars[0].State = CarState.ParkedWork;
            var sources = new List<Vector2Int> { V(0, 0), V(1, 0), V(2, 0), V(3, 0) };
            var sinks = new List<Vector2Int> { V(50, 50), V(50, 50), V(50, 50), V(50, 50) };
            s.Rebuild(sources, sinks, 8, 1, 96, 6f, 10f, 17f, 21f);
            Assert.AreEqual(4, s.Cars.Count);
            Assert.AreEqual(CarState.ParkedWork, s.Cars.First(c => c.Home == V(0, 0)).State);
            var seen = new HashSet<(Vector2Int, int)>();
            foreach (var c in s.Cars) Assert.IsTrue(seen.Add((c.Work, c.WorkSlot)), "슬롯 유일성");
        }
    }
}
