using System.Collections.Generic;
using System.Reflection;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Content.Transit;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.Gameplay.Progression;
using CityFlow.Save;
using CityFlow.View;
using CityFlow.ViewKit;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityFlow.Sim.Tests
{
    public sealed class SchoolBusFeatureTests
    {
        private const string PrefabPath =
            "Assets/02_Prefabs/Vehicles/SchoolBusContent.prefab";
        private const string DefinitionPath =
            "Assets/05_ScriptableObjects/CityFlow/Transit/SchoolBusDefinition.asset";
        private const string SchedulePath =
            "Assets/05_ScriptableObjects/CityFlow/Transit/KoreanSchoolBusSchedule.asset";
        private const string DebugTimeSettingsPath =
            "Assets/05_ScriptableObjects/CityFlow/Transit/SchoolBusDebugGameTimeSettings.asset";
        private const string MaterialPath =
            "Assets/03_Art/Materials/Vehicles/SchoolBus_URP.mat";
        private const string ScenePath =
            "Assets/00_Scenes/Debug/CityFlowIntegrated_Lee.unity";

        [Test]
        public void PrototypeAssets_ProvideDropInSchoolBusFeature()
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    PrefabPath);
            BusDefinitionSO definition =
                AssetDatabase.LoadAssetAtPath<BusDefinitionSO>(
                    DefinitionPath);
            SchoolBusScheduleSO schedule =
                AssetDatabase.LoadAssetAtPath<
                    SchoolBusScheduleSO>(SchedulePath);
            GameTimeSettingsSO debugTimeSettings =
                AssetDatabase.LoadAssetAtPath<
                    GameTimeSettingsSO>(
                    DebugTimeSettingsPath);
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(
                    MaterialPath);
            SceneAsset scene =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    ScenePath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(definition, Is.Not.Null);
            Assert.That(schedule, Is.Not.Null);
            Assert.That(debugTimeSettings, Is.Not.Null);
            Assert.That(material, Is.Not.Null);
            Assert.That(scene, Is.Not.Null);
            Assert.That(
                definition.BusType,
                Is.EqualTo(BusType.SchoolBus));
            Assert.That(
                definition.VehicleVisualPrefab,
                Is.Not.Null);
            Assert.That(
                definition.VehicleLengthTiles,
                Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(
                definition.VehicleWidthTiles,
                Is.EqualTo(0.24f).Within(0.0001f));
            Assert.That(
                definition.VehicleMinimumGapTiles,
                Is.EqualTo(0.11f).Within(0.0001f));
            Assert.That(
                definition.VehicleFootprintProfile,
                Is.Not.Null);
            Assert.That(
                definition.VehicleFootprint.SizeClass,
                Is.EqualTo(VehicleSizeClass.Large));
            Assert.That(
                schedule.MorningStartHour,
                Is.EqualTo(7));
            Assert.That(
                schedule.AfternoonStartHour,
                Is.EqualTo(15));
            Assert.That(
                schedule.OperateOnWeekends,
                Is.False);
            Assert.That(
                prefab.GetComponent<BusRoute>(),
                Is.Not.Null);
            Assert.That(
                prefab.GetComponent<SchoolBusService>(),
                Is.Not.Null);
            Assert.That(
                prefab.GetComponent<BusWorldView>(),
                Is.Not.Null);
            BusWorldView worldView =
                prefab.GetComponent<BusWorldView>();
            SerializedObject worldViewSerialized =
                new(worldView);
            Assert.That(
                worldViewSerialized
                    .FindProperty("laneOffset"),
                Is.Null,
                "The school bus must use MainCityView's lane setting.");
            Assert.That(
                worldViewSerialized
                    .FindProperty("minimumHeadway"),
                Is.Null,
                "The school bus must use MainCityView's headway setting.");
            Assert.That(
                worldViewSerialized
                    .FindProperty("schoolParkingSlot")
                    .intValue,
                Is.EqualTo(1),
                "The school bus must use the school's center parking slot.");
            Assert.That(
                worldViewSerialized
                    .FindProperty("parkingApproachDistance")
                    .floatValue,
                Is.EqualTo(0.7f).Within(0.0001f));
            Assert.That(
                prefab.GetComponent<BusStopRegistry>(),
                Is.Not.Null,
                "The school bus Prefab must provide its own registry.");
            Assert.That(
                GameObjectUtility
                    .GetMonoBehavioursWithMissingScriptCount(
                        prefab),
                Is.Zero);

            Scene openedScene =
                EditorSceneManager.OpenScene(
                    ScenePath,
                    OpenSceneMode.Additive);
            try
            {
                int schoolBusServiceCount = 0;
                int legacyRouteViewCount = 0;
                bool hasPrototypeCalendar = false;
                bool disablesSaveRestore = false;
                float sceneLaneOffset = -1f;
                foreach (GameObject root in
                         openedScene.GetRootGameObjects())
                {
                    Assert.That(
                        GameObjectUtility
                            .GetMonoBehavioursWithMissingScriptCount(
                                root),
                        Is.Zero,
                        $"Missing Script found under {root.name}.");

                    schoolBusServiceCount +=
                        root.GetComponentsInChildren<
                            SchoolBusService>(true).Length;
                    MonoBehaviour[] behaviours =
                        root.GetComponentsInChildren<
                            MonoBehaviour>(true);
                    foreach (MonoBehaviour behaviour in
                             behaviours)
                    {
                        if (behaviour != null &&
                            behaviour.GetType().Name ==
                            "SchoolBusRouteView")
                        {
                            legacyRouteViewCount++;
                        }

                        if (behaviour == null)
                        {
                            continue;
                        }

                        SerializedObject serialized =
                            new(behaviour);
                        if (behaviour is MainCityView mainCityView)
                        {
                            sceneLaneOffset =
                                mainCityView.LaneOffset;
                        }
                        else if (behaviour is GameCalendarService)
                        {
                            SerializedProperty sceneTimeSettings =
                                serialized.FindProperty(
                                    "timeSettings");
                            hasPrototypeCalendar =
                                serialized.FindProperty(
                                    "startHour").intValue == 7 &&
                                sceneTimeSettings != null &&
                                sceneTimeSettings
                                    .objectReferenceValue ==
                                debugTimeSettings &&
                                debugTimeSettings
                                    .RealSecondsPerGameHour <= 1f;
                        }
                        else if (
                            behaviour.GetType().FullName ==
                            "CityFlow.Gameplay.Save.GameSaveLifecycleService")
                        {
                            disablesSaveRestore =
                                !serialized.FindProperty(
                                    "loadOnStart").boolValue;
                        }
                    }
                }

                Assert.That(
                    schoolBusServiceCount,
                    Is.EqualTo(1),
                    "The copied Debug scene must contain one school bus service.");
                Assert.That(
                    legacyRouteViewCount,
                    Is.Zero,
                    "The copied Debug scene must not retain the legacy school bus view.");
                Assert.That(
                    hasPrototypeCalendar,
                    Is.True,
                    "The Debug scene must begin inside the morning school-bus window.");
                Assert.That(
                    disablesSaveRestore,
                    Is.True,
                    "The Debug scene must not replace its prototype clock with a save.");
                Assert.That(
                    sceneLaneOffset,
                    Is.EqualTo(0.25f).Within(0.0001f),
                    "Residential and bus traffic must use the integrated right-lane offset.");
            }
            finally
            {
                EditorSceneManager.CloseScene(
                    openedScene,
                    true);
            }
        }

        [Test]
        public void KoreanSchedule_RunsOncePerWeekdayWindow()
        {
            SchoolBusScheduleSO schedule =
                AssetDatabase.LoadAssetAtPath<
                    SchoolBusScheduleSO>(SchedulePath);
            Assert.That(schedule, Is.Not.Null);

            Assert.That(
                schedule.GetEligibleTrip(0L, 7, -1L, -1L),
                Is.EqualTo(
                    SchoolBusTripKind.MorningCommute));
            Assert.That(
                schedule.GetEligibleTrip(0L, 15, 0L, -1L),
                Is.EqualTo(
                    SchoolBusTripKind.AfternoonDismissal));
            Assert.That(
                schedule.GetEligibleTrip(0L, 16, 0L, 0L),
                Is.EqualTo(SchoolBusTripKind.None));
            Assert.That(
                schedule.GetEligibleTrip(5L, 7, -1L, -1L),
                Is.EqualTo(SchoolBusTripKind.None),
                "Saturday must not run by default.");
            Assert.That(
                schedule.GetEligibleTrip(6L, 15, -1L, -1L),
                Is.EqualTo(SchoolBusTripKind.None),
                "Sunday must not run by default.");
        }


        [Test]
        public void SchoolBusParkingCurve_UsesDrivewayBeforeCenterSlot()
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    PrefabPath);
            BusWorldView worldView =
                prefab.GetComponent<BusWorldView>();
            MethodInfo evaluatePoint =
                worldView.GetType().GetMethod(
                    "EvaluateQuadraticPoint",
                    BindingFlags.Static |
                    BindingFlags.NonPublic);
            Assert.That(evaluatePoint, Is.Not.Null);

            Vector3 start = new(1f, -2f, 0f);
            Vector3 driveway = new(0f, -1f, 0f);
            Vector3 parking = Vector3.zero;
            Vector3 halfway =
                (Vector3)evaluatePoint.Invoke(
                    null,
                    new object[]
                    {
                        start,
                        driveway,
                        parking,
                        0.5f
                    });

            Assert.That(
                halfway.x,
                Is.EqualTo(0.25f).Within(0.0001f),
                "Parking must bend through the driveway instead of cutting straight to the slot.");
            Assert.That(
                halfway.y,
                Is.EqualTo(-1f).Within(0.0001f));
        }


        [Test]
        public void ScheduledService_StartsOnlyAfterSchoolAndMorningWindow()
        {
            SimConfig config = SimConfig.Default();
            SimEventHub events = new();
            SimEngine engine = new(config, events);
            CityFlowServices services =
                new(
                    events,
                    engine,
                    engine,
                    null,
                    null,
                    engine);
            TestGameCalendar calendar = new(0L, 6);
            services.RegisterGameCalendar(calendar);

            GameObject registryObject =
                new("Scheduled School Bus Registry");
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    PrefabPath);
            GameObject instance = null;

            try
            {
                PlaceRoadRing(engine);
                Assert.That(
                    engine.Place(
                        new Vector2Int(8, 2),
                        TileType.School),
                    Is.True);
                Assert.That(
                    engine.Place(
                        new Vector2Int(2, 2),
                        TileType.House),
                    Is.True);
                engine.Tick(config.TickInterval);

                BusStopRegistry registry =
                    registryObject.AddComponent<
                        BusStopRegistry>();
                registry.Initialize(services);

                instance = Object.Instantiate(prefab);
                SchoolBusService service =
                    instance.GetComponent<
                        SchoolBusService>();
                service.Initialize(services);
                BusRoute route =
                    instance.GetComponent<BusRoute>();

                Assert.That(
                    service.State,
                    Is.EqualTo(
                        SchoolBusState.WaitingForSchedule));
                Assert.That(service.IsOperating, Is.False);
                Assert.That(
                    route.UseRoadsideStopApproach,
                    Is.True);
                Assert.That(
                    route.RoadsideStopSetbackTiles,
                    Is.EqualTo(1));
                Assert.That(
                    route.RoadsideStopFilter(
                        new Vector2Int(2, 2)),
                    Is.True);
                Assert.That(
                    route.RoadsideStopFilter(
                        new Vector2Int(8, 2)),
                    Is.False);

                calendar.SetTime(0L, 7);

                Assert.That(service.IsOperating, Is.True);
                Assert.That(
                    service.CurrentTrip,
                    Is.EqualTo(
                        SchoolBusTripKind.MorningCommute));
            }
            finally
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }

                Object.DestroyImmediate(registryObject);
            }
        }

        [Test]
        public void ScheduledService_RestoreInSameWindow_DoesNotStartTwice()
        {
            string savePath =
                System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"greenlight-school-bus-{System.Guid.NewGuid():N}.json");
            string backupPath = savePath + ".bak";
            SimConfig config = SimConfig.Default();
            SimEventHub firstEvents = new();
            SimEngine firstEngine =
                new(config, firstEvents);
            SaveService firstSave =
                new(firstEngine, null, null);
            CityFlowServices firstServices =
                new(
                    firstEvents,
                    firstEngine,
                    firstEngine,
                    firstSave,
                    null,
                    firstEngine);
            TestGameCalendar firstCalendar =
                new(0L, 7);
            firstServices.RegisterGameCalendar(firstCalendar);
            GameObject firstRegistryObject =
                new("First School Bus Registry");
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    PrefabPath);
            GameObject firstInstance = null;
            GameObject restoredRegistryObject = null;
            GameObject restoredInstance = null;

            try
            {
                PlaceRoadRing(firstEngine);
                Assert.That(
                    firstEngine.Place(
                        new Vector2Int(8, 2),
                        TileType.School),
                    Is.True);
                Assert.That(
                    firstEngine.Place(
                        new Vector2Int(2, 2),
                        TileType.House),
                    Is.True);
                firstEngine.Tick(config.TickInterval);

                BusStopRegistry firstRegistry =
                    firstRegistryObject.AddComponent<
                        BusStopRegistry>();
                firstRegistry.Initialize(firstServices);
                firstInstance = Object.Instantiate(prefab);
                SchoolBusService firstService =
                    firstInstance.GetComponent<
                        SchoolBusService>();
                firstService.Initialize(firstServices);

                Assert.That(firstService.StartService(), Is.True);
                Assert.That(firstService.IsOperating, Is.True);

                GameSaveData snapshot =
                    firstSave.CreateSnapshot();
                Assert.That(
                    snapshot.SchoolBus,
                    Is.Not.Null);
                Assert.That(
                    snapshot.SchoolBus.LastMorningTripDay,
                    Is.EqualTo(0L));

                SimEventHub restoredEvents = new();
                SimEngine restoredEngine =
                    new(config, restoredEvents);
                SaveService restoredSave =
                    new(
                        restoredEngine,
                        new JsonSaveRepository(
                            savePath,
                            backupPath),
                        null);
                CityFlowServices restoredServices =
                    new(
                        restoredEvents,
                        restoredEngine,
                        restoredEngine,
                        restoredSave,
                        null,
                        restoredEngine);
                TestGameCalendar restoredCalendar =
                    new(0L, 7);
                restoredServices.RegisterGameCalendar(
                    restoredCalendar);
                restoredRegistryObject =
                    new GameObject(
                        "Restored School Bus Registry");
                BusStopRegistry restoredRegistry =
                    restoredRegistryObject.AddComponent<
                        BusStopRegistry>();
                restoredRegistry.Initialize(
                    restoredServices);
                restoredInstance =
                    Object.Instantiate(prefab);
                SchoolBusService restoredService =
                    restoredInstance.GetComponent<
                        SchoolBusService>();
                restoredService.Initialize(
                    restoredServices);
                Assert.That(
                    restoredService.IsOperating,
                    Is.False,
                    "Scheduled dispatch must wait until save restoration has completed.");

                Assert.That(
                    restoredSave.Repository.TrySave(snapshot),
                    Is.True);
                Assert.That(
                    restoredSave.TryLoadAndRestore(),
                    Is.True);

                Assert.That(
                    restoredRegistry.TryGetFirstSchool(out _),
                    Is.True);
                Assert.That(
                    restoredRegistry.ResidentialStopCount,
                    Is.GreaterThan(0));
                Assert.That(
                    restoredService
                        .CreateSnapshot()
                        .LastMorningTripDay,
                    Is.EqualTo(0L));
                Assert.That(
                    restoredService.TryStartSchoolRoute(),
                    Is.False,
                    "Reloading during the same morning window must not dispatch a duplicate trip.");
                Assert.That(
                    restoredService.IsOperating,
                    Is.False);
            }
            finally
            {
                if (restoredInstance != null)
                {
                    Object.DestroyImmediate(
                        restoredInstance);
                }

                if (restoredRegistryObject != null)
                {
                    Object.DestroyImmediate(
                        restoredRegistryObject);
                }

                if (firstInstance != null)
                {
                    Object.DestroyImmediate(firstInstance);
                }

                Object.DestroyImmediate(
                    firstRegistryObject);

                if (System.IO.File.Exists(savePath))
                {
                    System.IO.File.Delete(savePath);
                }

                if (System.IO.File.Exists(backupPath))
                {
                    System.IO.File.Delete(backupPath);
                }

                string tempPath = savePath + ".tmp";
                if (System.IO.File.Exists(tempPath))
                {
                    System.IO.File.Delete(tempPath);
                }
            }
        }

        [Test]
        public void SchoolRoute_DoesNotTreatDepartureSchoolAsFinalReturn()
        {
            SimConfig config = SimConfig.Default();
            SimEventHub events = new();
            SimEngine engine = new(config, events);
            CityFlowServices services =
                new(
                    events,
                    engine,
                    engine,
                    null,
                    null,
                    engine);
            GameObject routeObject =
                new("School Route", typeof(BusRoute));

            try
            {
                PlaceRoadRing(engine);
                Vector2Int school =
                    new(8, 2);
                Vector2Int house =
                    new(2, 2);
                Assert.That(
                    engine.Place(school, TileType.School),
                    Is.True);
                Assert.That(
                    engine.Place(house, TileType.House),
                    Is.True);
                engine.Tick(config.TickInterval);

                BusRoute route =
                    routeObject.GetComponent<BusRoute>();
                route.Initialize(services);
                Assert.That(
                    route.ConfigureRoute(
                        new[]
                        {
                            school,
                            house,
                            school
                        },
                        false),
                    Is.True);
                Assert.That(route.StartRoute(), Is.True);

                MethodInfo moveOneTile =
                    typeof(BusRoute).GetMethod(
                        "MoveOneTile",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                Assert.That(moveOneTile, Is.Not.Null);
                moveOneTile.Invoke(route, null);

                Assert.That(
                    route.CurrentStopIndex,
                    Is.EqualTo(0),
                    "The departure school's access road must not be treated as the final school.");
                Assert.That(
                    route.State,
                    Is.EqualTo(BusRouteState.Moving));
                Assert.That(route.CurrentTile, Is.Not.EqualTo(school));
            }
            finally
            {
                Object.DestroyImmediate(routeObject);
            }
        }

        [Test]
        public void SchoolRoute_StopsBeforeResidentialParkingAccess()
        {
            SimConfig config = SimConfig.Default();
            SimEventHub events = new();
            SimEngine engine = new(config, events);
            CityFlowServices services =
                new(
                    events,
                    engine,
                    engine,
                    null,
                    null,
                    engine);
            GameObject routeObject =
                new("School Route", typeof(BusRoute));

            try
            {
                PlaceRoadRing(engine);
                Vector2Int school =
                    new(8, 2);
                Vector2Int house =
                    new(2, 2);
                Assert.That(
                    engine.Place(school, TileType.School),
                    Is.True);
                Assert.That(
                    engine.Place(house, TileType.House),
                    Is.True);
                engine.Tick(config.TickInterval);

                BusRoute route =
                    routeObject.GetComponent<BusRoute>();
                route.UseRoadsideStopApproach = true;
                route.RoadsideStopFilter =
                    stop => stop == house;
                route.Initialize(services);

                route.RoadsideStopSetbackTiles = 0;
                Assert.That(
                    route.ConfigureRoute(
                        new[]
                        {
                            school,
                            house,
                            school
                        },
                        false),
                    Is.True);
                Assert.That(route.StartRoute(), Is.True);
                Assert.That(
                    route.CurrentRoadPath.Count,
                    Is.GreaterThan(1));

                Vector2Int residentialAccessRoad =
                    route.CurrentRoadPath[
                        route.CurrentRoadPath.Count - 1];
                Vector2Int safeBoardingRoad =
                    route.CurrentRoadPath[
                        route.CurrentRoadPath.Count - 2];

                route.StopRoute();
                route.RoadsideStopSetbackTiles = 1;
                Assert.That(
                    route.ConfigureRoute(
                        new[]
                        {
                            school,
                            house,
                            school
                        },
                        false),
                    Is.True);
                Assert.That(route.StartRoute(), Is.True);

                Assert.That(
                    route.CurrentRoadPath[
                        route.CurrentRoadPath.Count - 1],
                    Is.EqualTo(safeBoardingRoad));
                Assert.That(
                    route.CurrentRoadPath,
                    Does.Not.Contains(
                        residentialAccessRoad),
                    "The school bus must leave the residential parking entrance clear.");

                AdvanceSchoolRouteToNextStop(route);

                Assert.That(
                    route.CurrentStop,
                    Is.EqualTo(house));
                Assert.That(
                    route.CurrentTile,
                    Is.EqualTo(safeBoardingRoad));

                AdvanceSchoolRouteAfterWait(route);
                Assert.That(
                    route.CurrentRoadPath[
                        route.CurrentRoadPath.Count - 1],
                    Is.EqualTo(school),
                    "The residential setback must not prevent the school bus from entering the school parking lot.");
            }
            finally
            {
                Object.DestroyImmediate(routeObject);
            }
        }

        [Test]
        public void Service_WaitsForRequiredBuildings_ThenRecovers()
        {
            SimConfig config = SimConfig.Default();
            SimEventHub events = new();
            SimEngine engine = new(config, events);
            CityFlowServices services =
                new(
                    events,
                    engine,
                    engine,
                    null,
                    null,
                    engine);

            GameObject registryObject =
                new("School Bus Registry");
            GameObject busObject =
                new(
                    "School Bus",
                    typeof(BusRoute),
                    typeof(SchoolBusService));

            try
            {
                PlaceRoadRing(engine);
                engine.Tick(config.TickInterval);

                BusStopRegistry registry =
                    registryObject.AddComponent<
                        BusStopRegistry>();
                SchoolBusService service =
                    busObject.GetComponent<
                        SchoolBusService>();

                registry.Initialize(services);
                service.Initialize(services);

                Assert.That(
                    service.State,
                    Is.EqualTo(
                        SchoolBusState.RouteUnavailable));
                Assert.That(
                    service.StartService(),
                    Is.False);

                Assert.That(
                    engine.Place(
                        new Vector2Int(8, 2),
                        TileType.School),
                    Is.True);
                engine.Tick(config.TickInterval);
                Assert.That(
                    service.State,
                    Is.EqualTo(
                        SchoolBusState.RouteUnavailable));

                Assert.That(
                    engine.Place(
                        new Vector2Int(2, 2),
                        TileType.House),
                    Is.True);
                engine.Tick(config.TickInterval);

                Assert.That(service.IsOperating, Is.True);
                Assert.That(
                    service.RouteStops,
                    Is.EqualTo(
                        new[]
                        {
                            new Vector2Int(8, 2),
                            new Vector2Int(2, 2),
                            new Vector2Int(8, 2)
                        }));
            }
            finally
            {
                Object.DestroyImmediate(busObject);
                Object.DestroyImmediate(registryObject);
            }
        }

        [Test]
        public void ResidentialBoarding_RespectsDefinitionCapacity()
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    PrefabPath);
            Assert.That(prefab, Is.Not.Null);

            GameObject instance =
                Object.Instantiate(prefab);

            try
            {
                SchoolBusService service =
                    instance.GetComponent<
                        SchoolBusService>();
                BusDefinitionSO definition =
                    AssetDatabase.LoadAssetAtPath<
                        BusDefinitionSO>(DefinitionPath);
                SetRuntime(service, definition);
                SetRouteStopsForBoarding(service);

                MethodInfo stopArrival =
                    typeof(SchoolBusService).GetMethod(
                        "OnStopArrived",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                Assert.That(stopArrival, Is.Not.Null);

                for (int i = 1; i <= 10; i++)
                {
                    stopArrival.Invoke(
                        service,
                        new object[]
                        {
                            new Vector2Int(i, 2),
                            i
                        });
                }

                Assert.That(
                    service.CurrentPassengers,
                    Is.EqualTo(
                        definition.PassengerCapacity));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void SetRuntime(
            SchoolBusService service,
            BusDefinitionSO definition)
        {
            PropertyInfo property =
                typeof(SchoolBusService).GetProperty(
                    "Runtime",
                    BindingFlags.Instance |
                    BindingFlags.Public);
            MethodInfo setter =
                property?.GetSetMethod(true);
            Assert.That(setter, Is.Not.Null);
            setter.Invoke(
                service,
                new object[]
                {
                    new BusRuntime(
                        definition.PassengerCapacity)
                });
        }

        private static void AdvanceSchoolRouteToNextStop(
            BusRoute route)
        {
            MethodInfo updateMoving =
                typeof(BusRoute).GetMethod(
                    "UpdateMoving",
                    BindingFlags.Instance |
                    BindingFlags.NonPublic);
            Assert.That(updateMoving, Is.Not.Null);
            updateMoving.Invoke(
                route,
                new object[] { 100f });
            Assert.That(
                route.State,
                Is.EqualTo(BusRouteState.WaitingAtStop));
        }

        private static void AdvanceSchoolRouteAfterWait(
            BusRoute route)
        {
            MethodInfo updateWaiting =
                typeof(BusRoute).GetMethod(
                    "UpdateWaiting",
                    BindingFlags.Instance |
                    BindingFlags.NonPublic);
            Assert.That(updateWaiting, Is.Not.Null);
            updateWaiting.Invoke(
                route,
                new object[] { 100f });
            Assert.That(
                route.State,
                Is.EqualTo(BusRouteState.Moving));
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
                $"Missing private field: {fieldName}");
            field.SetValue(target, value);
        }

        private static bool InvokeTrafficOverlap(
            MethodInfo method,
            Vector3 firstPosition,
            Vector3 firstDirection,
            Vector3 secondPosition,
            Vector3 secondDirection)
        {
            return (bool)method.Invoke(
                null,
                new object[]
                {
                    firstPosition,
                    firstDirection,
                    0.19f,
                    0.1f,
                    secondPosition,
                    secondDirection,
                    0.19f,
                    0.1f,
                    0.2f,
                    0.015f
                });
        }

        private static bool InvokeTrafficOverlap(
            MethodInfo method,
            Vector3 firstPosition,
            Vector3 firstDirection,
            Vector3 secondPosition,
            Vector3 secondDirection,
            float halfLength,
            float halfWidth)
        {
            return (bool)method.Invoke(
                null,
                new object[]
                {
                    firstPosition,
                    firstDirection,
                    halfLength,
                    halfWidth,
                    secondPosition,
                    secondDirection,
                    halfLength,
                    halfWidth,
                    0.2f,
                    0.015f
                });
        }

        private static void SetRouteStopsForBoarding(
            SchoolBusService service)
        {
            FieldInfo field =
                typeof(SchoolBusService).GetField(
                    "schoolRouteStops",
                    BindingFlags.Instance |
                    BindingFlags.NonPublic);
            List<Vector2Int> stops =
                field?.GetValue(service) as
                    List<Vector2Int>;
            Assert.That(stops, Is.Not.Null);

            stops.Add(Vector2Int.zero);
            for (int i = 1; i <= 10; i++)
            {
                stops.Add(new Vector2Int(i, 2));
            }
            stops.Add(Vector2Int.zero);
        }

        private static void PlaceRoadRing(
            SimEngine engine)
        {
            for (int x = 1; x <= 11; x++)
            {
                Assert.That(
                    engine.Place(
                        new Vector2Int(x, 1),
                        TileType.Road),
                    Is.True);
                Assert.That(
                    engine.Place(
                        new Vector2Int(x, 4),
                        TileType.Road),
                    Is.True);
            }

            for (int y = 2; y <= 3; y++)
            {
                Assert.That(
                    engine.Place(
                        new Vector2Int(1, y),
                        TileType.Road),
                    Is.True);
                Assert.That(
                    engine.Place(
                        new Vector2Int(11, y),
                        TileType.Road),
                    Is.True);
            }
        }

        private sealed class TestGameCalendar :
            IGameCalendarService
        {
            public TestGameCalendar(
                long totalDays,
                int hour)
            {
                TotalDays = totalDays;
                Hour = hour;
            }

            public int Year => 1;
            public int Month => 1;
            public int Day => (int)TotalDays + 1;
            public int Hour { get; private set; }
            public int TotalMonths => 0;
            public long TotalDays { get; private set; }
            public float RealSecondsPerGameHour => 1f;
            public float RealSecondsPerGameDay => 24f;
            public int HoursPerDay => 24;
            public float TimeOfDay01 => Hour / 24f;

            public event System.Action<int> HourChanged;
            public event System.Action<int> DayChanged;
            public event System.Action<int> MonthChanged
            {
                add { }
                remove { }
            }

            public void SetTime(long totalDays, int hour)
            {
                bool dayChanged = TotalDays != totalDays;
                TotalDays = totalDays;
                Hour = hour;
                HourChanged?.Invoke(hour);
                if (dayChanged)
                {
                    DayChanged?.Invoke(Day);
                }
            }
        }
    }
}
