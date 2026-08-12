using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    // 방문 예약 누수(#183, 2026-07-31 라이브 발견) 재현 시도.
    // 불변식: 예약된 통근차(SpecialTripReserved)는 반드시 활성 특수여정에 대응한다.
    //   (a) 매 스텝: reservedOwners <= ActiveTripCount
    //   (b) 여정 전부 종료 후: reservedOwners == 0
    // 어느 시나리오든 (a)/(b)가 깨지면 그게 누수 재현이다.
    public class SpecialTripReservationLeakTests
    {
        static Vector2Int V(int x, int y) => CarSimTests.V(x, y);

        static readonly FieldInfo SchedulerField = typeof(CarSim).GetField(
            "_scheduler", BindingFlags.NonPublic | BindingFlags.Instance);

        static readonly FieldInfo TripSchedulerField = typeof(CarSim).GetField(
            "_tripScheduler", BindingFlags.NonPublic | BindingFlags.Instance);

        static List<CommuteCar> ReservedOwners(CarSim sim)
        {
            var scheduler = (CommuteScheduler)SchedulerField.GetValue(sim);
            var reserved = new List<CommuteCar>();
            foreach (CommuteCar car in scheduler.Cars)
            {
                if (!car.IsTransient && car.SpecialTripReserved)
                {
                    reserved.Add(car);
                }
            }

            return reserved;
        }

        static void AssertNoLeak(CarSim sim, string context)
        {
            List<CommuteCar> reserved = ReservedOwners(sim);
            Assert.LessOrEqual(
                reserved.Count,
                sim.ActiveTripCount,
                $"{context}: 예약 {reserved.Count}건이 활성 여정 " +
                $"{sim.ActiveTripCount}건보다 많다 — 누수");
        }

        static void AssertFullyReleased(CarSim sim, string context)
        {
            Assert.AreEqual(
                0,
                sim.ActiveTripCount,
                $"{context}: 활성 여정이 남아 있다");
            Assert.AreEqual(
                0,
                ReservedOwners(sim).Count,
                $"{context}: 여정이 없는데 예약이 남아 있다 — 누수");
        }

        static SpecialTripJourney ActiveSpecialJourney(CarSim sim)
        {
            var scheduler = (TripScheduler)TripSchedulerField.GetValue(sim);
            foreach (SpecialTripJourney journey in scheduler.ActiveJourneys)
            {
                return journey;
            }

            return null;
        }

        static CarSim BuildCity(
            out SimConfig config,
            out CityGrid grid,
            out RoadNetwork roads,
            out DemandMap demands,
            out RoutePlanner planner,
            out RoadQueueNetwork queues,
            out SimEventBuffer events)
        {
            config = CarSimTests.Cfg();
            config.MaxSimCars = 16;
            config.MaxPendingVehicleTrips = 16;
            config.MaxConcurrentSpecialTrips = 2;
            CarSimTests.BuildSpecialVisitCity(
                config,
                out grid,
                out roads,
                out demands,
                out planner,
                out queues);
            var sim = new CarSim(config);
            sim.Rebuild(
                demands,
                planner,
                queues,
                grid: grid,
                roadNetwork: roads);
            events = new SimEventBuffer(new SimEventHub());
            return sim;
        }

        // 출근 창(7시)의 차는 소유차 자격이 없어 무소유 여정으로 빠진다.
        // AfterWork 패턴: 7시에 출근 완주 → 17.5시(퇴근 이후) 방문이 통근차를 빌린다.
        const float VisitHour = 17.5f;

        static void ScheduleVisit(CarSim sim, long day)
        {
            Assert.IsTrue(
                sim.TryScheduleSpecialBuildingVisit(
                    new SpecialBuildingVisitTripRequest(
                        "coffee-shop", V(6, 0), day, 0, VisitHour,
                        rewardCoins: 0)),
                "방문 예약 자체가 실패하면 전제가 깨진 것");
        }

        // 워밍업(출근 완료) 후 여정 활성(예약 성립)까지 스텝.
        static int StepUntilReserved(
            CarSim sim, RoadQueueNetwork queues, SimEventBuffer events)
        {
            int tick = 0;
            for (; tick < 40; tick++)
            {
                sim.Step(1L, 7f, queues, events, null, tick);
                events.Drain();
            }

            Assert.AreEqual(
                CarState.ParkedWork,
                sim.GetCar(0).State,
                "전제 실패: 워밍업 후 출근 상태가 아니다");
            ScheduleVisit(sim, 1L);
            for (int i = 0; i < 40; i++, tick++)
            {
                sim.Step(1L, VisitHour, queues, events, null, tick);
                events.Drain();
                if (sim.ActiveTripCount > 0 && ReservedOwners(sim).Count > 0)
                {
                    return tick;
                }
            }

            Assert.Fail("전제 실패: 40틱 안에 예약된 활성 여정이 생기지 않았다");
            return tick;
        }

        [Test]
        public void OrphanedReservation_IsReleasedAndLogged()
        {
            CarSim sim = BuildCity(
                out _, out _, out _, out _, out _,
                out RoadQueueNetwork queues, out SimEventBuffer events);
            var scheduler = (CommuteScheduler)SchedulerField.GetValue(sim);
            CommuteCar owner = scheduler.Cars[0];
            Assert.IsNotNull(owner, "전제 실패: 통근차 소유자가 없다");
            owner.SetSpecialTripReservation(true);

            LogAssert.Expect(
                LogType.Warning,
                new Regex("^\\[CarSim\\] Orphaned special-trip reservation released.*"));
            sim.Step(1L, 7f, queues, events, null, 0);
            events.Drain();

            Assert.IsFalse(owner.SpecialTripReserved);
        }

        [Test]
        public void StaleJourney_IsCancelledAndReleased()
        {
            CarSim sim = BuildCity(
                out _, out _, out _, out _, out _,
                out RoadQueueNetwork queues, out SimEventBuffer events);
            int tick = StepUntilReserved(sim, queues, events);
            SpecialTripJourney journey = ActiveSpecialJourney(sim);
            Assert.IsNotNull(journey, "전제 실패: 활성 특수 여정이 없다");
            var scheduler = (CommuteScheduler)SchedulerField.GetValue(sim);
            int transientCarId = -1;
            for (int index = 0; index < scheduler.Cars.Count; index++)
            {
                if (ReferenceEquals(scheduler.Cars[index], journey.Vehicle))
                {
                    transientCarId = index;
                    break;
                }
            }
            Assert.GreaterOrEqual(
                transientCarId,
                0,
                "The active special-trip vehicle must belong to the scheduler.");
            journey.BackdateStartForTest(25f);

            LogAssert.Expect(
                LogType.Warning,
                new Regex("^\\[CarSim\\] Stale special journey cancelled.*"));
            sim.Step(1L, VisitHour, queues, events, null, tick + 1);
            events.Drain();

            // ActiveTripCount는 통근 트립도 포함한다 — 해제된 소유차가 즉시
            // 통근을 재개하는 것이 정상(자가 치유 목적)이므로 특수 여정·예약만 본다.
            Assert.IsNull(
                ActiveSpecialJourney(sim), "스테일 여정이 취소되지 않았다");
            Assert.AreEqual(
                0, ReservedOwners(sim).Count, "예약이 해제되지 않았다");
            Assert.IsFalse(
                queues.TryRemoveCarForRescue(transientCarId),
                "A cancelled special-trip vehicle must not remain in the road network.");
        }

        [Test]
        public void Control_VisitRunsToCompletion_ReleasesReservation()
        {
            CarSim sim = BuildCity(
                out _, out _, out _, out _, out _,
                out RoadQueueNetwork queues, out SimEventBuffer events);
            int tick = StepUntilReserved(sim, queues, events);

            for (int i = 0; i < 200; i++, tick++)
            {
                sim.Step(1L, VisitHour, queues, events, null, tick);
                events.Drain();
                AssertNoLeak(sim, $"tick {tick}");
            }

            AssertFullyReleased(sim, "정상 완주");
        }

        [Test]
        public void MidJourney_TopologyRebuild_SameLayout_ReleasesEventually()
        {
            CarSim sim = BuildCity(
                out SimConfig config, out CityGrid grid, out RoadNetwork roads,
                out DemandMap demands, out RoutePlanner planner,
                out RoadQueueNetwork queues, out SimEventBuffer events);
            int tick = StepUntilReserved(sim, queues, events);

            // 건설 리빌드(needsSnap, 점프 아님): 그리드는 그대로.
            demands.Reassign(grid, roads);
            planner.Plan(demands, roads, grid, config);
            queues.RebuildTopology(grid);
            sim.Rebuild(
                demands, planner, queues, grid: grid, roadNetwork: roads);

            for (int i = 0; i < 400; i++, tick++)
            {
                sim.Step(1L, VisitHour, queues, events, null, tick);
                events.Drain();
                AssertNoLeak(sim, $"rebuild 후 tick {tick}");
            }

            AssertFullyReleased(sim, "리빌드 후 완주");
        }

        [Test]
        public void MidJourney_DestinationRouteDemolished_CancelsAndReleases()
        {
            CarSim sim = BuildCity(
                out SimConfig config, out CityGrid grid, out RoadNetwork _,
                out DemandMap demands, out RoutePlanner planner,
                out RoadQueueNetwork queues, out SimEventBuffer events);
            int tick = StepUntilReserved(sim, queues, events);

            // 목적지 앞 도로 절단 → 재계획 실패 경로.
            Assert.IsTrue(grid.Remove(V(6, 2)));
            var roads = new RoadNetwork(grid);
            demands.Reassign(grid, roads);
            planner.Plan(demands, roads, grid, config);
            queues.RebuildTopology(grid);
            sim.Rebuild(
                demands, planner, queues, grid: grid, roadNetwork: roads);

            for (int i = 0; i < 400; i++, tick++)
            {
                sim.Step(1L, VisitHour, queues, events, null, tick);
                events.Drain();
                AssertNoLeak(sim, $"철거 후 tick {tick}");
            }

            AssertFullyReleased(sim, "경로 단절 취소");
        }

        [Test]
        public void MidJourney_TimeJump_CancelsAndReleases()
        {
            CarSim sim = BuildCity(
                out _, out _, out _, out _, out _,
                out RoadQueueNetwork queues, out SimEventBuffer events);
            int tick = StepUntilReserved(sim, queues, events);

            // 1시간 초과 점프 → CancelAllSpecialJourneys 경로.
            sim.Step(1L, 20f, queues, events, null, tick);
            events.Drain();
            AssertFullyReleased(sim, "시간 점프 직후");
        }

        [Test]
        public void MidJourney_OwnerHouseDemolished_ReleasesEventually()
        {
            CarSim sim = BuildCity(
                out SimConfig config, out CityGrid grid, out RoadNetwork _,
                out DemandMap demands, out RoutePlanner planner,
                out RoadQueueNetwork queues, out SimEventBuffer events);
            int tick = StepUntilReserved(sim, queues, events);

            // 예약 소유차의 집 철거 → 스케줄러 리빌드에서 짝 소멸.
            Assert.IsTrue(grid.Remove(V(0, 0)));
            var roads = new RoadNetwork(grid);
            demands.Reassign(grid, roads);
            planner.Plan(demands, roads, grid, config);
            queues.RebuildTopology(grid);
            sim.Rebuild(
                demands, planner, queues, grid: grid, roadNetwork: roads);

            for (int i = 0; i < 400; i++, tick++)
            {
                sim.Step(1L, VisitHour, queues, events, null, tick);
                events.Drain();
                AssertNoLeak(sim, $"집 철거 후 tick {tick}");
            }

            AssertFullyReleased(sim, "소유차 소멸 후");
        }

        // 하루 리듬 소크: 시간이 실제로 흐르고 매일 방문이 잡히는 라이브 근사.
        [Test]
        public void Soak_TwoDaysWithDailyVisits_NeverLeaks()
        {
            CarSim sim = BuildCity(
                out _, out _, out _, out _, out _,
                out RoadQueueNetwork queues, out SimEventBuffer events);

            const float hoursPerTick = 0.05f;
            int tick = 0;
            var scheduledDays = new HashSet<long>();
            for (float total = 0f; total < 48f; total += hoursPerTick, tick++)
            {
                long day = 1L + (long)(total / 24f);
                float hour = total % 24f;
                if (!scheduledDays.Contains(day) && hour >= 7f)
                {
                    // 하루 2건: 오전(근무 중 방문)·저녁(퇴근 후 방문) 창.
                    sim.TryScheduleSpecialBuildingVisit(
                        new SpecialBuildingVisitTripRequest(
                            "coffee-shop", V(6, 0), day, 0, 7.5f, 0));
                    sim.TryScheduleSpecialBuildingVisit(
                        new SpecialBuildingVisitTripRequest(
                            "coffee-shop", V(6, 0), day, 1, 19f, 0));
                    scheduledDays.Add(day);
                }

                sim.Step(day, hour, queues, events, null, tick);
                events.Drain();
                AssertNoLeak(sim, $"day {day} hour {hour:0.0}");
            }

            AssertFullyReleased(sim, "이틀 소크 종료");
        }
    }
}
