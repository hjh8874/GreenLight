using System;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public sealed class SchoolZoneTests
    {
        private static Vector2Int V(int x, int y) =>
            new Vector2Int(x, y);

        private static SimConfig SchoolConfig()
        {
            SimConfig config = CarSimTests.Cfg();
            config.SchoolMorningStartHour = 7.5f;
            config.SchoolMorningEndHour = 8.5f;
            config.SchoolReturnStartHour = 14f;
            config.SchoolReturnEndHour = 15f;
            return config;
        }

        [Test]
        public void SchoolZone_RoadWithinRadiusTwoIsMarked()
        {
            var map = new SchoolZoneMap(10, 10);
            map.Rebuild(new[] { V(5, 5) });

            Assert.IsTrue(map.IsSchoolZone(V(5, 7)));
        }

        [Test]
        public void SchoolZone_RoadOutsideRadiusTwoIsNotMarked()
        {
            var map = new SchoolZoneMap(10, 10);
            map.Rebuild(new[] { V(5, 5) });

            Assert.IsFalse(map.IsSchoolZone(V(5, 8)));
            Assert.IsFalse(map.IsSchoolZone(V(7, 7)));
        }

        [Test]
        public void SchoolZone_StandardCarSlowsDuringSchoolWindow()
        {
            SchoolZoneMap map = BuildMap();

            Assert.AreEqual(
                SchoolZoneMap.SchoolZoneNumerator,
                map.GetEffectiveNumerator(60, V(5, 7), 8f, SchoolConfig()));
            Assert.AreEqual(
                SchoolZoneMap.SchoolZoneNumerator,
                map.GetEffectiveNumerator(60, V(5, 7), 14.5f, SchoolConfig()));
        }

        [Test]
        public void SchoolZone_StandardCarIsNormalOutsideSchoolWindow()
        {
            SchoolZoneMap map = BuildMap();

            Assert.AreEqual(
                60,
                map.GetEffectiveNumerator(60, V(5, 7), 10f, SchoolConfig()));
        }

        [Test]
        public void SchoolZone_RemovingSchoolClearsTheZone()
        {
            SimConfig config = SchoolConfig();
            var grid = new CityGrid(10, 10);
            Assert.IsTrue(grid.Place(V(5, 5), TileType.School));
            Assert.IsTrue(grid.Place(V(5, 7), TileType.Road));
            var roads = new RoadNetwork(grid);
            var demands = new DemandMap(config);
            demands.RegisterCompany(V(5, 5), TileType.School, 0d);
            demands.Reassign(grid, roads);
            Assert.IsTrue(demands.IsSchoolZone(V(5, 7)));

            demands.RemoveCompany(V(5, 5));

            Assert.IsFalse(demands.IsSchoolZone(V(5, 7)));
        }

        [Test]
        public void SchoolZone_WithoutSchoolsMarksNoTile()
        {
            var map = new SchoolZoneMap(10, 10);
            map.Rebuild(Array.Empty<Vector2Int>());

            Assert.IsFalse(map.IsSchoolZone(V(5, 5)));
            Assert.IsFalse(map.IsSchoolZone(V(0, 0)));
        }

        private static SchoolZoneMap BuildMap()
        {
            var map = new SchoolZoneMap(10, 10);
            map.Rebuild(new[] { V(5, 5) });
            return map;
        }

        // 리뷰 지적(#242, kimgeon-3seven): "스쿨존 캐시가 도로 배치 변경에 반응하지 않는다".
        // 학교가 이미 있는 상태에서 반경 안에 새 도로를 지으면 감속이 안 걸린다는 주장.
        // 실제 경로(DemandMap.Reassign)로 검증한다 — 도로 배치는 TopologyDirty 를 세우고
        // SimEngine.Step 이 Reassign 을 부르며, Reassign 첫 두 줄이 무조건 스쿨존을 재빌드한다.
        [Test]
        public void SchoolZone_RoadBuiltInsideRadiusAfterSchool_BecomesZoneOnReassign()
        {
            SimConfig config = SchoolConfig();
            var grid = new CityGrid(12, 12);
            var net = new RoadNetwork(grid);
            var demand = new DemandMap(config);

            // 학교부터 세운다. 이 시점에 (5,7) 은 아직 도로가 아니다.
            Assert.IsTrue(grid.Place(V(5, 5), TileType.School));
            demand.RegisterCompany(V(5, 5), TileType.School, 0d);
            demand.Reassign(grid, net);
            Assert.IsFalse(
                demand.IsSchoolZone(V(5, 7)),
                "도로가 없는 타일은 스쿨존이 아니어야 한다");

            // 학교가 있는 상태에서 반경 2 안에 새 도로를 건설한다.
            Assert.IsTrue(grid.Place(V(5, 7), TileType.Road));
            demand.Reassign(grid, net);

            Assert.IsTrue(
                demand.IsSchoolZone(V(5, 7)),
                "학교 설치 후 반경 안에 새로 지은 도로도 스쿨존이 되어야 한다");
        }

        // 반대 방향도 고정한다 — 도로를 철거하면 스쿨존에서 빠져야 한다.
        [Test]
        public void SchoolZone_RoadRemovedInsideRadius_LeavesZoneOnReassign()
        {
            SimConfig config = SchoolConfig();
            var grid = new CityGrid(12, 12);
            var net = new RoadNetwork(grid);
            var demand = new DemandMap(config);

            Assert.IsTrue(grid.Place(V(5, 5), TileType.School));
            Assert.IsTrue(grid.Place(V(5, 7), TileType.Road));
            demand.RegisterCompany(V(5, 5), TileType.School, 0d);
            demand.Reassign(grid, net);
            Assert.IsTrue(demand.IsSchoolZone(V(5, 7)));

            Assert.IsTrue(grid.TryRemove(V(5, 7), out _, out _));
            demand.Reassign(grid, net);

            Assert.IsFalse(
                demand.IsSchoolZone(V(5, 7)),
                "철거된 도로는 스쿨존에서 빠져야 한다");
        }


        // ── 무정차 연결(#241)과의 상호작용 ──
        // 스쿨존은 "감속"이지 "정지"가 아니다. 따라서 FreeFlowStreak 이 끊기면 안 된다.
        // 근거: 스쿨존 감속은 TryConsumeAdvanceCredit 에서 _creditWaiting=true 를 세우고,
        // UpdateFreeFlowStreaks 는 !moved 여도 _creditWaiting 이면 리셋하지 않는다.
        // 두 기능이 서로 모순되지 않는다는 것을 이 테스트가 고정한다.
        [Test]
        public void SchoolZone_SlowdownDoesNotBreakFreeFlowStreak()
        {
            SimConfig config = SchoolConfig();
            // 학교는 2x2 풋프린트다. (4,4) 앵커가 (5,5)까지 차지하므로 높이 7이 필요하다.
            var grid = new CityGrid(10, 7);
            for (int x = 0; x < 10; x++)
                Assert.IsTrue(grid.Place(V(x, 2), TileType.Road), $"main road x={x}");
            foreach (int x in new[] { 2, 4, 6 })
            {
                Assert.IsTrue(grid.Place(V(x, 1), TileType.Road));
                Assert.IsTrue(grid.Place(V(x, 3), TileType.Road));
                Assert.IsTrue(grid.IsIntersection(V(x, 2)));
            }

            Assert.IsTrue(grid.Place(V(0, 0), TileType.House), "house");
            Assert.IsTrue(grid.Place(V(8, 0), TileType.Office), "office");

            // 경로 한복판(4,2)에 붙여 학교를 세운다 → 반경 2 안에 주 간선이 들어온다.
            Assert.IsTrue(grid.Place(V(4, 4), TileType.School), "school");

            var roads = new RoadNetwork(grid);
            var demands = new DemandMap(config);
            demands.RegisterCompany(V(4, 4), TileType.School, 0d);
            demands.Reassign(grid, roads);

            // 전제: 주 간선 위에 실제로 스쿨존이 걸려 있어야 이 테스트가 의미를 갖는다.
            Assert.IsTrue(
                demands.IsSchoolZone(V(4, 2)),
                "전제 실패: 경로 위에 스쿨존이 없다");

            var planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demands, roads, grid, config);
            var queues = new RoadQueueNetwork(grid.Width, grid.Height, config);
            queues.RebuildTopology(grid);
            var sim = new CarSim(config);
            sim.Rebuild(demands, planner, queues, grid: grid, roadNetwork: roads);

            // 등교 시간(7.5~8.5)에 굴린다 — 스쿨존이 활성인 시간대다.
            var events = new SimEventBuffer(new SimEventHub());
            int bestStreak = 0;
            for (int tick = 0; tick < 200; tick++)
            {
                sim.Step(1L, 8f, queues, events, null, tick);
                events.Drain();
                for (int i = 0; i < sim.CarCount; i++)
                {
                    CarSnapshot snapshot = sim.GetCar(i);
                    if (snapshot.FreeFlowStreakMax > bestStreak)
                        bestStreak = snapshot.FreeFlowStreakMax;
                }
            }

            Assert.GreaterOrEqual(
                bestStreak,
                2,
                "스쿨존을 지나도 무정차 연결이 쌓여야 한다(감속은 정지가 아니다)");
        }

    }
}
