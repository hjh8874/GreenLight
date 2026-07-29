using System.Collections.Generic;
using System.Reflection;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Content.Transit;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
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
                definition.VehicleLengthTiles,
                Is.EqualTo(0.64f).Within(0.0001f));
            Assert.That(
                definition.VehicleWidthTiles,
                Is.EqualTo(0.24f).Within(0.0001f));
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
                        else if (behaviour.GetType().FullName ==
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

        [Test]
        public void SchoolBusWorldView_UsesWorldGridCoordinateSpaceAndViewTrafficSettings()
        {
            GameObject cityRoot =
                new("SchoolBusCoordinateTest_City");
            GameObject busRoot =
                new("SchoolBusCoordinateTest_Bus");

            try
            {
                MainCityView cityView =
                    cityRoot.AddComponent<MainCityView>();
                SchoolBusWorldView worldView =
                    busRoot.AddComponent<SchoolBusWorldView>();
                BusDefinitionSO definition =
                    AssetDatabase.LoadAssetAtPath<
                        BusDefinitionSO>(DefinitionPath);

                SetPrivateField(
                    cityView,
                    "gridOrigin",
                    new Vector2Int(90, 90));
                SetPrivateField(cityView, "tileSize", 2f);
                SetPrivateField(cityView, "laneOffset", 0.18f);
                SetPrivateField(
                    cityView,
                    "vehicleMinHeadway",
                    0.72f);
                SetPrivateField(
                    worldView,
                    "cityView",
                    cityView);
                SetPrivateField(
                    worldView,
                    "definition",
                    definition);

                MethodInfo createLanePosition =
                    typeof(SchoolBusWorldView).GetMethod(
                        "CreateLanePosition",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                MethodInfo getMinimumHeadway =
                    typeof(SchoolBusWorldView).GetMethod(
                        "GetMinimumHeadway",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                Assert.That(createLanePosition, Is.Not.Null);
                Assert.That(getMinimumHeadway, Is.Not.Null);

                Vector3 position =
                    (Vector3)createLanePosition.Invoke(
                        worldView,
                        new object[]
                        {
                            new Vector2Int(92, 93),
                            Vector2.right
                        });
                float headway =
                    (float)getMinimumHeadway.Invoke(
                        worldView,
                        null);
                cityView.GetTrafficFootprint(
                    definition.VehicleLengthTiles,
                    definition.VehicleWidthTiles,
                    out float halfLength,
                    out float halfWidth);

                Assert.That(
                    position.x,
                    Is.EqualTo(5f).Within(0.0001f));
                Assert.That(
                    position.y,
                    Is.EqualTo(6.64f).Within(0.0001f));
                Assert.That(
                    headway,
                    Is.EqualTo(1.44f).Within(0.0001f));
                Assert.That(
                    halfLength,
                    Is.EqualTo(0.64f).Within(0.0001f));
                Assert.That(
                    halfWidth,
                    Is.EqualTo(0.24f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(busRoot);
                Object.DestroyImmediate(cityRoot);
            }
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
                view.GetTrafficFootprint(
                    0.38f,
                    0.2f,
                    out float carHalfLength,
                    out float carHalfWidth);
                view.UpdateExternalTrafficVehicle(
                    blockerOwner,
                    new Vector3(0.8f, -0.25f, 0f),
                    Vector3.right,
                    0f,
                    true,
                    Vector2Int.zero,
                    true,
                    carHalfLength,
                    carHalfWidth);

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
                    true,
                    carHalfLength,
                    carHalfWidth);

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
                view.GetTrafficFootprint(
                    0.38f,
                    0.2f,
                    out float carHalfLength,
                    out float carHalfWidth);
                view.GetTrafficFootprint(
                    0.64f,
                    0.24f,
                    out float busHalfLength,
                    out float busHalfWidth);
                view.UpdateExternalTrafficVehicle(
                    blockerOwner,
                    new Vector3(0.6f, -0.25f, 0f),
                    Vector3.right,
                    0f,
                    true,
                    Vector2Int.zero,
                    true,
                    carHalfLength,
                    carHalfWidth);

                Assert.That(
                    view.CanExternalTrafficMoveVisual(
                        busOwner,
                        new Vector3(0f, -0.25f, 0f),
                        new Vector3(0.1f, -0.25f, 0f),
                        Vector3.right,
                        0.55f,
                        busHalfLength,
                        busHalfWidth),
                    Is.False,
                    "The moving bus must detect every vehicle inside its forward safety distance before the bodies overlap.");
                Assert.That(
                    view.LimitExternalTrafficVisualAdvance(
                        busOwner,
                        new Vector3(0f, -0.25f, 0f),
                        new Vector3(0.1f, -0.25f, 0f),
                        Vector3.right,
                        0.55f,
                        busHalfLength,
                        busHalfWidth),
                    Is.EqualTo(0.05f).Within(0.0001f),
                    "The bus must use the shared headway limiter and advance only into the remaining safe distance.");

                view.UpdateExternalTrafficVehicle(
                    blockerOwner,
                    new Vector3(5f, -0.25f, 0f),
                    Vector3.right,
                    0f,
                    true,
                    new Vector2Int(5, 0),
                    true,
                    carHalfLength,
                    carHalfWidth);

                Assert.That(
                    view.CanExternalTrafficMoveVisual(
                        busOwner,
                        new Vector3(0f, -0.25f, 0f),
                        new Vector3(0.1f, -0.25f, 0f),
                        Vector3.right,
                        0.55f,
                        busHalfLength,
                        busHalfWidth),
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
        public void ExternalTrafficVisualAdvance_UsesManagedLaneLeader()
        {
            GameObject viewObject =
                new("Traffic View");
            viewObject.SetActive(false);
            GameObject busOwner =
                new("School Bus Owner");
            GameObject managedCarObject =
                new("Managed Car");

            try
            {
                MainCityView view =
                    viewObject.AddComponent<MainCityView>();
                System.Type routeVehicleType =
                    typeof(MainCityView).GetNestedType(
                        "RouteVehicle",
                        BindingFlags.NonPublic);
                Assert.That(routeVehicleType, Is.Not.Null);
                object managedCar =
                    System.Activator.CreateInstance(
                        routeVehicleType);
                routeVehicleType.GetField(
                        "Object",
                        BindingFlags.Instance |
                        BindingFlags.Public)
                    ?.SetValue(
                        managedCar,
                        managedCarObject);
                routeVehicleType.GetField(
                        "Pos",
                        BindingFlags.Instance |
                        BindingFlags.Public)
                    ?.SetValue(
                        managedCar,
                        new Vector3(
                            0.6f,
                            -0.25f,
                            0f));
                routeVehicleType.GetField(
                        "Dir",
                        BindingFlags.Instance |
                        BindingFlags.Public)
                    ?.SetValue(
                        managedCar,
                        Vector3.right);
                routeVehicleType.GetField(
                        "HasCurrentTile",
                        BindingFlags.Instance |
                        BindingFlags.Public)
                    ?.SetValue(
                        managedCar,
                        true);

                FieldInfo vehiclesField =
                    typeof(MainCityView).GetField(
                        "vehicles",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                System.Collections.IList managedVehicles =
                    vehiclesField?.GetValue(view) as
                        System.Collections.IList;
                Assert.That(managedVehicles, Is.Not.Null);
                managedVehicles.Add(managedCar);

                view.GetTrafficFootprint(
                    0.64f,
                    0.24f,
                    out float busHalfLength,
                    out float busHalfWidth);
                float allowedAdvance =
                    view.LimitExternalTrafficVisualAdvance(
                        busOwner,
                        new Vector3(0f, -0.25f, 0f),
                        new Vector3(0.1f, -0.25f, 0f),
                        Vector3.right,
                        0.55f,
                        busHalfLength,
                        busHalfWidth);

                Assert.That(
                    allowedAdvance,
                    Is.EqualTo(0.05f).Within(0.0001f),
                    "Feature buses must follow managed cars through the same minimum-headway limiter.");
            }
            finally
            {
                Object.DestroyImmediate(managedCarObject);
                Object.DestroyImmediate(busOwner);
                Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void ManagedTrafficSweep_OnlyUsesExternalTrafficObstacles()
        {
            GameObject viewObject =
                new("Traffic View");
            viewObject.SetActive(false);
            GameObject managedBlockerObject =
                new("Managed Car");
            GameObject externalBlockerOwner =
                new("School Bus");

            try
            {
                MainCityView view =
                    viewObject.AddComponent<MainCityView>();
                RoutePolyline path =
                    RoutePolyline.Bake(
                        new BakeInput
                        {
                            Tiles = new[]
                            {
                                new Vector2Int(0, 0),
                                new Vector2Int(1, 0),
                                new Vector2Int(2, 0)
                            },
                            TileSize = 1f,
                            LaneOffset = 0.18f,
                            CornerRadiusFraction = 0.75f,
                            OrbitRadius = 0.775f,
                            EntryExitOffsetRad =
                                45f * Mathf.Deg2Rad,
                            TransitionLength =
                                RoutePolyline
                                    .MinTransitionSpan,
                            Z = -0.35f,
                            IsRoundabout = _ => false,
                            SamplesPerSegment = 8
                        });

                System.Type routeVehicleType =
                    typeof(MainCityView).GetNestedType(
                        "RouteVehicle",
                        BindingFlags.NonPublic);
                Assert.That(routeVehicleType, Is.Not.Null);
                object subject =
                    System.Activator.CreateInstance(
                        routeVehicleType);
                object managedBlocker =
                    System.Activator.CreateInstance(
                        routeVehicleType);
                Sample blockerSample =
                    path.SampleAt(0.5f);
                routeVehicleType.GetField(
                        "Object",
                        BindingFlags.Instance |
                        BindingFlags.Public)
                    ?.SetValue(
                        managedBlocker,
                        managedBlockerObject);
                routeVehicleType.GetField(
                        "Pos",
                        BindingFlags.Instance |
                        BindingFlags.Public)
                    ?.SetValue(
                        managedBlocker,
                        blockerSample.Pos);
                routeVehicleType.GetField(
                        "Dir",
                        BindingFlags.Instance |
                        BindingFlags.Public)
                    ?.SetValue(
                        managedBlocker,
                        blockerSample.Dir);
                routeVehicleType.GetField(
                        "HasCurrentTile",
                        BindingFlags.Instance |
                        BindingFlags.Public)
                    ?.SetValue(
                        managedBlocker,
                        true);

                FieldInfo vehiclesField =
                    typeof(MainCityView).GetField(
                        "vehicles",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                System.Collections.IList managedVehicles =
                    vehiclesField?.GetValue(view) as
                        System.Collections.IList;
                Assert.That(managedVehicles, Is.Not.Null);
                managedVehicles.Add(managedBlocker);

                MethodInfo limitTravel =
                    typeof(MainCityView).GetMethod(
                        "LimitVehicleTravelDistance",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                Assert.That(limitTravel, Is.Not.Null);
                float desiredDistance = path.Length;
                float withoutExternalTraffic =
                    (float)limitTravel.Invoke(
                        view,
                        new[]
                        {
                            subject,
                            path,
                            (object)0f,
                            desiredDistance
                        });

                Assert.That(
                    withoutExternalTraffic,
                    Is.EqualTo(desiredDistance)
                        .Within(0.0001f),
                    "Managed cars must keep using the lane-order model instead of an all-pairs body sweep.");

                view.UpdateExternalTrafficVehicle(
                    externalBlockerOwner,
                    blockerSample.Pos,
                    blockerSample.Dir,
                    0f,
                    true,
                    Vector2Int.zero,
                    true,
                    0.19f,
                    0.1f);
                float withExternalTraffic =
                    (float)limitTravel.Invoke(
                        view,
                        new[]
                        {
                            subject,
                            path,
                            (object)0f,
                            desiredDistance
                        });

                Assert.That(
                    withExternalTraffic,
                    Is.LessThan(desiredDistance),
                    "Managed cars must still stop before an active school bus.");
            }
            finally
            {
                Object.DestroyImmediate(externalBlockerOwner);
                Object.DestroyImmediate(managedBlockerObject);
                Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void SchoolBusAndManagedCarCrossing_UsesSingleStablePriority()
        {
            GameObject viewObject =
                new("Traffic View");
            viewObject.SetActive(false);
            GameObject managedCarObject =
                new("Managed Car");
            GameObject schoolBusOwner =
                new("School Bus");

            try
            {
                MainCityView view =
                    viewObject.AddComponent<MainCityView>();
                System.Type routeVehicleType =
                    typeof(MainCityView).GetNestedType(
                        "RouteVehicle",
                        BindingFlags.NonPublic);
                Assert.That(routeVehicleType, Is.Not.Null);
                object managedCar =
                    System.Activator.CreateInstance(
                        routeVehicleType);
                routeVehicleType.GetField(
                        "Object",
                        BindingFlags.Instance |
                        BindingFlags.Public)
                    ?.SetValue(
                        managedCar,
                        managedCarObject);
                routeVehicleType.GetField(
                        "Pos",
                        BindingFlags.Instance |
                        BindingFlags.Public)
                    ?.SetValue(
                        managedCar,
                        new Vector3(-0.35f, 0f, 0f));
                routeVehicleType.GetField(
                        "Dir",
                        BindingFlags.Instance |
                        BindingFlags.Public)
                    ?.SetValue(
                        managedCar,
                        Vector3.right);
                routeVehicleType.GetField(
                        "HasCurrentTile",
                        BindingFlags.Instance |
                        BindingFlags.Public)
                    ?.SetValue(
                        managedCar,
                        true);

                FieldInfo vehiclesField =
                    typeof(MainCityView).GetField(
                        "vehicles",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                System.Collections.IList managedVehicles =
                    vehiclesField?.GetValue(view) as
                        System.Collections.IList;
                Assert.That(managedVehicles, Is.Not.Null);
                managedVehicles.Add(managedCar);

                view.UpdateExternalTrafficVehicle(
                    schoolBusOwner,
                    new Vector3(0f, -0.35f, 0f),
                    Vector3.up,
                    0f,
                    true,
                    Vector2Int.zero,
                    true,
                    0.1f,
                    0.05f);

                MethodInfo isFootprintClear =
                    typeof(MainCityView).GetMethod(
                        "IsTrafficFootprintClear",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                Assert.That(isFootprintClear, Is.Not.Null);

                bool managedWhenFirst =
                    (bool)isFootprintClear.Invoke(
                        view,
                        new[]
                        {
                            managedCar,
                            null,
                            (object)new Vector3(-0.16f, 0f, 0f),
                            Vector3.right,
                            0.1f,
                            0.05f
                        });
                bool schoolWhenSecond =
                    (bool)isFootprintClear.Invoke(
                        view,
                        new object[]
                        {
                            null,
                            schoolBusOwner,
                            new Vector3(0f, -0.16f, 0f),
                            Vector3.up,
                            0.1f,
                            0.05f
                        });
                bool schoolWhenFirst =
                    (bool)isFootprintClear.Invoke(
                        view,
                        new object[]
                        {
                            null,
                            schoolBusOwner,
                            new Vector3(0f, -0.16f, 0f),
                            Vector3.up,
                            0.1f,
                            0.05f
                        });
                bool managedWhenSecond =
                    (bool)isFootprintClear.Invoke(
                        view,
                        new[]
                        {
                            managedCar,
                            null,
                            (object)new Vector3(-0.16f, 0f, 0f),
                            Vector3.right,
                            0.1f,
                            0.05f
                        });

                Assert.That(
                    managedWhenFirst,
                    Is.EqualTo(managedWhenSecond));
                Assert.That(
                    schoolWhenFirst,
                    Is.EqualTo(schoolWhenSecond));
                Assert.That(
                    managedWhenFirst,
                    Is.Not.EqualTo(schoolWhenFirst),
                    "Exactly one crossing vehicle must receive right of way.");

                bool managedHasPriority =
                    VehicleSpacingMath
                        .HasTrafficConflictPriority(
                            false,
                            managedCarObject.GetEntityId(),
                            false,
                            schoolBusOwner.GetEntityId());
                Assert.That(
                    managedWhenFirst,
                    Is.EqualTo(managedHasPriority));

                if (managedHasPriority)
                {
                    routeVehicleType.GetField(
                            "Pos",
                            BindingFlags.Instance |
                            BindingFlags.Public)
                        ?.SetValue(
                            managedCar,
                            new Vector3(0.3f, 0f, 0f));
                    schoolWhenSecond =
                        (bool)isFootprintClear.Invoke(
                            view,
                            new object[]
                            {
                                null,
                                schoolBusOwner,
                                Vector3.zero,
                                Vector3.up,
                                0.1f,
                                0.05f
                            });
                    Assert.That(schoolWhenSecond, Is.True);
                }
                else
                {
                    view.UpdateExternalTrafficVehicle(
                        schoolBusOwner,
                        new Vector3(0f, 0.3f, 0f),
                        Vector3.up,
                        0f,
                        true,
                        Vector2Int.zero,
                        true,
                        0.1f,
                        0.05f);
                    managedWhenSecond =
                        (bool)isFootprintClear.Invoke(
                            view,
                            new[]
                            {
                                managedCar,
                                null,
                                (object)Vector3.zero,
                                Vector3.right,
                                0.1f,
                                0.05f
                            });
                    Assert.That(managedWhenSecond, Is.True);
                }
            }
            finally
            {
                Object.DestroyImmediate(schoolBusOwner);
                Object.DestroyImmediate(managedCarObject);
                Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void SchoolBusAndCityBusCrossing_AdvancesContinuouslyWithoutOverlap()
        {
            GameObject viewObject =
                new("Traffic View");
            viewObject.SetActive(false);
            GameObject schoolBusOwner =
                new("School Bus");
            GameObject cityBusOwner =
                new("City Bus");

            try
            {
                MainCityView view =
                    viewObject.AddComponent<MainCityView>();
                Vector3 schoolPosition =
                    new(-0.35f, 0f, 0f);
                Vector3 cityPosition =
                    new(0f, -0.35f, 0f);
                const float frameAdvance = 0.04f;
                const float halfLength = 0.1f;
                const float halfWidth = 0.05f;
                MethodInfo overlaps =
                    typeof(MainCityView).GetMethod(
                        "TrafficFootprintsOverlap",
                        BindingFlags.Static |
                        BindingFlags.NonPublic);
                Assert.That(overlaps, Is.Not.Null);

                view.UpdateExternalTrafficVehicle(
                    schoolBusOwner,
                    schoolPosition,
                    Vector3.right,
                    0f,
                    true,
                    Vector2Int.left,
                    true,
                    halfLength,
                    halfWidth);
                view.UpdateExternalTrafficVehicle(
                    cityBusOwner,
                    cityPosition,
                    Vector3.up,
                    0f,
                    true,
                    Vector2Int.down,
                    true,
                    halfLength,
                    halfWidth);

                bool schoolHasPriority =
                    VehicleSpacingMath
                        .HasTrafficConflictPriority(
                            false,
                            schoolBusOwner.GetEntityId(),
                            false,
                            cityBusOwner.GetEntityId());
                for (int frame = 0; frame < 48; frame++)
                {
                    if (schoolHasPriority)
                    {
                        AdvanceExternalTraffic(
                            view,
                            schoolBusOwner,
                            ref schoolPosition,
                            Vector3.right,
                            frameAdvance,
                            halfLength,
                            halfWidth);
                        AdvanceExternalTraffic(
                            view,
                            cityBusOwner,
                            ref cityPosition,
                            Vector3.up,
                            frameAdvance,
                            halfLength,
                            halfWidth);
                    }
                    else
                    {
                        AdvanceExternalTraffic(
                            view,
                            cityBusOwner,
                            ref cityPosition,
                            Vector3.up,
                            frameAdvance,
                            halfLength,
                            halfWidth);
                        AdvanceExternalTraffic(
                            view,
                            schoolBusOwner,
                            ref schoolPosition,
                            Vector3.right,
                            frameAdvance,
                            halfLength,
                            halfWidth);
                    }

                    Assert.That(
                        InvokeTrafficOverlap(
                            overlaps,
                            schoolPosition,
                            Vector3.right,
                            cityPosition,
                            Vector3.up,
                            halfLength,
                            halfWidth),
                        Is.False,
                        $"Vehicle bodies overlapped on frame {frame}.");
                }

                Assert.That(
                    schoolPosition.x,
                    Is.GreaterThanOrEqualTo(0.34f));
                Assert.That(
                    cityPosition.y,
                    Is.GreaterThanOrEqualTo(0.34f));
            }
            finally
            {
                Object.DestroyImmediate(cityBusOwner);
                Object.DestroyImmediate(schoolBusOwner);
                Object.DestroyImmediate(viewObject);
            }
        }

        private static void AdvanceExternalTraffic(
            MainCityView view,
            Object owner,
            ref Vector3 position,
            Vector3 direction,
            float proposedAdvance,
            float halfLength,
            float halfWidth)
        {
            Vector3 target =
                position +
                direction * proposedAdvance;
            float allowedAdvance =
                view.LimitExternalTrafficVisualAdvance(
                    owner,
                    position,
                    target,
                    direction,
                    0.05f,
                    halfLength,
                    halfWidth);
            position +=
                direction *
                Mathf.Min(proposedAdvance, allowedAdvance);
            view.UpdateExternalTrafficVehicle(
                owner,
                position,
                direction,
                allowedAdvance,
                true,
                Vector2Int.zero,
                true,
                halfLength,
                halfWidth);
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
        public void ExternalTraffic_OppositeLaneBusIsNotAForwardLeader()
        {
            GameObject viewObject =
                new("Traffic View");
            viewObject.SetActive(false);
            GameObject busOwner =
                new("Opposite Lane Bus");

            try
            {
                MainCityView view =
                    viewObject.AddComponent<MainCityView>();
                RoutePolyline path =
                    RoutePolyline.Bake(
                        new BakeInput
                        {
                            Tiles = new[]
                            {
                                new Vector2Int(0, 0),
                                new Vector2Int(1, 0),
                                new Vector2Int(2, 0)
                            },
                            TileSize = 1f,
                            LaneOffset = 0.18f,
                            CornerRadiusFraction = 0.75f,
                            OrbitRadius = 0.775f,
                            EntryExitOffsetRad =
                                45f * Mathf.Deg2Rad,
                            TransitionLength =
                                RoutePolyline
                                    .MinTransitionSpan,
                            Z = -0.35f,
                            IsRoundabout = _ => false,
                            SamplesPerSegment = 8
                        });

                System.Type routeVehicleType =
                    typeof(MainCityView).GetNestedType(
                        "RouteVehicle",
                        BindingFlags.NonPublic);
                Assert.That(
                    routeVehicleType,
                    Is.Not.Null);
                object follower =
                    System.Activator.CreateInstance(
                        routeVehicleType);
                Sample followerSample =
                    path.SampleAt(0f);
                routeVehicleType.GetField(
                        "Dir",
                        BindingFlags.Instance |
                        BindingFlags.Public)
                    ?.SetValue(
                        follower,
                        followerSample.Dir);
                routeVehicleType.GetField(
                        "Pos",
                        BindingFlags.Instance |
                        BindingFlags.Public)
                    ?.SetValue(
                        follower,
                        followerSample.Pos);

                view.UpdateExternalTrafficVehicle(
                    busOwner,
                    new Vector3(
                        1.5f,
                        0.68f,
                        -0.35f),
                    Vector3.left,
                    0f,
                    true,
                    new Vector2Int(1, 0),
                    true,
                    0.32f,
                    0.12f);

                MethodInfo getHeadway =
                    typeof(MainCityView).GetMethod(
                        "TryGetExternalTrafficHeadway",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                Assert.That(getHeadway, Is.Not.Null);
                object[] arguments =
                {
                    follower,
                    path,
                    0f,
                    0f,
                    0f
                };

                bool found =
                    (bool)getHeadway.Invoke(
                        view,
                        arguments);

                Assert.That(
                    found,
                    Is.False,
                    "Opposite-lane traffic must not stop a car as its forward leader.");
            }
            finally
            {
                Object.DestroyImmediate(busOwner);
                Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void SchoolBus_IsTrafficOnlyWhileOccupyingTheRoad()
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

            bool operatingOnRoad =
                (bool)shouldPublish.Invoke(
                    null,
                    new object[]
                    {
                        true,
                        true,
                        true
                    });
            bool parkedOffRoad =
                (bool)shouldPublish.Invoke(
                    null,
                    new object[]
                    {
                        true,
                        true,
                        false
                    });
            bool unavailable =
                (bool)shouldPublish.Invoke(
                    null,
                    new object[]
                    {
                        false,
                        true,
                        true
                    });

            Assert.That(
                operatingOnRoad,
                Is.True,
                "An operating school bus must participate in road collision avoidance.");
            Assert.That(
                parkedOffRoad,
                Is.False,
                "A school bus fully parked in the school lot must not remain a road obstacle.");
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
