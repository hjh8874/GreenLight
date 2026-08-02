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

        // 종전 전역 창을 콜백 하나로 재현한다(Task 5 — 시각 인자 4개 제거).
        static CommuteWindow Window(
            float morningStart, float morningEnd, float eveningStart, float eveningEnd) =>
            new CommuteWindow(
                string.Empty,
                morningStart, morningEnd - morningStart,
                eveningStart, eveningEnd - eveningStart);

        static CommuteScheduler Build(int homes, int officeSlots, int maxCars = 96)
        {
            var sources = new List<Vector2Int>();
            var sinks = new List<Vector2Int>();
            for (int i = 0; i < homes; i++) { sources.Add(V(i, 0)); sinks.Add(V(50, 50)); }
            var s = new CommuteScheduler();
            s.Rebuild(sources, sinks, _ => officeSlots, _ => Window(6f, 10f, 17f, 21f),
                homeSlots: 1, maxCars: maxCars);
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
            one.Rebuild(sources, sinks, workCapacityFor: _ => 4,
                windowFor: _ => Window(6f, 10f, 17f, 21f), homeSlots: 1, maxCars: 96);
            Assert.AreEqual(1, one.Cars.Count, "homeSlots=1 → 선순위(route order) 수요만");
            Assert.AreEqual(V(50, 50), one.Cars[0].Work);

            var two = new CommuteScheduler();
            two.Rebuild(sources, sinks, workCapacityFor: _ => 4,
                windowFor: _ => Window(6f, 10f, 17f, 21f), homeSlots: 2, maxCars: 96);
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
            s.Rebuild(sources, sinks, _ => 4, _ => Window(6f, 10f, 17f, 21f), 1, 96);
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
            s.Rebuild(sources, sinks, _ => 8, _ => Window(6f, 10f, 17f, 21f), 1, 96);
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
                _ => Window(6f, 10f, 17f, 21f),
                homeSlots: 1,
                maxCars: 96
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

        // 자정을 넘는 근무(20시 출근 / 5시 퇴근). 전역 창이 아니라 차 개별 값으로 판정하므로 성립한다.
        [Test]
        public void NightShift_StaysAtWorkPastMidnight_AndLeavesAtDawn()
        {
            var s = Build(homes: 1, officeSlots: 4);
            var car = s.Cars[0];
            car.DepartHomeHour = 20f;     // 출근창 [20, 24)
            car.DepartWorkHour = 5f;      // 퇴근창 [5, 9)
            car.EveningStartHour = 5f;
            car.EveningEndHour = 9f;
            car.State = CarState.ParkedHome;

            s.UpdateDepartures(19f);
            Assert.AreEqual(CarState.ParkedHome, car.State, "19시엔 아직 집");
            s.UpdateDepartures(20f);
            Assert.AreEqual(CarState.Outbound, car.State, "20시에 출근");
            s.NotifyArrived(car);
            Assert.AreEqual(CarState.ParkedWork, car.State);

            s.UpdateDepartures(23f);
            Assert.AreEqual(CarState.ParkedWork, car.State, "23시엔 근무 중");
            s.UpdateDepartures(2f);
            Assert.AreEqual(CarState.ParkedWork, car.State, "새벽 2시에도 근무 중 — 자정을 넘겼다");

            s.UpdateDepartures(5f);
            Assert.AreEqual(CarState.Inbound, car.State, "5시에 퇴근");
        }

        // [리뷰 P1 회귀] 야간조 귀가 시각(새벽)은 아직 자기 출근 구간 [20,9) 안이다 —
        // 귀가가 다음 파도 대기를 세우지 않으면 다음 틱에 즉시 재출근한다. 출근은 파도당 한 번이다.
        [Test]
        public void NightShift_ArrivedHomeAtDawn_DoesNotRedepartUntilNextWindow()
        {
            var s = Build(homes: 1, officeSlots: 4);
            var car = s.Cars[0];
            car.DepartHomeHour = 20f;     // 출근창 [20, 24)
            car.DepartWorkHour = 5f;      // 퇴근창 [5, 9)
            car.EveningStartHour = 5f;
            car.EveningEndHour = 9f;
            car.State = CarState.Inbound; // 새벽 퇴근길

            s.NotifyArrived(car);         // 6시께 귀가
            Assert.AreEqual(CarState.ParkedHome, car.State);

            s.UpdateDepartures(6.5f);
            Assert.AreEqual(CarState.ParkedHome, car.State, "귀가 직후 같은 출근 구간 안에서 재출근하지 않는다");
            s.UpdateDepartures(8.9f);
            Assert.AreEqual(CarState.ParkedHome, car.State, "퇴근창 끝까지도 집");
            s.UpdateDepartures(12f);      // 구간 밖 관측 → 다음 파도 대기 해제
            Assert.AreEqual(CarState.ParkedHome, car.State, "낮에는 출근하지 않는다");
            s.UpdateDepartures(20f);
            Assert.AreEqual(CarState.Outbound, car.State, "다음 20~24시 창에서만 출근");
        }

        // 유형별 창: 목적지마다 다른 창이 오면 차의 출퇴근 시각도 자기 목적지 창에서 나온다.
        [Test]
        public void Rebuild_PerDestinationWindows_ProduceDifferentDepartureHours()
        {
            Vector2Int dayOffice = V(50, 50);
            Vector2Int nightFactory = V(60, 60);
            var sources = new List<Vector2Int> { V(0, 0), V(1, 0) };
            var sinks = new List<Vector2Int> { dayOffice, nightFactory };

            var s = new CommuteScheduler();
            s.Rebuild(sources, sinks, _ => 4,
                sink => sink == nightFactory
                    ? new CommuteWindow("factory", 20f, 4f, 5f, 4f)
                    : new CommuteWindow("office", 6f, 4f, 17f, 4f),
                homeSlots: 1, maxCars: 96);

            CommuteCar office = s.Cars.First(c => c.Work == dayOffice);
            CommuteCar factory = s.Cars.First(c => c.Work == nightFactory);

            Assert.AreEqual("office", office.CompanyTypeId);
            Assert.AreEqual("factory", factory.CompanyTypeId);

            Assert.GreaterOrEqual(office.DepartHomeHour, 6f, "사무실은 오전 출근창 [6,10)");
            Assert.Less(office.DepartHomeHour, 10f);
            Assert.GreaterOrEqual(factory.DepartHomeHour, 20f, "공장은 야간 출근창 [20,24)");
            Assert.Less(factory.DepartHomeHour, 24f);

            Assert.AreEqual(17f, office.EveningStartHour);
            Assert.AreEqual(21f, office.EveningEndHour);
            Assert.AreEqual(5f, factory.EveningStartHour);
            Assert.AreEqual(9f, factory.EveningEndHour, "공장 퇴근창 [5,9) — 근무가 자정을 넘는다");
        }

        // [P1 회귀] 같은 좌표의 회사가 다른 유형으로 재건축되면 생존 매칭된 차도
        // 새 유형의 창을 받아야 한다 — 옛 시간표가 영구 보존되면 안 된다.
        [Test]
        public void Rebuild_SameCoordinateTypeSwap_RefreshesSurvivorSchedule()
        {
            var sources = new List<Vector2Int> { V(0, 0) };
            var sinks = new List<Vector2Int> { V(50, 50) };
            var s = new CommuteScheduler();
            s.Rebuild(sources, sinks, _ => 4,
                _ => new CommuteWindow("office", 6f, 4f, 17f, 4f),
                homeSlots: 1, maxCars: 96);
            CommuteCar survivor = s.Cars[0];
            Assert.AreEqual("office", survivor.CompanyTypeId);

            s.Rebuild(sources, sinks, _ => 4,
                _ => new CommuteWindow("factory", 20f, 4f, 5f, 4f),
                homeSlots: 1, maxCars: 96);

            Assert.AreSame(survivor, s.Cars[0], "같은 (집,회사) 차는 생존 매칭돼야 전제가 성립한다");
            Assert.AreEqual("factory", survivor.CompanyTypeId, "유형 교체가 생존 차에 반영");
            Assert.GreaterOrEqual(survivor.DepartHomeHour, 20f, "출근 시각이 새 야간창 [20,24)에서 나온다");
            Assert.Less(survivor.DepartHomeHour, 24f);
            Assert.AreEqual(5f, survivor.EveningStartHour);
            Assert.AreEqual(9f, survivor.EveningEndHour);
        }

        // 창 자체가 자정을 넘으면 끝 시각이 24를 넘게 계산된다([23,27) 등).
        // 감싸 넣지 않으면 게임시각 [0,24)와 절대 만나지 않아 차가 조용히 멈춘다.
        [Test]
        public void Rebuild_WindowCrossingMidnight_WrapsHoursInto24()
        {
            var sources = new List<Vector2Int> { V(0, 0) };
            var sinks = new List<Vector2Int> { V(50, 50) };

            var s = new CommuteScheduler();
            s.Rebuild(sources, sinks, _ => 4,
                // 출근창 [22, 26) · 퇴근창 [23, 27)
                _ => new CommuteWindow("nightowl", 22f, 4f, 23f, 4f),
                homeSlots: 1, maxCars: 96);

            CommuteCar car = s.Cars[0];
            Assert.GreaterOrEqual(car.DepartHomeHour, 0f);
            Assert.Less(car.DepartHomeHour, 24f, "출근 시각이 24를 넘지 않는다");
            Assert.GreaterOrEqual(car.DepartWorkHour, 0f);
            Assert.Less(car.DepartWorkHour, 24f, "퇴근 시각이 24를 넘지 않는다");
            Assert.AreEqual(23f, car.EveningStartHour);
            Assert.AreEqual(3f, car.EveningEndHour, "23 + 4 = 27 → 3시로 감싼다");
        }

        // 퇴근창 자체가 자정을 넘는 경우(23시~2시)의 스냅. 2026-07-17 정책은 유지 —
        // 퇴근창 안만 ParkedWork이고 그 밖(근무 중인 낮 포함)은 전부 ParkedHome이다.
        [Test]
        public void SnapCar_EveningWindowWrapsMidnight()
        {
            var s = Build(homes: 1, officeSlots: 4);
            var car = s.Cars[0];
            car.DepartHomeHour = 14f;
            car.DepartWorkHour = 23.5f;
            car.EveningStartHour = 23f;   // 퇴근창 [23, 2) — 자정 넘김
            car.EveningEndHour = 2f;

            s.SnapCar(car, 23.5f);
            Assert.AreEqual(CarState.ParkedWork, car.State, "퇴근창 안(자정 전)");
            s.SnapCar(car, 1f);
            Assert.AreEqual(CarState.ParkedWork, car.State, "퇴근창 안(자정 후)");
            s.SnapCar(car, 2f);
            Assert.AreEqual(CarState.ParkedHome, car.State, "끝 시각은 배타");
            s.SnapCar(car, 18f);
            Assert.AreEqual(CarState.ParkedHome, car.State, "근무 중인 낮도 ParkedHome — 첫 움직임은 출근");
        }
    }
}
