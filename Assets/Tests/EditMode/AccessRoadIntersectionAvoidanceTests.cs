using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class AccessRoadIntersectionAvoidanceTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        [Test]
        public void TryGetAccessRoad_IntersectionThenOrdinary_PrefersOrdinary()
        {
            BuildMixedFrontageCity(
                out CityGrid grid,
                out Vector2Int house,
                out Vector2Int intersection,
                out Vector2Int ordinary);
            var roads = new RoadNetwork(grid);

            Assert.IsTrue(grid.IsIntersection(intersection),
                "전제: 발견 순서상 첫 프론티지는 실제 T자 교차로");
            Assert.IsFalse(grid.IsIntersection(ordinary),
                "전제: 두 번째 프론티지는 일반 도로");
            Assert.IsTrue(roads.TryGetAccessRoad(house, out Vector2Int access));
            Assert.AreEqual(ordinary, access,
                "일반 프론티지가 있으면 먼저 발견한 교차로 대신 일반 도로를 진입로로 골라야 한다");
        }

        [Test]
        public void TryGetAccessRoad_OnlyIntersectionFrontages_ReturnsFirstIntersection()
        {
            var grid = new CityGrid(7, 6);
            Vector2Int house = V(2, 1);
            Assert.IsTrue(grid.Place(
                house,
                TileType.House,
                PlacementDirection.South));
            for (int x = 1; x <= 4; x++)
                Assert.IsTrue(grid.Place(V(x, 3), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 4), TileType.Road));
            Assert.IsTrue(grid.Place(V(3, 4), TileType.Road));
            var roads = new RoadNetwork(grid);

            Assert.IsTrue(grid.IsIntersection(V(2, 3)));
            Assert.IsTrue(grid.IsIntersection(V(3, 3)));
            Assert.IsTrue(roads.TryGetAccessRoad(house, out Vector2Int access),
                "모든 프론티지가 교차로여도 건물을 접근 불가로 만들면 안 된다");
            Assert.AreEqual(V(2, 3), access,
                "일반 프론티지가 없으면 기존 발견 순서의 첫 교차로를 폴백으로 유지해야 한다");
        }

        [Test]
        public void CollectAccessRoads_MixedFrontages_PreservesEveryRoad()
        {
            BuildMixedFrontageCity(
                out CityGrid grid,
                out Vector2Int house,
                out _,
                out _);
            var roads = new RoadNetwork(grid);
            var actual = new List<Vector2Int>();

            roads.CollectAccessRoads(house, actual);

            CollectionAssert.AreEquivalent(
                new[] { V(2, 3), V(3, 3), V(1, 3), V(4, 3) },
                actual,
                "교차로 회피는 프론티지의 순서만 바꾸고 결과 집합의 원소를 빼면 안 된다");
            Assert.AreEqual(4, actual.Count,
                "같은 프론티지를 중복해 집합 보존 단정을 우회하면 안 된다");
        }

        [Test]
        public void CommuteCar_IntersectionFirstHouseFrontage_EnqueuesWithinFiniteTicks()
        {
            SimConfig cfg = CarSimTests.Cfg();
            var grid = new CityGrid(13, 6);
            Vector2Int house = V(2, 1);
            Vector2Int office = V(9, 1);
            Assert.IsTrue(grid.Place(house, TileType.House));
            Assert.IsTrue(grid.Place(office, TileType.Office));
            for (int x = 1; x <= 10; x++)
                Assert.IsTrue(grid.Place(V(x, 3), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 4), TileType.Road));
            Assert.IsTrue(grid.IsIntersection(V(2, 3)),
                "전제: 집의 첫 프론티지는 실제 T자 교차로");
            Assert.IsFalse(grid.IsIntersection(V(3, 3)),
                "전제: 같은 집에 큐 스폰 가능한 일반 프론티지가 존재");

            var roads = new RoadNetwork(grid);
            var demands = new DemandMap(cfg);
            demands.Reassign(grid, roads);
            var planner = new RoutePlanner(grid.Width, grid.Height);
            planner.Plan(demands, roads, grid, cfg);
            var queues = new RoadQueueNetwork(grid.Width, grid.Height, cfg);
            queues.RebuildTopology(grid);
            var sim = new CarSim(cfg);
            sim.Rebuild(demands, planner, queues);
            var events = new SimEventBuffer(new SimEventHub());

            Assert.AreEqual(1, sim.CarCount, "전제: 집 한 채의 통근차 한 대가 배정됨");

            bool enqueued = false;
            for (int tick = 0; tick < 4; tick++)
            {
                sim.Step(7f, queues, events);
                if (!queues.TryLocateCar(0, out _, out _, out _)) continue;
                enqueued = true;
                break;
            }

            Assert.IsTrue(enqueued,
                "교차로가 아닌 대체 진입로가 있으면 통근차가 오프네트워크에 영구 정지하지 않고 유한 틱 안에 출발해야 한다");
            Assert.AreEqual(CarState.Outbound, sim.GetCar(0).State,
                "큐에 들어간 차는 실제 출근 주행 상태여야 한다");
            Assert.GreaterOrEqual(sim.GetCar(0).QueueSlot, 0,
                "라이브 결함의 slot=-1 상태가 아니라 실제 도로 큐 슬롯을 가져야 한다");
        }

        private static void BuildMixedFrontageCity(
            out CityGrid grid,
            out Vector2Int house,
            out Vector2Int intersection,
            out Vector2Int ordinary)
        {
            grid = new CityGrid(7, 6);
            house = V(2, 1);
            intersection = V(2, 3);
            ordinary = V(3, 3);
            Assert.IsTrue(grid.Place(house, TileType.House));
            for (int x = 1; x <= 4; x++)
                Assert.IsTrue(grid.Place(V(x, 3), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 4), TileType.Road));
        }
    }
}
