using System.Collections.Generic;
using System.Reflection;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Content.Transit;
using CityFlow.Contracts;
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
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(
                    MaterialPath);
            SceneAsset scene =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    ScenePath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(definition, Is.Not.Null);
            Assert.That(schedule, Is.Not.Null);
            Assert.That(material, Is.Not.Null);
            Assert.That(scene, Is.Not.Null);
            Assert.That(
                definition.BusType,
                Is.EqualTo(BusType.SchoolBus));
            Assert.That(
                definition.VehicleVisualPrefab,
                Is.Not.Null);
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
                prefab.GetComponent(
                    "SchoolBusWorldView"),
                Is.Not.Null);
            Component worldView =
                prefab.GetComponent("SchoolBusWorldView");
            SerializedObject worldViewSerialized =
                new(worldView);
            Assert.That(
                worldViewSerialized
                    .FindProperty("laneOffset")
                    .floatValue,
                Is.EqualTo(0.25f).Within(0.0001f),
                "The school bus must use the Geon scene's right-hand lane center.");
            Assert.That(
                worldViewSerialized
                    .FindProperty("minimumHeadway")
                    .floatValue,
                Is.EqualTo(0.55f).Within(0.0001f));
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
                Is.Null,
                "The school bus must reuse the merged shared registry.");
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
                        if (behaviour.GetType().FullName ==
                            "CityFlow.Gameplay.Progression.GameCalendarService")
                        {
                            hasPrototypeCalendar =
                                serialized.FindProperty(
                                    "startHour").intValue == 7 &&
                                serialized.FindProperty(
                                    "realSecondsPerGameHour")
                                    .floatValue <= 1f;
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

        [TestCase(1f, 0f, 0f, -0.25f)]
        [TestCase(-1f, 0f, 0f, 0.25f)]
        [TestCase(0f, 1f, 0.25f, 0f)]
        [TestCase(0f, -1f, -0.25f, 0f)]
        public void SchoolBusLaneOffset_UsesDrivingRightForEveryDirection(
            float directionX,
            float directionY,
            float expectedX,
            float expectedY)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    PrefabPath);
            Component worldView =
                prefab.GetComponent("SchoolBusWorldView");
            MethodInfo getRightLaneOffset =
                worldView.GetType().GetMethod(
                    "GetRightLaneOffset",
                    BindingFlags.Static |
                    BindingFlags.NonPublic);
            Assert.That(getRightLaneOffset, Is.Not.Null);

            Vector3 actual =
                (Vector3)getRightLaneOffset.Invoke(
                    null,
                    new object[]
                    {
                        new Vector2(directionX, directionY),
                        0.25f
                    });

            Assert.That(
                actual.x,
                Is.EqualTo(expectedX).Within(0.0001f));
            Assert.That(
                actual.y,
                Is.EqualTo(expectedY).Within(0.0001f));
        }

        [TestCase(0.05f, 0.2f, 0.25f)]
        [TestCase(0.1f, 0.2f, 0.5f)]
        [TestCase(0.2f, 0.2f, 1f)]
        public void SchoolBusMovement_UsesConstantProgressBetweenTiles(
            float elapsed,
            float duration,
            float expected)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    PrefabPath);
            Component worldView =
                prefab.GetComponent("SchoolBusWorldView");
            MethodInfo evaluateProgress =
                worldView.GetType().GetMethod(
                    "EvaluateMovementProgress",
                    BindingFlags.Static |
                    BindingFlags.NonPublic);
            Assert.That(evaluateProgress, Is.Not.Null);

            float actual =
                (float)evaluateProgress.Invoke(
                    null,
                    new object[]
                    {
                        elapsed,
                        duration
                    });

            Assert.That(
                actual,
                Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void SchoolBusParkingCurve_UsesDrivewayBeforeCenterSlot()
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    PrefabPath);
            Component worldView =
                prefab.GetComponent("SchoolBusWorldView");
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
        public void ExternalTraffic_BlocksSchoolBusUntilLaneIsClear()
        {
            GameObject viewObject =
                new("Traffic View");
            viewObject.SetActive(false);
            GameObject busOwner =
                new("School Bus Owner");
            GameObject blockerOwner =
                new("Car Owner");

            try
            {
                MainCityView view =
                    viewObject.AddComponent<MainCityView>();
                view.UpdateExternalTrafficVehicle(
                    blockerOwner,
                    new Vector3(0.8f, -0.25f, 0f),
                    Vector3.right,
                    0f,
                    true,
                    Vector2Int.zero,
                    true);

                Assert.That(
                    view.CanExternalTrafficAdvance(
                        busOwner,
                        new Vector3(0f, -0.25f, 0f),
                        new Vector3(1f, -0.25f, 0f),
                        Vector3.right,
                        0.55f,
                        Vector2Int.right),
                    Is.False);

                view.UpdateExternalTrafficVehicle(
                    blockerOwner,
                    new Vector3(5f, -0.25f, 0f),
                    Vector3.right,
                    0f,
                    true,
                    new Vector2Int(5, 0),
                    true);

                Assert.That(
                    view.CanExternalTrafficAdvance(
                        busOwner,
                        new Vector3(0f, -0.25f, 0f),
                        new Vector3(1f, -0.25f, 0f),
                        Vector3.right,
                        0.55f,
                        Vector2Int.right),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(blockerOwner);
                Object.DestroyImmediate(busOwner);
                Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void ExternalTraffic_BlocksSchoolBusDuringVisualMovement()
        {
            GameObject viewObject =
                new("Traffic View");
            viewObject.SetActive(false);
            GameObject busOwner =
                new("School Bus Owner");
            GameObject blockerOwner =
                new("Car Owner");

            try
            {
                MainCityView view =
                    viewObject.AddComponent<MainCityView>();
                view.UpdateExternalTrafficVehicle(
                    blockerOwner,
                    new Vector3(0.5f, -0.25f, 0f),
                    Vector3.right,
                    0f,
                    true,
                    Vector2Int.zero,
                    true);

                Assert.That(
                    view.CanExternalTrafficMoveVisual(
                        busOwner,
                        new Vector3(0f, -0.25f, 0f),
                        new Vector3(0.1f, -0.25f, 0f),
                        Vector3.right,
                        0.55f),
                    Is.False,
                    "The bus must recheck the lane while its visual is moving.");

                view.UpdateExternalTrafficVehicle(
                    blockerOwner,
                    new Vector3(5f, -0.25f, 0f),
                    Vector3.right,
                    0f,
                    true,
                    new Vector2Int(5, 0),
                    true);

                Assert.That(
                    view.CanExternalTrafficMoveVisual(
                        busOwner,
                        new Vector3(0f, -0.25f, 0f),
                        new Vector3(0.1f, -0.25f, 0f),
                        Vector3.right,
                        0.55f),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(blockerOwner);
                Object.DestroyImmediate(busOwner);
                Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void ExternalTraffic_PathHeadwayFindsStoppedBusAfterCorner()
        {
            RoutePolyline path =
                RoutePolyline.Bake(
                    new BakeInput
                    {
                        Tiles = new[]
                        {
                            new Vector2Int(0, 0),
                            new Vector2Int(1, 0),
                            new Vector2Int(1, 1)
                        },
                        TileSize = 1f,
                        LaneOffset = 0.18f,
                        CornerRadiusFraction = 0.75f,
                        OrbitRadius = 0.775f,
                        EntryExitOffsetRad = 45f * Mathf.Deg2Rad,
                        TransitionLength =
                            RoutePolyline.MinTransitionSpan,
                        Z = -0.35f,
                        IsRoundabout = _ => false,
                        SamplesPerSegment = 8
                    });
            float followerDistance = 0f;
            Vector3 stoppedBusPosition =
                path.SampleAt(path.Length - 0.2f).Pos;
            MethodInfo findPathHeadway =
                typeof(MainCityView).GetMethod(
                    "TryGetForwardPathHeadway",
                    BindingFlags.Static |
                    BindingFlags.NonPublic);
            Assert.That(findPathHeadway, Is.Not.Null);

            object[] arguments =
            {
                path,
                followerDistance,
                stoppedBusPosition,
                path.Length,
                0.36f,
                0.125f,
                0f
            };
            bool found =
                (bool)findPathHeadway.Invoke(
                    null,
                    arguments);
            float headway = (float)arguments[6];

            Assert.That(
                found,
                Is.True,
                "A stopped bus after a corner must remain part of the car's forward path.");
            Assert.That(headway, Is.GreaterThan(0f));
            Assert.That(headway, Is.LessThan(path.Length));
        }

        [Test]
        public void ParkedSchoolBus_RemainsRegisteredAsTraffic()
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    PrefabPath);
            Component worldView =
                prefab.GetComponent("SchoolBusWorldView");
            Assert.That(worldView, Is.Not.Null);
            MethodInfo shouldPublish =
                worldView.GetType().GetMethod(
                    "ShouldPublishAsTraffic",
                    BindingFlags.Static |
                    BindingFlags.NonPublic);
            Assert.That(shouldPublish, Is.Not.Null);

            bool parkedAndVisible =
                (bool)shouldPublish.Invoke(
                    null,
                    new object[]
                    {
                        true,
                        true
                    });
            bool unavailable =
                (bool)shouldPublish.Invoke(
                    null,
                    new object[]
                    {
                        false,
                        true
                    });

            Assert.That(
                parkedAndVisible,
                Is.True,
                "A visible parked bus must keep blocking vehicles after its parking animation ends.");
            Assert.That(unavailable, Is.False);
        }

        [Test]
        public void TrafficFootprints_BlockOverlapWithoutBlockingOppositeLane()
        {
            MethodInfo overlaps =
                typeof(MainCityView).GetMethod(
                    "TrafficFootprintsOverlap",
                    BindingFlags.Static |
                    BindingFlags.NonPublic);
            Assert.That(overlaps, Is.Not.Null);

            bool sameLaneOverlap =
                InvokeTrafficOverlap(
                    overlaps,
                    Vector3.zero,
                    Vector3.right,
                    new Vector3(0.35f, 0f, 0f),
                    Vector3.right);
            bool oppositeLaneClear =
                InvokeTrafficOverlap(
                    overlaps,
                    Vector3.zero,
                    Vector3.right,
                    new Vector3(0f, 0.36f, 0f),
                    Vector3.left);
            bool crossingOverlap =
                InvokeTrafficOverlap(
                    overlaps,
                    Vector3.zero,
                    Vector3.right,
                    Vector3.zero,
                    Vector3.up);
            bool overpassClear =
                InvokeTrafficOverlap(
                    overlaps,
                    Vector3.zero,
                    Vector3.right,
                    new Vector3(0f, 0f, 0.3f),
                    Vector3.up);

            Assert.That(sameLaneOverlap, Is.True);
            Assert.That(oppositeLaneClear, Is.False);
            Assert.That(crossingOverlap, Is.True);
            Assert.That(overpassClear, Is.False);
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

                Assert.That(
                    service.State,
                    Is.EqualTo(
                        SchoolBusState.WaitingForSchedule));
                Assert.That(service.IsOperating, Is.False);

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
