using System;
using System.Reflection;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Content.Transit;
using CityFlow.Contracts;
using CityFlow.Sim;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CityFlow.View.Tests
{
    public sealed class CityBusDirectionalOperationTests
    {
        [Test]
        public void SingleSidedStopTiles_StartBothDirections()
        {
            SimEngine engine = CreateEngine();
            PlaceOneSidedLoop(engine);
            GameObject root = new("CityBusDirectionTest");

            try
            {
                CityBusService service = CreateService(
                    root,
                    engine);

                Assert.That(service.StartService(), Is.True);
                Assert.That(service.ActiveVehicles.Count, Is.EqualTo(2));
                Assert.That(
                    service.TryGetDirectionAvailability(
                        1,
                        out BusLineDirectionAvailability availability),
                    Is.True);
                Assert.That(
                    availability,
                    Is.EqualTo(
                        BusLineDirectionAvailability.Forward |
                        BusLineDirectionAvailability.Reverse));
                Assert.That(
                    service.ActiveVehicles[0].RouteId,
                    Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void LogicalStop_ReservesPlatformsOnBothSidesOfRoad()
        {
            SimEngine engine = CreateEngine();
            Vector2Int road = new(4, 4);
            Vector2Int roadContinuation = new(5, 4);
            Vector2Int stop = new(4, 5);
            Vector2Int oppositePlatform = new(4, 3);

            Assert.That(engine.Place(road, TileType.Road), Is.True);
            Assert.That(
                engine.Place(roadContinuation, TileType.Road),
                Is.True);
            Assert.That(engine.TryPlaceBusStop(stop), Is.True);
            Assert.That(
                engine.CanPlace(oppositePlatform, TileType.Road),
                Is.False);

            Assert.That(engine.TryRemoveBusStop(stop), Is.True);
            Assert.That(
                engine.CanPlace(oppositePlatform, TileType.Road),
                Is.True);
        }

        [Test]
        public void TwoSidedStops_StartForwardAndReverseVehicles()
        {
            SimEngine engine = CreateEngine();
            PlaceTwoSidedLoop(engine);
            GameObject root = new("CityBusDirectionTest");

            try
            {
                CityBusService service = CreateService(
                    root,
                    engine);

                Assert.That(service.StartService(), Is.True);
                Assert.That(service.ActiveVehicles.Count, Is.EqualTo(2));
                Assert.That(
                    service.TryGetDirectionAvailability(
                        1,
                        out BusLineDirectionAvailability availability),
                    Is.True);
                Assert.That(
                    availability,
                    Is.EqualTo(
                        BusLineDirectionAvailability.Forward |
                        BusLineDirectionAvailability.Reverse));

                Assert.That(
                    service.ActiveVehicles[0].RouteId,
                    Is.EqualTo(1));
                Assert.That(
                    service.ActiveVehicles[1].RouteId,
                    Is.EqualTo(1));
                Assert.That(
                    service.ActiveVehicles[0].Direction,
                    Is.Not.EqualTo(
                        service.ActiveVehicles[1].Direction));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DirectionalLoop_UsesLongerApproachInsteadOfUTurn()
        {
            SimEngine engine = CreateEngine();
            PlaceAsymmetricLoop(engine);
            GameObject root = new("CityBusDirectionTest");

            try
            {
                CityBusService service = CreateService(
                    root,
                    engine);

                Assert.That(service.StartService(), Is.True);
                Assert.That(service.ActiveVehicles.Count, Is.EqualTo(2));
                Assert.That(
                    service.ActiveVehicles[0].Route
                        .UseOppositePairedPlatformDirection,
                    Is.False);
                Assert.That(
                    service.ActiveVehicles[1].Route
                        .UseOppositePairedPlatformDirection,
                    Is.True);

                Assert.That(
                    service.ActiveVehicles[0].TryBuildLoopRoadRoute(
                        out RoadRoutePlan forward,
                        out _),
                    Is.True);
                Assert.That(
                    service.ActiveVehicles[1].TryBuildLoopRoadRoute(
                        out RoadRoutePlan reverse,
                        out _),
                    Is.True);
                AssertOppositeDirectedRoutes(forward, reverse);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BothDirections_StopAtSameLogicalStations()
        {
            SimEngine engine = CreateEngine();
            PlaceTwoSidedLoop(engine);
            GameObject root = new("CityBusDirectionTest");

            try
            {
                CityBusService service = CreateService(
                    root,
                    engine);

                Assert.That(service.StartService(), Is.True);
                Assert.That(service.ActiveVehicles.Count, Is.EqualTo(2));

                for (int index = 0;
                     index < service.ActiveVehicles.Count;
                     index++)
                {
                    BusRoute route =
                        service.ActiveVehicles[index].Route;
                    Vector2Int expectedLogicalStop = route.NextStop;
                    Vector2Int arrivedLogicalStop = default;
                    route.StopArrived += (tile, _) =>
                        arrivedLogicalStop = tile;

                    InvokePrivate(route, "UpdateMoving", 100f);

                    Assert.That(
                        route.State,
                        Is.EqualTo(BusRouteState.WaitingAtStop));
                    Assert.That(
                        arrivedLogicalStop,
                        Is.EqualTo(expectedLogicalStop));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void MultipleLines_KeepVehicleRouteIdsIsolated()
        {
            SimEngine engine = CreateEngine();
            PlaceTwoSidedLoop(engine);
            GameObject root = new("CityBusDirectionTest");

            try
            {
                CityBusService service = CreateService(
                    root,
                    engine);
                Assert.That(
                    service.BusLines.TryCreateLine(
                        2,
                        new[]
                        {
                            new Vector2Int(3, 3),
                            new Vector2Int(7, 3)
                        }),
                    Is.True);

                Assert.That(service.StartService(), Is.True);
                Assert.That(service.ActiveVehicles.Count, Is.EqualTo(4));

                int routeOneVehicles = 0;
                int routeTwoVehicles = 0;
                for (int index = 0;
                     index < service.ActiveVehicles.Count;
                     index++)
                {
                    CityBusVehicleAgent vehicle =
                        service.ActiveVehicles[index];
                    if (vehicle.RouteId == 1)
                    {
                        routeOneVehicles++;
                    }
                    else if (vehicle.RouteId == 2)
                    {
                        routeTwoVehicles++;
                    }
                }

                Assert.That(routeOneVehicles, Is.EqualTo(2));
                Assert.That(routeTwoVehicles, Is.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TerminalDeparture_ContinuesFromArrivalRoad()
        {
            SimEngine engine = CreateEngine();
            PlaceTwoSidedLoop(engine);
            GameObject root = new("CityBusDirectionTest");

            try
            {
                CityBusService service = CreateService(
                    root,
                    engine);
                Assert.That(service.StartService(), Is.True);

                BusRoute route = service.ActiveVehicles[0].Route;
                InvokePrivate(route, "UpdateMoving", 100f);
                Assert.That(
                    route.State,
                    Is.EqualTo(BusRouteState.WaitingAtStop));
                Vector2Int arrivalRoad = route.CurrentTile;

                InvokePrivate(route, "UpdateWaiting", 100f);

                Assert.That(
                    route.State,
                    Is.EqualTo(BusRouteState.Moving));
                Assert.That(route.CurrentRoadPath.Count, Is.GreaterThan(1));
                Assert.That(
                    route.CurrentRoadPath[0],
                    Is.EqualTo(arrivalRoad));
                Assert.That(
                    ManhattanDistance(
                        route.CurrentRoadPath[0],
                        route.CurrentRoadPath[1]),
                    Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static SimEngine CreateEngine()
        {
            SimConfig config = SimConfig.Default();
            config.GridWidth = 18;
            config.GridHeight = 18;
            config.AutoDetectSignals = false;
            return new SimEngine(config, new SimEventHub());
        }

        private static CityBusService CreateService(
            GameObject root,
            SimEngine engine)
        {
            BusRoute route = root.AddComponent<BusRoute>();
            BusStopRegistry registry =
                root.AddComponent<BusStopRegistry>();
            CityBusService service =
                root.AddComponent<CityBusService>();
            BusDefinitionSO definition =
                AssetDatabase.LoadAssetAtPath<BusDefinitionSO>(
                    "Assets/05_ScriptableObjects/CityFlow/Transit/CityBusDefinition.asset");
            CityBusScheduleSO schedule =
                AssetDatabase.LoadAssetAtPath<CityBusScheduleSO>(
                    "Assets/05_ScriptableObjects/CityFlow/Transit/DefaultCityBusSchedule.asset");

            Assert.That(definition, Is.Not.Null);
            Assert.That(schedule, Is.Not.Null);
            SetPrivateField(service, "definition", definition);
            SetPrivateField(service, "schedule", schedule);
            SetPrivateField(service, "busRoute", route);
            SetPrivateField(service, "stopRegistry", registry);
            SetPrivateField(service, "autoStart", false);

            var services = new CityFlowServices(
                new SimEventHub(),
                engine,
                engine);
            services.RegisterRoadRoutePlanning(engine);
            services.RegisterGameCalendar(
                new TestGameCalendar(12));
            service.Initialize(services);
            Assert.That(service.IsInitialized, Is.True);
            return service;
        }

        private static void PlaceOneSidedLoop(SimEngine engine)
        {
            PlaceRectangleRoad(engine, 2, 2, 8, 6);
            Assert.That(
                engine.TryPlaceBusStop(new Vector2Int(3, 1)),
                Is.True);
            Assert.That(
                engine.TryPlaceBusStop(new Vector2Int(7, 1)),
                Is.True);
            Assert.That(
                engine.TryPlaceBusStop(new Vector2Int(5, 7)),
                Is.True);
        }

        private static void PlaceTwoSidedLoop(SimEngine engine)
        {
            PlaceRectangleRoad(engine, 2, 2, 8, 4);
            Assert.That(
                engine.TryPlaceBusStop(new Vector2Int(3, 3)),
                Is.True);
            Assert.That(
                engine.TryPlaceBusStop(new Vector2Int(7, 3)),
                Is.True);
        }

        private static void PlaceAsymmetricLoop(SimEngine engine)
        {
            Vector2Int[] roads =
            {
                new(4, 1), new(5, 1), new(6, 1), new(7, 1),
                new(8, 1), new(9, 1), new(10, 1), new(11, 1),
                new(12, 1), new(13, 1), new(4, 2), new(11, 2),
                new(3, 3), new(4, 3), new(11, 3), new(3, 4),
                new(11, 4), new(3, 5), new(11, 5), new(3, 6),
                new(4, 6), new(11, 6), new(4, 7), new(11, 7),
                new(4, 8), new(11, 8), new(4, 9), new(11, 9),
                new(4, 10), new(8, 10), new(11, 10), new(4, 11),
                new(8, 11), new(11, 11), new(1, 12), new(2, 12),
                new(3, 12), new(4, 12), new(5, 12), new(6, 12),
                new(7, 12), new(8, 12), new(9, 12), new(10, 12),
                new(11, 12), new(4, 13), new(8, 13), new(4, 14),
                new(8, 14), new(8, 15)
            };

            for (int index = 0; index < roads.Length; index++)
            {
                Assert.That(
                    engine.Place(roads[index], TileType.Road),
                    Is.True);
            }

            Assert.That(
                engine.TryPlaceBusStop(new Vector2Int(10, 2)),
                Is.True);
            Assert.That(
                engine.TryPlaceBusStop(new Vector2Int(10, 6)),
                Is.True);
            Assert.That(
                engine.TryPlaceBusStop(new Vector2Int(5, 7)),
                Is.True);
            Assert.That(
                engine.TryPlaceBusStop(new Vector2Int(6, 11)),
                Is.True);
        }

        private static void PlaceRectangleRoad(
            SimEngine engine,
            int minX,
            int minY,
            int maxX,
            int maxY)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Assert.That(
                    engine.Place(
                        new Vector2Int(x, minY),
                        TileType.Road),
                    Is.True);
                Assert.That(
                    engine.Place(
                        new Vector2Int(x, maxY),
                        TileType.Road),
                    Is.True);
            }

            for (int y = minY + 1; y < maxY; y++)
            {
                Assert.That(
                    engine.Place(
                        new Vector2Int(minX, y),
                        TileType.Road),
                    Is.True);
                Assert.That(
                    engine.Place(
                        new Vector2Int(maxX, y),
                        TileType.Road),
                    Is.True);
            }
        }

        private static int ManhattanDistance(
            Vector2Int left,
            Vector2Int right) =>
            Mathf.Abs(left.x - right.x) +
            Mathf.Abs(left.y - right.y);

        private static void AssertOppositeDirectedRoutes(
            RoadRoutePlan forward,
            RoadRoutePlan reverse)
        {
            bool foundOppositeEdge = false;
            for (int forwardIndex = 0;
                 forwardIndex < forward.TileCount - 1;
                 forwardIndex++)
            {
                Vector2Int forwardStart =
                    forward.Tiles[forwardIndex];
                Vector2Int forwardEnd =
                    forward.Tiles[forwardIndex + 1];

                for (int reverseIndex = 0;
                     reverseIndex < reverse.TileCount - 1;
                     reverseIndex++)
                {
                    Vector2Int reverseStart =
                        reverse.Tiles[reverseIndex];
                    Vector2Int reverseEnd =
                        reverse.Tiles[reverseIndex + 1];

                    Assert.That(
                        reverseStart == forwardStart &&
                        reverseEnd == forwardEnd,
                        Is.False,
                        $"Both directions use {forwardStart} -> " +
                        $"{forwardEnd}.");

                    foundOppositeEdge |=
                        reverseStart == forwardEnd &&
                        reverseEnd == forwardStart;
                }
            }

            Assert.That(foundOppositeEdge, Is.True);
        }

        private static void SetPrivateField(
            object target,
            string fieldName,
            object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }

        private static void InvokePrivate(
            object target,
            string methodName,
            params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(target, arguments);
        }

        private sealed class TestGameCalendar :
            IGameCalendarService
        {
            public TestGameCalendar(int hour)
            {
                Hour = hour;
            }

            public int Year => 1;
            public int Month => 1;
            public int Day => 1;
            public int Hour { get; private set; }
            public int TotalMonths => 0;
            public long TotalDays => 0;
            public float RealSecondsPerGameHour => 1f;
            public float RealSecondsPerGameDay => 24f;
            public int HoursPerDay => 24;
            public float TimeOfDay01 => Hour / 24f;

            public event Action<int> HourChanged
            {
                add { }
                remove { }
            }

            public event Action<int> DayChanged
            {
                add { }
                remove { }
            }

            public event Action<int> MonthChanged
            {
                add { }
                remove { }
            }
        }

        // Unity integration: run as an EditMode test after script compilation.
    }
}
