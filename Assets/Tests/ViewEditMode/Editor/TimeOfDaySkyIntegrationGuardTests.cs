using System;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Environment;
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
        public void Profile_UsesCompleteSortedDayCycle()
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
                Assert.That(keyframe.SkyRotation, Is.EqualTo(0f));
                Assert.That(keyframe.TransitionHours, Is.EqualTo(0f));
                Assert.That(
                    keyframe.LightEuler,
                    Is.EqualTo(new Vector3(50f, -30f, 0f)));
                Assert.That(
                    keyframe.SkyboxMaterial,
                    Is.Not.Null,
                    $"Sky material is missing at {keyframe.Hour:0.##}:00.");
                Assert.That(
                    keyframe.SkyboxMaterial.HasProperty("_Tex"),
                    Is.True,
                    $"{keyframe.SkyboxMaterial.name} is not an AllSky cubemap material.");
                Assert.That(
                    keyframe.SkyboxMaterial.GetTexture("_Tex"),
                    Is.TypeOf<Cubemap>(),
                    $"{keyframe.SkyboxMaterial.name} has no generated cubemap texture.");
                previousHour = keyframe.Hour;
            }
        }

        [Test]
        public void Profile_EvaluatesMidnightWrap()
        {
            TimeOfDaySkyProfile profile =
                AssetDatabase.LoadAssetAtPath<
                    TimeOfDaySkyProfile>(ProfilePath);

            Assert.That(
                profile.TryEvaluate(
                    23f,
                    out TimeOfDaySkyEvaluation evaluation),
                Is.True);
            Assert.That(evaluation.Current.Hour, Is.EqualTo(18f));
            Assert.That(evaluation.Next.Hour, Is.EqualTo(0f));
            Assert.That(
                evaluation.SegmentProgress,
                Is.GreaterThan(0f));
            Assert.That(evaluation.SkyBlend, Is.EqualTo(0f));

            Assert.That(
                profile.TryEvaluate(
                    23.75f,
                    out evaluation),
                Is.True);
            Assert.That(evaluation.Current.Hour, Is.EqualTo(18f));
            Assert.That(evaluation.Next.Hour, Is.EqualTo(0f));
            Assert.That(evaluation.SkyBlend, Is.EqualTo(0f));
        }

        [Test]
        public void Profile_UsesFourStaticSixHourPhases()
        {
            TimeOfDaySkyProfile profile =
                AssetDatabase.LoadAssetAtPath<
                    TimeOfDaySkyProfile>(ProfilePath);

            AssertStaticPhase(profile, 0f, 0f);
            AssertStaticPhase(profile, 5.99f, 0f);
            AssertStaticPhase(profile, 6f, 6f);
            AssertStaticPhase(profile, 11.99f, 6f);
            AssertStaticPhase(profile, 12f, 12f);
            AssertStaticPhase(profile, 17.99f, 12f);
            AssertStaticPhase(profile, 18f, 18f);
            AssertStaticPhase(profile, 23.99f, 18f);
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

                Quaternion fixedLightRotation =
                    controller.KeyLight.transform.rotation;
                calendar.SetHour(12);
                TimeOfDaySkyKeyframe noon =
                    controller.Profile.Keyframes[2];
                Assert.That(noon.Hour, Is.EqualTo(12f));
                Assert.That(
                    RenderSettings.skybox.GetTexture("_TexA"),
                    Is.SameAs(
                        noon.SkyboxMaterial.GetTexture("_Tex")));
                Assert.That(
                    RenderSettings.skybox.GetTexture("_TexB"),
                    Is.SameAs(
                        noon.SkyboxMaterial.GetTexture("_Tex")));
                Assert.That(
                    RenderSettings.skybox.GetFloat("_Blend"),
                    Is.EqualTo(0f));
                Assert.That(
                    RenderSettings.skybox.GetFloat("_RotationA"),
                    Is.EqualTo(0f));
                Assert.That(
                    RenderSettings.skybox.GetFloat("_RotationB"),
                    Is.EqualTo(0f));
                Assert.That(
                    controller.KeyLight.transform.rotation,
                    Is.EqualTo(fixedLightRotation));

                calendar.SetHour(6);
                Assert.That(
                    controller.KeyLight.transform.rotation,
                    Is.EqualTo(fixedLightRotation));
                calendar.SetHour(18);
                Assert.That(
                    controller.KeyLight.transform.rotation,
                    Is.EqualTo(fixedLightRotation));

                cameraObject =
                    new GameObject("Sky Horizon Test Camera");
                Camera testCamera =
                    cameraObject.AddComponent<Camera>();
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

        private static void AssertStaticPhase(
            TimeOfDaySkyProfile profile,
            float gameHour,
            float expectedCurrentHour)
        {
            Assert.That(
                profile.TryEvaluate(
                    gameHour,
                    out TimeOfDaySkyEvaluation evaluation),
                Is.True);
            Assert.That(
                evaluation.Current.Hour,
                Is.EqualTo(expectedCurrentHour));
            Assert.That(evaluation.SkyBlend, Is.EqualTo(0f));
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
