using System;
using System.Collections.Generic;
using System.Reflection;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Content.Transit;
using CityFlow.Contracts;
using CityFlow.Sim;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

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
        public void PendingStopPresentation_PreservesReverseCompletedPath()
        {
            SimEngine engine = CreateEngine();
            PlaceTwoSidedLoop(engine);
            GameObject root = new("CityBusPresentationPathTest");

            try
            {
                CityBusService service = CreateService(
                    root,
                    engine);
                Assert.That(service.StartService(), Is.True);

                CityBusVehicleAgent reverse = FindVehicle(
                    service.ActiveVehicles,
                    BusTravelDirection.Reverse);
                Assert.That(reverse, Is.Not.Null);

                BusRoute route = reverse.Route;
                route.RequireStopPresentationConfirmation = true;
                Assert.That(
                    route.CurrentRoadPath.Count,
                    Is.GreaterThan(1));

                InvokePrivate(route, "UpdateMoving", 100f);

                Assert.That(
                    route.State,
                    Is.EqualTo(BusRouteState.WaitingAtStop));
                Assert.That(
                    route.IsStopPresentationPending,
                    Is.True);
                Assert.That(
                    route.CurrentRoadPath.Count,
                    Is.GreaterThan(1));
                Assert.That(
                    route.CurrentRoadPathIndex,
                    Is.EqualTo(route.CurrentRoadPath.Count - 1));
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

        [Test]
        public void StopPresentationGate_DelaysDwellUntilViewConfirms()
        {
            SimEngine engine = CreateEngine();
            PlaceTwoSidedLoop(engine);
            GameObject root = new("CityBusStopPresentationTest");

            try
            {
                CityBusService service = CreateService(
                    root,
                    engine);
                Assert.That(service.StartService(), Is.True);
                BusRoute route =
                    service.ActiveVehicles[0].Route;
                route.RequireStopPresentationConfirmation =
                    true;
                int arrivalCount = 0;
                route.StopArrived += (_, _) =>
                    arrivalCount++;

                InvokePrivate(route, "UpdateMoving", 100f);

                Assert.That(
                    route.IsStopPresentationPending,
                    Is.True);
                Assert.That(
                    route.State,
                    Is.EqualTo(BusRouteState.WaitingAtStop));
                Assert.That(arrivalCount, Is.Zero);

                InvokePrivate(route, "UpdateWaiting", 100f);
                Assert.That(
                    route.IsStopPresentationPending,
                    Is.True);
                Assert.That(arrivalCount, Is.Zero);

                Assert.That(
                    route.ConfirmStopPresentationReached(),
                    Is.True);
                Assert.That(
                    route.IsStopPresentationPending,
                    Is.False);
                Assert.That(arrivalCount, Is.EqualTo(1));
                Assert.That(
                    route.State,
                    Is.EqualTo(BusRouteState.WaitingAtStop));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PendingStopPresentation_ReplanCompletesStopBeforeReset()
        {
            SimEngine engine = CreateEngine();
            PlaceTwoSidedLoop(engine);
            GameObject root = new("CityBusPendingReplanTest");

            try
            {
                CityBusService service = CreateService(
                    root,
                    engine);
                Assert.That(service.StartService(), Is.True);
                CityBusVehicleAgent vehicle =
                    service.ActiveVehicles[0];
                BusRoute route = vehicle.Route;
                route.RequireStopPresentationConfirmation =
                    true;
                int arrivalCount = 0;
                route.StopArrived += (_, _) =>
                    arrivalCount++;

                InvokePrivate(route, "UpdateMoving", 100f);

                Assert.That(
                    route.IsStopPresentationPending,
                    Is.True);
                Assert.That(arrivalCount, Is.Zero);
                Assert.That(
                    vehicle.TryReplanFromCurrentPosition(),
                    Is.True);
                Assert.That(
                    route.IsStopPresentationPending,
                    Is.False);
                Assert.That(
                    arrivalCount,
                    Is.EqualTo(1),
                    "Replanning must not discard a stop already reached by the simulation.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PendingStopPresentation_ViewDisableDoesNotConfirmArrival()
        {
            SimEngine engine = CreateEngine();
            PlaceTwoSidedLoop(engine);
            GameObject root = new("CityBusDisabledViewGateTest");

            try
            {
                CityBusService service = CreateService(
                    root,
                    engine);
                Assert.That(service.StartService(), Is.True);
                CityBusVehicleAgent vehicle =
                    service.ActiveVehicles[0];
                BusRoute route = vehicle.Route;
                BusWorldView view =
                    vehicle.GetComponent<BusWorldView>();
                Assert.That(view, Is.Not.Null);

                route.RequireStopPresentationConfirmation =
                    true;
                int arrivalCount = 0;
                route.StopArrived += (_, _) =>
                    arrivalCount++;
                InvokePrivate(route, "UpdateMoving", 100f);

                Assert.That(
                    route.IsStopPresentationPending,
                    Is.True);
                InvokePrivate(view, "OnDisable");
                Assert.That(
                    route.IsStopPresentationPending,
                    Is.True,
                    "Disabling only the view must preserve the pending presentation gate.");
                Assert.That(arrivalCount, Is.Zero);

                InvokePrivate(view, "OnEnable");
                Assert.That(
                    route.IsStopPresentationPending,
                    Is.True);
                Assert.That(
                    route.ConfirmStopPresentationReached(),
                    Is.True);
                Assert.That(arrivalCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void StopPresentationGate_RequiresConfirmationAtEveryStop()
        {
            SimEngine engine = CreateEngine();
            PlaceTwoSidedLoop(engine);
            GameObject root = new("CityBusRepeatedStopPresentationTest");

            try
            {
                CityBusService service = CreateService(
                    root,
                    engine);
                Assert.That(service.StartService(), Is.True);
                BusRoute route =
                    service.ActiveVehicles[0].Route;
                route.RequireStopPresentationConfirmation =
                    true;
                int requestCount = 0;
                int arrivalCount = 0;
                route.StopPresentationRequested += (_, _) =>
                    requestCount++;
                route.StopArrived += (_, _) =>
                    arrivalCount++;

                for (int stop = 0; stop < 3; stop++)
                {
                    InvokePrivate(route, "UpdateMoving", 100f);

                    Assert.That(
                        route.IsStopPresentationPending,
                        Is.True);
                    Assert.That(
                        requestCount,
                        Is.EqualTo(stop + 1));
                    Assert.That(
                        arrivalCount,
                        Is.EqualTo(stop));

                    Assert.That(
                        route.ConfirmStopPresentationReached(),
                        Is.True);
                    Assert.That(
                        route.IsStopPresentationPending,
                        Is.False);
                    Assert.That(
                        arrivalCount,
                        Is.EqualTo(stop + 1));

                    InvokePrivate(route, "UpdateWaiting", 100f);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DirectionalVehicles_OrderThreeStopsByRoadDirection()
        {
            SimEngine engine = CreateEngine();
            PlaceThreeStopLoopOutOfCoordinateOrder(engine);
            GameObject root = new("CityBusRoadStopOrderTest");

            try
            {
                CityBusService service = CreateService(
                    root,
                    engine);
                Assert.That(service.StartService(), Is.True);
                Assert.That(
                    service.ActiveVehicles.Count,
                    Is.EqualTo(2));

                CityBusVehicleAgent forward =
                    FindVehicle(
                        service.ActiveVehicles,
                        BusTravelDirection.Forward);
                CityBusVehicleAgent reverse =
                    FindVehicle(
                        service.ActiveVehicles,
                        BusTravelDirection.Reverse);
                Assert.That(forward, Is.Not.Null);
                Assert.That(reverse, Is.Not.Null);
                Assert.That(
                    forward.Route.Stops,
                    Is.EqualTo(new[]
                    {
                        new Vector2Int(7, 1),
                        new Vector2Int(7, 7),
                        new Vector2Int(3, 7)
                    }));
                Assert.That(
                    reverse.Route.Stops,
                    Is.EqualTo(new[]
                    {
                        new Vector2Int(7, 7),
                        new Vector2Int(7, 1),
                        new Vector2Int(3, 7)
                    }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SavedThreeStopAsymmetricLayout_StartsBothDirections()
        {
            SimEngine engine = CreateEngine();
            PlaceSavedThreeStopAsymmetricLoop(engine);
            GameObject root = new("CityBusSavedThreeStopTest");

            try
            {
                CityBusService service = CreateService(
                    root,
                    engine);

                Assert.That(service.StartService(), Is.True);
                Assert.That(
                    service.ActiveVehicles.Count,
                    Is.EqualTo(2));
                Assert.That(
                    FindVehicle(
                        service.ActiveVehicles,
                        BusTravelDirection.Forward),
                    Is.Not.Null);
                Assert.That(
                    FindVehicle(
                        service.ActiveVehicles,
                        BusTravelDirection.Reverse),
                    Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RebuiltStopLayout_SkipsUnreachableStopAndStartsBuses()
        {
            SimEngine engine = CreateEngine();
            PlaceRebuiltFourStopAsymmetricLoop(engine);
            GameObject root = new("CityBusRebuiltStopTest");

            try
            {
                CityBusService service = CreateService(
                    root,
                    engine);

                LogAssert.Expect(
                    LogType.Warning,
                    new System.Text.RegularExpressions.Regex(
                        "skipped 1 unreachable stop"));
                LogAssert.Expect(
                    LogType.Warning,
                    new System.Text.RegularExpressions.Regex(
                        "skipped 1 unreachable stop"));

                Assert.That(service.StartService(), Is.True);
                Assert.That(
                    service.ActiveVehicles.Count,
                    Is.EqualTo(2));

                for (int index = 0;
                     index < service.ActiveVehicles.Count;
                     index++)
                {
                    IReadOnlyList<Vector2Int> stops =
                        service.ActiveVehicles[index].Route.Stops;
                    Assert.That(stops.Count, Is.EqualTo(3));
                    Assert.That(
                        ContainsTile(
                            stops,
                            new Vector2Int(6, 13)),
                        Is.False);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RoundaboutLoop_StartsForwardAndReverseVehicles()
        {
            SimEngine engine = CreateEngine();
            PlaceSavedRoundaboutLoop(engine);
            GameObject root = new("CityBusRoundaboutDirectionTest");

            try
            {
                CityBusService service = CreateService(
                    root,
                    engine);

                Assert.That(service.StartService(), Is.True);
                Assert.That(
                    service.ActiveVehicles.Count,
                    Is.EqualTo(2));
                Assert.That(
                    FindVehicle(
                        service.ActiveVehicles,
                        BusTravelDirection.Forward),
                    Is.Not.Null);
                Assert.That(
                    FindVehicle(
                        service.ActiveVehicles,
                        BusTravelDirection.Reverse),
                    Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RoadRemovedAndRestored_RefreshesServiceImmediately()
        {
            var eventHub = new SimEventHub();
            SimEngine engine = CreateEngine(eventHub);
            PlaceTwoSidedLoop(engine);
            GameObject root = new("CityBusRoadRefreshTest");

            try
            {
                CityBusService service = CreateService(
                    root,
                    engine,
                    eventHub: eventHub);
                Assert.That(service.StartService(), Is.True);
                Assert.That(
                    service.ActiveVehicles.Count,
                    Is.EqualTo(2));

                Vector2Int removedRoad = new(5, 2);
                Assert.That(engine.Remove(removedRoad), Is.True);
                eventHub.Publish(
                    new PlacedEvent(
                        removedRoad,
                        TileType.Road,
                        true));

                Assert.That(service.ActiveVehicles, Is.Empty);
                Assert.That(
                    service.IsVehicleVisible,
                    Is.False);

                Assert.That(
                    engine.Place(removedRoad, TileType.Road),
                    Is.True);
                eventHub.Publish(
                    new PlacedEvent(
                        removedRoad,
                        TileType.Road,
                        false));

                Assert.That(
                    service.ActiveVehicles.Count,
                    Is.EqualTo(2));
                Assert.That(
                    service.IsVehicleVisible,
                    Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RoundaboutLoop_RestoredRoad_RecreatesVisibleDirections()
        {
            var eventHub = new SimEventHub();
            SimEngine engine = CreateEngine(eventHub);
            PlaceSavedRoundaboutLoop(engine);
            GameObject root = new("CityBusRoundaboutRestoreTest");

            try
            {
                CityBusService service = CreateService(
                    root,
                    engine,
                    eventHub: eventHub,
                    registerRoadTraffic: true);
                Assert.That(service.StartService(), Is.True);
                AdvanceUntilVisibleDirections(engine, service);

                Vector2Int removedRoad = new(11, 5);
                Assert.That(engine.Remove(removedRoad), Is.True);
                LogAssert.Expect(
                    LogType.Warning,
                    new System.Text.RegularExpressions.Regex(
                        "Route 1 Forward was not started: " +
                        "loop route could not be built"));
                LogAssert.Expect(
                    LogType.Warning,
                    new System.Text.RegularExpressions.Regex(
                        "Route 1 Reverse was not started: " +
                        "loop route could not be built"));
                engine.Tick(engine.TickInterval);

                Assert.That(service.ActiveVehicles, Is.Empty);
                Assert.That(service.IsVehicleVisible, Is.False);

                Assert.That(
                    engine.Place(removedRoad, TileType.Road),
                    Is.True);
                engine.Tick(engine.TickInterval);
                Assert.That(
                    service.ActiveVehicles.Count,
                    Is.EqualTo(2));

                AdvanceUntilVisibleDirections(engine, service);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ServiceEnd_RetiresEachBusAtItsNextStop()
        {
            SimEngine engine = CreateEngine();
            PlaceTwoSidedLoop(engine);
            GameObject root = new("CityBusServiceEndTest");

            try
            {
                var calendar = new TestGameCalendar(12);
                CityBusService service = CreateService(
                    root,
                    engine,
                    calendar);
                Assert.That(service.StartService(), Is.True);
                SetPrivateField(service, "autoStart", true);
                var vehicles =
                    new List<CityBusVehicleAgent>(
                        service.ActiveVehicles);
                for (int index = 0;
                     index < vehicles.Count;
                     index++)
                {
                    vehicles[index].Route
                        .RequireStopPresentationConfirmation =
                        true;
                }
                int stoppedEventCount = 0;
                service.ServiceStopped += () =>
                    stoppedEventCount++;

                calendar.SetHour(22);
                Assert.That(
                    service.IsStoppingAtNextStop,
                    Is.True);
                Assert.That(
                    service.IsVehicleVisible,
                    Is.True);
                Assert.That(
                    service.ActiveVehicles.Count,
                    Is.EqualTo(2));

                for (int index = 0;
                     index < vehicles.Count;
                     index++)
                {
                    CityBusVehicleAgent vehicle =
                        vehicles[index];
                    InvokePrivate(
                        vehicle.Route,
                        "UpdateMoving",
                        100f);

                    Assert.That(
                        vehicle.Route.State,
                        Is.EqualTo(
                            BusRouteState.WaitingAtStop));
                    Assert.That(
                        vehicle.Route
                            .IsStopPresentationPending,
                        Is.True);

                    InvokePrivate(service, "Update");

                    Assert.That(
                        ContainsVehicle(
                            service.ActiveVehicles,
                            vehicle),
                        Is.True);

                    Assert.That(
                        vehicle.Route
                            .ConfirmStopPresentationReached(),
                        Is.True);
                    InvokePrivate(service, "Update");

                    Assert.That(
                        ContainsVehicle(
                            service.ActiveVehicles,
                            vehicle),
                        Is.False);
                }

                Assert.That(
                    service.IsStoppingAtNextStop,
                    Is.False);
                Assert.That(
                    service.IsVehicleVisible,
                    Is.False);
                Assert.That(
                    service.ActiveVehicles,
                    Is.Empty);
                Assert.That(stoppedEventCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static SimEngine CreateEngine(
            SimEventHub eventHub = null)
        {
            SimConfig config = SimConfig.Default();
            config.GridWidth = 18;
            config.GridHeight = 18;
            config.AutoDetectSignals = false;
            return new SimEngine(
                config,
                eventHub ?? new SimEventHub());
        }

        private static CityBusService CreateService(
            GameObject root,
            SimEngine engine,
            TestGameCalendar calendar = null,
            SimEventHub eventHub = null,
            bool registerRoadTraffic = false)
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
                eventHub ?? new SimEventHub(),
                engine,
                engine);
            services.RegisterRoadRoutePlanning(engine);
            if (registerRoadTraffic)
            {
                services.RegisterRoadTraffic(engine.RoadTraffic);
            }

            services.RegisterGameCalendar(
                calendar ?? new TestGameCalendar(12));
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
            PlaceAsymmetricRoads(engine);

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

        private static void PlaceSavedThreeStopAsymmetricLoop(
            SimEngine engine)
        {
            PlaceAsymmetricRoads(engine);

            Assert.That(
                engine.TryPlaceBusStop(new Vector2Int(5, 2)),
                Is.True);
            Assert.That(
                engine.TryPlaceBusStop(new Vector2Int(10, 6)),
                Is.True);
            Assert.That(
                engine.TryPlaceBusStop(new Vector2Int(6, 11)),
                Is.True);
        }

        private static void PlaceRebuiltFourStopAsymmetricLoop(
            SimEngine engine)
        {
            PlaceAsymmetricRoads(engine);

            Assert.That(
                engine.TryPlaceBusStop(new Vector2Int(7, 2)),
                Is.True);
            Assert.That(
                engine.TryPlaceBusStop(new Vector2Int(5, 7)),
                Is.True);
            Assert.That(
                engine.TryPlaceBusStop(new Vector2Int(10, 7)),
                Is.True);
            Assert.That(
                engine.TryPlaceBusStop(new Vector2Int(6, 13)),
                Is.True);
        }

        private static void PlaceSavedRoundaboutLoop(
            SimEngine engine)
        {
            PlaceAsymmetricRoads(engine);

            Assert.That(
                engine.TryPlaceBusStop(new Vector2Int(7, 2)),
                Is.True);
            Assert.That(
                engine.TryPlaceBusStop(new Vector2Int(10, 7)),
                Is.True);
            Assert.That(
                engine.TryPlaceBusStop(new Vector2Int(5, 8)),
                Is.True);
            Assert.That(
                engine.TryPlaceBusStop(new Vector2Int(6, 11)),
                Is.True);
            Assert.That(
                engine.TryPlaceRoundabout(new Vector2Int(4, 12)),
                Is.True);
        }

        private static void PlaceAsymmetricRoads(SimEngine engine)
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
        }

        private static void PlaceThreeStopLoopOutOfCoordinateOrder(
            SimEngine engine)
        {
            PlaceRectangleRoad(engine, 2, 2, 8, 6);
            Assert.That(
                engine.TryPlaceBusStop(new Vector2Int(7, 1)),
                Is.True);
            Assert.That(
                engine.TryPlaceBusStop(new Vector2Int(3, 7)),
                Is.True);
            Assert.That(
                engine.TryPlaceBusStop(new Vector2Int(7, 7)),
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

        private static void AdvanceUntilVisibleDirections(
            SimEngine engine,
            CityBusService service)
        {
            const int maxTicks = 32;
            for (int tick = 0; tick < maxTicks; tick++)
            {
                engine.Tick(engine.TickInterval);
                if (AreDirectionsVisible(service))
                {
                    return;
                }
            }

            AssertVisibleDirections(service);
        }

        private static bool AreDirectionsVisible(
            CityBusService service)
        {
            if (service.ActiveVehicles.Count != 2)
            {
                return false;
            }

            for (int directionIndex = 0;
                 directionIndex < 2;
                 directionIndex++)
            {
                BusTravelDirection direction =
                    directionIndex == 0
                        ? BusTravelDirection.Forward
                        : BusTravelDirection.Reverse;
                CityBusVehicleAgent vehicle = FindVehicle(
                    service.ActiveVehicles,
                    direction);

                if (vehicle == null ||
                    !vehicle.Route.TryGetRoadTrafficSnapshot(
                        out RoadTrafficSnapshot snapshot) ||
                    !snapshot.IsVisible ||
                    snapshot.State ==
                        RoadTrafficAgentState.RouteUnavailable)
                {
                    return false;
                }
            }

            return true;
        }

        private static void AssertVisibleDirections(
            CityBusService service)
        {
            Assert.That(
                service.ActiveVehicles.Count,
                Is.EqualTo(2));

            for (int directionIndex = 0;
                 directionIndex < 2;
                 directionIndex++)
            {
                BusTravelDirection direction =
                    directionIndex == 0
                        ? BusTravelDirection.Forward
                        : BusTravelDirection.Reverse;
                CityBusVehicleAgent vehicle = FindVehicle(
                    service.ActiveVehicles,
                    direction);

                Assert.That(vehicle, Is.Not.Null);
                Assert.That(
                    vehicle.Route.TryGetRoadTrafficSnapshot(
                        out RoadTrafficSnapshot snapshot),
                    Is.True);
                Assert.That(
                    snapshot.State,
                    Is.Not.EqualTo(
                        RoadTrafficAgentState.RouteUnavailable));
                Assert.That(snapshot.IsVisible, Is.True);
            }
        }

        private static bool ContainsVehicle(
            IReadOnlyList<CityBusVehicleAgent> vehicles,
            CityBusVehicleAgent expected)
        {
            for (int index = 0;
                 index < vehicles.Count;
                 index++)
            {
                if (ReferenceEquals(
                        vehicles[index],
                        expected))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsTile(
            IReadOnlyList<Vector2Int> tiles,
            Vector2Int expected)
        {
            for (int index = 0; index < tiles.Count; index++)
            {
                if (tiles[index] == expected)
                {
                    return true;
                }
            }

            return false;
        }

        private static CityBusVehicleAgent FindVehicle(
            IReadOnlyList<CityBusVehicleAgent> vehicles,
            BusTravelDirection direction)
        {
            for (int index = 0;
                 index < vehicles.Count;
                 index++)
            {
                if (vehicles[index] != null &&
                    vehicles[index].Direction == direction)
                {
                    return vehicles[index];
                }
            }

            return null;
        }

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

            public event Action<int> HourChanged;

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

            public void SetHour(int hour)
            {
                Hour = Mathf.Clamp(hour, 0, 23);
                HourChanged?.Invoke(Hour);
            }
        }

        // Unity integration: run as an EditMode test after script compilation.
    }
}
