using System;
using System.Collections.Generic;
using System.Reflection;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Content.Transit;
using CityFlow.Contracts;
using CityFlow.DebugTools;
using CityFlow.Sim;
using CityFlow.UI;
using CityFlow.UI.Controllers;
using CityFlow.View;
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
        public void BuildMenu_OpensWithoutSelectingDefaultRoadPiece()
        {
            GameObject controllerObject =
                new("PlacementController");
            GameObject dockObject = new("UIDockController");
            GameObject buildPanel = new("BuildPanel");
            try
            {
                PlacementController placementController =
                    controllerObject.AddComponent<PlacementController>();
                placementController.ToggleBuildMode(true);

                UIDockController dock =
                    dockObject.AddComponent<UIDockController>();
                SetPrivateField(
                    dock,
                    "panelBuild",
                    buildPanel);
                SetPrivateField(
                    dock,
                    "placementController",
                    placementController);

                dock.ToggleMenu(UIDockController.MenuType.Build);

                Assert.That(buildPanel.activeSelf, Is.True);
                Assert.That(
                    placementController.IsBuildingMode,
                    Is.False,
                    "Opening Build must not auto-select the default road piece.");
            }
            finally
            {
                Object.DestroyImmediate(buildPanel);
                Object.DestroyImmediate(dockObject);
                Object.DestroyImmediate(controllerObject);
            }
        }

        [Test]
        public void CityBusPresentation_AlignsGroundContactToRoadSurface()
        {
            const string contentPath =
                "Assets/02_Prefabs/Vehicles/CityBusContent.prefab";
            GameObject contentPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(contentPath);
            Assert.That(contentPrefab, Is.Not.Null);

            GameObject cityObject = new("MainCityView");
            GameObject contentObject = null;
            try
            {
                MainCityView cityView =
                    cityObject.AddComponent<MainCityView>();
                contentObject = Object.Instantiate(contentPrefab);
                BusWorldView worldView =
                    contentObject.GetComponent<BusWorldView>();
                Assert.That(worldView, Is.Not.Null);

                SetPrivateField(worldView, "cityView", cityView);
                InvokePrivate(worldView, "EnsureVisual");

                float depth = (float)typeof(BusWorldView)
                    .GetMethod(
                        "GetVisualSurfaceDepth",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(worldView, null);

                Assert.That(
                    depth,
                    Is.EqualTo(cityView.VehicleGroundZ).Within(0.0001f));
            }
            finally
            {
                if (contentObject != null)
                {
                    Object.DestroyImmediate(contentObject);
                }

                Object.DestroyImmediate(cityObject);
            }
        }

        [Test]
        public void CityBusPresentation_UsesProjectOwnedVisualMaterials()
        {
            const string contentPath =
                "Assets/02_Prefabs/Vehicles/CityBusContent.prefab";
            const string visualPath =
                "Assets/02_Prefabs/Vehicles/CityBus_Blue.prefab";
            const string definitionPath =
                "Assets/05_ScriptableObjects/CityFlow/Transit/" +
                "CityBusDefinition.asset";

            GameObject content =
                AssetDatabase.LoadAssetAtPath<GameObject>(contentPath);
            GameObject expectedVisual =
                AssetDatabase.LoadAssetAtPath<GameObject>(visualPath);
            BusDefinitionSO definition =
                AssetDatabase.LoadAssetAtPath<BusDefinitionSO>(
                    definitionPath);

            Assert.That(content, Is.Not.Null);
            Assert.That(expectedVisual, Is.Not.Null);
            Assert.That(definition, Is.Not.Null);

            BusWorldView worldView =
                content.GetComponent<BusWorldView>();
            Assert.That(worldView, Is.Not.Null);

            SerializedObject serializedView =
                new(worldView);
            Assert.That(
                serializedView
                    .FindProperty("busVisualPrefab")
                    .objectReferenceValue,
                Is.SameAs(expectedVisual));
            Assert.That(
                serializedView
                    .FindProperty("busMaterial")
                    .objectReferenceValue,
                Is.Null,
                "The city-bus wrapper must keep its authored materials.");
            Assert.That(
                serializedView
                    .FindProperty("visualScale")
                    .floatValue,
                Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(
                definition.VehicleVisualPrefab,
                Is.SameAs(expectedVisual));
        }

        [Test]
        public void SchoolBusPresentation_UsesAuthoredPackCarsVisualAndMaterials()
        {
            const string contentPath =
                "Assets/02_Prefabs/Vehicles/SchoolBusContent.prefab";
            const string visualPath =
                "Assets/02_Prefabs/Vehicles/SchoolBusVisual.prefab";

            GameObject content =
                AssetDatabase.LoadAssetAtPath<GameObject>(contentPath);
            GameObject expectedVisual =
                AssetDatabase.LoadAssetAtPath<GameObject>(visualPath);

            Assert.That(content, Is.Not.Null);
            Assert.That(expectedVisual, Is.Not.Null);

            BusWorldView worldView =
                content.GetComponent<BusWorldView>();
            Assert.That(worldView, Is.Not.Null);

            SerializedObject serializedView = new(worldView);
            Assert.That(
                serializedView
                    .FindProperty("busVisualPrefab")
                    .objectReferenceValue,
                Is.SameAs(expectedVisual));
            Assert.That(
                serializedView
                    .FindProperty("busMaterial")
                    .objectReferenceValue,
                Is.Null,
                "The school bus must keep the authored Pack Cars materials.");
            Assert.That(
                serializedView
                    .FindProperty("visualScale")
                    .floatValue,
                Is.EqualTo(0.76f).Within(0.0001f),
                "The normalized school-bus visual must remain twice the " +
                "0.38-tile normal-vehicle length.");
        }

        [Test]
        public void VisualCatalogs_ReferenceProjectOwnedPrefabCopies()
        {
            BuildingVisualCatalogSO buildingCatalog =
                AssetDatabase.LoadAssetAtPath<BuildingVisualCatalogSO>(
                    "Assets/05_ScriptableObjects/Resources/CityFlow/" +
                    "BuildingVisualCatalog.asset");
            VehicleVisualCatalogSO vehicleCatalog =
                AssetDatabase.LoadAssetAtPath<VehicleVisualCatalogSO>(
                    "Assets/05_ScriptableObjects/Resources/CityFlow/" +
                    "VehicleVisualCatalog.asset");

            Assert.That(buildingCatalog, Is.Not.Null);
            Assert.That(vehicleCatalog, Is.Not.Null);

            AssertProjectOwnedPrefab(buildingCatalog.HousePrefab);
            AssertProjectOwnedPrefab(buildingCatalog.OfficePrefab);
            AssertProjectOwnedPrefab(buildingCatalog.SchoolPrefab);
            Assert.That(
                AssetDatabase.GetAssetPath(buildingCatalog.SchoolPrefab),
                Is.EqualTo(
                    "Assets/02_Prefabs/Buildings/SchoolVisual_StudioHorizon.prefab"),
                "The building catalog must use the Studio Horizon school visual selected for the school.");
            MeshFilter schoolMesh =
                buildingCatalog.SchoolPrefab
                    .GetComponentInChildren<MeshFilter>(true);
            Assert.That(schoolMesh, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(schoolMesh.sharedMesh),
                Is.EqualTo(
                    "Assets/02_Prefabs/Buildings/" +
                    "School_Right_StudioHorizon.obj"),
                "The project-owned school wrapper must use only the selected front Studio Horizon school building.");
            MeshRenderer schoolRenderer =
                buildingCatalog.SchoolPrefab
                    .GetComponentInChildren<MeshRenderer>(true);
            Assert.That(schoolRenderer, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(schoolRenderer.sharedMaterial),
                Is.EqualTo(
                    "Assets/99_Download/Studio Horizon/" +
                    "Simple Building Generic Free/Materials/" +
                    "Simple Building 01.mat"),
                "The extracted school building must keep the authored atlas material and textures.");
            AssertProjectOwnedPrefab(buildingCatalog.HospitalPrefab);
            AssertProjectOwnedPrefab(buildingCatalog.FoundationPrefab);

            foreach (GameObject prefab in
                     vehicleCatalog.NormalVehiclePrefabs)
            {
                AssertProjectOwnedPrefab(prefab);
            }

            AssertProjectOwnedPrefab(vehicleCatalog.SchoolBusPrefab);
            AssertProjectOwnedPrefab(vehicleCatalog.AmbulancePrefab);
            foreach (GameObject prefab in vehicleCatalog.CityBusPrefabs)
            {
                AssertProjectOwnedPrefab(prefab);
            }
        }

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
        public void BusStopPlacement_SuccessEndsInfrastructurePlacementMode()
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
                Assert.That(
                    engine.Place(new Vector2Int(3, 2), TileType.Road),
                    Is.True);

                BusStopRegistry registry =
                    registryObject.AddComponent<BusStopRegistry>();
                CityFlowServices services =
                    new(
                        events,
                        engine,
                        engine);
                registry.Initialize(services);

                InfrastructurePlacementCoordinator coordinator =
                    coordinatorObject.AddComponent<
                        InfrastructurePlacementCoordinator>();
                coordinator.Initialize(services);
                coordinator.StartPlacement(busStopData);

                InvokePrivate(
                    coordinator,
                    "TryPurchaseAndPlace",
                    stop);

                Assert.That(
                    engine.BusStopTiles,
                    Does.Contain(stop));
                Assert.That(
                    coordinator.IsBuildingMode,
                    Is.False,
                    "A successful bus-stop purchase must not consume the next right click just to cancel placement mode.");
            }
            finally
            {
                Object.DestroyImmediate(coordinatorObject);
                Object.DestroyImmediate(registryObject);
                Object.DestroyImmediate(busStopData);
            }
        }

        [TestCase(InfrastructureKind.Signal)]
        [TestCase(InfrastructureKind.Roundabout)]
        [TestCase(InfrastructureKind.Overpass)]
        [TestCase(InfrastructureKind.Oneway)]
        [TestCase(InfrastructureKind.TurnRestriction)]
        [TestCase(InfrastructureKind.PriorityRoad)]
        [TestCase(InfrastructureKind.Highway)]
        public void InfrastructurePlacement_SuccessCompletionEndsMode(
            InfrastructureKind kind)
        {
            GameObject coordinatorObject =
                new("InfrastructurePlacementCoordinator");
            InfrastructureDataSO data = CreateInfrastructureData(kind);

            try
            {
                InfrastructurePlacementCoordinator coordinator =
                    coordinatorObject.AddComponent<
                        InfrastructurePlacementCoordinator>();
                coordinator.StartPlacement(data);

                InvokePrivate(coordinator, "CompletePlacement");

                Assert.That(
                    coordinator.IsBuildingMode,
                    Is.False,
                    $"Successful {kind} placement must clear its selection.");
            }
            finally
            {
                Object.DestroyImmediate(coordinatorObject);
                Object.DestroyImmediate(data);
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
                    engine.Place(new Vector2Int(3, 2), TileType.Road),
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

        [TestCase(false)]
        [TestCase(true)]
        public void BusStopDemolition_RemovesPlacementAndRegistryState(
            bool demolishOppositePlatform)
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
                    engine.Place(new Vector2Int(3, 2), TileType.Road),
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
                    coordinator.TryDemolishInfrastructureAt(
                        demolishOppositePlatform
                            ? new Vector2Int(2, 1)
                            : stop),
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
        public void BusStopDemolition_UsesInstalledPlatformPairAfterRoadChanges()
        {
            SimEventHub events = new();
            SimEngine engine = new(SimConfig.Default(), events);
            Vector2Int stop = new(2, 3);
            Vector2Int originalOppositePlatform = new(2, 1);

            Assert.That(
                engine.Place(new Vector2Int(2, 2), TileType.Road),
                Is.True);
            Assert.That(
                engine.Place(new Vector2Int(3, 2), TileType.Road),
                Is.True);
            Assert.That(engine.TryPlaceBusStop(stop), Is.True);

            Assert.That(
                engine.Place(new Vector2Int(2, 4), TileType.Road),
                Is.True);
            Assert.That(
                engine.Place(new Vector2Int(3, 4), TileType.Road),
                Is.True);

            Assert.That(
                engine.TryRemoveBusStop(originalOppositePlatform),
                Is.True,
                "Either platform must remove the pair chosen at installation time.");
            Assert.That(engine.BusStopTiles, Is.Empty);
            Assert.That(
                engine.Place(originalOppositePlatform, TileType.Road),
                Is.True,
                "Demolition must release the originally reserved opposite platform.");
        }

        [Test]
        public void BusStopSave_RestoresInstalledPlatformPairAfterRoadChanges()
        {
            SimEventHub events = new();
            SimEngine source = new(SimConfig.Default(), events);
            Vector2Int stop = new(2, 3);
            Vector2Int originalOppositePlatform = new(2, 1);

            Assert.That(
                source.Place(new Vector2Int(2, 2), TileType.Road),
                Is.True);
            Assert.That(
                source.Place(new Vector2Int(3, 2), TileType.Road),
                Is.True);
            Assert.That(source.TryPlaceBusStop(stop), Is.True);
            Assert.That(
                source.Place(new Vector2Int(2, 4), TileType.Road),
                Is.True);
            Assert.That(
                source.Place(new Vector2Int(3, 4), TileType.Road),
                Is.True);

            var snapshot = source.CreateSnapshot();
            Assert.That(snapshot.BusStops, Has.Length.EqualTo(1));
            Assert.That(snapshot.BusStops[0].HasOppositePlatform, Is.True);
            Assert.That(snapshot.BusStops[0].OppositeX, Is.EqualTo(2));
            Assert.That(snapshot.BusStops[0].OppositeY, Is.EqualTo(1));

            SimEngine restored = new(SimConfig.Default(), new SimEventHub());
            restored.RestoreSnapshot(snapshot);

            Assert.That(
                restored.TryRemoveBusStop(originalOppositePlatform),
                Is.True,
                "Save restore must preserve the pair chosen at installation time.");
        }

        [Test]
        public void InfrastructureChanged_SynchronizesEveryBusStopRegistry()
        {
            GameObject firstObject =
                new("BusStopRegistry A");
            GameObject secondObject =
                new("BusStopRegistry B");

            try
            {
                SimEventHub events = new();
                SimEngine engine =
                    new(SimConfig.Default(), events);
                CityFlowServices services =
                    new(
                        events,
                        engine,
                        engine);
                Assert.That(
                    engine.Place(
                        new Vector2Int(2, 2),
                        TileType.Road),
                    Is.True);
                Assert.That(
                    engine.Place(
                        new Vector2Int(3, 2),
                        TileType.Road),
                    Is.True);

                BusStopRegistry first =
                    firstObject.AddComponent<
                        BusStopRegistry>();
                BusStopRegistry second =
                    secondObject.AddComponent<
                        BusStopRegistry>();
                first.Initialize(services);
                second.Initialize(services);

                Vector2Int stop =
                    new(2, 3);
                Assert.That(
                    engine.TryPlaceBusStop(stop),
                    Is.True);
                events.Publish(
                    new InfrastructureChangedEvent(
                        stop,
                        false));

                Assert.That(
                    first.ContainsBusStop(stop),
                    Is.True);
                Assert.That(
                    second.ContainsBusStop(stop),
                    Is.True,
                    "The city bus must not depend on which duplicate registry the UI found first.");
            }
            finally
            {
                Object.DestroyImmediate(
                    secondObject);
                Object.DestroyImmediate(
                    firstObject);
            }
        }

        [Test]
        public void CityBus_StopArrival_GrantsConfiguredRevenueOnlyOnce()
        {
            GameObject busObject = new("CityBus");

            try
            {
                SimEventHub events = new();
                SimEngine engine =
                    new(SimConfig.Default(), events);
                PlaceBusLoop(engine);
                RecordingEconomy economy = new();
                (
                    CityBusService service,
                    BusRoute route,
                    BusStopRegistry registry) =
                    CreateBusService(
                        busObject,
                        events,
                        engine,
                        economy);
                BusDefinitionSO definition =
                    AssetDatabase.LoadAssetAtPath<
                        BusDefinitionSO>(
                        "Assets/05_ScriptableObjects/CityFlow/Transit/CityBusDefinition.asset");

                Assert.That(registry, Is.Not.Null);
                Assert.That(definition, Is.Not.Null);
                Assert.That(
                    definition.StopRevenueCoins,
                    Is.GreaterThan(0));
                Assert.That(service.StartService(), Is.True);

                AdvanceToNextStop(route);

                Assert.That(economy.AddCalls, Is.EqualTo(1));
                Assert.That(
                    economy.Coins,
                    Is.EqualTo(
                        definition.StopRevenueCoins));
                Assert.That(
                    economy.LastReason,
                    Is.EqualTo("city bus stop"));

                InvokePrivate(service, "Update");

                Assert.That(
                    economy.AddCalls,
                    Is.EqualTo(1),
                    "Waiting at the same stop must not grant revenue again.");

                InvokePrivate(
                    route,
                    "UpdateWaiting",
                    100f);
                InvokePrivate(service, "Update");
                AdvanceToNextStop(route);

                Assert.That(economy.AddCalls, Is.EqualTo(2));
                Assert.That(
                    economy.Coins,
                    Is.EqualTo(
                        definition.StopRevenueCoins * 2L));
            }
            finally
            {
                Object.DestroyImmediate(busObject);
            }
        }

        [Test]
        public void CityBus_OppositeSidePassByStop_IsNotServed()
        {
            GameObject busObject = new("CityBus");

            try
            {
                SimEventHub events = new();
                SimEngine engine =
                    new(SimConfig.Default(), events);
                PlaceDirectionalBusLoop(engine);
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

                Vector2Int scheduledStop =
                    new(7, 1);
                Vector2Int oppositeSideStop =
                    new(5, 3);

                Assert.That(registry.BusStopCount, Is.EqualTo(3));
                Assert.That(service.StartService(), Is.True);
                AdvanceToNextStop(route);

                Assert.That(
                    route.CurrentStop,
                    Is.EqualTo(scheduledStop));
                Assert.That(
                    ContainsTile(
                        servedStops,
                        oppositeSideStop),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(busObject);
            }
        }

        [Test]
        public void CityBus_DisconnectedRoad_StopsAtLastValidRoad()
        {
            GameObject busObject = new("CityBus");

            try
            {
                SimEventHub events = new();
                SimEngine engine =
                    new(SimConfig.Default(), events);
                PlaceDirectionalBusLoop(engine);
                (
                    CityBusService service,
                    BusRoute route,
                    BusStopRegistry _) =
                    CreateBusService(
                        busObject,
                        events,
                        engine);

                Assert.That(service.StartService(), Is.True);
                MoveUntilTile(
                    route,
                    new Vector2Int(3, 2));
                Vector2Int lastValidRoad =
                    route.CurrentTile;
                Vector2Int removedRoad =
                    new(4, 2);

                Assert.That(
                    engine.Remove(removedRoad),
                    Is.True);
                Assert.That(
                    engine.Remove(new Vector2Int(4, 6)),
                    Is.True);

                InvokePrivate(route, "MoveOneTile");

                Assert.That(
                    route.State,
                    Is.EqualTo(
                        BusRouteState.RouteUnavailable));
                Assert.That(
                    route.CurrentTile,
                    Is.EqualTo(lastValidRoad));
                Assert.That(
                    route.CurrentTile,
                    Is.Not.EqualTo(removedRoad));
            }
            finally
            {
                Object.DestroyImmediate(busObject);
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
                SimEngine engine,
                IEconomyService economy = null)
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
                    engine,
                    null,
                    economy));
            Assert.That(service.IsInitialized, Is.True);
            Assert.That(registry.BusStopCount, Is.EqualTo(3));
            Assert.That(
                route.SecondsPerTile,
                Is.EqualTo(definition.SecondsPerTile)
                    .Within(0.0001f),
                "City bus route speed must come from its definition asset.");
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
                    new Vector2Int(3, 1)),
                Is.True);
            Assert.That(
                engine.TryPlaceBusStop(
                    new Vector2Int(7, 1)),
                Is.True);
            Assert.That(
                engine.TryPlaceBusStop(
                    new Vector2Int(5, 7)),
                Is.True);
        }

        private static void PlaceDirectionalBusLoop(
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
                    new Vector2Int(3, 1)),
                Is.True);
            Assert.That(
                engine.TryPlaceBusStop(
                    new Vector2Int(7, 1)),
                Is.True);
            Assert.That(
                engine.TryPlaceBusStop(
                    new Vector2Int(5, 3)),
                Is.True);
        }

        private static void MoveUntilTile(
            BusRoute route,
            Vector2Int target)
        {
            for (int i = 0;
                 i < 8 &&
                 route.CurrentTile != target;
                 i++)
            {
                InvokePrivate(route, "MoveOneTile");
            }

            Assert.That(
                route.CurrentTile,
                Is.EqualTo(target));
            Assert.That(
                route.State,
                Is.EqualTo(BusRouteState.Moving));
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

        private sealed class RecordingEconomy :
            IEconomyService
        {
            public long Coins { get; private set; }
            public int AddCalls { get; private set; }
            public string LastReason { get; private set; }

            public event Action<long> CoinsChanged;

            public bool TrySpend(long amount)
            {
                if (amount <= 0L || Coins < amount)
                {
                    return false;
                }

                Coins -= amount;
                CoinsChanged?.Invoke(Coins);
                return true;
            }

            public void AddCoins(
                long amount,
                string reason)
            {
                Coins += amount;
                AddCalls++;
                LastReason = reason;
                CoinsChanged?.Invoke(Coins);
            }
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

        private static void AssertProjectOwnedPrefab(GameObject prefab)
        {
            Assert.That(prefab, Is.Not.Null);
            string path = AssetDatabase.GetAssetPath(prefab);
            Assert.That(
                path.Replace('\\', '/'),
                Does.StartWith("Assets/02_Prefabs/"),
                $"Catalogs must reference a project-owned prefab copy, not {path}.");
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
