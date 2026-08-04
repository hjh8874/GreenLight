using System;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Environment;
using CityFlow.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityFlow.Tests.ViewEditMode
{
    public sealed class TimeOfDaySkyIntegrationGuardTests
    {
        private const string ProfilePath =
            "Assets/05_ScriptableObjects/Environment/TimeOfDaySkyProfile.asset";
        private const string PrefabPath =
            "Assets/02_Prefabs/Environment/TimeOfDaySkySystem.prefab";
        private const string WorldCoordinatePrefabPath =
            "Assets/02_Prefabs/WorldCoordinates/WorldCoordinateSystem.prefab";
        private const string WorldGridPrefabPath =
            "Assets/02_Prefabs/WorldGrid/WorldGridSystem.prefab";
        private const string CityBusPrefabPath =
            "Assets/02_Prefabs/Vehicles/CityBusContent.prefab";
        private const string SpecialBuildingPrefabPath =
            "Assets/02_Prefabs/Buildings/SpecialBuildingSystem.prefab";
        private const string IntegrationScenePath =
            "Assets/00_Scenes/CityFlowIntegrated_cmt.unity";

        [Test]
        public void Profile_UsesCompleteSortedLightingCycle()
        {
            TimeOfDaySkyProfile profile =
                AssetDatabase.LoadAssetAtPath<
                    TimeOfDaySkyProfile>(ProfilePath);

            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.Keyframes.Count, Is.EqualTo(4));

            float[] expectedHours = { 0f, 6f, 12f, 18f };
            float previousHour = -1f;
            for (int i = 0; i < profile.Keyframes.Count; i++)
            {
                TimeOfDaySkyKeyframe keyframe =
                    profile.Keyframes[i];
                Assert.That(keyframe, Is.Not.Null);
                Assert.That(
                    keyframe.Hour,
                    Is.EqualTo(expectedHours[i]));
                Assert.That(
                    keyframe.Hour,
                    Is.GreaterThan(previousHour));
                previousHour = keyframe.Hour;
            }
        }

        [Test]
        public void Profile_MidnightIsTheDarkestLightingKeyframe()
        {
            TimeOfDaySkyProfile profile =
                AssetDatabase.LoadAssetAtPath<
                    TimeOfDaySkyProfile>(ProfilePath);
            TimeOfDaySkyKeyframe midnight = profile.Keyframes[0];

            for (int i = 1; i < profile.Keyframes.Count; i++)
            {
                Assert.That(
                    midnight.LightIntensity,
                    Is.LessThan(profile.Keyframes[i].LightIntensity));
                Assert.That(
                    midnight.AmbientIntensity,
                    Is.LessThan(profile.Keyframes[i].AmbientIntensity));
                Assert.That(
                    midnight.SkyExposure,
                    Is.LessThan(profile.Keyframes[i].SkyExposure));
            }
        }

        [Test]
        public void CelestialCycle_UsesTwelveHourSunAndMoonArcs()
        {
            TimeOfDaySkyController.CelestialCycleState sunrise =
                TimeOfDaySkyController.EvaluateCelestialCycle(6f);
            TimeOfDaySkyController.CelestialCycleState noon =
                TimeOfDaySkyController.EvaluateCelestialCycle(12f);
            TimeOfDaySkyController.CelestialCycleState sunset =
                TimeOfDaySkyController.EvaluateCelestialCycle(18f);
            TimeOfDaySkyController.CelestialCycleState midnight =
                TimeOfDaySkyController.EvaluateCelestialCycle(0f);

            Assert.That(sunrise.IsSun, Is.True);
            Assert.That(sunrise.Progress, Is.EqualTo(0f));
            Assert.That(sunrise.Altitude, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(sunrise.EastWeight, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(noon.IsSun, Is.True);
            Assert.That(noon.Progress, Is.EqualTo(0.5f));
            Assert.That(noon.Altitude, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(noon.EastWeight, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(sunset.IsSun, Is.False);
            Assert.That(sunset.Progress, Is.EqualTo(0f));
            Assert.That(midnight.IsSun, Is.False);
            Assert.That(midnight.Progress, Is.EqualTo(0.5f));
            Assert.That(midnight.Altitude, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void GridLineBrightness_DimsAtNightAndRestoresAtNoon()
        {
            float midnightBrightness =
                TimeOfDaySkyController.CalculateGridLineBrightness(
                    TimeOfDaySkyController.EvaluateCelestialCycle(0f));
            float noonBrightness =
                TimeOfDaySkyController.CalculateGridLineBrightness(
                    TimeOfDaySkyController.EvaluateCelestialCycle(12f));

            Assert.That(
                midnightBrightness,
                Is.EqualTo(0.32f).Within(0.0001f));
            Assert.That(
                noonBrightness,
                Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void VehicleHeadlights_RequireNightAndMovingState()
        {
            var vehicle =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            var calendar = new TestGameCalendar(17);
            var services = new CityFlowServices(
                new SimEventHub(),
                null,
                null);
            services.RegisterGameCalendar(calendar);

            try
            {
                VehicleNightLighting lighting =
                    VehicleNightLighting.Attach(vehicle, services);
                Light[] headlights =
                    vehicle.GetComponentsInChildren<Light>(true);

                Assert.That(headlights.Length, Is.EqualTo(2));
                Assert.That(headlights[0].type, Is.EqualTo(LightType.Spot));
                Assert.That(headlights[0].enabled, Is.False);
                Assert.That(
                    headlights[0].transform.localPosition.z,
                    Is.GreaterThan(0f));

                calendar.SetHour(18);
                Assert.That(headlights[0].enabled, Is.False);

                lighting.SetMoving(true);
                Assert.That(headlights[0].enabled, Is.True);
                Assert.That(headlights[1].enabled, Is.True);

                calendar.SetHour(5);
                Assert.That(headlights[0].enabled, Is.True);

                calendar.SetHour(6);
                Assert.That(headlights[0].enabled, Is.False);

                calendar.SetHour(23);
                lighting.SetMoving(false);
                Assert.That(headlights[0].enabled, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(vehicle);
            }
        }

        [Test]
        public void BuildingWindowLights_DoNotChangeBodyMaterial()
        {
            var building =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            Renderer renderer = building.GetComponent<Renderer>();
            Shader shader = Shader.Find(
                "Universal Render Pipeline/Lit");
            shader ??= Shader.Find("Standard");
            Assert.That(shader, Is.Not.Null);
            var originalMaterial = new Material(shader);
            renderer.sharedMaterial = originalMaterial;

            var facadeDetail = new GameObject("FacadeDetail");
            facadeDetail.transform.SetParent(
                building.transform,
                false);
            Mesh facadeMesh = new()
            {
                vertices = new[]
                {
                    new Vector3(-0.1f, 0.51f, 0.35f),
                    new Vector3(0.1f, 0.51f, 0.35f),
                    new Vector3(-0.1f, 0.51f, 0.55f),
                    new Vector3(0.1f, 0.51f, 0.55f)
                },
                normals = new[]
                {
                    Vector3.up,
                    Vector3.up,
                    Vector3.up,
                    Vector3.up
                },
                uv = new[]
                {
                    Vector2.zero,
                    Vector2.right,
                    Vector2.up,
                    Vector2.one
                },
                triangles = new[] { 0, 2, 1, 1, 2, 3 }
            };
            facadeMesh.RecalculateBounds();
            facadeMesh.UploadMeshData(true);
            facadeDetail.AddComponent<MeshFilter>()
                .sharedMesh = facadeMesh;
            MeshRenderer facadeRenderer =
                facadeDetail.AddComponent<MeshRenderer>();
            var atlasTexture = new Texture2D(4, 4);
            var atlasPixels = new Color[16];
            for (int index = 0;
                 index < atlasPixels.Length;
                 index++)
            {
                atlasPixels[index] = Color.white;
            }
            atlasPixels[0] = new Color(0.12f, 0.12f, 0.12f);
            atlasTexture.SetPixels(atlasPixels);
            atlasTexture.Apply();
            var facadeMaterial = new Material(shader)
            {
                mainTexture = atlasTexture
            };
            facadeRenderer.sharedMaterial = facadeMaterial;

            var calendar = new TestGameCalendar(17);
            var services = new CityFlowServices(
                new SimEventHub(),
                null,
                null);
            services.RegisterGameCalendar(calendar);

            try
            {
                BuildingNightLighting.Attach(
                    building,
                    services,
                    BuildingNightLightProfile.House);
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(originalMaterial));
                Assert.That(
                    facadeRenderer.sharedMaterials.Length,
                    Is.EqualTo(1));
                Assert.That(
                    facadeRenderer.sharedMaterials[0],
                    Is.SameAs(facadeMaterial));
                Transform overlayObject = facadeDetail.transform.Find(
                    "FacadeDetail_NightWindowOverlay");
                Assert.That(overlayObject, Is.Not.Null);
                Assert.That(
                    overlayObject.gameObject.activeSelf,
                    Is.False);
                Material windowMaterial = overlayObject
                    .GetComponent<MeshRenderer>()
                    .sharedMaterial;
                Assert.That(
                    windowMaterial.GetFloat("_Enabled"),
                    Is.EqualTo(0f));
                Assert.That(
                    windowMaterial.GetFloat("_WindowMaskProfile"),
                    Is.EqualTo(1f));
                string colorProperty = windowMaterial.HasProperty(
                    "_BaseColor")
                    ? "_BaseColor"
                    : "_Color";
                Color windowColor =
                    windowMaterial.GetColor(colorProperty);
                Assert.That(windowColor.r, Is.EqualTo(1f).Within(0.001f));
                Assert.That(windowColor.g, Is.EqualTo(0.9f).Within(0.001f));
                Assert.That(windowColor.b, Is.EqualTo(0.72f).Within(0.001f));

                calendar.SetHour(18);
                Assert.That(
                    windowMaterial.GetFloat("_Enabled"),
                    Is.EqualTo(1f));
                Assert.That(
                    overlayObject.gameObject.activeSelf,
                    Is.True);
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(originalMaterial));

                calendar.SetHour(23);
                Assert.That(
                    windowMaterial.GetFloat("_Enabled"),
                    Is.EqualTo(1f));

                calendar.SetHour(0);
                Assert.That(
                    windowMaterial.GetFloat("_Enabled"),
                    Is.EqualTo(0f));
                Assert.That(
                    overlayObject.gameObject.activeSelf,
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(building);
                UnityEngine.Object.DestroyImmediate(originalMaterial);
                UnityEngine.Object.DestroyImmediate(facadeMaterial);
                UnityEngine.Object.DestroyImmediate(atlasTexture);
                UnityEngine.Object.DestroyImmediate(facadeMesh);
            }
        }

        [Test]
        public void CelestialScreenPosition_KeepsBodyTangentToScreenEdges()
        {
            GameObject cameraObject = new("Celestial Orbit Test Camera");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 12.1875f;
                camera.aspect = 16f / 9f;

                TimeOfDaySkyController.CelestialCycleState sunrise =
                    TimeOfDaySkyController.EvaluateCelestialCycle(6f);
                TimeOfDaySkyController.CelestialCycleState noon =
                    TimeOfDaySkyController.EvaluateCelestialCycle(12f);
                Vector3 sunrisePosition =
                    TimeOfDaySkyController.CalculateCelestialCameraPosition(
                        camera,
                        0.5f,
                        10f,
                        1.6f,
                        sunrise);
                Vector3 noonPosition =
                    TimeOfDaySkyController.CalculateCelestialCameraPosition(
                        camera,
                        0.5f,
                        10f,
                        1.6f,
                        noon);

                Vector3 sunriseViewport =
                    camera.WorldToViewportPoint(sunrisePosition);
                Vector3 noonViewport =
                    camera.WorldToViewportPoint(noonPosition);
                float verticalRadius = 1.6f / (4f * 12.1875f);
                float horizontalRadius = verticalRadius / camera.aspect;
                Assert.That(
                    sunriseViewport.x + horizontalRadius,
                    Is.EqualTo(1f).Within(0.001f));
                Assert.That(sunriseViewport.y, Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(noonViewport.x, Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(
                    noonViewport.y + verticalRadius,
                    Is.EqualTo(1f).Within(0.001f));

                camera.transform.rotation = Quaternion.Euler(35f, 90f, 12f);
                noonPosition =
                    TimeOfDaySkyController.CalculateCelestialCameraPosition(
                        camera,
                        0.5f,
                        10f,
                        1.6f,
                        noon);
                noonViewport = camera.WorldToViewportPoint(noonPosition);
                Assert.That(noonViewport.x, Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(
                    noonViewport.y + verticalRadius,
                    Is.EqualTo(1f).Within(0.001f));

                camera.orthographicSize = 6.09375f;
                noonPosition =
                    TimeOfDaySkyController.CalculateCelestialCameraPosition(
                        camera,
                        0.5f,
                        10f,
                        1.6f,
                        noon);
                noonViewport = camera.WorldToViewportPoint(noonPosition);
                verticalRadius = 1.6f / (4f * 6.09375f);
                Assert.That(
                    noonViewport.y + verticalRadius,
                    Is.EqualTo(1f).Within(0.001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void CelestialDisplayDiameter_GetsSmallerWhenZoomingIn()
        {
            float maximumDiameter =
                TimeOfDaySkyController.CalculateCelestialDisplayDiameter(
                    1.6f,
                    16f,
                    16f,
                    0.7f);
            float zoomedInDiameter =
                TimeOfDaySkyController.CalculateCelestialDisplayDiameter(
                    1.6f,
                    8f,
                    16f,
                    0.7f);
            float maximumScreenDiameter =
                maximumDiameter / (2f * 16f);
            float zoomedInScreenDiameter =
                zoomedInDiameter / (2f * 8f);

            Assert.That(maximumDiameter, Is.EqualTo(1.6f));
            Assert.That(zoomedInDiameter, Is.EqualTo(0.68f).Within(0.001f));
            Assert.That(
                zoomedInScreenDiameter,
                Is.LessThan(maximumScreenDiameter));
        }

        [Test]
        public void CelestialCycle_ReappearsOnOppositeSideAtSixAndEighteen()
        {
            TimeOfDaySkyController.CelestialCycleState beforeSunrise =
                TimeOfDaySkyController.EvaluateCelestialCycle(5.999f);
            TimeOfDaySkyController.CelestialCycleState sunrise =
                TimeOfDaySkyController.EvaluateCelestialCycle(6f);
            TimeOfDaySkyController.CelestialCycleState beforeSunset =
                TimeOfDaySkyController.EvaluateCelestialCycle(17.999f);
            TimeOfDaySkyController.CelestialCycleState sunset =
                TimeOfDaySkyController.EvaluateCelestialCycle(18f);

            Assert.That(beforeSunrise.IsSun, Is.False);
            Assert.That(beforeSunrise.EastWeight, Is.LessThan(-0.999f));
            Assert.That(sunrise.IsSun, Is.True);
            Assert.That(sunrise.EastWeight, Is.EqualTo(1f).Within(0.001f));
            Assert.That(beforeSunset.IsSun, Is.True);
            Assert.That(beforeSunset.EastWeight, Is.LessThan(-0.999f));
            Assert.That(sunset.IsSun, Is.False);
            Assert.That(sunset.EastWeight, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void Prefab_HasSelfContainedRuntimeReferences()
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    PrefabPath);

            Assert.That(prefab, Is.Not.Null);
            TimeOfDaySkyController controller =
                prefab.GetComponent<TimeOfDaySkyController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.Profile, Is.Not.Null);
            Assert.That(
                controller.BlendSkyboxTemplate,
                Is.Not.Null);
            Assert.That(
                controller.BlendSkyboxTemplate.shader,
                Is.Not.Null);
            Assert.That(
                controller.BlendSkyboxTemplate.shader.name,
                Is.EqualTo("CityFlow/Skybox Cubemap Blend"));
            Assert.That(
                controller.BlendSkyboxTemplate.HasProperty("_TexA"),
                Is.True);
            Assert.That(
                controller.BlendSkyboxTemplate.HasProperty("_TexB"),
                Is.True);
            Assert.That(
                controller.BlendSkyboxTemplate.HasProperty("_Blend"),
                Is.True);
            Assert.That(
                controller.BlendSkyboxTemplate.HasProperty(
                    "_HorizonRotation"),
                Is.True);
            Assert.That(
                controller.BlendSkyboxTemplate.GetTexture("_TexA"),
                Is.TypeOf<Cubemap>());
            Assert.That(
                controller.BlendSkyboxTemplate.GetTexture("_TexB"),
                Is.SameAs(
                    controller.BlendSkyboxTemplate.GetTexture("_TexA")));
            Assert.That(
                controller.CelestialOverlayTemplate,
                Is.Not.Null);
            Assert.That(
                controller.CelestialOverlayTemplate.shader.name,
                Is.EqualTo("CityFlow/Celestial Overlay"));
            Assert.That(controller.ShowCelestialVisual, Is.False);
            Assert.That(controller.KeyLight, Is.Not.Null);
            Assert.That(
                controller.KeyLight.type,
                Is.EqualTo(LightType.Directional));
            Assert.That(
                controller.KeyLight.transform.IsChildOf(
                    prefab.transform),
                Is.True);
        }

        [Test]
        public void IntegrationScene_ContainsSkySystemPrefab()
        {
            string[] dependencies =
                AssetDatabase.GetDependencies(
                    IntegrationScenePath,
                    true);

            Assert.That(
                dependencies,
                Does.Contain(PrefabPath));
        }

        [Test]
        public void IntegrationScene_ContainsMergedFeaturePrefabs()
        {
            string[] dependencies =
                AssetDatabase.GetDependencies(
                    IntegrationScenePath,
                    true);

            Assert.That(
                dependencies,
                Does.Contain(WorldCoordinatePrefabPath));
            Assert.That(
                dependencies,
                Does.Contain(WorldGridPrefabPath));
            Assert.That(
                dependencies,
                Does.Contain(CityBusPrefabPath));
            Assert.That(
                dependencies,
                Does.Contain(SpecialBuildingPrefabPath));
        }

        [Test]
        public void Controller_AppliesRegisteredCalendarAndRestoresRenderSettings()
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    PrefabPath);
            Material originalSkybox = RenderSettings.skybox;
            Light originalSun = RenderSettings.sun;
            AmbientMode originalAmbientMode =
                RenderSettings.ambientMode;
            GameObject instance = null;
            GameObject cameraObject = null;
            TimeOfDaySkyController controller = null;

            try
            {
                cameraObject =
                    new GameObject("Sky Horizon Test Camera");
                cameraObject.tag = "MainCamera";
                Camera testCamera =
                    cameraObject.AddComponent<Camera>();
                testCamera.orthographic = true;
                testCamera.orthographicSize = 10f;

                instance = UnityEngine.Object.Instantiate(prefab);
                controller =
                    instance.GetComponent<
                        TimeOfDaySkyController>();
                controller.ActivateRenderSettings();
                CityFlowServices services =
                    new(
                        new SimEventHub(),
                        null,
                        null);
                TestGameCalendar calendar =
                    new(hour: 20);

                controller.Initialize(services);
                services.RegisterGameCalendar(calendar);

                Assert.That(
                    RenderSettings.skybox,
                    Is.Not.SameAs(originalSkybox));
                Assert.That(
                    RenderSettings.skybox.shader.name,
                    Is.EqualTo(
                        "CityFlow/Skybox Cubemap Blend"));
                Assert.That(
                    RenderSettings.sun,
                    Is.SameAs(controller.KeyLight));
                Assert.That(
                    RenderSettings.ambientMode,
                    Is.EqualTo(AmbientMode.Trilight));

                Texture fixedSkyTexture =
                    RenderSettings.skybox.GetTexture("_TexA");
                calendar.SetHour(12);
                Assert.That(
                    RenderSettings.skybox.GetTexture("_TexA"),
                    Is.SameAs(fixedSkyTexture));
                Assert.That(
                    RenderSettings.skybox.GetTexture("_TexB"),
                    Is.SameAs(fixedSkyTexture));
                Assert.That(
                    RenderSettings.skybox.GetFloat("_Blend"),
                    Is.EqualTo(0f));
                Assert.That(
                    RenderSettings.skybox.GetFloat("_ExposureA"),
                    Is.EqualTo(1f).Within(0.001f));
                Transform celestialBody =
                    instance.transform.Find(
                        "TimeOfDayCelestialBody");
                Assert.That(celestialBody, Is.Null);
                Assert.That(
                    controller.KeyLight.intensity,
                    Is.EqualTo(1.15f).Within(0.001f));
                Quaternion noonLightRotation =
                    controller.KeyLight.transform.rotation;

                calendar.SetHour(6);
                Assert.That(
                    RenderSettings.skybox.GetTexture("_TexA"),
                    Is.SameAs(fixedSkyTexture));
                Assert.That(
                    controller.KeyLight.intensity,
                    Is.EqualTo(0.12f).Within(0.001f));
                Assert.That(
                    controller.KeyLight.transform.rotation,
                    Is.Not.EqualTo(noonLightRotation));
                calendar.SetHour(18);
                Assert.That(
                    RenderSettings.skybox.GetTexture("_TexA"),
                    Is.SameAs(fixedSkyTexture));
                Assert.That(
                    controller.KeyLight.intensity,
                    Is.EqualTo(0.1f).Within(0.001f));
                Assert.That(
                    RenderSettings.skybox.GetFloat("_ExposureA"),
                    Is.EqualTo(0.9f).Within(0.001f));
                calendar.SetHour(0);
                Assert.That(
                    controller.KeyLight.intensity,
                    Is.EqualTo(0.01f).Within(0.001f));
                Assert.That(
                    RenderSettings.skybox.GetFloat("_ExposureA"),
                    Is.EqualTo(0.035f).Within(0.001f));
                Assert.That(
                    RenderSettings.ambientIntensity,
                    Is.EqualTo(0.05f).Within(0.001f));
                Assert.That(
                    controller.KeyLight.color.b,
                    Is.GreaterThan(controller.KeyLight.color.r));

                testCamera.transform.rotation =
                    Quaternion.LookRotation(
                        new Vector3(-0.58f, 0.58f, 0.57f),
                        new Vector3(-1f, 1f, 0f));
                controller.ApplyHorizonCorrection(testCamera);

                Vector4 serializedCorrection =
                    RenderSettings.skybox.GetVector(
                        "_HorizonRotation");
                Quaternion correction =
                    new(
                        serializedCorrection.x,
                        serializedCorrection.y,
                        serializedCorrection.z,
                        serializedCorrection.w);
                Vector3 apparentSkyUp =
                    Quaternion.Inverse(correction) *
                    Vector3.up;
                Assert.That(
                    Mathf.Abs(
                        Vector3.Dot(
                            testCamera.transform.right,
                            apparentSkyUp.normalized)),
                    Is.LessThan(0.001f));
            }
            finally
            {
                if (controller != null)
                {
                    controller.DeactivateRenderSettings();
                }

                if (cameraObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        cameraObject);
                }

                if (instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        instance);
                }
            }

            Assert.That(
                RenderSettings.skybox,
                Is.SameAs(originalSkybox));
            Assert.That(
                RenderSettings.sun,
                Is.SameAs(originalSun));
            Assert.That(
                RenderSettings.ambientMode,
                Is.EqualTo(originalAmbientMode));
        }

        private sealed class TestGameCalendar
            : IGameCalendarService
        {
            public TestGameCalendar(int hour)
            {
                Hour = hour;
            }

            public int Year => 1;
            public int Month => 1;
            public int Day => 1;
            public int Hour { get; private set; }
            public int TotalMonths => 1;
            public long TotalDays => 0L;
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
                Hour = hour;
                HourChanged?.Invoke(hour);
            }
        }
    }
}
