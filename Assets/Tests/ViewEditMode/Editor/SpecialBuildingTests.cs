using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CityFlow.Bootstrap;
using CityFlow.Buildings;
using CityFlow.Configs;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.Gameplay.Research;
using CityFlow.Save;
using CityFlow.Sim;
using CityFlow.View;
using CityFlow.UI;
using CityFlow.UI.Controllers.Placement;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CityFlow.Tests
{
    public sealed class SpecialBuildingTests
    {
        private const string CatalogPath =
            "Assets/05_ScriptableObjects/Buildings/SpecialBuildingCatalog.asset";
        private const string SystemPrefabPath =
            "Assets/02_Prefabs/Buildings/SpecialBuildingSystem.prefab";
        private const string FallbackPrefabPath =
            "Assets/02_Prefabs/Buildings/SpecialBuildingFallback.prefab";
        private const string PoliceVisualPrefabPath =
            "Assets/02_Prefabs/Buildings/" +
            "PoliceStationVisual_StudioHorizon.prefab";

        [Test]
        public void Catalog_ContainsEightDefinitionsWithExpectedCadences()
        {
            BuildingCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<BuildingCatalogSO>(CatalogPath);

            Assert.NotNull(catalog);
            Assert.AreEqual(8, catalog.Count);
            AssertCadence(catalog, "mall", 1, 7, new Vector2Int(2, 2));
            AssertCadence(
                catalog,
                "petrol_station",
                1,
                7,
                new Vector2Int(2, 2));
            AssertCadence(
                catalog,
                "police_station",
                1,
                7,
                new Vector2Int(2, 2));
            AssertCadence(
                catalog,
                "video_store",
                1,
                7,
                new Vector2Int(2, 2));
            AssertCadence(
                catalog,
                "pharmacy",
                1,
                7,
                new Vector2Int(1, 2));
            AssertCadence(
                catalog,
                "coffee_shop",
                1,
                2,
                new Vector2Int(1, 2));
            AssertCadence(catalog, "cinema", 1, 7, new Vector2Int(2, 2));
            AssertCadence(
                catalog,
                "auto_repair",
                1,
                7,
                new Vector2Int(2, 2));
        }

        [Test]
        public void HiddenGym_IsNotOfferedButRemainsResolvable()
        {
            BuildingCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<BuildingCatalogSO>(CatalogPath);
            GameObject serviceObject = new("Hidden Gym Build Option Test");

            try
            {
                Assert.NotNull(catalog);
                SpecialBuildingService service =
                    serviceObject.AddComponent<SpecialBuildingService>();
                SetPrivateField(service, "catalog", catalog);

                SpecialBuildingBuildOption[] visible =
                    service.CreateBuildOptionSnapshot();
                for (int index = 0; index < visible.Length; index++)
                {
                    Assert.AreNotEqual("video_store", visible[index].BuildingId);
                }

                Assert.IsTrue(
                    service.TryGetBuildOption(
                        "video_store",
                        out SpecialBuildingBuildOption hidden));
                Assert.AreEqual("video_store", hidden.BuildingId);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(serviceObject);
            }
        }

        [Test]
        public void BuildOptions_RejectUnsupportedFootprintAndKeepCompact()
        {
            BuildingDefinitionSO compact =
                ScriptableObject.CreateInstance<BuildingDefinitionSO>();
            BuildingDefinitionSO unsupported =
                ScriptableObject.CreateInstance<BuildingDefinitionSO>();
            BuildingCatalogSO catalog =
                ScriptableObject.CreateInstance<BuildingCatalogSO>();
            GameObject serviceObject = new("Special Building Option Test");

            try
            {
                compact.buildingId = "compact";
                compact.buildingName = "Compact";
                compact.unlockedByDefault = true;
                SetPrivateField(
                    compact,
                    "footprint",
                    new Vector2Int(1, 2));

                unsupported.buildingId = "unsupported";
                unsupported.buildingName = "Unsupported";
                unsupported.unlockedByDefault = true;
                SetPrivateField(
                    unsupported,
                    "footprint",
                    new Vector2Int(1, 1));

                SetPrivateField(
                    catalog,
                    "buildings",
                    new List<BuildingDefinitionSO>
                    {
                        compact,
                        unsupported
                    });
                SpecialBuildingService service =
                    serviceObject.AddComponent<SpecialBuildingService>();
                SetPrivateField(service, "catalog", catalog);

                SpecialBuildingBuildOption[] options =
                    service.CreateBuildOptionSnapshot();

                Assert.AreEqual(1, options.Length);
                Assert.AreEqual("compact", options[0].BuildingId);
                Assert.AreEqual(new Vector2Int(1, 2), options[0].Footprint);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(serviceObject);
                UnityEngine.Object.DestroyImmediate(catalog);
                UnityEngine.Object.DestroyImmediate(unsupported);
                UnityEngine.Object.DestroyImmediate(compact);
            }
        }

        [Test]
        public void UnderConstructionVisual_UsesConstructionTargetFootprint()
        {
            Vector2Int anchor = new(3, 6);
            BuildingDefinitionSO definition =
                ScriptableObject.CreateInstance<BuildingDefinitionSO>();
            BuildingCatalogSO catalog =
                ScriptableObject.CreateInstance<BuildingCatalogSO>();
            GameObject fallbackPrefab = new("Special Building Fallback Test");
            GameObject viewObject = new("Construction Special Building View");

            try
            {
                definition.buildingId = "compact";
                definition.buildingName = "Compact";
                SetPrivateField(
                    definition,
                    "footprint",
                    new Vector2Int(1, 2));
                SetPrivateField(
                    catalog,
                    "buildings",
                    new List<BuildingDefinitionSO> { definition });

                var tileData = new ConstructionTileData(
                    anchor,
                    TileType.SpecialBuilding,
                    PlacementDirection.East);
                var services = new CityFlowServices(
                    new SimEventHub(),
                    tileData,
                    null);
                var coordinates = new TestCoordinateSpace();
                var buildingService = new TestSpecialBuildingService();
                buildingService.SetBuilding(new SpecialBuildingInstance(
                    "compact",
                    anchor,
                    PlacementDirection.East));
                SpecialBuildingView view =
                    viewObject.AddComponent<SpecialBuildingView>();
                SetPrivateField(view, "catalog", catalog);
                SetPrivateField(view, "fallbackPrefab", fallbackPrefab);
                Assert.IsTrue(services.RegisterWorldCoordinates(coordinates));
                Assert.IsTrue(
                    services.RegisterSpecialBuildings(buildingService));

                view.Initialize(services);

                Dictionary<Vector2Int, GameObject> visuals =
                    ReadPrivateField<Dictionary<Vector2Int, GameObject>>(
                        view,
                        "visuals");
                Assert.IsTrue(visuals.TryGetValue(anchor, out GameObject visual));
                Assert.That(visual.transform.position.x,
                    Is.EqualTo(anchor.x + 1f).Within(0.0001f));
                Assert.That(visual.transform.position.z,
                    Is.EqualTo(anchor.y + 1f).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(viewObject);
                UnityEngine.Object.DestroyImmediate(fallbackPrefab);
                UnityEngine.Object.DestroyImmediate(catalog);
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void Prefabs_ContainPrewiredRuntimeComponents()
        {
            GameObject systemPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(SystemPrefabPath);
            GameObject fallbackPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(FallbackPrefabPath);

            Assert.NotNull(systemPrefab);
            Assert.NotNull(systemPrefab.GetComponent<SpecialBuildingService>());
            Assert.NotNull(systemPrefab.GetComponent<ResearchUnlockService>());
            Assert.NotNull(systemPrefab.GetComponent<SpecialBuildingView>());
            Assert.NotNull(
                systemPrefab.GetComponent<SpecialBuildingVisitService>());
            Assert.NotNull(
                systemPrefab.GetComponent<SpecialBuildingVisitTripSource>());
            Assert.NotNull(fallbackPrefab);
            Assert.NotNull(
                fallbackPrefab.GetComponent<
                    SpecialBuildingFallbackPresenter>());
        }

        [Test]
        public void PoliceStation_UsesProjectOwnedTwoByTwoVisualWithParking()
        {
            BuildingCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<BuildingCatalogSO>(
                    CatalogPath);
            GameObject visualPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    PoliceVisualPrefabPath);

            Assert.NotNull(catalog);
            Assert.NotNull(visualPrefab);
            Assert.IsTrue(catalog.TryGet(
                "police_station",
                out BuildingDefinitionSO definition));
            Assert.AreEqual(
                new Vector2Int(2, 2),
                definition.Footprint);
            Assert.AreSame(visualPrefab, definition.VisualPrefab);
            Assert.AreEqual(
                PoliceVisualPrefabPath,
                AssetDatabase.GetAssetPath(definition.VisualPrefab));

            Transform buildingSurface =
                visualPrefab.transform.Find("BuildingSurface");
            Transform parkingLot =
                visualPrefab.transform.Find("ParkingLot");
            Assert.NotNull(buildingSurface);
            Assert.NotNull(parkingLot);
            Assert.That(
                buildingSurface.localPosition.y,
                Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(
                buildingSurface.localScale.x,
                Is.EqualTo(2f).Within(0.0001f));
            Assert.That(
                buildingSurface.localScale.y,
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                parkingLot.localPosition.y,
                Is.EqualTo(-0.5f).Within(0.0001f));
            Assert.That(
                parkingLot.localScale.x,
                Is.EqualTo(2f).Within(0.0001f));
            Assert.That(
                parkingLot.localScale.y,
                Is.EqualTo(1f).Within(0.0001f));

            GameObject visualInstance =
                UnityEngine.Object.Instantiate(visualPrefab);
            try
            {
                Transform model =
                    visualInstance.transform.Find("Model");
                Assert.NotNull(model);
                Assert.IsTrue(TryGetLocalRendererBounds(
                    model,
                    visualInstance.transform,
                    out Bounds modelBounds));
                Assert.That(
                    modelBounds.center.x,
                    Is.EqualTo(0f).Within(0.001f));
                Assert.That(
                    modelBounds.center.y,
                    Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(
                    modelBounds.size.x,
                    Is.EqualTo(1.9f).Within(0.001f));
                Assert.That(
                    modelBounds.size.y,
                    Is.LessThanOrEqualTo(0.901f));
                Assert.That(
                    modelBounds.max.z,
                    Is.EqualTo(0f).Within(0.001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(visualInstance);
            }

            BuildingParkingLayout layout =
                visualPrefab.GetComponent<BuildingParkingLayout>();
            Assert.NotNull(layout);
            Assert.AreEqual(2, layout.ParkingSlotCount);
            for (int slotIndex = 0;
                 slotIndex < layout.ParkingSlotCount;
                 slotIndex++)
            {
                Transform slot = visualPrefab.transform.Find(
                    $"ParkingSlot_{slotIndex}");
                Assert.NotNull(slot);
                Assert.Less(slot.localPosition.y, 0f);
                Assert.Greater(
                    Vector3.Dot(slot.forward, Vector3.down),
                    0.999f);
                Assert.IsTrue(layout.TryGetParkingPose(
                    slotIndex,
                    out BuildingParkingPose pose));
                Assert.That(
                    Vector3.Distance(pose.WorldPosition, slot.position),
                    Is.LessThan(0.0001f));
                Assert.That(
                    Vector3.Dot(pose.WorldForward, slot.forward),
                    Is.GreaterThan(0.999f));
            }
        }

        [Test]
        public void SpecialBuildingParking_IsExposedToMainCityView()
        {
            BuildingCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<BuildingCatalogSO>(
                    CatalogPath);
            GameObject fallbackPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    FallbackPrefabPath);
            GameObject cityObject = new("Police Parking City");
            GameObject viewObject = new("Special Building View");
            GameObject vehicleObject = null;

            try
            {
                Assert.NotNull(catalog);
                Assert.NotNull(fallbackPrefab);
                SimConfig config = SimConfig.Default();
                var events = new SimEventHub();
                var engine = new SimEngine(config, events);
                var services = new CityFlowServices(
                    events,
                    engine,
                    engine,
                    stats: engine);
                var coordinates = new TestCoordinateSpace();
                var buildingService = new TestSpecialBuildingService();
                MainCityView cityView =
                    cityObject.AddComponent<MainCityView>();
                SpecialBuildingView specialView =
                    viewObject.AddComponent<SpecialBuildingView>();

                cityObject.transform.SetPositionAndRotation(
                    coordinates.Origin,
                    coordinates.CoordinateRotation);
                SetPrivateField(specialView, "catalog", catalog);
                SetPrivateField(
                    specialView,
                    "fallbackPrefab",
                    fallbackPrefab);
                SetPrivateField(
                    cityView,
                    "specialBuildingParkingView",
                    specialView);
                Assert.IsTrue(services.RegisterWorldCoordinates(coordinates));
                Assert.IsTrue(
                    services.RegisterSpecialBuildings(buildingService));
                specialView.Initialize(services);
                Assert.AreEqual(0, specialView.VisualCount);

                Vector2Int anchor = new(8, 12);
                buildingService.SetBuilding(new SpecialBuildingInstance(
                    "police_station",
                    anchor,
                    PlacementDirection.North));
                Assert.IsTrue(cityView.TryGetBuildingParkingPose(
                    anchor,
                    0,
                    out _,
                    out _));
                Assert.AreEqual(1, specialView.VisualCount);
                Assert.IsTrue(services.RegisterWorldCoordinateRoot(cityView));

                Assert.IsTrue(cityView.TryGetBuildingParkingPose(
                    anchor,
                    0,
                    out Vector3 firstPosition,
                    out Vector3 firstForward));
                Assert.IsTrue(cityView.TryGetBuildingParkingPose(
                    anchor,
                    1,
                    out Vector3 secondPosition,
                    out Vector3 secondForward));
                Assert.AreEqual(1, specialView.VisualCount);
                Assert.That(
                    firstPosition.z,
                    Is.EqualTo(cityView.RoadSurfaceZ).Within(0.0001f));
                Assert.That(
                    secondPosition.z,
                    Is.EqualTo(cityView.RoadSurfaceZ).Within(0.0001f));
                Assert.That(
                    Vector3.Distance(firstPosition, secondPosition),
                    Is.GreaterThan(0.5f));
                Assert.Greater(
                    Vector3.Dot(firstForward, Vector3.down),
                    0.999f);
                Assert.Greater(
                    Vector3.Dot(secondForward, Vector3.down),
                    0.999f);

                MethodInfo getParkingAnchor =
                    typeof(MainCityView).GetMethod(
                        "GetParkingAnchor",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                Assert.NotNull(getParkingAnchor);
                Vector3 consumedPosition = (Vector3)getParkingAnchor.Invoke(
                    cityView,
                    new object[]
                    {
                        anchor,
                        new Vector2Int(8, 14),
                        1,
                        4
                    });
                Assert.That(
                    Vector3.Distance(
                        consumedPosition,
                        secondPosition),
                    Is.LessThan(0.0001f),
                    "CarMotion은 절차 폴백보다 authored 슬롯 pose를 우선해야 한다.");

                Type routeVehicleType =
                    typeof(MainCityView).GetNestedType(
                        "RouteVehicle",
                        BindingFlags.NonPublic);
                Assert.NotNull(routeVehicleType);
                object routeVehicle = Activator.CreateInstance(
                    routeVehicleType,
                    nonPublic: true);
                vehicleObject = new GameObject(
                    "Authored Parking Rotation Vehicle");
                FieldInfo routeVehicleObjectField =
                    routeVehicleType.GetField(
                        "Object",
                        BindingFlags.Instance |
                        BindingFlags.Public);
                Assert.NotNull(routeVehicleObjectField);
                routeVehicleObjectField.SetValue(
                    routeVehicle,
                    vehicleObject);
                MethodInfo setParkingRotation =
                    typeof(MainCityView).GetMethod(
                        "SetForwardBuildingParkingRotation",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                Assert.NotNull(setParkingRotation);
                setParkingRotation.Invoke(
                    cityView,
                    new object[] { routeVehicle, anchor, 1 });
                Vector3 consumedForward =
                    vehicleObject.transform.localRotation * Vector3.right;
                Assert.Greater(
                    Vector3.Dot(
                        consumedForward.normalized,
                        secondForward.normalized),
                    0.999f,
                    "주차 완료 회전도 authored 슬롯 forward를 사용해야 한다.");
            }
            finally
            {
                if (vehicleObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(vehicleObject);
                }
                UnityEngine.Object.DestroyImmediate(viewObject);
                UnityEngine.Object.DestroyImmediate(cityObject);
            }
        }

        [Test]
        public void ExistingBuildSlotPrefab_SupportsLockedSpecialBuilding()
        {
            const string buildSlotPath =
                "Assets/02_Prefabs/UI_BuildSlot.prefab";
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(buildSlotPath);
            GameObject instance = null;

            try
            {
                Assert.NotNull(prefab);
                instance = UnityEngine.Object.Instantiate(prefab);
                BuildSlotController slot =
                    instance.GetComponent<BuildSlotController>();
                var locked = new SpecialBuildingBuildOption(
                    "mall",
                    "큰 상점",
                    "Commercial",
                    "설명",
                    null,
                    Color.green,
                    SpecialBuildingMenuCategory.Commercial,
                    100,
                    false,
                    "research_building_mall",
                    true,
                    1,
                    1,
                    0,
                    1f,
                    1);

                slot.ConfigureSpecialBuilding(locked, null, null);

                Button button = instance.transform
                    .Find("Btn_Buy")
                    .GetComponent<Button>();
                TMP_Text cost = instance.transform
                    .Find("CostText")
                    .GetComponent<TMP_Text>();
                Assert.IsFalse(button.interactable);
                Assert.AreEqual("잠김", cost.text);

                var unlocked = new SpecialBuildingBuildOption(
                    "mall",
                    "큰 상점",
                    "Commercial",
                    "설명",
                    null,
                    Color.green,
                    SpecialBuildingMenuCategory.Commercial,
                    100,
                    true,
                    "research_building_mall",
                    true,
                    1,
                    1,
                    0,
                    1f,
                    1);
                slot.RefreshSpecialBuilding(unlocked);

                Assert.IsTrue(button.interactable);
                Assert.AreEqual("100", cost.text);
            }
            finally
            {
                if (instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void DeterministicDemand_LowFrequencyTotalsMatchPeriod()
        {
            const int population = 17;
            int total = 0;

            for (long day = 0; day < 5; day++)
            {
                int first = DeterministicVisitDemand.CalculateDailyDemand(
                    population,
                    1,
                    5,
                    day,
                    "auto_repair");
                int second = DeterministicVisitDemand.CalculateDailyDemand(
                    population,
                    1,
                    5,
                    day,
                    "auto_repair");
                Assert.AreEqual(first, second);
                total += first;
            }

            Assert.AreEqual(population, total);
            Assert.AreEqual(
                population * 2,
                DeterministicVisitDemand.CalculateDailyDemand(
                    population,
                    2,
                    1,
                    12,
                    "petrol_station"));
        }

        [Test]
        public void VisitService_ProcessesDayAndPersistsStatistics()
        {
            BuildingCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<BuildingCatalogSO>(CatalogPath);
            Assert.IsTrue(catalog.TryGet(
                "cinema",
                out BuildingDefinitionSO cinema));

            GameObject serviceObject = null;
            string savePath = Path.Combine(
                Path.GetTempPath(),
                $"greenlight-special-trip-{Guid.NewGuid():N}.json");
            string backupPath = savePath + ".bak";

            try
            {
                SimConfig config = SimConfig.Default();
                config.GridWidth = 20;
                config.GridHeight = 20;
                var events = new SimEventHub();
                var engine = new SimEngine(config, events);
                var save = new SaveService(
                    engine,
                    new JsonSaveRepository(savePath, backupPath),
                    new SystemSaveClock());
                var economy = new TestEconomy();
                var services = new CityFlowServices(
                    events,
                    engine,
                    engine,
                    save,
                    economy,
                    engine);
                services.RegisterVehicleTrips(engine);

                serviceObject = new GameObject("SpecialBuildingVisitTest");
                ResearchUnlockService research =
                    serviceObject.AddComponent<ResearchUnlockService>();
                research.Initialize(services);
                UnlockCinemaForTest(research);

                SpecialBuildingService buildingService =
                    serviceObject.AddComponent<SpecialBuildingService>();
                SetPrivateField(buildingService, "catalog", catalog);
                buildingService.Initialize(services);
                Assert.IsTrue(buildingService.TryPlace(
                    "cinema",
                    new Vector2Int(2, 3)));

                var population = new TestPopulation(10);
                var calendar = new TestCalendar();
                services.RegisterPopulation(population);
                services.RegisterGameCalendar(calendar);

                SpecialBuildingVisitService visitService =
                    serviceObject.AddComponent<SpecialBuildingVisitService>();
                visitService.Initialize(services);
                SpecialBuildingVisitTripSource tripSource =
                    serviceObject.AddComponent<SpecialBuildingVisitTripSource>();
                tripSource.Initialize(services);
                calendar.AdvanceDay();

                Assert.IsTrue(visitService.TryGetStatistics(
                    new Vector2Int(3, 4),
                    out SpecialBuildingVisitStatistics statistics));
                int expectedDemand = DeterministicVisitDemand.CalculateDailyDemand(
                    population.CurrentPopulation,
                    cinema.VisitCadence.VisitsPerPeriod,
                    cinema.VisitCadence.PeriodDays,
                    calendar.TotalDays,
                    cinema.buildingId);
                Assert.AreEqual(expectedDemand, statistics.PlannedToday);
                Assert.AreEqual(expectedDemand, statistics.TotalPlannedVisits);
                Assert.AreEqual(0L, economy.Coins);
                Assert.AreEqual(expectedDemand, engine.PendingTripCount);

                GameSaveData snapshot = save.CreateSnapshot();
                Assert.NotNull(snapshot.SpecialBuildingVisits);
                Assert.IsTrue(snapshot.SpecialBuildingVisits.HasState);
                Assert.AreEqual(
                    calendar.TotalDays,
                    snapshot.SpecialBuildingVisits.LastProcessedTotalDay);
                Assert.AreEqual(
                    1,
                    snapshot.SpecialBuildingVisits.Statistics.Length);

                calendar.SetHour(12);
                Assert.IsTrue(save.Repository.TrySave(snapshot));
                Assert.IsTrue(save.TryLoadAndRestore());

                int expectedRemainingTrips = 0;
                for (int visitIndex = 0; visitIndex < expectedDemand; visitIndex++)
                {
                    float scheduledHour = VisitTimeProfileSampler.SampleHour(
                        cinema.VisitTimeProfile,
                        visitIndex,
                        expectedDemand);
                    if (scheduledHour > calendar.Hour)
                    {
                        expectedRemainingTrips++;
                    }
                }

                Assert.AreEqual(
                    expectedRemainingTrips,
                    engine.PendingTripCount,
                    "Only visits scheduled after the restored hour should be rebuilt.");
            }
            finally
            {
                if (serviceObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(serviceObject);
                }

                if (File.Exists(savePath))
                {
                    File.Delete(savePath);
                }

                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
            }
        }

        [Test]
        public void FallbackPresenter_CreatesColliderFreeVisualParts()
        {
            BuildingCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<BuildingCatalogSO>(CatalogPath);
            GameObject fallbackPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(FallbackPrefabPath);
            GameObject instance = null;

            try
            {
                Assert.IsTrue(catalog.TryGet(
                    "mall",
                    out BuildingDefinitionSO definition));
                instance = UnityEngine.Object.Instantiate(fallbackPrefab);
                instance.GetComponent<SpecialBuildingFallbackPresenter>()
                    .Configure(definition, 1f);

                Assert.NotNull(instance.transform.Find("Body"));
                Assert.NotNull(instance.transform.Find("Roof"));
                Assert.NotNull(instance.transform.Find("FrontMarker"));
                Collider[] colliders =
                    instance.GetComponentsInChildren<Collider>(true);
                for (int index = 0; index < colliders.Length; index++)
                {
                    Assert.IsFalse(colliders[index].enabled);
                }
            }
            finally
            {
                if (instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void Service_RejectsLockedBuildingUntilResearchUnlocks()
        {
            BuildingCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<BuildingCatalogSO>(CatalogPath);
            GameObject serviceObject = null;

            try
            {
                RuntimeContext runtime = CreateRuntime(
                    catalog,
                    out serviceObject,
                    unlockCinema: false);

                Assert.IsFalse(runtime.Service.IsBuildingUnlocked("cinema"));
                Assert.IsFalse(runtime.Service.TryPlace(
                    "cinema",
                    new Vector2Int(2, 3)));

                UnlockCinemaForTest(runtime.Research);
                Assert.IsTrue(runtime.Service.IsBuildingUnlocked("cinema"));
                Assert.IsTrue(runtime.Service.TryPlace(
                    "cinema",
                    new Vector2Int(2, 3)));
            }
            finally
            {
                if (serviceObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(serviceObject);
                }
            }
        }

        [Test]
        public void Service_PlaceAndRestore_PreservesIdentityAndDirection()
        {
            BuildingCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<BuildingCatalogSO>(CatalogPath);
            Assert.NotNull(catalog);

            GameObject firstObject = null;
            GameObject restoredObject = null;

            try
            {
                var first = CreateRuntime(catalog, out firstObject);
                Assert.IsTrue(first.Service.TryPlace(
                    "cinema",
                    new Vector2Int(2, 3),
                    PlacementDirection.East));
                Assert.AreEqual(
                    TileType.SpecialBuilding,
                    first.Engine.GetTileType(new Vector2Int(3, 4)));
                Assert.IsTrue(first.Service.TryGetBuilding(
                    new Vector2Int(3, 4),
                    out SpecialBuildingInstance placed));
                Assert.AreEqual("cinema", placed.BuildingId);

                HappinessEffectDescriptor[] effects =
                    first.Service.CreateActiveHappinessEffectSnapshot();
                Assert.AreEqual(1, effects.Length);
                Assert.AreEqual(
                    "happiness_building_cinema",
                    effects[0].EffectKey);

                GameSaveData snapshot = first.Save.CreateSnapshot();
                var restored = CreateRuntime(catalog, out restoredObject);
                restored.Save.RestoreSnapshot(snapshot);

                Assert.IsTrue(restored.Service.TryGetBuilding(
                    new Vector2Int(3, 4),
                    out SpecialBuildingInstance loaded));
                Assert.AreEqual("cinema", loaded.BuildingId);
                Assert.AreEqual(
                    PlacementDirection.East,
                    loaded.Direction);
                Assert.AreEqual(1, restored.Service.BuildingCount);
                Assert.IsTrue(restored.Research.IsUnlocked(
                    "research_building_cinema"));
            }
            finally
            {
                if (firstObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(firstObject);
                }

                if (restoredObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(restoredObject);
                }
            }
        }

        [Test]
        public void Service_PendingConstruction_RestoresAndActivatesHappinessOnCompletion()
        {
            BuildingCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<BuildingCatalogSO>(CatalogPath);
            GameObject firstObject = null;
            GameObject restoredObject = null;

            try
            {
                RuntimeContext first = CreateRuntime(
                    catalog,
                    out firstObject,
                    constructionHoursSpecial: 4f);
                int firstActivations = 0;
                first.Service.HappinessEffectChanged += changed =>
                {
                    if (changed.IsActive)
                    {
                        firstActivations++;
                    }
                };

                Vector2Int anchor = new Vector2Int(2, 3);
                Assert.IsTrue(first.Service.TryPlace("cinema", anchor));
                Assert.AreEqual(
                    TileType.UnderConstruction,
                    first.Engine.GetTileType(anchor));
                Assert.AreEqual(1, first.Service.BuildingCount);
                Assert.AreEqual(
                    0,
                    first.Service.CreateActiveHappinessEffectSnapshot().Length);
                Assert.AreEqual(0, firstActivations);

                GameSaveData snapshot = first.Save.CreateSnapshot();
                RuntimeContext restored = CreateRuntime(
                    catalog,
                    out restoredObject,
                    constructionHoursSpecial: 4f);
                int restoredActivations = 0;
                restored.Service.HappinessEffectChanged += changed =>
                {
                    if (changed.IsActive)
                    {
                        restoredActivations++;
                    }
                };

                restored.Save.RestoreSnapshot(snapshot);

                Assert.AreEqual(1, restored.Service.BuildingCount);
                Assert.IsTrue(restored.Service.TryGetBuilding(
                    new Vector2Int(3, 4),
                    out SpecialBuildingInstance loaded));
                Assert.AreEqual("cinema", loaded.BuildingId);
                Assert.AreEqual(
                    0,
                    restored.Service.CreateActiveHappinessEffectSnapshot().Length);
                Assert.AreEqual(0, restoredActivations);

                for (int i = 0; i < 16; i++)
                {
                    restored.Engine.Tick(0.25f);
                }

                Assert.AreEqual(
                    TileType.SpecialBuilding,
                    restored.Engine.GetTileType(anchor));
                Assert.AreEqual(
                    1,
                    restored.Service.CreateActiveHappinessEffectSnapshot().Length);
                Assert.AreEqual(1, restoredActivations);
            }
            finally
            {
                if (firstObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(firstObject);
                }

                if (restoredObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(restoredObject);
                }
            }
        }

        [Test]
        public void Dispatcher_DemolishesPendingSpecialBuilding_AndAllowsReplacement()
        {
            BuildingCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<BuildingCatalogSO>(CatalogPath);
            GameObject serviceObject = null;

            try
            {
                RuntimeContext runtime = CreateRuntime(
                    catalog,
                    out serviceObject,
                    constructionHoursSpecial: 4f);
                Vector2Int anchor = new Vector2Int(2, 3);
                Assert.IsTrue(runtime.Service.TryPlace("cinema", anchor));
                Assert.AreEqual(
                    TileType.UnderConstruction,
                    runtime.Engine.GetTileType(anchor));
                Assert.AreEqual(
                    0,
                    runtime.Service.CreateActiveHappinessEffectSnapshot().Length);

                var dispatcher = new PlacementActionDispatcher(
                    availableTiles: null,
                    useFakeMode: false);
                dispatcher.PlaceInfrastructure(
                    new Vector2Int(3, 4),
                    TileType.Empty,
                    PlacementDirection.North,
                    runtime.Services);

                Assert.AreEqual(0, runtime.Service.BuildingCount);
                Assert.AreEqual(
                    TileType.Empty,
                    runtime.Engine.GetTileType(anchor));
                Assert.AreEqual(
                    0,
                    runtime.Service.CreateActiveHappinessEffectSnapshot().Length);
                Assert.AreEqual(
                    0,
                    runtime.Service.CreateSnapshot().Buildings.Length);
                Assert.IsTrue(
                    runtime.Service.TryPlace("cinema", anchor),
                    "공사 중 철거 뒤 같은 앵커에 다시 배치할 수 있어야 한다");
            }
            finally
            {
                if (serviceObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(serviceObject);
                }
            }
        }

        [Test]
        public void Dispatcher_DemolishingConstruction_RefundsTargetTypeCost()
        {
            TileDataSO hospitalData =
                ScriptableObject.CreateInstance<TileDataSO>();

            try
            {
                const int hospitalCost = 800;
                hospitalData.Initialize(
                    "hospital",
                    "병원",
                    TileType.Hospital,
                    hospitalCost,
                    0,
                    0,
                    string.Empty);

                SimConfig config = SimConfig.Default();
                config.GridWidth = 20;
                config.GridHeight = 20;
                config.DayLengthSeconds = 24f;
                config.ConstructionHoursHospital = 4f;
                var events = new SimEventHub();
                var engine = new SimEngine(config, events);
                var economy = new TestEconomy();
                var services = new CityFlowServices(
                    events,
                    engine,
                    engine,
                    economy: economy,
                    stats: engine);
                var dispatcher = new PlacementActionDispatcher(
                    new[] { hospitalData },
                    useFakeMode: false);
                Vector2Int anchor = new Vector2Int(2, 3);
                Assert.IsTrue(engine.Place(anchor, TileType.Hospital));
                Assert.AreEqual(
                    TileType.UnderConstruction,
                    engine.GetTileType(anchor));

                dispatcher.PlaceInfrastructure(
                    new Vector2Int(3, 4),
                    TileType.Empty,
                    PlacementDirection.North,
                    services);

                Assert.AreEqual(
                    hospitalCost,
                    economy.Coins,
                    "공사 중 비앵커 철거도 목표 타입 Hospital 단가로 환불해야 한다");
                Assert.AreEqual(TileType.Empty, engine.GetTileType(anchor));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hospitalData);
            }
        }

        [Test]
        public void LegacySave_RestoresSpecialBuildingAndVisitsAtWorldOrigin()
        {
            BuildingCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<BuildingCatalogSO>(CatalogPath);
            Assert.NotNull(catalog);

            GameObject serviceObject = null;

            try
            {
                var worldGrid = new TestWorldGridAccess();
                SimConfig config = SimConfig.Default();
                var events = new SimEventHub();
                var engine = new SimEngine(config, events, worldGrid);
                var save = new SaveService(
                    engine,
                    new JsonSaveRepository(),
                    new SystemSaveClock(),
                    worldGridAccess: worldGrid);
                var services = new CityFlowServices(
                    events,
                    engine,
                    engine,
                    save,
                    stats: engine);

                serviceObject = new GameObject(
                    "LegacySpecialBuildingWorldMigrationTest");
                ResearchUnlockService research =
                    serviceObject.AddComponent<ResearchUnlockService>();
                research.Initialize(services);

                SpecialBuildingService buildingService =
                    serviceObject.AddComponent<SpecialBuildingService>();
                SetPrivateField(buildingService, "catalog", catalog);
                buildingService.Initialize(services);

                services.RegisterPopulation(new TestPopulation(10));
                services.RegisterGameCalendar(new TestCalendar());
                SpecialBuildingVisitService visitService =
                    serviceObject.AddComponent<SpecialBuildingVisitService>();
                visitService.Initialize(services);

                save.RestoreSnapshot(new GameSaveData
                {
                    SaveVersion = SaveConstants.CurrentSaveVersion,
                    GridWidth = 20,
                    GridHeight = 20,
                    Simulation = new SimSaveData
                    {
                        GridWidth = 20,
                        GridHeight = 20,
                        PlacedTiles = new[]
                        {
                            new TileSaveData
                            {
                                X = 2,
                                Y = 3,
                                Type = TileType.SpecialBuilding,
                                Direction = PlacementDirection.East
                            }
                        }
                    },
                    SpecialBuildings = new SpecialBuildingSaveData
                    {
                        Buildings = new[]
                        {
                            new SpecialBuildingInstanceSaveData
                            {
                                BuildingId = "cinema",
                                X = 2,
                                Y = 3,
                                Direction = PlacementDirection.East
                            }
                        }
                    },
                    SpecialBuildingVisits = new SpecialBuildingVisitSaveData
                    {
                        HasState = true,
                        LastProcessedTotalDay = 8L,
                        Statistics = new[]
                        {
                            new SpecialBuildingVisitStatisticsSaveData
                            {
                                BuildingId = "cinema",
                                X = 2,
                                Y = 3,
                                Day = 8L,
                                PlannedToday = 3,
                                TotalPlannedVisits = 19L
                            }
                        }
                    }
                });

                var migratedAnchor = new Vector2Int(92, 93);
                Assert.IsTrue(buildingService.TryGetBuilding(
                    migratedAnchor,
                    out SpecialBuildingInstance building));
                Assert.AreEqual("cinema", building.BuildingId);
                Assert.AreEqual(PlacementDirection.East, building.Direction);
                Assert.IsTrue(visitService.TryGetStatistics(
                    migratedAnchor,
                    out SpecialBuildingVisitStatistics statistics));
                Assert.AreEqual(8L, statistics.Day);
                Assert.AreEqual(3, statistics.PlannedToday);
                Assert.AreEqual(19L, statistics.TotalPlannedVisits);
            }
            finally
            {
                if (serviceObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(serviceObject);
                }

            }
        }

        private static RuntimeContext CreateRuntime(
            BuildingCatalogSO catalog,
            out GameObject serviceObject,
            bool unlockCinema = true,
            float constructionHoursSpecial = 0f)
        {
            SimConfig config = SimConfig.Default();
            config.GridWidth = 20;
            config.GridHeight = 20;
            config.ConstructionHoursSpecial = constructionHoursSpecial;
            if (constructionHoursSpecial > 0f)
            {
                config.DayLengthSeconds = 24f;
            }
            var events = new SimEventHub();
            var engine = new SimEngine(config, events);
            var save = new SaveService(
                engine,
                new JsonSaveRepository(),
                new SystemSaveClock());
            var services = new CityFlowServices(
                events,
                engine,
                engine,
                save,
                stats: engine);

            serviceObject = new GameObject("SpecialBuildingServiceTest");
            ResearchUnlockService research =
                serviceObject.AddComponent<ResearchUnlockService>();
            research.Initialize(services);
            if (unlockCinema)
            {
                UnlockCinemaForTest(research);
            }

            SpecialBuildingService service =
                serviceObject.AddComponent<SpecialBuildingService>();
            SetPrivateField(service, "catalog", catalog);
            service.Initialize(services);

            return new RuntimeContext(
                engine,
                save,
                service,
                research,
                services);
        }

        private static void UnlockCinemaForTest(ResearchUnlockService research)
        {
            research.RestoreSnapshot(new ResearchSaveData
            {
                UnlockedResearchIds = new[]
                {
                    "research_building_coffee_shop",
                    "research_building_video_store",
                    "research_building_cinema"
                }
            });
            Assert.IsTrue(research.IsUnlocked(
                "research_building_cinema"));
        }

        private static void AssertCadence(
            BuildingCatalogSO catalog,
            string buildingId,
            int visits,
            int days,
            Vector2Int footprint)
        {
            Assert.IsTrue(catalog.TryGet(buildingId, out BuildingDefinitionSO definition));
            Assert.AreEqual(visits, definition.VisitCadence.VisitsPerPeriod);
            Assert.AreEqual(days, definition.VisitCadence.PeriodDays);
            Assert.AreEqual(footprint, definition.Footprint);
            Assert.IsFalse(string.IsNullOrEmpty(
                definition.HappinessEffectKey));
            Assert.Greater(definition.FallbackHeight, 0f);
        }

        private static T ReadPrivateField<T>(
            object target,
            string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, fieldName);
            return (T)field.GetValue(target);
        }

        private static void SetPrivateField<T>(
            object target,
            string fieldName,
            T value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, fieldName);
            field.SetValue(target, value);
        }

        private static bool TryGetLocalRendererBounds(
            Transform contentRoot,
            Transform relativeTo,
            out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;
            Renderer[] renderers =
                contentRoot.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                Bounds rendererBounds = renderers[rendererIndex].bounds;
                Vector3 min = rendererBounds.min;
                Vector3 max = rendererBounds.max;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 world = new(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    Vector3 local =
                        relativeTo.InverseTransformPoint(world);
                    if (!hasBounds)
                    {
                        bounds = new Bounds(local, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(local);
                    }
                }
            }

            return hasBounds;
        }

        private sealed class TestCoordinateSpace : IWorldCoordinateSpace
        {
            public WorldCoordinatePlane Plane => WorldCoordinatePlane.XZ;
            public float TileSize => 1f;
            public Vector3 Origin => Vector3.zero;
            public Vector3 GridXAxis => Vector3.right;
            public Vector3 GridYAxis => Vector3.forward;
            public Vector3 GroundNormal => Vector3.up;
            public Quaternion CoordinateRotation =>
                Quaternion.Euler(90f, 0f, 0f);

            public Vector3 GridToWorld(
                Vector2Int tile,
                float surfaceOffset = 0f) =>
                new(
                    tile.x + 0.5f,
                    surfaceOffset,
                    tile.y + 0.5f);

            public Vector3 GridPointToWorld(
                Vector2 gridPoint,
                float surfaceOffset = 0f) =>
                new(gridPoint.x, surfaceOffset, gridPoint.y);

            public Vector2 WorldToGridPoint(Vector3 worldPosition) =>
                new(worldPosition.x, worldPosition.z);

            public Vector2Int WorldToGrid(Vector3 worldPosition) =>
                Vector2Int.FloorToInt(WorldToGridPoint(worldPosition));

            public bool TryRayToGrid(
                Ray ray,
                out Vector2Int tile,
                out Vector3 worldHitPoint)
            {
                tile = default;
                worldHitPoint = default;
                return false;
            }
        }

        private sealed class ConstructionTileData : IReadOnlyTileData
        {
            private readonly Vector2Int anchor;
            private readonly TileType targetType;
            private readonly PlacementDirection direction;

            public ConstructionTileData(
                Vector2Int anchor,
                TileType targetType,
                PlacementDirection direction)
            {
                this.anchor = anchor;
                this.targetType = targetType;
                this.direction = direction;
            }

            public CongestionLevel GetCongestion(Vector2Int tile) =>
                CongestionLevel.Free;

            public float GetDensity01(Vector2Int tile) => 0f;

            public int GetQueueCount(Vector2Int tile, Dir entryDir) => 0;

            public TileType GetTileType(Vector2Int tile) =>
                tile == anchor
                    ? TileType.UnderConstruction
                    : TileType.Empty;

            public PlacementDirection GetDirection(Vector2Int tile) =>
                direction;

            public Vector2Int GetFootprintSize(TileType type) =>
                TileFootprint.GetSize(type);

            public bool TryGetFootprintAnchor(
                Vector2Int tile,
                out Vector2Int footprintAnchor)
            {
                footprintAnchor = anchor;
                return tile == anchor;
            }

            public bool IsFootprintAnchor(Vector2Int tile) =>
                tile == anchor;

            public bool TryGetConstructionProgress01(
                Vector2Int tile,
                out float progress01)
            {
                progress01 = tile == anchor ? 0.5f : 0f;
                return tile == anchor;
            }

            public bool TryGetConstructionTargetType(
                Vector2Int tile,
                out TileType constructionTargetType)
            {
                constructionTargetType = targetType;
                return tile == anchor;
            }
        }

        private sealed class TestSpecialBuildingService :
            ISpecialBuildingService
        {
            private SpecialBuildingInstance? building;

            public int BuildingCount => building.HasValue ? 1 : 0;

            public event Action<SpecialBuildingChangedEvent> BuildingChanged
            {
                add { }
                remove { }
            }

            public event Action BuildingsRestored
            {
                add { }
                remove { }
            }

            public event Action BuildOptionsChanged
            {
                add { }
                remove { }
            }

            public event Action<HappinessEffectChangedEvent>
                HappinessEffectChanged
            {
                add { }
                remove { }
            }

            public void SetBuilding(SpecialBuildingInstance nextBuilding)
            {
                building = nextBuilding;
            }

            public bool CanPlace(
                string buildingId,
                Vector2Int anchor,
                PlacementDirection direction = PlacementDirection.North) =>
                false;

            public bool TryPlace(
                string buildingId,
                Vector2Int anchor,
                PlacementDirection direction = PlacementDirection.North) =>
                false;

            public bool TryRemove(Vector2Int tile) => false;

            public bool TryGetBuilding(
                Vector2Int tile,
                out SpecialBuildingInstance foundBuilding)
            {
                if (building.HasValue &&
                    tile == building.Value.Anchor)
                {
                    foundBuilding = building.Value;
                    return true;
                }

                foundBuilding = default;
                return false;
            }

            public bool IsBuildingUnlocked(string buildingId) => false;

            public bool TryGetBuildOption(
                string buildingId,
                out SpecialBuildingBuildOption option)
            {
                option = default;
                return false;
            }

            public SpecialBuildingInstance[] CreateBuildingSnapshot() =>
                building.HasValue
                    ? new[] { building.Value }
                    : Array.Empty<SpecialBuildingInstance>();

            public SpecialBuildingBuildOption[] CreateBuildOptionSnapshot() =>
                Array.Empty<SpecialBuildingBuildOption>();

            public HappinessEffectDescriptor[]
                CreateActiveHappinessEffectSnapshot() =>
                Array.Empty<HappinessEffectDescriptor>();
        }

        private readonly struct RuntimeContext
        {
            public RuntimeContext(
                SimEngine engine,
                SaveService save,
                SpecialBuildingService service,
                ResearchUnlockService research,
                CityFlowServices services)
            {
                Engine = engine;
                Save = save;
                Service = service;
                Research = research;
                Services = services;
            }

            public SimEngine Engine { get; }
            public SaveService Save { get; }
            public SpecialBuildingService Service { get; }
            public ResearchUnlockService Research { get; }
            public CityFlowServices Services { get; }
        }

        private sealed class TestPopulation : IReadOnlyPopulationData
        {
            public TestPopulation(int population)
            {
                CurrentPopulation = population;
            }

            public int CurrentPopulation { get; private set; }

            public event Action<int> PopulationChanged;

            public void SetPopulation(int population)
            {
                CurrentPopulation = Math.Max(0, population);
                PopulationChanged?.Invoke(CurrentPopulation);
            }
        }

        private sealed class TestWorldGridAccess : IWorldGridAccess
        {
            public int WorldWidth => 200;
            public int WorldHeight => 200;
            public int ChunkSize => 10;
            public int ChunkColumns => 20;
            public int ChunkRows => 20;
            public Vector2Int InitialPlayableOrigin => new(90, 90);
            public Vector2Int InitialPlayableSize => new(20, 20);

            public event Action<GridChunkId> ChunkUnlocked
            {
                add { }
                remove { }
            }

            public event Action AccessRestored
            {
                add { }
                remove { }
            }

            public bool IsInsideWorld(Vector2Int tile) =>
                tile.x >= 0 && tile.x < WorldWidth &&
                tile.y >= 0 && tile.y < WorldHeight;

            public bool IsTileUnlocked(Vector2Int tile) =>
                IsAreaUnlocked(tile, Vector2Int.one);

            public bool IsChunkUnlocked(GridChunkId chunk) =>
                chunk.X is 9 or 10 && chunk.Y is 9 or 10;

            public bool IsAreaUnlocked(
                Vector2Int anchor,
                Vector2Int footprint)
            {
                Vector2Int max = anchor + footprint;
                return anchor.x >= 90 && anchor.y >= 90 &&
                       max.x <= 110 && max.y <= 110;
            }

            public bool TryGetChunkId(
                Vector2Int tile,
                out GridChunkId chunk)
            {
                if (!IsInsideWorld(tile))
                {
                    chunk = default;
                    return false;
                }

                chunk = new GridChunkId(
                    tile.x / ChunkSize,
                    tile.y / ChunkSize);
                return true;
            }
        }

        private sealed class TestCalendar : IGameCalendarService
        {
            public int Year { get; private set; } = 1;
            public int Month { get; private set; } = 1;
            public int Day { get; private set; } = 1;
            public int Hour { get; private set; }
            public int TotalMonths { get; private set; } = 1;
            public long TotalDays { get; private set; }
            public float RealSecondsPerGameHour => 1f;
            public float RealSecondsPerGameDay => 24f;
            public int HoursPerDay => 24;
            public float TimeOfDay01 => Hour / 24f;

            public event Action<int> HourChanged;
            public event Action<int> DayChanged;
            public event Action<int> MonthChanged;

            public void AdvanceDay()
            {
                Day++;
                TotalDays++;
                DayChanged?.Invoke(Day);
            }

            public void SetHour(int hour)
            {
                Hour = Mathf.Clamp(hour, 0, 23);
                HourChanged?.Invoke(Hour);
            }
        }

        private sealed class TestEconomy : IEconomyService
        {
            public long Coins { get; private set; }

            public event Action<long> CoinsChanged;

            public bool TrySpend(long amount)
            {
                if (amount < 0L || amount > Coins)
                {
                    return false;
                }

                Coins -= amount;
                CoinsChanged?.Invoke(Coins);
                return true;
            }

            public void AddCoins(long amount, string reason)
            {
                if (amount <= 0L)
                {
                    return;
                }

                Coins += amount;
                CoinsChanged?.Invoke(Coins);
            }
        }
    }
}
