using System;
using System.Collections.Generic;
using System.Reflection;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Content.Transit;
using CityFlow.Contracts;
using CityFlow.Sim;
using CityFlow.UI;
using CityFlow.UI.Controllers;
using CityFlow.UI.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Tests.EditMode
{
    public sealed class BusStopIntegrationGuardTests
    {
        [Test]
        public void BusStopUi_IsUnavailableWithoutRegistry()
        {
            InfrastructureDataSO busStopData =
                CreateInfrastructureData(
                    InfrastructureKind.BusStop);
            InfrastructureDataSO signalData =
                CreateInfrastructureData(
                    InfrastructureKind.Signal);
            GameObject panelObject =
                new("BuildPanel");
            GameObject infraPage =
                new("InfraPage");
            GameObject slotObject =
                new("SignalSlot");
            GameObject coordinatorObject =
                new("InfrastructurePlacementCoordinator");

            try
            {
                slotObject.transform.SetParent(
                    infraPage.transform,
                    false);
                slotObject
                    .AddComponent<InfrastructureSlotController>()
                    .Configure(signalData);

                BuildPanelController panel =
                    panelObject.AddComponent<BuildPanelController>();
                SetPrivateField(
                    panel,
                    "categoryPages",
                    new[] { infraPage });
                SetPrivateField(
                    panel,
                    "busStopData",
                    busStopData);
                InvokePrivate(panel, "EnsureBusStopSlot");

                Assert.That(
                    infraPage.transform.childCount,
                    Is.EqualTo(1));

                InfrastructurePlacementCoordinator coordinator =
                    coordinatorObject.AddComponent<
                        InfrastructurePlacementCoordinator>();
                coordinator.StartPlacement(busStopData);
                Assert.That(
                    coordinator.IsBuildingMode,
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(coordinatorObject);
                Object.DestroyImmediate(slotObject);
                Object.DestroyImmediate(infraPage);
                Object.DestroyImmediate(panelObject);
                Object.DestroyImmediate(signalData);
                Object.DestroyImmediate(busStopData);
            }
        }

        [Test]
        public void BusStopUi_IsAvailableWithRegistry()
        {
            InfrastructureDataSO busStopData =
                CreateInfrastructureData(
                    InfrastructureKind.BusStop);
            InfrastructureDataSO signalData =
                CreateInfrastructureData(
                    InfrastructureKind.Signal);
            GameObject panelObject =
                new("BuildPanel");
            GameObject infraPage =
                new("InfraPage");
            GameObject slotObject =
                new("SignalSlot");
            GameObject integrationObject = null;
            GameObject coordinatorObject =
                new("InfrastructurePlacementCoordinator");

            try
            {
                GameObject integrationPrefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/02_Prefabs/Vehicles/CityBusContent.prefab");
                Assert.That(integrationPrefab, Is.Not.Null);
                integrationObject =
                    Object.Instantiate(integrationPrefab);
                Assert.That(
                    integrationObject.GetComponent<BusStopRegistry>(),
                    Is.Not.Null);
                slotObject.transform.SetParent(
                    infraPage.transform,
                    false);
                slotObject
                    .AddComponent<InfrastructureSlotController>()
                    .Configure(signalData);

                BuildPanelController panel =
                    panelObject.AddComponent<BuildPanelController>();
                SetPrivateField(
                    panel,
                    "categoryPages",
                    new[] { infraPage });
                SetPrivateField(
                    panel,
                    "busStopData",
                    busStopData);
                InvokePrivate(panel, "EnsureBusStopSlot");

                InfrastructureSlotController[] slots =
                    infraPage.GetComponentsInChildren<
                        InfrastructureSlotController>(true);
                Assert.That(slots.Length, Is.EqualTo(2));
                Assert.That(
                    Array.Exists(
                        slots,
                        slot =>
                            slot.InfraData != null &&
                            slot.InfraData.Kind ==
                            InfrastructureKind.BusStop),
                    Is.True);

                InfrastructurePlacementCoordinator coordinator =
                    coordinatorObject.AddComponent<
                        InfrastructurePlacementCoordinator>();
                coordinator.StartPlacement(busStopData);
                Assert.That(
                    coordinator.IsBuildingMode,
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(coordinatorObject);
                Object.DestroyImmediate(integrationObject);
                Object.DestroyImmediate(slotObject);
                Object.DestroyImmediate(infraPage);
                Object.DestroyImmediate(panelObject);
                Object.DestroyImmediate(signalData);
                Object.DestroyImmediate(busStopData);
            }
        }

        [Test]
        public void BusStopPlacement_RegistryLostDuringPurchase_RollsBackTransaction()
        {
            GameObject registryObject =
                new("BusStopRegistry");
            GameObject coordinatorObject =
                new("InfrastructurePlacementCoordinator");
            InfrastructureDataSO busStopData =
                CreateInfrastructureData(
                    InfrastructureKind.BusStop);

            try
            {
                SimEventHub events = new();
                SimEngine engine =
                    new(SimConfig.Default(), events);
                Vector2Int road = new(2, 2);
                Vector2Int stop = new(2, 3);
                Assert.That(
                    engine.Place(road, TileType.Road),
                    Is.True);

                BusStopRegistry registry =
                    registryObject.AddComponent<BusStopRegistry>();
                RegistryDestroyingEconomy economy =
                    new(registryObject, 1000L);
                CityFlowServices services =
                    new(
                        events,
                        engine,
                        engine,
                        economy: economy);
                registry.Initialize(services);

                InfrastructurePlacementCoordinator coordinator =
                    coordinatorObject.AddComponent<
                        InfrastructurePlacementCoordinator>();
                coordinator.Initialize(services);
                busStopData.Cost = 150;
                coordinator.StartPlacement(busStopData);

                int infrastructureChangedCount = 0;
                events.InfrastructureChanged +=
                    _ => infrastructureChangedCount++;

                LogAssert.Expect(
                    LogType.Error,
                    "[InfrastructurePlacementCoordinator] " +
                    $"Bus-stop placement at {stop} was rolled back because " +
                    "BusStopRegistry became unavailable.");
                InvokePrivate(
                    coordinator,
                    "TryPurchaseAndPlace",
                    stop);

                Assert.That(
                    engine.BusStopTiles,
                    Is.Empty);
                Assert.That(
                    economy.Coins,
                    Is.EqualTo(1000L));
                Assert.That(
                    infrastructureChangedCount,
                    Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(coordinatorObject);
                Object.DestroyImmediate(registryObject);
                Object.DestroyImmediate(busStopData);
            }
        }

        [Test]
        public void BusStopDemolition_IsBlockedWithoutRegistry()
        {
            GameObject coordinatorObject =
                new("InfrastructurePlacementCoordinator");

            try
            {
                SimEventHub events = new();
                SimEngine engine =
                    new(SimConfig.Default(), events);
                Vector2Int road = new(2, 2);
                Vector2Int stop = new(2, 3);
                Assert.That(
                    engine.Place(road, TileType.Road),
                    Is.True);
                Assert.That(
                    engine.TryPlaceBusStop(stop),
                    Is.True);

                InfrastructurePlacementCoordinator coordinator =
                    coordinatorObject.AddComponent<
                        InfrastructurePlacementCoordinator>();
                coordinator.Initialize(
                    new CityFlowServices(
                        events,
                        engine,
                        engine));

                LogAssert.Expect(
                    LogType.Error,
                    "[InfrastructurePlacementCoordinator] " +
                    $"Cannot remove bus stop at {stop} without an active BusStopRegistry.");
                Assert.That(
                    coordinator.TryDemolishInfrastructureAt(stop),
                    Is.False);
                Assert.That(
                    engine.BusStopTiles,
                    Does.Contain(stop));
            }
            finally
            {
                Object.DestroyImmediate(coordinatorObject);
            }
        }

        [Test]
        public void BusStopDemolition_RemovesPlacementAndRegistryState()
        {
            GameObject registryObject =
                new("BusStopRegistry");
            GameObject coordinatorObject =
                new("InfrastructurePlacementCoordinator");

            try
            {
                SimEventHub events = new();
                SimEngine engine =
                    new(SimConfig.Default(), events);
                Vector2Int road = new(2, 2);
                Vector2Int stop = new(2, 3);
                Assert.That(
                    engine.Place(road, TileType.Road),
                    Is.True);
                Assert.That(
                    engine.TryPlaceBusStop(stop),
                    Is.True);

                BusStopRegistry registry =
                    registryObject.AddComponent<BusStopRegistry>();
                registry.Initialize(
                    new CityFlowServices(
                        events,
                        engine,
                        engine));
                Assert.That(
                    registry.ContainsBusStop(stop),
                    Is.True);

                InfrastructurePlacementCoordinator coordinator =
                    coordinatorObject.AddComponent<
                        InfrastructurePlacementCoordinator>();
                coordinator.Initialize(
                    new CityFlowServices(
                        events,
                        engine,
                        engine));

                Assert.That(
                    coordinator.TryDemolishInfrastructureAt(stop),
                    Is.True);
                Assert.That(
                    engine.BusStopTiles.Count,
                    Is.Zero);
                Assert.That(
                    registry.ContainsBusStop(stop),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(coordinatorObject);
                Object.DestroyImmediate(registryObject);
            }
        }

        [Test]
        public void CityBus_RemovedNextStop_IsOnlyUsedAsDepartureOnce()
        {
            GameObject busObject = new("CityBus");

            try
            {
                SimEventHub events = new();
                SimEngine engine =
                    new(SimConfig.Default(), events);
                PlaceBusLoop(engine);
                (
                    CityBusService service,
                    BusRoute route,
                    BusStopRegistry registry) =
                    CreateBusService(
                        busObject,
                        events,
                        engine);
                List<Vector2Int> servedStops = new();
                service.StopServed +=
                    (tile, _, _) => servedStops.Add(tile);

                Assert.That(service.StartService(), Is.True);
                Vector2Int removedStop = route.NextStop;
                Assert.That(
                    registry.RemoveBusStop(removedStop),
                    Is.True);

                AdvanceToNextStop(route);
                Assert.That(
                    route.CurrentStop,
                    Is.EqualTo(removedStop));
                InvokePrivate(service, "Update");

                Assert.That(
                    route.State,
                    Is.EqualTo(BusRouteState.Moving));
                Assert.That(
                    ContainsTile(route.Stops, removedStop),
                    Is.False);
                Assert.That(
                    ContainsTile(
                        service.RouteStops,
                        removedStop),
                    Is.False);

                servedStops.Clear();
                AdvanceRepeatedStops(
                    service,
                    route,
                    arrivalCount: 6);

                Assert.That(
                    ContainsTile(
                        servedStops,
                        removedStop),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(busObject);
            }
        }

        [Test]
        public void CityBus_RemovedCurrentWaitingStop_IsNotServedAgain()
        {
            GameObject busObject = new("CityBus");

            try
            {
                SimEventHub events = new();
                SimEngine engine =
                    new(SimConfig.Default(), events);
                PlaceBusLoop(engine);
                (
                    CityBusService service,
                    BusRoute route,
                    BusStopRegistry registry) =
                    CreateBusService(
                        busObject,
                        events,
                        engine);
                List<Vector2Int> servedStops = new();
                service.StopServed +=
                    (tile, _, _) => servedStops.Add(tile);

                Assert.That(service.StartService(), Is.True);
                AdvanceToNextStop(route);
                Vector2Int removedStop = route.CurrentStop;
                Assert.That(
                    route.State,
                    Is.EqualTo(BusRouteState.WaitingAtStop));
                Assert.That(
                    registry.RemoveBusStop(removedStop),
                    Is.True);

                InvokePrivate(service, "Update");

                Assert.That(
                    route.State,
                    Is.EqualTo(BusRouteState.Moving));
                Assert.That(
                    ContainsTile(route.Stops, removedStop),
                    Is.False);
                Assert.That(
                    ContainsTile(
                        service.RouteStops,
                        removedStop),
                    Is.False);

                servedStops.Clear();
                AdvanceRepeatedStops(
                    service,
                    route,
                    arrivalCount: 6);

                Assert.That(
                    ContainsTile(
                        servedStops,
                        removedStop),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(busObject);
            }
        }

        private static (
            CityBusService service,
            BusRoute route,
            BusStopRegistry registry)
            CreateBusService(
                GameObject busObject,
                SimEventHub events,
                SimEngine engine)
        {
            BusRoute route =
                busObject.AddComponent<BusRoute>();
            BusStopRegistry registry =
                busObject.AddComponent<BusStopRegistry>();
            CityBusService service =
                busObject.AddComponent<CityBusService>();
            BusDefinitionSO definition =
                AssetDatabase.LoadAssetAtPath<BusDefinitionSO>(
                    "Assets/05_ScriptableObjects/CityFlow/Transit/CityBusDefinition.asset");
            Assert.That(definition, Is.Not.Null);

            SetPrivateField(
                service,
                "definition",
                definition);
            SetPrivateField(
                service,
                "busRoute",
                route);
            SetPrivateField(
                service,
                "stopRegistry",
                registry);
            SetPrivateField(
                service,
                "autoStart",
                false);

            service.Initialize(
                new CityFlowServices(
                    events,
                    engine,
                    engine));
            Assert.That(service.IsInitialized, Is.True);
            Assert.That(registry.BusStopCount, Is.EqualTo(3));
            return (service, route, registry);
        }

        private static void PlaceBusLoop(
            SimEngine engine)
        {
            for (int x = 2; x <= 8; x++)
            {
                Assert.That(
                    engine.Place(
                        new Vector2Int(x, 2),
                        TileType.Road),
                    Is.True);
                Assert.That(
                    engine.Place(
                        new Vector2Int(x, 6),
                        TileType.Road),
                    Is.True);
            }

            for (int y = 3; y <= 5; y++)
            {
                Assert.That(
                    engine.Place(
                        new Vector2Int(2, y),
                        TileType.Road),
                    Is.True);
                Assert.That(
                    engine.Place(
                        new Vector2Int(8, y),
                        TileType.Road),
                    Is.True);
            }

            Assert.That(
                engine.TryPlaceBusStop(
                    new Vector2Int(2, 1)),
                Is.True);
            Assert.That(
                engine.TryPlaceBusStop(
                    new Vector2Int(8, 1)),
                Is.True);
            Assert.That(
                engine.TryPlaceBusStop(
                    new Vector2Int(8, 7)),
                Is.True);
        }

        private static void AdvanceToNextStop(
            BusRoute route)
        {
            InvokePrivate(
                route,
                "UpdateMoving",
                100f);
            Assert.That(
                route.State,
                Is.EqualTo(BusRouteState.WaitingAtStop));
        }

        private static void AdvanceRepeatedStops(
            CityBusService service,
            BusRoute route,
            int arrivalCount)
        {
            for (int i = 0; i < arrivalCount; i++)
            {
                Assert.That(
                    route.State,
                    Is.EqualTo(BusRouteState.Moving));
                AdvanceToNextStop(route);
                InvokePrivate(service, "Update");
                InvokePrivate(
                    route,
                    "UpdateWaiting",
                    100f);
                InvokePrivate(service, "Update");
            }
        }

        private static bool ContainsTile(
            IReadOnlyList<Vector2Int> tiles,
            Vector2Int target)
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                if (tiles[i] == target)
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class RegistryDestroyingEconomy
            : IEconomyService
        {
            private readonly GameObject registryObject;

            public RegistryDestroyingEconomy(
                GameObject registryObject,
                long coins)
            {
                this.registryObject = registryObject;
                Coins = coins;
            }

            public long Coins { get; private set; }

            public event Action<long> CoinsChanged
            {
                add { }
                remove { }
            }

            public bool TrySpend(long amount)
            {
                if (amount <= 0L || Coins < amount)
                {
                    return false;
                }

                Coins -= amount;
                Object.DestroyImmediate(registryObject);
                return true;
            }

            public void AddCoins(long amount, string reason)
            {
                Coins += amount;
            }
        }

        private static InfrastructureDataSO
            CreateInfrastructureData(
                InfrastructureKind kind)
        {
            InfrastructureDataSO data =
                ScriptableObject.CreateInstance<
                    InfrastructureDataSO>();
            data.Kind = kind;
            data.InfrastructureName = kind.ToString();
            return data;
        }

        private static void SetPrivateField(
            object target,
            string fieldName,
            object value)
        {
            FieldInfo field =
                target.GetType().GetField(
                    fieldName,
                    BindingFlags.Instance |
                    BindingFlags.NonPublic);
            Assert.That(
                field,
                Is.Not.Null,
                $"Field {fieldName} was not found.");
            field.SetValue(target, value);
        }

        private static void InvokePrivate(
            object target,
            string methodName,
            params object[] arguments)
        {
            MethodInfo method =
                target.GetType().GetMethod(
                    methodName,
                    BindingFlags.Instance |
                    BindingFlags.NonPublic);
            Assert.That(
                method,
                Is.Not.Null,
                $"Method {methodName} was not found.");
            method.Invoke(target, arguments);
        }
    }
}
