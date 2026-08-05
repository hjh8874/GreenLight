using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CityFlow.Bootstrap;
using CityFlow.Buildings;
using CityFlow.Configs;
using CityFlow.Content;
using CityFlow.Content.Transit;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.DebugTools;
using CityFlow.Environment;
using CityFlow.Gameplay.Progression;
using CityFlow.Gameplay.Research;
using CityFlow.UI;
using CityFlow.UI.Controllers;
using CityFlow.UI.Controllers.Placement;
using CityFlow.UI.Data;
using CityFlow.View;
using CityFlow.WorldCoordinates;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CityFlow.Tests.ViewEditMode
{
    public sealed class MergedFeatureIntegrationPlayModeTests
    {
        private const string IntegrationScenePath =
            "Assets/00_Scenes/CityFlowIntegrated_cmt.unity";
        private const string ScreenshotPath =
            "Logs/MergedFeatureIntegrationPlay.png";
        private const string SpecialBuildingScreenshotPath =
            "Logs/SpecialBuildingFallbackIntegrationPlay.png";
        private const string IntegratedSimConfigPath =
            "Assets/05_ScriptableObjects/SimConfig_Integrated.asset";
        private const string GameTimeSettingsPath =
            "Assets/05_ScriptableObjects/Resources/CityFlow/GameTimeSettings.asset";

        [UnityTest]
        public IEnumerator IntegrationScene_MergedFeaturesWorkInPlayMode()
        {
            Scene scene =
                EditorSceneManager.OpenScene(
                    IntegrationScenePath,
                    OpenSceneMode.Single);
            Assert.That(scene.IsValid(), Is.True);

            GameObject saveGuardObject =
                new("MergedFeatureIntegration_SaveGuard");
            saveGuardObject.AddComponent<DebugDisableSaving>();

            AssertMainCityViewIntegrationSettings();
            AssertNewFeatureIntegrationSettings();

            yield return new EnterPlayMode();
            yield return null;
            yield return new WaitForSeconds(2f);

            CityBootstrap bootstrap =
                RequireObject<CityBootstrap>();
            CityFlowServices services = bootstrap.Services;
            Assert.That(services, Is.Not.Null);
            services.Save?.SetSavingEnabled(false);

            Assert.That(services.WorldGrid, Is.Not.Null);
            Assert.That(services.WorldGrid.WorldWidth, Is.EqualTo(200));
            Assert.That(services.WorldGrid.WorldHeight, Is.EqualTo(200));
            Assert.That(
                services.WorldGrid.InitialPlayableOrigin,
                Is.EqualTo(new Vector2Int(90, 90)));
            Assert.That(services.WorldCoordinates, Is.Not.Null);
            Assert.That(
                services.WorldCoordinates.Plane,
                Is.EqualTo(WorldCoordinatePlane.XZ));
            Assert.That(
                RequireObject<WorldCoordinateService>().GridOrigin,
                Is.EqualTo(new Vector2Int(90, 90)));
            Assert.That(services.WorldCoordinateRoot, Is.Not.Null);
            Assert.That(services.TerrainDecorations, Is.Not.Null);
            Assert.That(services.Research, Is.Not.Null);
            Assert.That(services.SpecialBuildings, Is.Not.Null);
            Assert.That(services.SpecialBuildingVisits, Is.Not.Null);
            Assert.That(services.VehicleTrips, Is.Not.Null);
            Assert.That(
                services.GameCalendar.RealSecondsPerGameDay,
                Is.EqualTo(720f).Within(0.01f));

            SchoolBusService schoolBus =
                RequireObject<SchoolBusService>();
            BusWorldView schoolBusView =
                schoolBus.GetComponent<BusWorldView>();
            Assert.That(schoolBus.IsInitialized, Is.True);
            Assert.That(schoolBus.IsScheduled, Is.True);
            Assert.That(schoolBusView, Is.Not.Null);

            schoolBus.StopService();
            schoolBus.RestoreSnapshot(null);
            GameCalendarService calendar =
                RequireObject<GameCalendarService>();
            var originalCalendarSnapshot =
                calendar.CreateSnapshot();
            var calendarSnapshot = calendar.CreateSnapshot();
            calendarSnapshot.Year = 1;
            calendarSnapshot.Month = 1;
            calendarSnapshot.Day = 1;
            calendarSnapshot.Hour = 7;
            calendarSnapshot.TotalMonths = 1;
            calendarSnapshot.TotalDays = 1;
            calendarSnapshot.AccumulatedRealSeconds = 0f;
            calendar.RestoreSnapshot(calendarSnapshot);

            BusRoute schoolBusRoute =
                schoolBus.GetComponent<BusRoute>();
            Assert.That(schoolBusRoute, Is.Not.Null);
            float originalSchoolBusSecondsPerTile =
                schoolBusRoute.SecondsPerTile;
            float originalSchoolBusStopWaitSeconds =
                schoolBusRoute.StopWaitSeconds;
            var schoolBusCanEnterTile =
                schoolBusRoute.CanEnterTile;
            Assert.That(
                schoolBusCanEnterTile,
                Is.Not.Null,
                "School-bus traffic entry validation must stay connected.");
            schoolBusRoute.SecondsPerTile = 0.01f;
            schoolBusRoute.StopWaitSeconds = 0f;
            Assert.That(schoolBus.StartService(), Is.True);
            MethodInfo updateSchoolBusRoute =
                typeof(BusRoute).GetMethod(
                    "UpdateMoving",
                    BindingFlags.Instance |
                    BindingFlags.NonPublic);
            Assert.That(updateSchoolBusRoute, Is.Not.Null);
            for (int step = 0;
                 step < 100 &&
                 schoolBus.VisitedResidentialCount < 2;
                 step++)
            {
                updateSchoolBusRoute.Invoke(
                    schoolBusRoute,
                    new object[] { 1f });
                yield return null;
            }
            schoolBusRoute.SecondsPerTile =
                originalSchoolBusSecondsPerTile;
            schoolBusRoute.StopWaitSeconds =
                originalSchoolBusStopWaitSeconds;
            Assert.That(
                schoolBusRoute.CanEnterTile,
                Is.EqualTo(schoolBusCanEnterTile),
                "School-bus traffic entry validation was replaced during the trip.");

            Assert.That(
                schoolBus.State,
                Is.Not.EqualTo(
                    SchoolBusState.RouteUnavailable));
            Assert.That(
                schoolBus.VisitedResidentialCount,
                Is.GreaterThanOrEqualTo(1));
            Assert.That(
                schoolBus.RouteStops.Contains(
                    new Vector2Int(97, 93)),
                Is.False);
            Assert.That(
                schoolBus.RouteStops.Contains(
                    new Vector2Int(99, 94)),
                Is.False);
            Assert.That(
                schoolBus.RouteStops.Contains(
                    new Vector2Int(96, 95)),
                Is.False);
            Assert.That(schoolBusView.HasVisibleBus, Is.True);
            GameObject schoolBusVisual =
                GameObject.Find("SchoolBusVisual");
            Assert.That(schoolBusVisual, Is.Not.Null);
            Renderer[] schoolBusRenderers =
                schoolBusVisual.GetComponentsInChildren<Renderer>(true);
            Assert.That(schoolBusRenderers, Is.Not.Empty);
            foreach (Renderer schoolBusRenderer in schoolBusRenderers)
            {
                Material[] rendererMaterials =
                    schoolBusRenderer.sharedMaterials;
                Assert.That(rendererMaterials, Is.Not.Empty);
                foreach (Material rendererMaterial in rendererMaterials)
                {
                    Assert.That(
                        rendererMaterial.shader.name,
                        Is.EqualTo(
                            "GreenLight/CityFlow Opaque Unlit"));
                }
            }
            schoolBus.StopService();
            calendar.RestoreSnapshot(
                originalCalendarSnapshot);

            SpecialBuildingVisitTripSource visitTripSource =
                RequireObject<SpecialBuildingVisitTripSource>();
            Assert.That(
                visitTripSource.MaximumVisualTripsPerBuildingPerDay,
                Is.EqualTo(64));

            MainCityView cityView = RequireObject<MainCityView>();
            Assert.That(
                cityView.GridOrigin,
                Is.EqualTo(new Vector2Int(90, 90)));

            WorldGridVisualStreamer streamer =
                RequireObject<WorldGridVisualStreamer>();
            TerrainDecorationView terrain =
                RequireObject<TerrainDecorationView>();
            Assert.That(streamer.WorldGrid, Is.Not.Null);
            Assert.That(terrain.Catalog, Is.Not.Null);
            Assert.That(terrain.CityView, Is.SameAs(cityView));

            int previousStage =
                services.WorldGridExpansion.CurrentStageIndex;
            Assert.That(
                services.WorldGridExpansion.TryUnlockNextStage(),
                Is.True);
            Assert.That(
                services.WorldGridExpansion.CurrentStageIndex,
                Is.GreaterThan(previousStage));

            SpecialBuildingInstance specialBuilding =
                PlaceSpecialBuilding(services);
            AssertSpecialBuildingVisual(
                services,
                specialBuilding);
            CaptureGameCameraScreenshot(
                GetSpecialBuildingCenter(
                    services,
                    specialBuilding),
                SpecialBuildingScreenshotPath);

            FindRoadCrossSite(
                services,
                out Vector2Int roadStart,
                out Vector2Int intersection,
                out Vector2Int firstStop,
                out Vector2Int secondStop);
            PlaceTrafficAndBusInfrastructure(
                services,
                roadStart,
                intersection,
                firstStop,
                secondStop);
            float roadVisualDeadline =
                Time.realtimeSinceStartup + 2f;
            while (FindRoadVisual(intersection) == null &&
                   Time.realtimeSinceStartup < roadVisualDeadline)
            {
                yield return null;
            }
            AssertRoadVisualAlignment(
                services,
                intersection);

            BusStopRegistry registry =
                RequireObject<BusStopRegistry>();
            foreach (Vector2Int existingStop in registry.CopyBusStops())
            {
                registry.RemoveBusStop(existingStop);
            }
            registry.RegisterBusStop(firstStop);
            registry.RegisterBusStop(secondStop);
            Assert.That(registry.BusStopCount, Is.EqualTo(2));
            yield return null;
            AssertBusStopVisualAlignment(
                cityView,
                firstStop);
            AssertBusStopVisualAlignment(
                cityView,
                secondStop);

            CityBusService cityBus =
                RequireObject<CityBusService>();
            CityBusWorldView busView =
                RequireObject<CityBusWorldView>();
            CityBusStopWorldView stopView =
                RequireObject<CityBusStopWorldView>();
            AssertInfrastructurePlacementPreviewFactories(
                cityView,
                stopView);
            Assert.That(cityBus.StartService(), Is.True);

            services.Events.Publish(
                new FlowBurstEvent(intersection, 5));
            yield return null;

            Assert.That(
                FindActiveGreenWaveText(),
                Is.True);

            bool sawVisibleBus = busView.HasVisibleBus;
            bool sawActiveBusState =
                cityBus.Runtime != null &&
                (cityBus.Runtime.State == BusOperatingState.Moving ||
                 cityBus.Runtime.State ==
                 BusOperatingState.WaitingAtStop);
            int maximumCompletedStops =
                cityBus.Runtime?.CompletedStops ?? 0;
            int maximumVisibleStations =
                stopView.VisibleStationCount;
            int maximumRenderedTiles =
                streamer.RenderedTileCount;
            int maximumTrafficLightLenses =
                CountTrafficLightLenses();
            bool screenshotCaptured = false;
            float deadline = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < deadline &&
                   (maximumCompletedStops == 0 ||
                     !sawVisibleBus ||
                     maximumVisibleStations < 2 ||
                     maximumRenderedTiles <= 400 ||
                     maximumTrafficLightLenses < 2))
            {
                yield return null;

                sawVisibleBus |= busView.HasVisibleBus;
                sawActiveBusState |=
                    cityBus.Runtime != null &&
                    (cityBus.Runtime.State ==
                         BusOperatingState.Moving ||
                     cityBus.Runtime.State ==
                         BusOperatingState.WaitingAtStop);
                maximumCompletedStops =
                    Mathf.Max(
                        maximumCompletedStops,
                        cityBus.Runtime?.CompletedStops ?? 0);
                maximumVisibleStations =
                    Mathf.Max(
                        maximumVisibleStations,
                        stopView.VisibleStationCount);
                maximumRenderedTiles =
                    Mathf.Max(
                        maximumRenderedTiles,
                        streamer.RenderedTileCount);
                maximumTrafficLightLenses =
                    Mathf.Max(
                        maximumTrafficLightLenses,
                        CountTrafficLightLenses());

                if (!screenshotCaptured &&
                    busView.HasVisibleBus &&
                    stopView.VisibleStationCount >= 2)
                {
                    CaptureGameCameraScreenshot(
                        services.WorldCoordinates.GridToWorld(
                            intersection),
                        ScreenshotPath);
                    screenshotCaptured = true;
                }
            }

            Assert.That(maximumRenderedTiles, Is.GreaterThan(400));
            Assert.That(
                maximumVisibleStations,
                Is.GreaterThanOrEqualTo(2));
            Assert.That(sawActiveBusState, Is.True);
            Assert.That(sawVisibleBus, Is.True);
            Assert.That(maximumCompletedStops, Is.GreaterThan(0));
            AssertBusVisualAlignment(
                cityView,
                cityBus.Runtime.CurrentTile);
            Assert.That(
                maximumTrafficLightLenses,
                Is.GreaterThanOrEqualTo(2));
            Assert.That(screenshotCaptured, Is.True);

            TimeOfDaySkyController sky =
                RequireObject<TimeOfDaySkyController>();
            Assert.That(sky.Profile, Is.Not.Null);
            Assert.That(RenderSettings.skybox, Is.Not.Null);

            services.Events.Publish(
                new FlowBurstEvent(
                    cityBus.Runtime.CurrentTile,
                    5));
            yield return null;
            yield return null;

            yield return new ExitPlayMode();

            GameObject restoredGuard =
                GameObject.Find(
                    "MergedFeatureIntegration_SaveGuard");
            if (restoredGuard != null)
            {
                Object.DestroyImmediate(restoredGuard);
            }
        }

        [UnityTest]
        public IEnumerator IntegrationScene_PlacementPreviewRemainsReadableAtAllHours()
        {
            Scene scene =
                EditorSceneManager.OpenScene(
                    IntegrationScenePath,
                    OpenSceneMode.Single);
            Assert.That(scene.IsValid(), Is.True);

            GameObject saveGuardObject =
                new("PlacementPreviewLighting_SaveGuard");
            saveGuardObject.AddComponent<DebugDisableSaving>();

            yield return new EnterPlayMode();
            yield return null;

            CityFlowServices services =
                RequireObject<CityBootstrap>().Services;
            Assert.That(services, Is.Not.Null);
            services.Save?.SetSavingEnabled(false);

            GameCalendarService calendar =
                RequireObject<GameCalendarService>();
            GameCalendarSaveData originalSnapshot =
                calendar.CreateSnapshot();
            var ghostObject =
                new GameObject("PlacementPreviewLighting_Ghost");
            var ghostRenderer =
                ghostObject.AddComponent<SpriteRenderer>();
            var visualManager = new PlacementVisualManager(
                ghostRenderer,
                Color.green,
                Color.red,
                true,
                1f,
                Color.green,
                Color.red,
                null,
                null,
                null);
            GameObject preview =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            var validationCameraObject =
                new GameObject("PlacementPreviewLighting_Camera");
            Camera validationCamera =
                validationCameraObject.AddComponent<Camera>();
            validationCamera.enabled = false;
            validationCamera.clearFlags = CameraClearFlags.SolidColor;
            validationCamera.backgroundColor = Color.black;
            validationCamera.transform.position =
                new Vector3(10000f, 10000f, -5f);
            preview.transform.position =
                new Vector3(10000f, 10000f, 0f);
            var renderTarget =
                new RenderTexture(64, 64, 24);
            var readbackTexture =
                new Texture2D(64, 64, TextureFormat.RGBA32, false);
            validationCamera.targetTexture = renderTarget;

            try
            {
                visualManager.Initialize();
                visualManager.SetBuildingPreview(preview);
                visualManager.SetGhostActive(true);

                int[] representativeHours =
                {
                    0, 5, 6, 12, 17, 18, 23
                };
                var properties = new MaterialPropertyBlock();
                Renderer previewRenderer =
                    preview.GetComponent<Renderer>();

                foreach (int hour in representativeHours)
                {
                    GameCalendarSaveData snapshot =
                        calendar.CreateSnapshot();
                    snapshot.Hour = hour;
                    calendar.RestoreSnapshot(snapshot);
                    yield return null;

                    Assert.That(preview.activeInHierarchy, Is.True);
                    Assert.That(previewRenderer.enabled, Is.True);
                    Assert.That(
                        previewRenderer.sharedMaterial.shader.name,
                        Does.Contain("Unlit")
                            .Or.EqualTo("Sprites/Default"),
                        $"{hour:00}시 미리보기가 시간대 조명의 영향을 받으면 안 된다.");

                    visualManager.UpdateColors(canPlace: true);
                    previewRenderer.GetPropertyBlock(properties);
                    Assert.That(
                        properties.GetColor("_BaseColor"),
                        Is.EqualTo(Color.green),
                        $"{hour:00}시 설치 가능 초록색이 유지되어야 한다.");
                    Color renderedGreen = RenderCenterPixel(
                        validationCamera,
                        renderTarget,
                        readbackTexture);
                    Assert.That(
                        renderedGreen.g,
                        Is.GreaterThan(0.5f),
                        $"{hour:00}시 설치 가능 미리보기가 화면에서 밝게 보여야 한다.");

                    visualManager.UpdateColors(canPlace: false);
                    previewRenderer.GetPropertyBlock(properties);
                    Assert.That(
                        properties.GetColor("_BaseColor"),
                        Is.EqualTo(Color.red),
                        $"{hour:00}시 설치 불가능 빨간색이 유지되어야 한다.");
                    Color renderedRed = RenderCenterPixel(
                        validationCamera,
                        renderTarget,
                        readbackTexture);
                    Assert.That(
                        renderedRed.r,
                        Is.GreaterThan(0.5f),
                        $"{hour:00}시 설치 불가능 미리보기가 화면에서 밝게 보여야 한다.");
                }
            }
            finally
            {
                calendar.RestoreSnapshot(originalSnapshot);
                visualManager.Cleanup();
                Object.Destroy(ghostObject);
                validationCamera.targetTexture = null;
                renderTarget.Release();
                Object.Destroy(renderTarget);
                Object.Destroy(readbackTexture);
                Object.Destroy(validationCameraObject);
            }

            yield return new ExitPlayMode();

            GameObject restoredGuard =
                GameObject.Find(
                    "PlacementPreviewLighting_SaveGuard");
            if (restoredGuard != null)
            {
                Object.DestroyImmediate(restoredGuard);
            }
        }

        private static Color RenderCenterPixel(
            Camera camera,
            RenderTexture target,
            Texture2D readback)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.Render();
                RenderTexture.active = target;
                readback.ReadPixels(
                    new Rect(0f, 0f, target.width, target.height),
                    0,
                    0,
                    false);
                readback.Apply(false, false);
                return readback.GetPixel(
                    target.width / 2,
                    target.height / 2);
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        [UnityTest]
        public IEnumerator IntegrationScene_RotatedBuildingKeepsDirection()
        {
            Scene scene =
                EditorSceneManager.OpenScene(
                    IntegrationScenePath,
                    OpenSceneMode.Single);
            Assert.That(scene.IsValid(), Is.True);

            GameObject saveGuardObject =
                new("RotatedBuildingIntegration_SaveGuard");
            saveGuardObject.AddComponent<DebugDisableSaving>();

            yield return new EnterPlayMode();
            yield return null;

            CityFlowServices services =
                RequireObject<CityBootstrap>().Services;
            Assert.That(services, Is.Not.Null);
            services.Save?.SetSavingEnabled(false);

            MainCityView cityView =
                RequireObject<MainCityView>();
            PlacementController placementController =
                RequireObject<PlacementController>();
            PlacementVisualManager visualManager =
                GetPlacementVisualManager(
                    placementController);
            TileType[] buildingTypes =
            {
                TileType.House,
                TileType.Office,
                TileType.School,
                TileType.Hospital
            };

            foreach (TileType buildingType in buildingTypes)
            {
                placementController.SetBuildType(buildingType);
                RotateCurrentPlacement(placementController);
                yield return null;

                GameObject preview =
                    visualManager.BuildingPreviewObject;
                AssertPlacementPreview(
                    services,
                    preview,
                    buildingType.ToString());
                string[] previewParts =
                    GetRenderedPartNames(preview);

                Vector2Int anchor =
                    FindPlacementAnchor(
                        services,
                        buildingType);
                Assert.That(
                    services.Placement.Place(
                        anchor,
                        buildingType,
                        PlacementDirection.East),
                    Is.True);

                GameObject visual = null;
                float visualDeadline =
                    Time.realtimeSinceStartup + 2f;
                while (!TryFindTileVisual(
                           cityView,
                           anchor,
                           out visual) &&
                       Time.realtimeSinceStartup < visualDeadline)
                {
                    yield return null;
                }
                Assert.That(
                    visual,
                    Is.Not.Null,
                    $"{buildingType} visual was not created.");
                Assert.That(
                    Quaternion.Angle(
                        Quaternion.Euler(0f, 0f, 90f),
                        visual.transform.localRotation),
                    Is.LessThan(0.01f),
                    $"{buildingType} visual did not keep its East placement direction.");
                Assert.That(
                    GetRenderedPartNames(visual),
                    Is.EquivalentTo(previewParts),
                    $"{buildingType} placement preview did not use the installed building model.");
            }

            SpecialBuildingBuildOption specialOption =
                UnlockSpecialBuilding(services);
            Assert.That(
                placementController.SetSpecialBuilding(
                    specialOption.BuildingId),
                Is.True);
            RotateCurrentPlacement(placementController);
            yield return null;

            GameObject specialPreview =
                visualManager.BuildingPreviewObject;
            AssertPlacementPreview(
                services,
                specialPreview,
                specialOption.BuildingId);
            string[] specialPreviewParts =
                GetRenderedPartNames(specialPreview);

            Vector2Int specialAnchor =
                FindPlacementAnchor(
                    services,
                    TileType.SpecialBuilding);
            Assert.That(
                services.SpecialBuildings.TryPlace(
                    specialOption.BuildingId,
                    specialAnchor,
                    PlacementDirection.East),
                Is.True);
            Assert.That(
                services.SpecialBuildings.TryGetBuilding(
                    specialAnchor,
                    out SpecialBuildingInstance specialBuilding),
                Is.True);
            placementController.ToggleBuildMode(false);
            yield return null;

            Assert.That(
                specialBuilding.Direction,
                Is.EqualTo(PlacementDirection.East));
            AssertSpecialBuildingVisual(
                services,
                specialBuilding);
            GameObject specialVisual =
                GameObject.Find(
                    $"Fallback_{specialBuilding.BuildingId}");
            Assert.That(
                GetRenderedPartNames(specialVisual),
                Is.EquivalentTo(specialPreviewParts),
                "Special-building placement preview did not use the installed building model.");

            yield return new ExitPlayMode();

            GameObject restoredGuard =
                GameObject.Find(
                    "RotatedBuildingIntegration_SaveGuard");
            if (restoredGuard != null)
            {
                Object.DestroyImmediate(restoredGuard);
            }
        }

        private static bool TryFindTileVisual(
            MainCityView cityView,
            Vector2Int tile,
            out GameObject visual)
        {
            visual = null;
            FieldInfo field =
                typeof(MainCityView).GetField(
                    "tileVisuals",
                    BindingFlags.NonPublic |
                    BindingFlags.Instance);
            if (field == null)
            {
                return false;
            }

            var visuals =
                field.GetValue(cityView) as IDictionary;
            if (visuals == null || !visuals.Contains(tile))
            {
                return false;
            }

            object tileVisual = visuals[tile];
            FieldInfo objectField =
                tileVisual.GetType().GetField(
                    "Object",
                    BindingFlags.Public |
                    BindingFlags.Instance);
            if (objectField == null)
            {
                return false;
            }

            visual =
                objectField.GetValue(tileVisual) as GameObject;
            return visual != null;
        }

        private static PlacementVisualManager
            GetPlacementVisualManager(
                PlacementController controller)
        {
            FieldInfo field =
                typeof(PlacementController).GetField(
                    "_visualManager",
                    BindingFlags.NonPublic |
                    BindingFlags.Instance);
            Assert.That(field, Is.Not.Null);
            var manager =
                field.GetValue(controller) as
                    PlacementVisualManager;
            Assert.That(manager, Is.Not.Null);
            return manager;
        }

        private static void RotateCurrentPlacement(
            PlacementController controller)
        {
            MethodInfo rotateMethod =
                typeof(PlacementController).GetMethod(
                    "HandleRotate",
                    BindingFlags.NonPublic |
                    BindingFlags.Instance);
            Assert.That(rotateMethod, Is.Not.Null);
            rotateMethod.Invoke(controller, null);
        }

        private static void AssertPlacementPreview(
            CityFlowServices services,
            GameObject preview,
            string buildingName)
        {
            Assert.That(
                preview,
                Is.Not.Null,
                $"{buildingName} placement preview was not created.");
            Assert.That(
                preview.activeInHierarchy,
                Is.True,
                $"{buildingName} placement preview was not visible.");
            Assert.That(
                preview.GetComponentsInChildren<Renderer>(true).Length,
                Is.GreaterThan(0),
                $"{buildingName} placement preview had no visible model.");

            Quaternion expectedRotation =
                services.WorldCoordinates.CoordinateRotation *
                Quaternion.Euler(0f, 0f, 90f);
            Assert.That(
                Quaternion.Angle(
                    expectedRotation,
                    preview.transform.rotation),
                Is.LessThan(0.01f),
                $"{buildingName} placement preview did not rotate before placement.");
        }

        private static void
            AssertInfrastructurePlacementPreviewFactories(
                MainCityView cityView,
                CityBusStopWorldView stopView)
        {
            InfrastructureKind[] kinds =
            {
                InfrastructureKind.Signal,
                InfrastructureKind.Roundabout,
                InfrastructureKind.Overpass,
                InfrastructureKind.Oneway,
                InfrastructureKind.TurnRestriction,
                InfrastructureKind.PriorityRoad,
                InfrastructureKind.Highway
            };

            foreach (InfrastructureKind kind in kinds)
            {
                InfrastructureDataSO data =
                    ScriptableObject.CreateInstance<
                        InfrastructureDataSO>();
                data.Kind = kind;
                data.OnewayDir = Vector2Int.right;
                GameObject preview = null;
                try
                {
                    Assert.That(
                        cityView
                            .TryCreateInfrastructurePlacementPreview(
                                data,
                                kind == InfrastructureKind.Highway
                                    ? new Vector2Int(2, 2)
                                    : null,
                                new Vector2Int(5, 2),
                                out preview),
                        Is.True,
                        $"{kind} placement preview factory was unavailable.");
                    Assert.That(
                        preview.GetComponentsInChildren<
                            Renderer>(true).Length,
                        Is.GreaterThan(0),
                        $"{kind} placement preview had no visible model.");
                }
                finally
                {
                    if (preview != null)
                    {
                        preview.SetActive(false);
                        Object.Destroy(preview);
                    }
                    Object.Destroy(data);
                }
            }

            Assert.That(
                stopView.TryCreatePlacementPreview(
                    out GameObject busStopPreview),
                Is.True);
            Assert.That(
                busStopPreview.GetComponentsInChildren<
                    Renderer>(true).Length,
                Is.GreaterThan(0));
            busStopPreview.SetActive(false);
            Object.Destroy(busStopPreview);
        }

        private static string[] GetRenderedPartNames(
            GameObject root)
        {
            Assert.That(root, Is.Not.Null);
            return root
                .GetComponentsInChildren<Renderer>(true)
                .Select(renderer => renderer.gameObject.name)
                .OrderBy(name => name)
                .ToArray();
        }

        private static void AssertMainCityViewIntegrationSettings()
        {
            MainCityView cityView =
                RequireObject<MainCityView>();
            var serializedView =
                new SerializedObject(cityView);

            Assert.That(
                serializedView.FindProperty(
                    "cameraRotationSpeed").floatValue,
                Is.EqualTo(500f));
            Assert.That(
                serializedView.FindProperty(
                    "cameraRotationSmoothTime").floatValue,
                Is.EqualTo(0.15f));
            Assert.That(
                serializedView.FindProperty(
                    "invertCameraRotationDirection").boolValue,
                Is.False);
            Assert.That(
                serializedView.FindProperty(
                    "signalPrefab").objectReferenceValue,
                Is.Not.Null);
        }

        private static void AssertNewFeatureIntegrationSettings()
        {
            AssertTopBarContainsCurrentHudOnly();

            SchoolBusService[] schoolBusServices =
                Object.FindObjectsByType<SchoolBusService>(
                    FindObjectsInactive.Include);
            SchoolBusRouteView[] legacyRouteViews =
                Object.FindObjectsByType<SchoolBusRouteView>(
                    FindObjectsInactive.Include);

            Assert.That(schoolBusServices, Has.Length.EqualTo(1));
            Assert.That(legacyRouteViews, Is.Empty);

            BusWorldView schoolBusView =
                schoolBusServices[0]
                    .GetComponent<BusWorldView>();
            Assert.That(schoolBusView, Is.Not.Null);

            var serializedSchoolBus =
                new SerializedObject(schoolBusServices[0]);
            Assert.That(
                serializedSchoolBus.FindProperty(
                    "definition").objectReferenceValue,
                Is.Not.Null);
            Assert.That(
                serializedSchoolBus.FindProperty(
                    "schedule").objectReferenceValue,
                Is.Not.Null);
            Assert.That(
                serializedSchoolBus.FindProperty(
                    "busRoute").objectReferenceValue,
                Is.Not.Null);

            var serializedSchoolBusView =
                new SerializedObject(schoolBusView);
            Material schoolBusMaterial =
                serializedSchoolBusView.FindProperty(
                        "busMaterial")
                    .objectReferenceValue as Material;
            Assert.That(
                serializedSchoolBusView.FindProperty(
                    "definition").objectReferenceValue,
                Is.Not.Null);
            Assert.That(
                serializedSchoolBusView.FindProperty(
                    "busRoute").objectReferenceValue,
                Is.Not.Null);
            Assert.That(
                schoolBusMaterial,
                Is.Null,
                "The authored school-bus materials must not be overridden.");
            Assert.That(
                serializedSchoolBusView.FindProperty(
                    "busVisualPrefab").objectReferenceValue,
                Is.Not.Null);
            Assert.That(
                serializedSchoolBusView.FindProperty(
                    "visualScale").floatValue,
                Is.EqualTo(0.76f).Within(0.0001f));

            SpecialBuildingVisitTripSource[] visitTripSources =
                Object.FindObjectsByType<SpecialBuildingVisitTripSource>(
                    FindObjectsInactive.Include);
            Assert.That(visitTripSources, Has.Length.EqualTo(1));
            Assert.That(
                visitTripSources[0]
                    .MaximumVisualTripsPerBuildingPerDay,
                Is.EqualTo(64));

            GameTimeSettingsSO timeSettings =
                AssetDatabase.LoadAssetAtPath<GameTimeSettingsSO>(
                    GameTimeSettingsPath);
            Assert.That(timeSettings, Is.Not.Null);
            Assert.That(
                timeSettings.RealMinutesPerGameDay,
                Is.EqualTo(12f));
            Assert.That(
                timeSettings.RealSecondsPerGameHour,
                Is.EqualTo(30f));

            SimConfigAsset simConfig =
                AssetDatabase.LoadAssetAtPath<SimConfigAsset>(
                    IntegratedSimConfigPath);
            Assert.That(simConfig, Is.Not.Null);
            Assert.That(simConfig.Value.ConstructionHoursHouse, Is.Zero);
            Assert.That(simConfig.Value.ConstructionHoursOffice, Is.Zero);
            Assert.That(simConfig.Value.ConstructionHoursSchool, Is.Zero);
            Assert.That(simConfig.Value.ConstructionHoursHospital, Is.Zero);
            Assert.That(simConfig.Value.ConstructionHoursSpecial, Is.Zero);
        }

        private static void AssertTopBarContainsCurrentHudOnly()
        {
            string[] objectNames =
                Object.FindObjectsByType<Transform>(
                        FindObjectsInactive.Include)
                    .Select(item => item.name)
                    .ToArray();
            Assert.That(objectNames, Does.Not.Contain("StabilityText"));
            Assert.That(objectNames, Does.Not.Contain("StabilityBar"));

            GameObject topBar = GameObject.Find("HUD_TopBar");
            Assert.That(topBar, Is.Not.Null);

            string[] directHeaderTextNames =
                topBar.GetComponentsInChildren<TextMeshProUGUI>(true)
                    .Where(
                        text =>
                            text.transform.parent == topBar.transform)
                    .Select(text => text.name)
                    .OrderBy(name => name)
                    .ToArray();
            Assert.That(
                directHeaderTextNames,
                Is.EquivalentTo(
                    new[]
                    {
                        "CoinHarvestReceipt",
                        "CoinText",
                        "TimeText",
                        "VehicleCountText"
                    }));
        }

        private static SpecialBuildingInstance PlaceSpecialBuilding(
            CityFlowServices services,
            PlacementDirection direction = PlacementDirection.North)
        {
            SpecialBuildingBuildOption option =
                UnlockSpecialBuilding(services);

            Vector2Int anchor =
                FindPlacementAnchor(
                    services,
                    TileType.SpecialBuilding);
            Assert.That(
                services.SpecialBuildings.TryPlace(
                    option.BuildingId,
                    anchor,
                    direction),
                Is.True);
            Assert.That(
                services.SpecialBuildings.BuildingCount,
                Is.GreaterThan(0));
            Assert.That(
                services.SpecialBuildings.TryGetBuilding(
                    anchor,
                    out SpecialBuildingInstance building),
                Is.True);
            return building;
        }

        private static SpecialBuildingBuildOption
            UnlockSpecialBuilding(
                CityFlowServices services)
        {
            SpecialBuildingBuildOption[] options =
                services.SpecialBuildings.CreateBuildOptionSnapshot();
            Assert.That(options.Length, Is.GreaterThanOrEqualTo(8));

            SpecialBuildingBuildOption option =
                options.First(
                    item =>
                        !string.IsNullOrWhiteSpace(
                            item.RequiredResearchId));
            Assert.That(
                services.Research,
                Is.TypeOf<ResearchUnlockService>());
            ((ResearchUnlockService)services.Research)
                .RestoreSnapshot(new ResearchSaveData
                {
                    UnlockedResearchIds = new[]
                    {
                        option.RequiredResearchId
                    }
                });
            Assert.That(
                services.SpecialBuildings.IsBuildingUnlocked(
                    option.BuildingId),
                Is.True);
            return option;
        }

        private static void PlaceTrafficAndBusInfrastructure(
            CityFlowServices services,
            Vector2Int roadStart,
            Vector2Int intersection,
            Vector2Int firstStop,
            Vector2Int secondStop)
        {
            for (int x = 0; x < 7; x++)
            {
                Assert.That(
                    services.Placement.Place(
                        roadStart + new Vector2Int(x, 0),
                        TileType.Road),
                    Is.True);
            }

            for (int y = -2; y <= 2; y++)
            {
                Vector2Int tile =
                    intersection + new Vector2Int(0, y);
                if (tile == intersection)
                {
                    continue;
                }

                Assert.That(
                    services.Placement.Place(
                        tile,
                        TileType.Road),
                    Is.True);
            }

            IIntersectionFacilityService facilities =
                services.Placement as IIntersectionFacilityService;
            Assert.That(facilities, Is.Not.Null);
            Assert.That(
                facilities.TryPlaceSignal(intersection, 3),
                Is.True);

            IBusStopInfrastructureService infrastructure =
                services.Placement as IBusStopInfrastructureService;
            Assert.That(infrastructure, Is.Not.Null);
            Assert.That(
                infrastructure.TryPlaceBusStop(firstStop),
                Is.True);
            Assert.That(
                infrastructure.TryPlaceBusStop(secondStop),
                Is.True);
        }

        private static Vector2Int FindPlacementAnchor(
            CityFlowServices services,
            TileType type)
        {
            Vector2Int footprint =
                TileFootprint.GetSize(type);
            Vector2Int origin =
                services.WorldGrid.InitialPlayableOrigin;
            const int searchSize = 20;
            for (int y = origin.y;
                 y < origin.y + searchSize;
                 y++)
            {
                for (int x = origin.x;
                     x < origin.x + searchSize;
                     x++)
                {
                    Vector2Int tile = new(x, y);
                    if (services.WorldGrid.IsAreaUnlocked(
                            tile,
                            footprint) &&
                        services.Placement.CanPlace(tile, type))
                    {
                        return tile;
                    }
                }
            }

            Assert.Fail(
                $"No free placement anchor found for {type}.");
            return default;
        }

        private static void FindRoadCrossSite(
            CityFlowServices services,
            out Vector2Int roadStart,
            out Vector2Int intersection,
            out Vector2Int firstStop,
            out Vector2Int secondStop)
        {
            for (int y = 92; y < 108; y++)
            {
                for (int x = 90; x <= 103; x++)
                {
                    Vector2Int start = new(x, y);
                    Vector2Int center =
                        start + new Vector2Int(3, 0);
                    Vector2Int stopA =
                        start + new Vector2Int(1, -1);
                    Vector2Int stopB =
                        start + new Vector2Int(6, -1);
                    var required =
                        new List<Vector2Int>();

                    for (int offset = 0; offset < 7; offset++)
                    {
                        required.Add(
                            start + new Vector2Int(offset, 0));
                    }

                    required.Add(center + Vector2Int.up);
                    required.Add(center + Vector2Int.up * 2);
                    required.Add(center + Vector2Int.down);
                    required.Add(center + Vector2Int.down * 2);
                    required.Add(stopA);
                    required.Add(stopB);

                    if (required.All(
                            tile =>
                                services.WorldGrid.IsTileUnlocked(tile) &&
                                services.TileData.GetTileType(tile) ==
                                TileType.Empty))
                    {
                        roadStart = start;
                        intersection = center;
                        firstStop = stopA;
                        secondStop = stopB;
                        return;
                    }
                }
            }

            Assert.Fail(
                "No free road and bus-stop test site found.");
            roadStart = default;
            intersection = default;
            firstStop = default;
            secondStop = default;
        }

        private static bool FindActiveGreenWaveText()
        {
            TextMeshPro[] texts =
                Object.FindObjectsByType<TextMeshPro>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);
            return texts.Any(text => text.text == "GREEN WAVE!");
        }

        private static void AssertRoadVisualAlignment(
            CityFlowServices services,
            Vector2Int intersection)
        {
            GameObject road =
                FindRoadVisual(intersection);
            Assert.That(road, Is.Not.Null);
            Assert.That(
                Vector3.Distance(
                    road.transform.position,
                    services.WorldCoordinates.GridToWorld(
                        intersection)),
                Is.LessThan(0.01f));
        }

        private static void AssertSpecialBuildingVisual(
            CityFlowServices services,
            SpecialBuildingInstance building)
        {
            SpecialBuildingView view =
                RequireObject<SpecialBuildingView>();
            Assert.That(view.VisualCount, Is.GreaterThan(0));

            GameObject visual =
                GameObject.Find(
                    $"Fallback_{building.BuildingId}");
            Assert.That(
                visual,
                Is.Not.Null,
                "The prewired special-building fallback was not created.");
            Assert.That(visual.activeInHierarchy, Is.True);
            Assert.That(
                Vector3.Distance(
                    visual.transform.position,
                    GetSpecialBuildingCenter(
                        services,
                        building)),
                Is.LessThan(0.01f));

            SpecialBuildingService buildingService =
                RequireObject<SpecialBuildingService>();
            Assert.That(
                buildingService.Catalog.TryGet(
                    building.BuildingId,
                    out BuildingDefinitionSO definition),
                Is.True);
            Quaternion expectedRotation =
                services.WorldCoordinates.CoordinateRotation *
                Quaternion.Euler(
                    0f,
                    0f,
                    TileFootprint.ToAngle(building.Direction)) *
                Quaternion.Euler(definition.VisualEulerAngles);
            Assert.That(
                Quaternion.Angle(
                    expectedRotation,
                    visual.transform.rotation),
                Is.LessThan(0.01f),
                "Special-building visual did not keep its placement direction.");

            Renderer[] renderers =
                visual.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers.Length, Is.GreaterThanOrEqualTo(3));
            Assert.That(
                renderers.All(
                    renderer =>
                        renderer.enabled &&
                        !renderer.forceRenderingOff &&
                        renderer.gameObject.activeInHierarchy),
                Is.True);
        }

        private static Vector3 GetSpecialBuildingCenter(
            CityFlowServices services,
            SpecialBuildingInstance building)
        {
            Vector2Int footprint =
                TileFootprint.GetSize(TileType.SpecialBuilding);
            return services.WorldCoordinates.GridPointToWorld(
                new Vector2(
                    building.Anchor.x + footprint.x * 0.5f,
                    building.Anchor.y + footprint.y * 0.5f),
                0.02f);
        }

        private static void AssertBusStopVisualAlignment(
            MainCityView cityView,
            Vector2Int tile)
        {
            string nameSuffix = $"_{tile.x}_{tile.y}";
            Transform markerTransform =
                Object.FindObjectsByType<Transform>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None)
                    .FirstOrDefault(
                        candidate =>
                            candidate.name.StartsWith("BusStop_") &&
                            candidate.name.EndsWith(nameSuffix));
            GameObject marker =
                markerTransform != null
                    ? markerTransform.gameObject
                    : null;
            Assert.That(marker, Is.Not.Null);

            Vector2Int localTile = tile - cityView.GridOrigin;
            Assert.That(
                Vector2.Distance(
                    new Vector2(
                        marker.transform.localPosition.x,
                        marker.transform.localPosition.y),
                    new Vector2(
                        localTile.x + 0.5f,
                        localTile.y + 0.5f)),
                Is.LessThan(0.01f));
            Assert.That(
                marker.GetComponentsInChildren<Renderer>(true)
                    .Any(
                        renderer =>
                            renderer.enabled &&
                            renderer.gameObject.activeInHierarchy),
                Is.True);
        }

        private static void AssertBusVisualAlignment(
            MainCityView cityView,
            Vector2Int currentTile)
        {
            GameObject visual = GameObject.Find("CityBusVisual");
            Assert.That(visual, Is.Not.Null);
            Assert.That(visual.activeInHierarchy, Is.True);

            Vector2Int localTile =
                currentTile - cityView.GridOrigin;
            Assert.That(
                Vector2.Distance(
                    new Vector2(
                        visual.transform.localPosition.x,
                        visual.transform.localPosition.y),
                    new Vector2(
                        localTile.x + 0.5f,
                        localTile.y + 0.5f)),
                Is.LessThan(2f));
            Assert.That(
                visual.GetComponentsInChildren<Renderer>(true)
                    .Any(
                        renderer =>
                            renderer.enabled &&
                            renderer.gameObject.activeInHierarchy),
                Is.True);
        }

        private static GameObject FindRoadVisual(
            Vector2Int intersection)
        {
            return GameObject.Find(
                $"Road_{intersection.x}_{intersection.y}");
        }

        private static int CountTrafficLightLenses()
        {
            return Object.FindObjectsByType<TrafficLightLensView>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Length;
        }

        private static void CaptureGameCameraScreenshot(
            Vector3 focusPosition,
            string screenshotPath)
        {
            Camera camera =
                Camera.main ??
                Object.FindFirstObjectByType<Camera>();
            Assert.That(camera, Is.Not.Null);

            const int width = 1280;
            const int height = 720;
            var renderTexture =
                new RenderTexture(width, height, 24);
            var texture =
                new Texture2D(
                    width,
                    height,
                    TextureFormat.RGB24,
                    false);
            RenderTexture previousActive =
                RenderTexture.active;
            RenderTexture previousTarget =
                camera.targetTexture;
            Vector3 previousPosition =
                camera.transform.position;
            float previousOrthographicSize =
                camera.orthographicSize;

            try
            {
                camera.transform.position =
                    focusPosition -
                    camera.transform.forward * 20f;
                camera.orthographicSize = 8f;
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                texture.ReadPixels(
                    new Rect(0f, 0f, width, height),
                    0,
                    0);
                texture.Apply();

                string absolutePath =
                    Path.Combine(
                        Directory.GetParent(
                            Application.dataPath).FullName,
                        screenshotPath);
                Directory.CreateDirectory(
                    Path.GetDirectoryName(absolutePath));
                File.WriteAllBytes(
                    absolutePath,
                    texture.EncodeToPNG());
            }
            finally
            {
                camera.transform.position = previousPosition;
                camera.orthographicSize =
                    previousOrthographicSize;
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(renderTexture);
            }
        }

        private static T RequireObject<T>()
            where T : Object
        {
            T target =
                Object.FindFirstObjectByType<T>(
                    FindObjectsInactive.Include);
            Assert.That(
                target,
                Is.Not.Null,
                $"{typeof(T).Name} was not found.");
            return target;
        }
    }
}
