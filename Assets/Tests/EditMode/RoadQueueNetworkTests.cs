using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;
using CityFlow.Sim;

namespace CityFlow.Sim.Tests
{
    public class RoadQueueNetworkTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static SimConfig Cfg()
        {
            var c = SimConfig.Default();
            c.QueueCapacityPerTile = 4;
            c.QueueServicePerTick = 1;
            return c;
        }

        private sealed class FakeRouteProvider : ICarRouteProvider
        {
            private sealed class Route
            {
                public Vector2Int[] Tiles;
                public bool DestinationAtEnd;
            }

            private readonly Dictionary<int, Route> _routes = new();

            public void AddRoute(
                int carId,
                bool destinationAtEnd,
                params Vector2Int[] tiles)
            {
                _routes.Add(carId, new Route
                {
                    Tiles = tiles,
                    DestinationAtEnd = destinationAtEnd
                });
            }

            public bool TryGetNextTile(
                int carId,
                Vector2Int current,
                out Vector2Int next,
                out Dir entryDirAtNext)
            {
                next = default;
                entryDirAtNext = default;
                if (!_routes.TryGetValue(carId, out Route route))
                {
                    return false;
                }

                for (int i = 0; i < route.Tiles.Length - 1; i++)
                {
                    if (route.Tiles[i] != current)
                    {
                        continue;
                    }

                    next = route.Tiles[i + 1];
                    entryDirAtNext = DirectionOf(next - current);
                    return true;
                }

                return false;
            }

            public bool IsDestination(int carId, Vector2Int tile)
            {
                return _routes.TryGetValue(carId, out Route route)
                    && route.DestinationAtEnd
                    && route.Tiles.Length > 0
                    && route.Tiles[route.Tiles.Length - 1] == tile;
            }

            private static Dir DirectionOf(Vector2Int delta)
            {
                if (delta == Vector2Int.up) return Dir.N;
                if (delta == Vector2Int.right) return Dir.E;
                if (delta == Vector2Int.down) return Dir.S;
                if (delta == Vector2Int.left) return Dir.W;
                Assert.Fail($"직교 인접 스텝이 아님: {delta}");
                return default;
            }
        }

        [Test]
        public void Enqueue_FourCarsSpillAcrossTwoTilesAtPhysicalCapacity()
        {
            var q = new RoadQueueNetwork(2, 1, Cfg());

            Assert.IsTrue(q.TryEnqueue(V(1, 0), Dir.E, 0));
            Assert.IsTrue(q.TryEnqueue(V(1, 0), Dir.E, 1));
            Assert.IsTrue(q.TryEnqueue(V(0, 0), Dir.E, 2));
            Assert.IsTrue(q.TryEnqueue(V(0, 0), Dir.E, 3));

            Assert.AreEqual(2, q.QueueCount(V(1, 0), Dir.E), "목표 타일 물리 슬롯 2개");
            Assert.AreEqual(2, q.QueueCount(V(0, 0), Dir.E), "초과 수요는 상류 타일에 잔류");
            Assert.AreEqual(0, q.CarAtHead(V(1, 0), Dir.E), "하류 FIFO 머리");
            Assert.AreEqual(2, q.CarAtHead(V(0, 0), Dir.E), "상류 FIFO 머리");
        }

        [Test]
        public void ConfiguredCapacityFour_NormalQueueNeverAcceptsThirdVehicle()
        {
            var q = new RoadQueueNetwork(1, 1, Cfg());

            Assert.IsTrue(q.TryEnqueue(V(0, 0), Dir.E, 0));
            Assert.IsTrue(q.TryEnqueue(V(0, 0), Dir.E, 1));
            Assert.IsFalse(q.TryEnqueue(V(0, 0), Dir.E, 2));
            Assert.AreEqual(2, q.QueueCount(V(0, 0), Dir.E));
        }

        [Test]
        public void ThreeWaitingCars_LeaveOneUpstream_AndQueueSlotsStayPhysical()
        {
            var q = new RoadQueueNetwork(2, 1, Cfg());
            var routes = new FakeRouteProvider();
            routes.AddRoute(0, destinationAtEnd: false, V(1, 0));
            routes.AddRoute(1, destinationAtEnd: false, V(1, 0));
            routes.AddRoute(2, destinationAtEnd: true, V(0, 0), V(1, 0));
            Assert.IsTrue(q.TryEnqueue(V(1, 0), Dir.E, 0));
            Assert.IsTrue(q.TryEnqueue(V(1, 0), Dir.E, 1));
            Assert.IsTrue(q.TryEnqueue(V(0, 0), Dir.E, 2));

            q.Step(routes);

            Assert.AreEqual(2, q.QueueCount(V(1, 0), Dir.E));
            Assert.AreEqual(1, q.QueueCount(V(0, 0), Dir.E), "세 번째 차는 상류 잔류");
            for (int carId = 0; carId < 3; carId++)
            {
                Assert.IsTrue(q.TryLocateCar(carId, out _, out _, out int slot));
                Assert.LessOrEqual(slot, 1, $"car {carId} 물리 슬롯 상한");
            }
        }

        [Test]
        public void TwoVehiclesOccupancy_IsJamAtConfiguredThreshold()
        {
            SimConfig cfg = Cfg();
            var q = new RoadQueueNetwork(1, 1, cfg);
            Assert.IsTrue(q.TryEnqueue(V(0, 0), Dir.E, 0));
            Assert.IsTrue(q.TryEnqueue(V(0, 0), Dir.E, 1));

            float occupancy = q.MaxOccupancy01(V(0, 0));

            Assert.AreEqual(1f, occupancy, 1e-4f, "2/2 = 물리 만석");
            Assert.AreEqual(
                CongestionLevel.Jam,
                SimEngine.CongestionForOccupancy(occupancy, cfg));
        }

        [Test]
        public void Occupancy_IsMaxOverDirections()
        {
            var q = new RoadQueueNetwork(5, 5, Cfg());
            q.TryEnqueue(V(1, 1), Dir.N, 0);
            q.TryEnqueue(V(1, 1), Dir.N, 1);
            q.TryEnqueue(V(1, 1), Dir.E, 2);

            Assert.AreEqual(
                1f,
                q.MaxOccupancy01(V(1, 1)),
                1e-4f,
                "N큐 2/2가 최대");
        }

        [Test]
        public void DirectionQueues_AreIndependent()
        {
            var q = new RoadQueueNetwork(5, 5, Cfg());
            for (int i = 0; i < 2; i++)
            {
                Assert.IsTrue(q.TryEnqueue(V(3, 3), Dir.N, i));
            }

            Assert.IsTrue(
                q.TryEnqueue(V(3, 3), Dir.S, 10),
                "다른 방향 큐는 독립");
        }

        [Test]
        public void Step_StraightFiveTiles_ArrivesAfterFiveTicks()
        {
            var q = new RoadQueueNetwork(5, 1, Cfg());
            var routes = new FakeRouteProvider();
            routes.AddRoute(
                7,
                destinationAtEnd: true,
                V(0, 0), V(1, 0), V(2, 0), V(3, 0), V(4, 0));
            Assert.IsTrue(q.TryEnqueue(V(0, 0), Dir.E, 7));

            for (int tick = 1; tick <= 4; tick++)
            {
                StepResult result = q.Step(routes);
                Assert.AreEqual(0, result.Arrivals, $"tick {tick} 조기 도착 금지");
                Assert.AreEqual(7, q.CarAtHead(V(tick, 0), Dir.E));
            }

            StepResult arrival = q.Step(routes);
            Assert.AreEqual(1, arrival.Arrivals);
            Assert.AreEqual(-1, q.CarAtHead(V(4, 0), Dir.E));
        }

        [Test]
        public void Step_FullDownstream_WaitsUntilHeadDrains()
        {
            SimConfig cfg = Cfg();
            cfg.QueueCapacityPerTile = 4;
            var q = new RoadQueueNetwork(3, 1, cfg);
            var routes = new FakeRouteProvider();
            routes.AddRoute(0, true, V(0, 0), V(1, 0), V(2, 0));
            Assert.IsTrue(q.TryEnqueue(V(0, 0), Dir.E, 0));

            for (int id = 10; id < 12; id++)
            {
                routes.AddRoute(id, true, V(1, 0));
                Assert.IsTrue(q.TryEnqueue(V(1, 0), Dir.E, id));
            }

            q.Step(routes);
            Assert.AreEqual(0, q.CarAtHead(V(0, 0), Dir.E), "만석이면 상류 대기");
            Assert.AreEqual(1, q.QueueCount(V(1, 0), Dir.E), "하류 머리 1대 드레인");

            q.Step(routes);
            Assert.AreEqual(-1, q.CarAtHead(V(0, 0), Dir.E), "공간이 생긴 다음 틱에 진행");
            Assert.AreEqual(1, q.QueueCount(V(1, 0), Dir.E), "이동 차가 하류 FIFO에 보존");
        }

        [Test]
        public void Step_BlockedEnd_SpillsBackAcrossTwoUpstreamTiles()
        {
            SimConfig cfg = Cfg();
            cfg.QueueCapacityPerTile = 2;
            var q = new RoadQueueNetwork(3, 1, cfg);
            var routes = new FakeRouteProvider();

            for (int id = 0; id < 2; id++)
            {
                routes.AddRoute(id, destinationAtEnd: false, V(2, 0));
                Assert.IsTrue(q.TryEnqueue(V(2, 0), Dir.E, id));
            }

            for (int id = 2; id < 6; id++)
            {
                routes.AddRoute(id, true, V(0, 0), V(1, 0), V(2, 0));
                Assert.IsTrue(q.TryEnqueue(V(0, 0), Dir.E, id));
                q.Step(routes);
            }

            Assert.AreEqual(2, q.QueueCount(V(2, 0), Dir.E), "막힌 끝 타일 만석");
            Assert.AreEqual(2, q.QueueCount(V(1, 0), Dir.E), "첫 상류까지 스필백");
            Assert.AreEqual(2, q.QueueCount(V(0, 0), Dir.E), "둘째 상류까지 스필백");
        }

        [Test]
        public void Step_SameSetup_ProducesIdenticalStateEveryTick()
        {
            SimConfig cfg = Cfg();
            cfg.QueueCapacityPerTile = 2;
            var first = BuildDeterminismScenario(cfg, out FakeRouteProvider firstRoutes);
            var second = BuildDeterminismScenario(cfg, out FakeRouteProvider secondRoutes);

            for (int tick = 0; tick < 8; tick++)
            {
                StepResult firstResult = first.Step(firstRoutes);
                StepResult secondResult = second.Step(secondRoutes);
                Assert.AreEqual(firstResult.Arrivals, secondResult.Arrivals, $"tick {tick} 도착");

                for (int x = 0; x < 4; x++)
                {
                    foreach (Dir dir in new[] { Dir.N, Dir.E, Dir.S, Dir.W })
                    {
                        Assert.AreEqual(
                            first.QueueCount(V(x, 0), dir),
                            second.QueueCount(V(x, 0), dir),
                            $"tick {tick}, tile {x}, dir {dir} count");
                        Assert.AreEqual(
                            first.CarAtHead(V(x, 0), dir),
                            second.CarAtHead(V(x, 0), dir),
                            $"tick {tick}, tile {x}, dir {dir} head");
                    }
                }
            }
        }

        private static RoadQueueNetwork BuildDeterminismScenario(
            SimConfig cfg,
            out FakeRouteProvider routes)
        {
            var q = new RoadQueueNetwork(4, 1, cfg);
            routes = new FakeRouteProvider();
            for (int id = 0; id < 4; id++)
            {
                routes.AddRoute(id, true, V(0, 0), V(1, 0), V(2, 0), V(3, 0));
            }

            q.TryEnqueue(V(0, 0), Dir.E, 0);
            q.TryEnqueue(V(0, 0), Dir.E, 1);
            q.TryEnqueue(V(1, 0), Dir.E, 2);
            q.TryEnqueue(V(2, 0), Dir.E, 3);
            return q;
        }

        [Test]
        public void Step_IntersectionExitFull_DoesNotEnterIntersection()
        {
            SimConfig cfg = Cfg();
            cfg.QueueCapacityPerTile = 1;
            cfg.GridlockValveTicks = 8;
            var q = new RoadQueueNetwork(5, 3, cfg);
            CityGrid grid = BuildCrossIntersection();
            q.RebuildTopology(grid, new FakeDeviceState());

            var routes = new FakeRouteProvider();
            routes.AddRoute(0, true, V(1, 1), V(2, 1), V(3, 1));
            routes.AddRoute(10, destinationAtEnd: false, V(3, 1));
            Assert.IsTrue(q.TryEnqueue(V(1, 1), Dir.E, 0));
            Assert.IsTrue(q.TryEnqueue(V(3, 1), Dir.E, 10));

            q.Step(routes);

            Assert.AreEqual(0, q.CarAtHead(V(1, 1), Dir.E), "출구가 차면 진입 전 대기");
            Assert.AreEqual(0, q.QueueCount(V(2, 1), Dir.E), "교차로 내부 점유 금지");
        }

        [Test]
        public void Step_IntersectionExitOccupied_DoesNotEnterWithSpareQueueCapacity()
        {
            SimConfig cfg = Cfg();
            cfg.QueueCapacityPerTile = 4;
            var q = new RoadQueueNetwork(5, 3, cfg);
            CityGrid grid = BuildCrossIntersection();
            q.RebuildTopology(grid, new FakeDeviceState());

            var routes = new FakeRouteProvider();
            routes.AddRoute(20, true, V(1, 1), V(2, 1), V(3, 1));
            routes.AddRoute(21, false, V(3, 1));
            Assert.IsTrue(q.TryEnqueue(V(1, 1), Dir.E, 20));
            Assert.IsTrue(q.TryEnqueue(V(3, 1), Dir.E, 21));

            q.Step(routes);

            Assert.AreEqual(20, q.CarAtHead(V(1, 1), Dir.E));
            Assert.AreEqual(0, q.QueueCount(V(2, 1), Dir.E),
                "A vehicle must not enter unless the far-side lane can fully receive it.");
        }

        [Test]
        public void Step_FullCycle_ValveActivatesOnConfiguredTick()
        {
            SimConfig cfg = Cfg();
            cfg.QueueCapacityPerTile = 1;
            cfg.GridlockValveTicks = 8;
            RoadQueueNetwork q = BuildFullCycle(cfg, out FakeRouteProvider routes);

            for (int tick = 1; tick < cfg.GridlockValveTicks; tick++)
            {
                StepResult beforeThreshold = q.Step(routes);
                Assert.AreEqual(
                    0,
                    beforeThreshold.ValveActivations,
                    $"tick {tick} 조기 밸브 금지");
            }

            StepResult atThreshold = q.Step(routes);
            Assert.Greater(atThreshold.ValveActivations, 0, "설정 틱에 순환 데드락 탈출");
        }

        [Test]
        public void Step_ValveActivation_PreservesTotalCarCount()
        {
            SimConfig cfg = Cfg();
            cfg.QueueCapacityPerTile = 1;
            cfg.GridlockValveTicks = 8;
            RoadQueueNetwork q = BuildFullCycle(cfg, out FakeRouteProvider routes);
            int before = TotalCars(q, width: 2, height: 2);

            int activations = 0;
            for (int tick = 0; tick < cfg.GridlockValveTicks; tick++)
            {
                activations += q.Step(routes).ValveActivations;
            }

            Assert.Greater(activations, 0);
            Assert.AreEqual(before, TotalCars(q, width: 2, height: 2), "밸브 후 증발·복제 금지");
        }

        [Test]
        public void Step_DestinationRecordsCarAndTile_ThenResetsNextStep()
        {
            var q = new RoadQueueNetwork(2, 1, Cfg());
            var routes = new FakeRouteProvider();
            routes.AddRoute(70, true, V(0, 0), V(1, 0));
            Assert.IsTrue(q.TryEnqueue(V(1, 0), Dir.E, 70));

            StepResult arrived = q.Step(routes);

            Assert.AreEqual(1, arrived.Arrivals);
            Assert.AreEqual(arrived.Arrivals, q.ArrivalCount);
            ArrivalRecord record = q.GetArrival(0);
            Assert.AreEqual(70, record.CarId);
            Assert.AreEqual(V(1, 0), record.Tile);

            q.Step(routes);
            Assert.AreEqual(0, q.ArrivalCount, "Step 시작 시 관찰 버퍼 리셋");
        }

        [Test]
        public void RemoveAllCars_ClearsQueuesAndAllowsPoolReuse()
        {
            var q = new RoadQueueNetwork(2, 1, Cfg());
            Assert.IsTrue(q.TryEnqueue(V(0, 0), Dir.E, 80));
            Assert.IsTrue(q.TryEnqueue(V(1, 0), Dir.W, 81));

            q.RemoveAllCars();

            Assert.AreEqual(0, TotalCars(q, width: 2, height: 1));
            Assert.IsTrue(q.TryEnqueue(V(0, 0), Dir.E, 82), "고정 노드 풀 재사용");
            Assert.AreEqual(82, q.CarAtHead(V(0, 0), Dir.E));
        }

        [Test]
        public void Step_OpposingStraights_EnterIntersectionTogether()
        {
            SimConfig cfg = Cfg();
            var q = new RoadQueueNetwork(5, 3, cfg);
            CityGrid grid = BuildCrossIntersection();
            q.RebuildTopology(grid, new FakeDeviceState());

            var routes = new FakeRouteProvider();
            routes.AddRoute(0, true, V(1, 1), V(2, 1), V(3, 1));
            routes.AddRoute(1, true, V(3, 1), V(2, 1), V(1, 1));
            Assert.IsTrue(q.TryEnqueue(V(1, 1), Dir.E, 0));
            Assert.IsTrue(q.TryEnqueue(V(3, 1), Dir.W, 1));

            q.Step(routes);

            Assert.AreEqual(0, q.CarAtHead(V(2, 1), Dir.E));
            Assert.AreEqual(1, q.CarAtHead(V(2, 1), Dir.W));
        }

        [Test]
        public void Step_TurnWaitsInsideUntilOpposingStraightQueueClears()
        {
            SimConfig cfg = Cfg();
            var q = new RoadQueueNetwork(5, 3, cfg);
            CityGrid grid = BuildCrossIntersection();
            q.RebuildTopology(grid, new FakeDeviceState());

            var routes = new FakeRouteProvider();
            routes.AddRoute(0, true, V(1, 1), V(2, 1), V(2, 2));
            routes.AddRoute(1, true, V(3, 1), V(2, 1), V(1, 1));
            routes.AddRoute(2, true, V(3, 1), V(2, 1), V(1, 1));
            Assert.IsTrue(q.TryEnqueue(V(1, 1), Dir.E, 0));
            Assert.IsTrue(q.TryEnqueue(V(3, 1), Dir.W, 1));
            Assert.IsTrue(q.TryEnqueue(V(3, 1), Dir.W, 2));

            q.Step(routes);

            Assert.AreEqual(0, q.CarAtHead(V(2, 1), Dir.E), "회전 차량은 진입 셀까지 들어와야 한다");
            Assert.IsTrue(q.TryLocateCar(0, out _, out _, out _, out float turnProgress));
            Assert.AreEqual(0.25f, turnProgress, 1e-4f);
            Assert.AreEqual(1, q.CarAtHead(V(2, 1), Dir.W), "첫 직진 차량이 먼저 통과해야 한다");

            q.Step(routes);

            Assert.IsTrue(q.TryLocateCar(0, out _, out _, out _, out turnProgress));
            Assert.AreEqual(0.25f, turnProgress, 1e-4f, "대기 직진 차량이 남으면 회전을 보류해야 한다");
            Assert.AreEqual(2, q.CarAtHead(V(3, 1), Dir.W),
                "The next straight must wait until the previous car visibly clears the intersection.");

            q.Step(routes);

            Assert.IsTrue(q.TryLocateCar(0, out _, out _, out _, out turnProgress));
            Assert.AreEqual(0.25f, turnProgress, 1e-4f,
                "A queued opposing straight remains a threat while its exit is occupied.");

            q.Step(routes);
            Assert.IsTrue(q.TryLocateCar(0, out _, out _, out _, out turnProgress));
            Assert.AreEqual(0.25f, turnProgress, 1e-4f);
            Assert.AreEqual(2, q.CarAtHead(V(2, 1), Dir.W));

            q.Step(routes);
            Assert.IsTrue(q.TryLocateCar(0, out _, out _, out _, out turnProgress));
            Assert.AreEqual(0.25f, turnProgress, 1e-4f,
                "The turn waits through the straight vehicle's exit tick.");

            q.Step(routes);
            Assert.IsTrue(q.TryLocateCar(0, out _, out _, out _, out turnProgress));
            Assert.AreEqual(0.25f, turnProgress, 1e-4f,
                "The turn waits one rear-clearance tick after the straight vehicle exits.");

            q.Step(routes);
            Assert.IsTrue(q.TryLocateCar(0, out _, out _, out _, out turnProgress));
            Assert.AreEqual(0.75f, turnProgress, 1e-4f,
                "The turn proceeds after the opposing approach queue fully clears.");
        }

        // 교차 축은 여전히 순번을 지켜야 한다 — 위 변경이 모든 이동을 통과시키면 안 된다.
        [Test]
        public void Step_OpposingStraightWaitingForExit_BlocksTurn()
        {
            SimConfig cfg = Cfg();
            var q = new RoadQueueNetwork(5, 3, cfg);
            CityGrid grid = BuildCrossIntersection();
            q.RebuildTopology(grid, new FakeDeviceState());

            var routes = new FakeRouteProvider();
            routes.AddRoute(40, true, V(1, 1), V(2, 1), V(2, 2));
            routes.AddRoute(41, true, V(3, 1), V(2, 1), V(1, 1));
            routes.AddRoute(42, false, V(1, 1));
            Assert.IsTrue(q.TryEnqueue(V(1, 1), Dir.E, 40));
            Assert.IsTrue(q.TryEnqueue(V(3, 1), Dir.W, 41));
            Assert.IsTrue(q.TryEnqueue(V(1, 1), Dir.W, 42));

            q.Step(routes);

            Assert.IsTrue(q.TryLocateCar(40, out _, out _, out _, out float turnProgress));
            Assert.AreEqual(0.25f, turnProgress, 1e-4f);
            Assert.AreEqual(41, q.CarAtHead(V(3, 1), Dir.W),
                "A waiting oncoming straight must retain priority pressure.");
        }

        [Test]
        public void Step_VehicleMovingAwayOnOppositeTile_DoesNotBlockTurn()
        {
            SimConfig cfg = Cfg();
            var q = new RoadQueueNetwork(5, 3, cfg);
            CityGrid grid = BuildCrossIntersection();
            Assert.IsTrue(grid.Place(V(4, 1), TileType.Road));
            q.RebuildTopology(grid, new FakeDeviceState());

            var routes = new FakeRouteProvider();
            routes.AddRoute(50, true, V(1, 1), V(2, 1), V(2, 2));
            routes.AddRoute(51, true, V(3, 1), V(4, 1));
            Assert.IsTrue(q.TryEnqueue(V(1, 1), Dir.E, 50));
            Assert.IsTrue(q.TryEnqueue(V(3, 1), Dir.E, 51));

            q.Step(routes);

            Assert.IsTrue(q.TryLocateCar(50, out _, out _, out _, out float turnProgress));
            Assert.AreEqual(0.75f, turnProgress, 1e-4f,
                "A vehicle moving away from the intersection must not create a threat.");
        }

        [Test]
        public void Step_TurnInside_IsNotStarvedByNewTurningArrival()
        {
            SimConfig cfg = Cfg();
            var q = new RoadQueueNetwork(5, 3, cfg);
            CityGrid grid = BuildCrossIntersection();
            q.RebuildTopology(grid, new FakeDeviceState());

            var routes = new FakeRouteProvider();
            routes.AddRoute(10, true, V(1, 1), V(2, 1), V(2, 2));
            routes.AddRoute(11, true, V(3, 1), V(2, 1), V(2, 0));
            routes.AddRoute(12, true, V(3, 1), V(2, 1), V(1, 1));
            Assert.IsTrue(q.TryEnqueue(V(1, 1), Dir.E, 10));
            Assert.IsTrue(q.TryEnqueue(V(3, 1), Dir.W, 12));

            q.Step(routes);
            Assert.IsTrue(q.TryLocateCar(10, out _, out _, out _, out float firstProgress));
            Assert.AreEqual(0.25f, firstProgress, 1e-4f);

            Assert.IsTrue(q.TryEnqueue(V(3, 1), Dir.W, 11));
            q.Step(routes);

            Assert.IsTrue(q.TryLocateCar(10, out _, out _, out _, out firstProgress));
            Assert.AreEqual(0.25f, firstProgress, 1e-4f,
                "The turn must keep waiting during the straight vehicle's exit tick.");
            Assert.IsTrue(q.TryLocateCar(
                11,
                out Vector2Int waitingTile,
                out _,
                out _,
                out float waitingProgress));
            Assert.AreEqual(V(3, 1), waitingTile);
            Assert.AreEqual(-1f, waitingProgress, 1e-4f);

            q.Step(routes);

            Assert.IsTrue(q.TryLocateCar(10, out _, out _, out _, out firstProgress));
            Assert.AreEqual(0.25f, firstProgress, 1e-4f,
                "The inside turn waits through the exiting vehicle's rear-clearance tick.");

            q.Step(routes);

            Assert.IsTrue(q.TryLocateCar(10, out _, out _, out _, out firstProgress));
            Assert.AreEqual(0.75f, firstProgress, 1e-4f,
                "The inside turn proceeds after the conflicting vehicle fully clears.");
        }

        [Test]
        public void Step_ContinuousOpposingStraights_StarvedLeftTurnPassesWithinBound()
        {
            SimConfig cfg = Cfg();
            cfg.GridlockValveTicks = 8;
            RoadQueueNetwork q = BuildContinuousOpposingStraightScenario(
                cfg,
                out FakeRouteProvider routes);
            int advancedTick = -1;
            int exitedTick = -1;

            for (int tick = 1; tick <= cfg.GridlockValveTicks + 4; tick++)
            {
                EnqueueOpposingStraight(q, routes, 100 + tick);
                q.Step(routes);
                Assert.IsTrue(q.TryLocateCar(
                    90,
                    out _,
                    out _,
                    out _,
                    out float turnProgress));
                if (advancedTick < 0 && turnProgress >= 0.75f) advancedTick = tick;
                if (q.CarAtHead(V(2, 2), Dir.N) == 90)
                {
                    exitedTick = tick;
                    break;
                }
            }

            Assert.That(
                advancedTick,
                Is.InRange(1, cfg.GridlockValveTicks + 3),
                "A starved turn must win once the occupied path and one rear-clearance tick finish.");
            Assert.That(
                exitedTick,
                Is.InRange(1, cfg.GridlockValveTicks + 4),
                "Continuous straight arrivals cannot keep the turn inside forever.");
        }

        [Test]
        public void Step_ContinuousCrossingStraights_StarvedLeftTurnEntersWithinBound()
        {
            SimConfig cfg = Cfg();
            cfg.GridlockValveTicks = 8;
            RoadQueueNetwork q = BuildContinuousCrossingStraightScenario(
                cfg,
                out FakeRouteProvider routes);
            int enteredTick = -1;

            for (int tick = 1; tick <= cfg.GridlockValveTicks + 3; tick++)
            {
                EnqueueCrossingStraight(q, routes, 300 + tick);
                q.Step(routes);
                if (q.CarAtHead(V(2, 1), Dir.E) == 90)
                {
                    enteredTick = tick;
                    break;
                }
            }

            Assert.That(
                enteredTick,
                Is.InRange(1, cfg.GridlockValveTicks + 3),
                "A starved approach head must beat newly arriving non-starved straights.");
        }

        [Test]
        public void Step_ContinuousCrossingStraights_BelowAgingThresholdKeepPriority()
        {
            SimConfig cfg = Cfg();
            cfg.GridlockValveTicks = 8;
            RoadQueueNetwork q = BuildContinuousCrossingStraightScenario(
                cfg,
                out FakeRouteProvider routes);

            for (int tick = 1; tick < cfg.GridlockValveTicks; tick++)
            {
                EnqueueCrossingStraight(q, routes, 400 + tick);
                q.Step(routes);
            }

            Assert.AreEqual(
                90,
                q.CarAtHead(V(1, 1), Dir.E),
                "Before the threshold, the existing straight-over-turn rule is unchanged.");
            Assert.AreEqual(-1, q.CarAtHead(V(2, 1), Dir.E));
        }

        [Test]
        public void Step_IntersectionPath_IsNotReusedDuringVehicleExitTick()
        {
            SimConfig cfg = Cfg();
            var q = new RoadQueueNetwork(5, 3, cfg);
            CityGrid grid = BuildCrossIntersection();
            q.RebuildTopology(grid, new FakeDeviceState());

            var routes = new FakeRouteProvider();
            routes.AddRoute(30, true, V(1, 1), V(2, 1), V(3, 1), V(4, 1));
            routes.AddRoute(31, true, V(1, 1), V(2, 1), V(3, 1), V(4, 1));
            Assert.IsTrue(q.TryEnqueue(V(1, 1), Dir.E, 30));
            Assert.IsTrue(q.TryEnqueue(V(1, 1), Dir.E, 31));

            q.Step(routes);
            Assert.AreEqual(30, q.CarAtHead(V(2, 1), Dir.E));

            q.Step(routes);

            Assert.AreEqual(30, q.CarAtHead(V(3, 1), Dir.E));
            Assert.AreEqual(31, q.CarAtHead(V(1, 1), Dir.E));
            Assert.AreEqual(0, q.QueueCount(V(2, 1), Dir.E));
        }

        [Test]
        public void Step_IntersectionRearClearing_BlocksConflictingEntryForFollowingTick()
        {
            RoadQueueNetwork q = BuildIntersectionRearClearanceScenario(
                out FakeRouteProvider routes);

            q.Step(routes);

            Assert.AreEqual(31, q.CarAtHead(V(2, 0), Dir.N),
                "The crossing vehicle must wait while the exited vehicle's rear clears.");
            Assert.AreEqual(0, q.QueueCount(V(2, 1), Dir.N));
        }

        [Test]
        public void Step_IntersectionRearClearing_AllowsConflictingEntryAfterFollowingTick()
        {
            RoadQueueNetwork q = BuildIntersectionRearClearanceScenario(
                out FakeRouteProvider routes);

            q.Step(routes);
            q.Step(routes);

            Assert.AreEqual(31, q.CarAtHead(V(2, 1), Dir.N),
                "The crossing path becomes available after the fixed one-tick rear-clearance window.");
            Assert.AreEqual(-1, q.CarAtHead(V(2, 0), Dir.N));
        }

        [Test]
        public void Step_CrossingApproaches_OnlyOneEnters()
        {
            SimConfig cfg = Cfg();
            var q = new RoadQueueNetwork(5, 3, cfg);
            CityGrid grid = BuildCrossIntersection();
            q.RebuildTopology(grid, new FakeDeviceState());

            var routes = new FakeRouteProvider();
            routes.AddRoute(0, true, V(1, 1), V(2, 1), V(3, 1));
            routes.AddRoute(1, true, V(2, 0), V(2, 1), V(2, 2));
            Assert.IsTrue(q.TryEnqueue(V(1, 1), Dir.E, 0));
            Assert.IsTrue(q.TryEnqueue(V(2, 0), Dir.N, 1));

            q.Step(routes);

            int entered = q.QueueCount(V(2, 1), Dir.E) + q.QueueCount(V(2, 1), Dir.N);
            int waiting = q.QueueCount(V(1, 1), Dir.E) + q.QueueCount(V(2, 0), Dir.N);
            Assert.AreEqual(1, entered);
            Assert.AreEqual(1, waiting, "Only one conflicting approach may enter per tick.");
        }

        [Test]
        public void Step_OpposingUTurnApproaches_OnlyOneEnters()
        {
            SimConfig cfg = Cfg();
            var q = new RoadQueueNetwork(5, 3, cfg);
            CityGrid grid = BuildCrossIntersection();
            q.RebuildTopology(grid, new FakeDeviceState());

            var routes = new FakeRouteProvider();
            routes.AddRoute(0, false, V(1, 1), V(2, 1), V(1, 1));
            routes.AddRoute(1, false, V(3, 1), V(2, 1), V(3, 1));
            Assert.IsTrue(q.TryEnqueue(V(1, 1), Dir.E, 0));
            Assert.IsTrue(q.TryEnqueue(V(3, 1), Dir.W, 1));

            q.Step(routes);

            int entered = q.QueueCount(V(2, 1), Dir.E) + q.QueueCount(V(2, 1), Dir.W);
            int waiting = q.QueueCount(V(1, 1), Dir.E) + q.QueueCount(V(3, 1), Dir.W);
            Assert.AreEqual(1, entered);
            Assert.AreEqual(1, waiting, "Only one conflicting U-turn may enter per tick.");
        }

        private static CityGrid BuildCrossIntersection()
        {
            var grid = new CityGrid(5, 3);
            Assert.IsTrue(grid.Place(V(2, 1), TileType.Road));
            Assert.IsTrue(grid.Place(V(1, 1), TileType.Road));
            Assert.IsTrue(grid.Place(V(3, 1), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 0), TileType.Road));
            Assert.IsTrue(grid.Place(V(2, 2), TileType.Road));
            Assert.IsTrue(grid.IsIntersection(V(2, 1)));
            return grid;
        }

        private static RoadQueueNetwork BuildContinuousOpposingStraightScenario(
            SimConfig cfg,
            out FakeRouteProvider routes)
        {
            var q = new RoadQueueNetwork(5, 3, cfg);
            CityGrid grid = BuildCrossIntersection();
            q.RebuildTopology(grid, new FakeDeviceState());
            routes = new FakeRouteProvider();
            routes.AddRoute(90, true, V(1, 1), V(2, 1), V(2, 2));
            routes.AddRoute(100, true, V(3, 1), V(2, 1), V(1, 1));
            Assert.IsTrue(q.TryEnqueue(V(1, 1), Dir.E, 90));
            Assert.IsTrue(q.TryEnqueue(V(3, 1), Dir.W, 100));
            q.Step(routes);
            Assert.IsTrue(q.TryLocateCar(
                90,
                out _,
                out _,
                out _,
                out float turnProgress));
            Assert.AreEqual(0.25f, turnProgress, 1e-4f);
            return q;
        }

        private static void EnqueueOpposingStraight(
            RoadQueueNetwork q,
            FakeRouteProvider routes,
            int carId)
        {
            routes.AddRoute(carId, true, V(3, 1), V(2, 1), V(1, 1));
            q.TryEnqueue(V(3, 1), Dir.W, carId);
            Assert.Greater(
                q.QueueCount(V(3, 1), Dir.W),
                0,
                "Every arbitration tick must have an opposing straight head.");
        }

        private static RoadQueueNetwork BuildContinuousCrossingStraightScenario(
            SimConfig cfg,
            out FakeRouteProvider routes)
        {
            var q = new RoadQueueNetwork(5, 3, cfg);
            CityGrid grid = BuildCrossIntersection();
            q.RebuildTopology(grid, new FakeDeviceState());
            routes = new FakeRouteProvider();
            routes.AddRoute(90, true, V(1, 1), V(2, 1), V(2, 2));
            // Southbound crosses the turn's E-entry cell (SouthWest). The previous
            // northbound route shared its N exit instead, so both could legally enter.
            routes.AddRoute(300, true, V(2, 2), V(2, 1), V(2, 0));
            Assert.IsTrue(q.TryEnqueue(V(1, 1), Dir.E, 90));
            Assert.IsTrue(q.TryEnqueue(V(2, 2), Dir.S, 300));
            q.Step(routes);
            Assert.AreEqual(90, q.CarAtHead(V(1, 1), Dir.E));
            Assert.AreEqual(300, q.CarAtHead(V(2, 1), Dir.S));
            return q;
        }

        private static void EnqueueCrossingStraight(
            RoadQueueNetwork q,
            FakeRouteProvider routes,
            int carId)
        {
            routes.AddRoute(carId, true, V(2, 2), V(2, 1), V(2, 0));
            q.TryEnqueue(V(2, 2), Dir.S, carId);
            Assert.Greater(
                q.QueueCount(V(2, 2), Dir.S),
                0,
                "Every arbitration tick must have a crossing straight head.");
        }

        private static RoadQueueNetwork BuildIntersectionRearClearanceScenario(
            out FakeRouteProvider routes)
        {
            var q = new RoadQueueNetwork(5, 3, Cfg());
            CityGrid grid = BuildCrossIntersection();
            q.RebuildTopology(grid, new FakeDeviceState());
            routes = new FakeRouteProvider();
            routes.AddRoute(30, true, V(1, 1), V(2, 1), V(3, 1));
            routes.AddRoute(31, true, V(2, 0), V(2, 1), V(2, 2));
            Assert.IsTrue(q.TryEnqueue(V(1, 1), Dir.E, 30));

            q.Step(routes);
            Assert.AreEqual(30, q.CarAtHead(V(2, 1), Dir.E));
            q.Step(routes);
            Assert.AreEqual(30, q.CarAtHead(V(3, 1), Dir.E),
                "The scenario begins immediately after the eastbound vehicle exits.");
            Assert.IsTrue(q.TryEnqueue(V(2, 0), Dir.N, 31));
            return q;
        }

        private static RoadQueueNetwork BuildFullCycle(
            SimConfig cfg,
            out FakeRouteProvider routes)
        {
            var q = new RoadQueueNetwork(2, 2, cfg);
            routes = new FakeRouteProvider();
            Vector2Int a = V(0, 0);
            Vector2Int b = V(1, 0);
            Vector2Int c = V(1, 1);
            Vector2Int d = V(0, 1);

            routes.AddRoute(0, false, a, b, c, d, a);
            routes.AddRoute(1, false, b, c, d, a, b);
            routes.AddRoute(2, false, c, d, a, b, c);
            routes.AddRoute(3, false, d, a, b, c, d);

            // 큐 키는 다음 진행 방향이 아니라 현재 타일에 들어온 방향이다.
            Assert.IsTrue(q.TryEnqueue(a, Dir.S, 0));
            Assert.IsTrue(q.TryEnqueue(b, Dir.E, 1));
            Assert.IsTrue(q.TryEnqueue(c, Dir.N, 2));
            Assert.IsTrue(q.TryEnqueue(d, Dir.W, 3));
            return q;
        }

        private static int TotalCars(RoadQueueNetwork q, int width, int height)
        {
            int total = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    total += q.QueueCount(V(x, y), Dir.N);
                    total += q.QueueCount(V(x, y), Dir.E);
                    total += q.QueueCount(V(x, y), Dir.S);
                    total += q.QueueCount(V(x, y), Dir.W);
                }
            }

            return total;
        }
    }
}
