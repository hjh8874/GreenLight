using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Sim;

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
            s.Rebuild(sources, sinks, _ => officeSlots, homeSlots: 1, maxCars,
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

        // 시각 점프 수렴(기획 결정 2026-07-17 환): 첫 움직임은 항상 출근이어야 하므로,
        // 저녁 창[eveningStart, eveningEnd) 외 전부(새벽·아침·낮·밤)는 ParkedHome으로 수렴한다.
        // 저녁 창 안(퇴근이 자연스러운 시간대)만 ParkedWork. 낮 로드(13시)는 UpdateDepartures가
        // 즉시 지각 출근시키는 게 의도(전원 첫 파도 = 출근).
        [Test]
        public void SnapToHour_Converges()
        {
            var s = Build(homes: 5, officeSlots: 8);
            s.SnapToHour(13f);   // 낮 — 변경: 더 이상 ParkedWork가 아니다
            foreach (var car in s.Cars) Assert.AreEqual(CarState.ParkedHome, car.State);
            s.SnapToHour(18f);   // 저녁 창 안 — 퇴근이 자연스러움
            foreach (var car in s.Cars) Assert.AreEqual(CarState.ParkedWork, car.State);
            s.SnapToHour(2f);    // 새벽
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
            one.Rebuild(sources, sinks, workCapacityFor: _ => 4, homeSlots: 1, maxCars: 96,
                morningStart: 6f, morningEnd: 10f, eveningStart: 17f, eveningEnd: 21f);
            Assert.AreEqual(1, one.Cars.Count, "homeSlots=1 → 선순위(route order) 수요만");
            Assert.AreEqual(V(50, 50), one.Cars[0].Work);

            var two = new CommuteScheduler();
            two.Rebuild(sources, sinks, workCapacityFor: _ => 4, homeSlots: 2, maxCars: 96,
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
            s.Rebuild(sources, sinks, _ => 4, 1, 96, 6f, 10f, 17f, 21f);
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
            s.Rebuild(sources, sinks, _ => 8, 1, 96, 6f, 10f, 17f, 21f);
            Assert.AreEqual(4, s.Cars.Count);
            Assert.AreEqual(CarState.ParkedWork, s.Cars.First(c => c.Home == V(0, 0)).State);
            var seen = new HashSet<(Vector2Int, int)>();
            foreach (var c in s.Cars) Assert.IsTrue(seen.Add((c.Work, c.WorkSlot)), "슬롯 유일성");
        }

        [Test]
        public void PerCompanyCapacity_LimitsWorkSlotsIndependently()
        {
            Vector2Int smallOffice = V(50, 50);
            Vector2Int largeOffice = V(60, 60);
            var sources = new List<Vector2Int>();
            var sinks = new List<Vector2Int>();

            for (int i = 0; i < 3; i++)
            {
                sources.Add(V(i, 0));
                sinks.Add(smallOffice);
            }

            for (int i = 0; i < 4; i++)
            {
                sources.Add(V(i + 10, 0));
                sinks.Add(largeOffice);
            }

            var scheduler = new CommuteScheduler();
            scheduler.Rebuild(
                sources,
                sinks,
                tile => tile == smallOffice ? 1 : 3,
                homeSlots: 1,
                maxCars: 96,
                morningStart: 6f,
                morningEnd: 10f,
                eveningStart: 17f,
                eveningEnd: 21f
            );

            Assert.AreEqual(
                1,
                scheduler.Cars.Count(c =>
                    c.Work == smallOffice)
            );
            Assert.AreEqual(
                3,
                scheduler.Cars.Count(c =>
                    c.Work == largeOffice)
            );
        }

        // 지각 출근(2026-07-30 환 결정): 출근 시각이 지난 낮 시간대에 채용된 신규 차는
        // 다음 날을 기다리지 않고 그날 즉시 출근한다. 퇴근창 이후 채용은 현행대로 다음 날.
        [Test]
        public void NewHire_DuringDay_DepartsSameDay()
        {
            var s = BuildDeferred(hour: 12f);   // 출근창(6~10)은 지났고 퇴근창(17~)은 전
            var car = s.Cars[0];
            Assert.IsFalse(car.AwaitingNextWave, "낮 채용은 대기 해제");
            s.UpdateDepartures(12f);
            Assert.AreEqual(CarState.Outbound, car.State, "그날 즉시 지각 출근");
        }

        [Test]
        public void NewHire_DuringEvening_WaitsForNextDay()
        {
            var s = BuildDeferred(hour: 18f);   // 퇴근창(17~21) 안
            var car = s.Cars[0];
            Assert.IsTrue(car.AwaitingNextWave, "퇴근창 채용은 다음 날");
            s.UpdateDepartures(18f);
            Assert.AreEqual(CarState.ParkedHome, car.State);
        }

        [Test]
        public void NewHire_BeforeMorning_KeepsNormalSameDayFlow()
        {
            var s = BuildDeferred(hour: 4f);    // 출근 시각 전 — 종전에도 그날 출근했다
            var car = s.Cars[0];
            s.UpdateDepartures(4f);
            Assert.AreEqual(CarState.ParkedHome, car.State, "아직 출근 시각 전");
            s.UpdateDepartures(car.DepartHomeHour + 0.05f);
            Assert.AreEqual(CarState.Outbound, car.State, "정상 출근 유지");
        }

        static CommuteScheduler BuildDeferred(float hour)
        {
            var sources = new List<Vector2Int> { V(0, 0) };
            var sinks = new List<Vector2Int> { V(50, 50) };
            var s = new CommuteScheduler();
            s.Rebuild(sources, sinks, _ => 4, homeSlots: 1, maxCars: 96,
                morningStart: 6f, morningEnd: 10f, eveningStart: 17f, eveningEnd: 21f,
                deferNewAssignments: true);
            s.SnapNewToHour(hour);
            return s;
        }
    }
}
