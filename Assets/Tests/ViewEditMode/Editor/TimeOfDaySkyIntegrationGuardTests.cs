using System;
using System.Collections.Generic;
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
            vehicle.GetComponent<BoxCollider>().center =
                new Vector3(0f, 0f, -0.5f);
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
                Assert.That(
                    VehicleNightLighting.Attach(vehicle, services),
                    Is.SameAs(lighting));
                Light[] headlights =
                    vehicle.GetComponentsInChildren<Light>(true);

                Assert.That(headlights.Length, Is.EqualTo(2));
                Assert.That(headlights[0].type, Is.EqualTo(LightType.Spot));
                Assert.That(headlights[0].intensity, Is.EqualTo(0.75f));
                Assert.That(headlights[0].spotAngle, Is.EqualTo(60f));
                Assert.That(headlights[0].innerSpotAngle, Is.EqualTo(20f));
                Assert.That(
                    headlights[0].range,
                    Is.EqualTo(6f).Within(0.0001f));
                Assert.That(headlights[0].enabled, Is.False);
                Assert.That(
                    headlights[0].transform.localPosition.z,
                    Is.EqualTo(-0.30f).Within(0.0001f));
                Renderer leftLens = vehicle.transform.Find(
                    "NightHeadlights/HeadlightLens_Left")
                    ?.GetComponent<Renderer>();
                Renderer rightLens = vehicle.transform.Find(
                    "NightHeadlights/HeadlightLens_Right")
                    ?.GetComponent<Renderer>();
                Assert.That(leftLens, Is.Not.Null);
                Assert.That(rightLens, Is.Not.Null);
                int lensCount = 0;
                foreach (Renderer renderer in
                         vehicle.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.name.StartsWith(
                            "HeadlightLens_",
                            StringComparison.Ordinal))
                    {
                        lensCount++;
                    }
                }
                Assert.That(lensCount, Is.EqualTo(2));
                Assert.That(leftLens.forceRenderingOff, Is.True);
                Assert.That(rightLens.forceRenderingOff, Is.True);

                calendar.SetHour(18);
                Assert.That(headlights[0].enabled, Is.False);

                lighting.SetMoving(true);
                Assert.That(headlights[0].enabled, Is.True);
                Assert.That(headlights[1].enabled, Is.True);
                Assert.That(leftLens.forceRenderingOff, Is.False);
                Assert.That(rightLens.forceRenderingOff, Is.False);

                calendar.SetHour(5);
                Assert.That(headlights[0].enabled, Is.True);

                calendar.SetHour(6);
                Assert.That(headlights[0].enabled, Is.False);
                Assert.That(leftLens.forceRenderingOff, Is.True);
                Assert.That(rightLens.forceRenderingOff, Is.True);
                leftLens.enabled = true;
                rightLens.enabled = true;
                Assert.That(
                    leftLens.forceRenderingOff,
                    Is.True,
                    "Vehicle presentation must not override the daytime lens state.");
                Assert.That(
                    rightLens.forceRenderingOff,
                    Is.True,
                    "Vehicle presentation must not override the daytime lens state.");

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
        public void VehicleHeadlights_RespectAuthoredForwardAxis()
        {
            var vehicle =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            vehicle.transform.localScale =
                new Vector3(0.4f, 1f, 0.2f);

            try
            {
                VehicleNightLighting.Attach(
                    vehicle,
                    null,
                    Vector3.right);
                Light[] headlights =
                    vehicle.GetComponentsInChildren<Light>(true);

                Assert.That(headlights.Length, Is.EqualTo(2));
                Assert.That(
                    headlights[0].transform.localPosition.x,
                    Is.GreaterThan(0.2f),
                    "The authored +X nose must place both headlights at the vehicle front.");
                Assert.That(
                    headlights[1].transform.localPosition.x,
                    Is.GreaterThan(0.2f));
                Assert.That(
                    Mathf.Sign(headlights[0].transform.localPosition.y),
                    Is.Not.EqualTo(
                        Mathf.Sign(headlights[1].transform.localPosition.y)),
                    "The two headlights must remain on opposite sides of the vehicle.");
                Assert.That(
                    headlights[0].gameObject.hideFlags &
                    HideFlags.HideInHierarchy,
                    Is.Not.EqualTo(HideFlags.None),
                    "Runtime light helpers must not draw distracting editor gizmo icons.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(vehicle);
            }
        }

        [Test]
        public void VehicleHeadlights_UseBodyRelativeLampHeightForAllProfiles()
        {
            var cityObject = new GameObject("Headlight Height City");
            MainCityView cityView =
                cityObject.AddComponent<MainCityView>();
            var regularVehicle =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            regularVehicle.transform.SetParent(
                cityObject.transform,
                false);
            regularVehicle.transform.localPosition =
                new Vector3(0f, 0f, cityView.RoadSurfaceZ);
            regularVehicle.transform.localScale =
                new Vector3(0.4f, 1f, 0.2f);
            regularVehicle.GetComponent<BoxCollider>().center =
                new Vector3(0f, 0f, -0.5f);
            var bus =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            bus.transform.SetParent(
                cityObject.transform,
                false);
            bus.transform.localPosition =
                new Vector3(1f, 0f, cityView.RoadSurfaceZ);
            bus.transform.localScale =
                new Vector3(0.4f, 1f, 0.2f);
            bus.GetComponent<BoxCollider>().center =
                new Vector3(0f, 0f, -0.5f);

            try
            {
                VehicleNightLighting.Attach(
                    regularVehicle,
                    null,
                    Vector3.right);
                VehicleNightLighting.AttachTallVehicle(
                    bus,
                    null,
                    Vector3.right);

                Light regularHeadlight =
                    regularVehicle.GetComponentInChildren<Light>(true);
                Light busHeadlight =
                    bus.GetComponentInChildren<Light>(true);

                Assert.That(regularHeadlight, Is.Not.Null);
                Assert.That(busHeadlight, Is.Not.Null);
                Assert.That(
                    regularHeadlight.transform.localPosition.z,
                    Is.EqualTo(-0.30f).Within(0.0001f));
                Assert.That(
                    busHeadlight.transform.localPosition.z,
                    Is.EqualTo(-0.30f).Within(0.0001f));
                float regularRoadDistance = Mathf.Abs(
                    cityView.RoadSurfaceZ -
                    cityObject.transform.InverseTransformPoint(
                        regularHeadlight.transform.position).z);
                float busRoadDistance = Mathf.Abs(
                    cityView.RoadSurfaceZ -
                    cityObject.transform.InverseTransformPoint(
                        busHeadlight.transform.position).z);
                Assert.That(
                    busRoadDistance,
                    Is.EqualTo(regularRoadDistance).Within(0.0001f),
                    "All vehicle profiles must place their lamps at the authored body-relative height.");
                Transform regularLens = regularVehicle.transform.Find(
                    "NightHeadlights/HeadlightLens_Left");
                Transform busLens = bus.transform.Find(
                    "NightHeadlights/HeadlightLens_Left");
                Assert.That(regularLens, Is.Not.Null);
                Assert.That(busLens, Is.Not.Null);
                Assert.That(
                    regularLens.localPosition.z,
                    Is.EqualTo(-0.30f).Within(0.0001f));
                Assert.That(
                    busLens.localPosition.z,
                    Is.EqualTo(-0.30f).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cityObject);
            }
        }

        [Test]
        public void TallVehicleHeadlights_LandAtVisibleRoadAngleAtRuntimeScale()
        {
            VehicleVisualCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<VehicleVisualCatalogSO>(
                    "Assets/05_ScriptableObjects/Resources/CityFlow/" +
                    "VehicleVisualCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.SchoolBusPrefab, Is.Not.Null);

            var cases = new List<(GameObject Prefab, float Scale)>
            {
                (catalog.SchoolBusPrefab, 0.76f)
            };
            foreach (GameObject cityBusPrefab in catalog.CityBusPrefabs)
            {
                cases.Add((cityBusPrefab, 0.75f));
            }

            var cityObject = new GameObject("Tall Headlight Runtime City");
            MainCityView cityView =
                cityObject.AddComponent<MainCityView>();
            var calendar = new TestGameCalendar(22);
            var services = new CityFlowServices(
                new SimEventHub(),
                null,
                null);
            services.RegisterGameCalendar(calendar);

            try
            {
                foreach ((GameObject prefab, float scale) in cases)
                {
                    Assert.That(prefab, Is.Not.Null);
                    GameObject vehicle =
                        UnityEngine.Object.Instantiate(prefab);
                    try
                    {
                        vehicle.transform.SetParent(
                            cityObject.transform,
                            false);
                        vehicle.transform.localScale =
                            Vector3.one * scale;

                        BoxCollider body =
                            vehicle.GetComponent<BoxCollider>();
                        Assert.That(body, Is.Not.Null);
                        VehicleNightLighting lighting =
                            VehicleNightLighting.AttachTallVehicle(
                                vehicle,
                                services,
                                Vector3.right);
                        lighting.SetMoving(true);
                        vehicle.transform.localPosition = new Vector3(
                            0f,
                            0f,
                            cityView.RoadSurfaceZ);

                        Light[] headlights =
                            vehicle.GetComponentsInChildren<Light>(true);
                        Assert.That(
                            headlights.Length,
                            Is.EqualTo(2),
                            $"{prefab.name} must create two headlights.");
                        foreach (Light headlight in headlights)
                        {
                            Assert.That(headlight.enabled, Is.True);
                            Vector3 cityOrigin =
                                cityObject.transform.InverseTransformPoint(
                                    headlight.transform.position);
                            Vector3 cityDirection =
                                cityObject.transform.InverseTransformDirection(
                                    headlight.transform.forward).normalized;
                            float hitDistance =
                                (cityView.RoadSurfaceZ - cityOrigin.z) /
                                cityDirection.z;
                            Vector3 cityRoadHit =
                                cityOrigin + cityDirection * hitDistance;
                            Vector3 cityVehicleNose =
                                cityObject.transform.InverseTransformPoint(
                                    vehicle.transform.TransformPoint(
                                        new Vector3(
                                            body.center.x +
                                            body.size.x * 0.5f,
                                            body.center.y,
                                            0f)));
                            float roadLandingDistance =
                                cityRoadHit.x - cityVehicleNose.x;
                            float pitchDegrees = Mathf.Atan2(
                                Mathf.Abs(cityDirection.z),
                                new Vector2(
                                    cityDirection.x,
                                    cityDirection.y).magnitude) *
                                Mathf.Rad2Deg;

                            Assert.That(
                                cityOrigin.z,
                                Is.LessThan(cityView.RoadSurfaceZ),
                                $"{prefab.name} headlights must stay above " +
                                "the road after the bus receives its road pose.");
                            Assert.That(hitDistance, Is.GreaterThan(0f));
                            Assert.That(
                                roadLandingDistance,
                                Is.EqualTo(body.size.x * scale * 0.25f)
                                    .Within(0.001f),
                                $"{prefab.name} must light the road close to its nose.");
                            Assert.That(
                                pitchDegrees,
                                Is.InRange(22f, 26f),
                                $"{prefab.name} must project an elongated road beam from its body-height lamps.");
                            Assert.That(
                                pitchDegrees,
                                Is.GreaterThan(headlight.innerSpotAngle * 0.5f),
                                $"{prefab.name} must keep the complete inner cone directed toward the road.");
                            Assert.That(
                                headlight.intensity,
                                Is.EqualTo(3.4f).Within(0.0001f));
                            Assert.That(
                                headlight.innerSpotAngle,
                                Is.EqualTo(20f).Within(0.0001f));
                            Assert.That(
                                headlight.range,
                                Is.EqualTo(body.size.x * scale * 6f)
                                    .Within(0.001f));

                            float roadHeight = Mathf.Abs(
                                cityView.RoadSurfaceZ - cityOrigin.z);
                            float planarDistance = new Vector2(
                                cityRoadHit.x - cityOrigin.x,
                                cityRoadHit.y - cityOrigin.y).magnitude;
                            float lightDistance = Mathf.Sqrt(
                                planarDistance * planarDistance +
                                roadHeight * roadHeight);
                            float roadIlluminationProxy =
                                headlight.intensity * roadHeight /
                                Mathf.Pow(lightDistance, 3f);
                            Assert.That(
                                roadIlluminationProxy,
                                Is.InRange(32f, 38f),
                                $"{prefab.name} must preserve the intended " +
                                "tall-vehicle road brightness at runtime scale.");
                        }

                        Transform leftLens = vehicle.transform.Find(
                            "NightHeadlights/HeadlightLens_Left");
                        Transform rightLens = vehicle.transform.Find(
                            "NightHeadlights/HeadlightLens_Right");
                        Transform leftLight = vehicle.transform.Find(
                            "NightHeadlights/Headlight_Left");
                        Transform rightLight = vehicle.transform.Find(
                            "NightHeadlights/Headlight_Right");
                        Assert.That(leftLens, Is.Not.Null);
                        Assert.That(rightLens, Is.Not.Null);
                        Assert.That(leftLight, Is.Not.Null);
                        Assert.That(rightLight, Is.Not.Null);
                        Assert.That(
                            leftLens.GetComponent<Renderer>()
                                .forceRenderingOff,
                            Is.False);
                        Assert.That(
                            rightLens.GetComponent<Renderer>()
                                .forceRenderingOff,
                            Is.False);
                        Assert.That(
                            leftLens.localPosition.z,
                            Is.EqualTo(
                                body.center.z +
                                body.size.z * 0.5f -
                                body.size.z * 0.30f)
                                .Within(0.0001f));
                        Assert.That(
                            rightLens.localPosition.z,
                            Is.EqualTo(leftLens.localPosition.z)
                                .Within(0.0001f));
                        Assert.That(
                            leftLens.localPosition.x,
                            Is.EqualTo(leftLight.localPosition.x)
                                .Within(0.0001f));
                        Assert.That(
                            leftLens.localPosition.y,
                            Is.EqualTo(leftLight.localPosition.y)
                                .Within(0.0001f));
                        Assert.That(
                            rightLens.localPosition.x,
                            Is.EqualTo(rightLight.localPosition.x)
                                .Within(0.0001f));
                        Assert.That(
                            rightLens.localPosition.y,
                            Is.EqualTo(rightLight.localPosition.y)
                                .Within(0.0001f));
                        Assert.That(
                            leftLens.localScale.x,
                            Is.EqualTo(body.size.x * 0.015f)
                                .Within(0.0001f));
                        Assert.That(
                            leftLens.localScale.y,
                            Is.EqualTo(body.size.y * 0.12f)
                                .Within(0.0001f));
                        Assert.That(
                            leftLens.localScale.z,
                            Is.EqualTo(body.size.z * 0.08f)
                                .Within(0.0001f));
                        Assert.That(
                            leftLens.GetComponent<Collider>(),
                            Is.Null);
                        Assert.That(
                            leftLens.GetComponent<Renderer>()
                                .shadowCastingMode,
                            Is.EqualTo(ShadowCastingMode.Off));

                        float spacing = Mathf.Abs(
                            headlights[0].transform.localPosition.y -
                            headlights[1].transform.localPosition.y);
                        Assert.That(
                            spacing,
                            Is.EqualTo(body.size.y * 0.36f)
                                .Within(0.0001f));
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(vehicle);
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cityObject);
            }
        }

        [Test]
        public void StandardVehicleHeadlights_UseBodyHeightAtRuntimeScale()
        {
            VehicleVisualCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<VehicleVisualCatalogSO>(
                    "Assets/05_ScriptableObjects/Resources/CityFlow/" +
                    "VehicleVisualCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.NormalVehiclePrefabs, Is.Not.Empty);

            var cityObject = new GameObject("Standard Headlight Runtime City");
            MainCityView cityView =
                cityObject.AddComponent<MainCityView>();
            var calendar = new TestGameCalendar(22);
            var services = new CityFlowServices(
                new SimEventHub(),
                null,
                null);
            services.RegisterGameCalendar(calendar);

            try
            {
                foreach (GameObject prefab in catalog.NormalVehiclePrefabs)
                {
                    Assert.That(prefab, Is.Not.Null);
                    GameObject vehicle =
                        UnityEngine.Object.Instantiate(prefab);
                    try
                    {
                        vehicle.transform.SetParent(
                            cityObject.transform,
                            false);
                        const float runtimeScale = 0.38f;
                        vehicle.transform.localScale =
                            Vector3.one * runtimeScale;

                        BoxCollider body =
                            vehicle.GetComponent<BoxCollider>();
                        Assert.That(body, Is.Not.Null);
                        VehicleNightLighting lighting =
                            VehicleNightLighting.Attach(
                                vehicle,
                                services,
                                Vector3.right);
                        lighting.SetMoving(true);
                        vehicle.transform.localPosition = new Vector3(
                            0f,
                            0f,
                            cityView.RoadSurfaceZ);

                        Light[] headlights =
                            vehicle.GetComponentsInChildren<Light>(true);
                        Assert.That(headlights.Length, Is.EqualTo(2));
                        foreach (Light headlight in headlights)
                        {
                            Assert.That(headlight.enabled, Is.True);
                            Vector3 cityOrigin =
                                cityObject.transform.InverseTransformPoint(
                                    headlight.transform.position);
                            Vector3 cityDirection =
                                cityObject.transform
                                    .InverseTransformDirection(
                                        headlight.transform.forward)
                                    .normalized;
                            float hitDistance =
                                (cityView.RoadSurfaceZ - cityOrigin.z) /
                                cityDirection.z;
                            Vector3 cityRoadHit =
                                cityOrigin + cityDirection * hitDistance;
                            Vector3 cityVehicleNose =
                                cityObject.transform.InverseTransformPoint(
                                    vehicle.transform.TransformPoint(
                                        new Vector3(
                                            body.center.x +
                                            body.size.x * 0.5f,
                                            body.center.y,
                                            0f)));
                            float roadLandingDistance =
                                cityRoadHit.x - cityVehicleNose.x;
                            float pitchDegrees = Mathf.Atan2(
                                Mathf.Abs(cityDirection.z),
                                new Vector2(
                                    cityDirection.x,
                                    cityDirection.y).magnitude) *
                                Mathf.Rad2Deg;

                            Assert.That(
                                cityOrigin.z,
                                Is.LessThan(cityView.RoadSurfaceZ));
                            Assert.That(hitDistance, Is.GreaterThan(0f));
                            Assert.That(
                                roadLandingDistance,
                                Is.EqualTo(
                                    body.size.x * runtimeScale * 0.25f)
                                    .Within(0.001f));
                            Assert.That(
                                pitchDegrees,
                                Is.InRange(20f, 32f),
                                $"{prefab.name} must aim from its actual " +
                                "body-height lamp position.");
                            Assert.That(
                                headlight.intensity,
                                Is.EqualTo(0.75f).Within(0.0001f));
                            Assert.That(
                                headlight.innerSpotAngle,
                                Is.EqualTo(20f).Within(0.0001f));
                            Assert.That(
                                headlight.range,
                                Is.EqualTo(
                                    body.size.x * runtimeScale * 6f)
                                    .Within(0.001f));

                            float roadHeight = Mathf.Abs(
                                cityView.RoadSurfaceZ - cityOrigin.z);
                            float planarDistance = new Vector2(
                                cityRoadHit.x - cityOrigin.x,
                                cityRoadHit.y - cityOrigin.y).magnitude;
                            float lightDistance = Mathf.Sqrt(
                                planarDistance * planarDistance +
                                roadHeight * roadHeight);
                            float roadIlluminationProxy =
                                headlight.intensity * roadHeight /
                                Mathf.Pow(lightDistance, 3f);
                            Assert.That(
                                roadIlluminationProxy,
                                Is.InRange(27f, 35f),
                                $"{prefab.name} must preserve the accepted " +
                                "normal-vehicle road brightness.");
                        }

                        Transform leftLens = vehicle.transform.Find(
                            "NightHeadlights/HeadlightLens_Left");
                        Transform rightLens = vehicle.transform.Find(
                            "NightHeadlights/HeadlightLens_Right");
                        Assert.That(leftLens, Is.Not.Null);
                        Assert.That(rightLens, Is.Not.Null);
                        Assert.That(
                            leftLens.GetComponent<Renderer>()
                                .forceRenderingOff,
                            Is.False);
                        Assert.That(
                            rightLens.GetComponent<Renderer>()
                                .forceRenderingOff,
                            Is.False);
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(vehicle);
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cityObject);
            }
        }

        [Test]
        public void VehicleHeadlights_AimAtRoadImmediatelyAheadOfVehicle()
        {
            var vehicle =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            vehicle.transform.localScale =
                new Vector3(0.4f, 1f, 0.2f);
            vehicle.GetComponent<BoxCollider>().center =
                new Vector3(0f, 0f, -0.5f);

            try
            {
                VehicleNightLighting.Attach(
                    vehicle,
                    null,
                    Vector3.right);
                Light headlight =
                    vehicle.GetComponentInChildren<Light>(true);

                Assert.That(headlight, Is.Not.Null);
                Vector3 rayOrigin =
                    headlight.transform.position;
                Vector3 rayDirection =
                    headlight.transform.forward.normalized;
                float roadHeight = vehicle.transform.position.z;
                float hitDistance =
                    (roadHeight - rayOrigin.z) /
                    rayDirection.z;
                Vector3 roadHit =
                    rayOrigin + rayDirection * hitDistance;
                Vector3 vehicleNose = vehicle.transform.TransformPoint(
                    new Vector3(0.5f, 0f, 0f));

                Assert.That(rayDirection.z, Is.GreaterThan(0f));
                Assert.That(
                    roadHit.x - vehicleNose.x,
                    Is.InRange(0.096f, 0.104f),
                    "Non-uniform vehicle scaling must keep the world-space " +
                    "road landing immediately ahead of the vehicle nose.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(vehicle);
            }
        }

        [Test]
        public void VehicleHeadlights_AimAtLocalRoadPlaneInRotatedWorld()
        {
            var cityObject = new GameObject("Rotated Main City View");
            cityObject.transform.position = new Vector3(12f, -7f, 5f);
            cityObject.transform.rotation =
                Quaternion.Euler(62f, 17f, -28f);
            MainCityView cityView =
                cityObject.AddComponent<MainCityView>();
            GameObject vehicle =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            vehicle.transform.SetParent(cityObject.transform, false);
            vehicle.transform.localPosition =
                new Vector3(2f, -1f, cityView.RoadSurfaceZ);
            vehicle.transform.localRotation =
                Quaternion.Euler(0f, 0f, 31f);
            vehicle.transform.localScale =
                Vector3.one * 0.4f;
            vehicle.GetComponent<BoxCollider>().center =
                new Vector3(0f, 0f, -0.5f);

            try
            {
                VehicleNightLighting.Attach(
                    vehicle,
                    null,
                    Vector3.right);
                Light headlight =
                    vehicle.GetComponentInChildren<Light>(true);

                Assert.That(headlight, Is.Not.Null);
                Vector3 cityOrigin =
                    cityObject.transform.InverseTransformPoint(
                        headlight.transform.position);
                Vector3 cityDirection =
                    cityObject.transform.InverseTransformDirection(
                        headlight.transform.forward).normalized;
                float hitDistance =
                    (cityView.RoadSurfaceZ - cityOrigin.z) /
                    cityDirection.z;
                Vector3 cityRoadHit =
                    cityOrigin + cityDirection * hitDistance;
                Vector3 vehicleRoadHit =
                    vehicle.transform.InverseTransformPoint(
                        cityObject.transform.TransformPoint(cityRoadHit));

                Assert.That(hitDistance, Is.GreaterThan(0f));
                Assert.That(
                    cityRoadHit.z,
                    Is.EqualTo(cityView.RoadSurfaceZ).Within(0.0001f));
                Assert.That(
                    vehicleRoadHit.x - 0.5f,
                    Is.InRange(0.23f, 0.27f),
                    "The rotated world must keep the light landing immediately ahead of the vehicle nose.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cityObject);
            }
        }

        [Test]
        public void VehicleHeadlights_KeepFixedSpacingWhileVehicleTurns()
        {
            var vehicle =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            vehicle.transform.localScale =
                new Vector3(0.6f, 0.3f, 0.2f);

            try
            {
                VehicleNightLighting lighting =
                    VehicleNightLighting.Attach(
                        vehicle,
                        null,
                        Vector3.right);
                Light[] headlights =
                    vehicle.GetComponentsInChildren<Light>(true);
                float initialSpacing = Mathf.Abs(
                    headlights[0].transform.localPosition.y -
                    headlights[1].transform.localPosition.y);
                Assert.That(
                    initialSpacing,
                    Is.EqualTo(0.36f).Within(0.0001f),
                    "The two headlights must use 36 percent of the vehicle width.");

                vehicle.transform.rotation =
                    Quaternion.Euler(0f, 0f, 45f);
                lighting.SetMoving(true);
                float turningSpacing = Mathf.Abs(
                    headlights[0].transform.localPosition.y -
                    headlights[1].transform.localPosition.y);

                Assert.That(
                    turningSpacing,
                    Is.EqualTo(initialSpacing).Within(0.0001f),
                    "Turning must not spread the two headlights apart in vehicle-local space.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(vehicle);
            }
        }

        [Test]
        public void CatalogVehicleHeadlights_UseNarrowBodyRelativeSpacing()
        {
            VehicleVisualCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<VehicleVisualCatalogSO>(
                    "Assets/05_ScriptableObjects/Resources/CityFlow/" +
                    "VehicleVisualCatalog.asset");
            Assert.That(catalog, Is.Not.Null);

            var prefabs = new List<GameObject>(
                catalog.NormalVehiclePrefabs)
            {
                catalog.SchoolBusPrefab,
                catalog.AmbulancePrefab
            };
            prefabs.AddRange(catalog.CityBusPrefabs);
            prefabs.Add(
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/02_Prefabs/Vehicles/" +
                    "PoliceVehicleVisual.prefab"));

            foreach (GameObject prefab in prefabs)
            {
                Assert.That(prefab, Is.Not.Null);
                GameObject instance =
                    UnityEngine.Object.Instantiate(prefab);
                try
                {
                    BoxCollider bodyBounds =
                        instance.GetComponent<BoxCollider>();
                    Assert.That(bodyBounds, Is.Not.Null);

                    VehicleNightLighting.Attach(
                        instance,
                        null,
                        Vector3.right);
                    Light[] headlights =
                        instance.GetComponentsInChildren<Light>(true);

                    Assert.That(
                        headlights.Length,
                        Is.EqualTo(2),
                        $"{prefab.name} must always create two headlights.");
                    float spacing = Mathf.Abs(
                        headlights[0].transform.localPosition.y -
                        headlights[1].transform.localPosition.y);
                    Assert.That(
                        spacing,
                        Is.EqualTo(bodyBounds.size.y * 0.36f)
                            .Within(0.0001f),
                        $"{prefab.name} must keep its light centers at " +
                        "36 percent of the rendered body width.");
                    float localGround =
                        bodyBounds.center.z +
                        bodyBounds.size.z * 0.5f;
                    Assert.That(
                        localGround,
                        Is.EqualTo(0f).Within(0.0001f),
                        $"{prefab.name} must keep its wrapper-local ground at Z=0.");
                    for (int index = 0; index < headlights.Length; index++)
                    {
                        Assert.That(
                            headlights[index].transform.localPosition.z,
                            Is.EqualTo(
                                localGround -
                                bodyBounds.size.z * 0.30f)
                                .Within(0.0001f),
                            $"{prefab.name} headlights must not depend on " +
                            "the root pose assigned after initialization.");
                    }

                    Transform leftLens = instance.transform.Find(
                        "NightHeadlights/HeadlightLens_Left");
                    Transform rightLens = instance.transform.Find(
                        "NightHeadlights/HeadlightLens_Right");
                    Assert.That(leftLens, Is.Not.Null);
                    Assert.That(rightLens, Is.Not.Null);
                    Assert.That(
                        leftLens.localPosition.z,
                        Is.EqualTo(
                            localGround -
                            bodyBounds.size.z * 0.30f)
                            .Within(0.0001f));
                    Assert.That(
                        rightLens.localPosition.z,
                        Is.EqualTo(leftLens.localPosition.z)
                            .Within(0.0001f));
                    Material lensMaterial =
                        leftLens.GetComponent<Renderer>().sharedMaterial;
                    Assert.That(lensMaterial, Is.Not.Null);
                    Assert.That(
                        lensMaterial.shader.name,
                        Is.EqualTo("GreenLight/CityFlow Headlight Lens"));
                    Assert.That(
                        lensMaterial.HasProperty("_LensColor"),
                        Is.True);
                    Assert.That(
                        lensMaterial.HasProperty("_BaseColor"),
                        Is.False,
                        "Vehicle tint effects must not recolor the warm-white lens.");
                    Assert.That(
                        lensMaterial.HasProperty("_Color"),
                        Is.False,
                        "Legacy vehicle tint effects must not recolor the lens.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void VehicleHeadlights_IgnoreChildVfxRendererBounds()
        {
            VehicleVisualCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<VehicleVisualCatalogSO>(
                    "Assets/05_ScriptableObjects/Resources/CityFlow/" +
                    "VehicleVisualCatalog.asset");
            FreeFlowStreakVfxProfileSO vfxProfile =
                AssetDatabase.LoadAssetAtPath<FreeFlowStreakVfxProfileSO>(
                    "Assets/05_ScriptableObjects/Resources/CityFlow/" +
                    "FreeFlowStreakVfxProfile.asset");
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.NormalVehiclePrefabs, Is.Not.Empty);
            Assert.That(vfxProfile, Is.Not.Null);
            Assert.That(vfxProfile.StageThreeStarsPrefab, Is.Not.Null);

            GameObject vehicle = UnityEngine.Object.Instantiate(
                catalog.NormalVehiclePrefabs[0]);
            GameObject effect = PrefabUtility.InstantiatePrefab(
                vfxProfile.StageThreeStarsPrefab) as GameObject;
            try
            {
                Assert.That(effect, Is.Not.Null);
                effect.transform.SetParent(vehicle.transform, false);
                effect.transform.localScale =
                    Vector3.one * vfxProfile.VfxScale;

                BoxCollider bodyBounds =
                    vehicle.GetComponent<BoxCollider>();
                Assert.That(bodyBounds, Is.Not.Null);
                VehicleNightLighting.Attach(
                    vehicle,
                    null,
                    Vector3.right);

                Light[] headlights =
                    vehicle.GetComponentsInChildren<Light>(true);
                Assert.That(headlights.Length, Is.EqualTo(2));
                float expectedFront =
                    bodyBounds.center.x +
                    bodyBounds.size.x * 0.5f +
                    bodyBounds.size.x * 0.01f;
                for (int index = 0; index < headlights.Length; index++)
                {
                    Assert.That(
                        headlights[index].transform.localPosition.x,
                        Is.EqualTo(expectedFront).Within(0.0001f),
                        "Child particle, trail, and text renderers must " +
                        "not move the headlights away from the vehicle nose.");
                }

                float spacing = Mathf.Abs(
                    headlights[0].transform.localPosition.y -
                    headlights[1].transform.localPosition.y);
                Assert.That(
                    spacing,
                    Is.EqualTo(bodyBounds.size.y * 0.36f)
                        .Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(effect);
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
