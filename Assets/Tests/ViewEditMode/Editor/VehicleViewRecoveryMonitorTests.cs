using System;
using System.Reflection;
using CityFlow.View;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CityFlow.Sim.Tests
{
    public sealed class VehicleViewRecoveryMonitorTests
    {
        [Test]
        public void Observe_StalledBehindAuthority_RecoversAfterTwoSeconds()
        {
            var monitor = new VehicleViewRecoveryMonitor();

            Assert.AreEqual(
                VehicleViewRecoveryReason.None,
                Observe(monitor, deltaTime: 0f));
            for (int sample = 0; sample < 3; sample++)
            {
                Assert.AreEqual(
                    VehicleViewRecoveryReason.None,
                    Observe(monitor, deltaTime: 0.5f));
            }

            Assert.AreEqual(
                VehicleViewRecoveryReason.StalledBehindAuthority,
                Observe(monitor, deltaTime: 0.5f));
        }

        [Test]
        public void Observe_ProgressResetsStallTimer()
        {
            var monitor = new VehicleViewRecoveryMonitor();

            Observe(monitor, deltaTime: 0f);
            Observe(monitor, deltaTime: 1f);
            Assert.AreEqual(
                VehicleViewRecoveryReason.None,
                monitor.Observe(
                    currentDistance: 0.1f,
                    authoritativeDistance: 2f,
                    tileSize: 1f,
                    unscaledDeltaTime: 1f,
                    currentRouteVersion: 1,
                    eligible: true,
                    stopPresentationPending: false,
                    profile: null));
            Assert.AreEqual(
                VehicleViewRecoveryReason.None,
                monitor.Observe(
                    currentDistance: 0.1f,
                    authoritativeDistance: 2f,
                    tileSize: 1f,
                    unscaledDeltaTime: 1f,
                    currentRouteVersion: 1,
                    eligible: true,
                    stopPresentationPending: false,
                    profile: null));
        }

        [Test]
        public void Observe_SmallPerFrameProgress_DoesNotFalseTrigger()
        {
            var monitor = new VehicleViewRecoveryMonitor();
            float distance = 0f;

            monitor.Observe(
                distance,
                authoritativeDistance: 2f,
                tileSize: 1f,
                unscaledDeltaTime: 0f,
                currentRouteVersion: 1,
                eligible: true,
                stopPresentationPending: false,
                profile: null);
            for (int sample = 0; sample < 12; sample++)
            {
                distance += 0.01f;
                Assert.AreEqual(
                    VehicleViewRecoveryReason.None,
                    monitor.Observe(
                        distance,
                        authoritativeDistance: 2f,
                        tileSize: 1f,
                        unscaledDeltaTime: 0.25f,
                        currentRouteVersion: 1,
                        eligible: true,
                        stopPresentationPending: false,
                        profile: null));
            }
        }

        [Test]
        public void Observe_StopPresentationPending_UsesShorterTimeout()
        {
            var monitor = new VehicleViewRecoveryMonitor();

            Observe(monitor, deltaTime: 0f, stopPending: true);
            Assert.AreEqual(
                VehicleViewRecoveryReason.None,
                Observe(monitor, deltaTime: 0.5f, stopPending: true));
            Assert.AreEqual(
                VehicleViewRecoveryReason.None,
                Observe(monitor, deltaTime: 0.5f, stopPending: true));
            Assert.AreEqual(
                VehicleViewRecoveryReason.StopPresentationTimeout,
                Observe(monitor, deltaTime: 0.5f, stopPending: true));
        }

        [Test]
        public void LeaderConstraint_ZeroAdvanceStillBlocksRecoverySnap()
        {
            Assert.IsTrue(
                MainCityView.IsVehicleViewRecoveryBlockedByLeader(
                    headway: 0.29f,
                    minimumHeadway: 0.59f,
                    followingSpeed: 0f,
                    unconstrainedSpeed: 0f,
                    authoritativeAdvance: 2f),
                "완전히 멈춘 프레임에도 앞차 최소 간격 안이면 복구 순간이동을 허용하면 안 됩니다.");
            Assert.IsFalse(
                MainCityView.IsVehicleViewRecoveryBlockedByLeader(
                    headway: 1.2f,
                    minimumHeadway: 0.59f,
                    followingSpeed: 1f,
                    unconstrainedSpeed: 1f,
                    authoritativeAdvance: 0.5f),
                "충분히 먼 앞차는 정상 view recovery를 불필요하게 막지 않아야 합니다.");
            Assert.IsTrue(
                MainCityView.IsVehicleViewRecoveryBlockedByLeader(
                    headway: 0.8f,
                    minimumHeadway: 0.59f,
                    followingSpeed: 1f,
                    unconstrainedSpeed: 1f,
                    authoritativeAdvance: 5f),
                "복구 목표가 앞차의 안전 경계를 넘으면 현재 속도를 제한하지 않아도 스냅을 막아야 합니다.");
        }

        [Test]
        public void RoundaboutEntryClearance_UsesFullCenterSpacing()
        {
            Assert.That(
                MainCityView.ResolveRoundaboutEntryClearance(
                    requiredCenterSpacing: 0.59f,
                    vehicleLength: 0.51f,
                    tileSize: 1f),
                Is.EqualTo(0.59f).Within(0.0001f),
                "로터리 정지선은 앞차까지의 전체 중심 간격을 확보해야 합니다.");
        }

        [Test]
        public void CommuteVehicleVisibility_HidesMovingCarUntilRoadAdmission()
        {
            Assert.IsFalse(MainCityView.ShouldShowCommuteVehicle(
                isVisible: true,
                state: CarState.Inbound,
                queueSlot: -1,
                intersectionProgress: -1f,
                linkProgress: 0f,
                roundaboutProgress: -1f));
            Assert.IsTrue(MainCityView.ShouldShowCommuteVehicle(
                isVisible: true,
                state: CarState.Inbound,
                queueSlot: 0,
                intersectionProgress: -1f,
                linkProgress: 0f,
                roundaboutProgress: -1f));
            Assert.IsTrue(MainCityView.ShouldShowCommuteVehicle(
                isVisible: true,
                state: CarState.ParkedHome,
                queueSlot: -1,
                intersectionProgress: -1f,
                linkProgress: 0f,
                roundaboutProgress: -1f));
        }

        [Test]
        public void HiddenCommuteVehicle_DisablesWholePresentation()
        {
            GameObject vehicle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject detail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject brakeLight = new GameObject("BrakeLight");
            GameObject angryMark = new GameObject("AngryMark");
            detail.transform.SetParent(vehicle.transform, false);
            brakeLight.transform.SetParent(vehicle.transform, false);
            angryMark.transform.SetParent(vehicle.transform, false);
            try
            {
                Renderer renderer = vehicle.GetComponent<Renderer>();
                Collider collider = vehicle.GetComponent<Collider>();
                Renderer detailRenderer = detail.GetComponent<Renderer>();
                Collider detailCollider = detail.GetComponent<Collider>();
                VehicleNightLighting nightLighting =
                    VehicleNightLighting.Attach(vehicle, null);
                nightLighting.SetMoving(true);
                typeof(VehicleNightLighting)
                    .GetMethod(
                        "ApplyHour",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(nightLighting, new object[] { 18 });

                Type routeVehicleType = typeof(MainCityView).GetNestedType(
                    "RouteVehicle",
                    BindingFlags.NonPublic);
                Assert.NotNull(routeVehicleType);
                object routeVehicle = Activator.CreateInstance(
                    routeVehicleType,
                    nonPublic: true);
                routeVehicleType.GetField("Renderers")?.SetValue(
                    routeVehicle,
                    new[] { renderer });
                routeVehicleType.GetField("Colliders")?.SetValue(
                    routeVehicle,
                    new[] { collider, detailCollider });
                routeVehicleType.GetField("DetailRenderer")?.SetValue(
                    routeVehicle,
                    detailRenderer);
                routeVehicleType.GetField("BrakeLight")?.SetValue(
                    routeVehicle,
                    brakeLight);
                routeVehicleType.GetField("BrakeOn")?.SetValue(
                    routeVehicle,
                    true);
                routeVehicleType.GetField("NightLighting")?.SetValue(
                    routeVehicle,
                    nightLighting);
                routeVehicleType.GetField("AngryMark")?.SetValue(
                    routeVehicle,
                    angryMark);

                MethodInfo setPresentation = typeof(MainCityView).GetMethod(
                    "SetVehiclePresentationEnabled",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.NotNull(setPresentation);
                setPresentation.Invoke(
                    null,
                    new[] { routeVehicle, (object)false });

                Assert.IsFalse(renderer.enabled);
                Assert.IsFalse(collider.enabled);
                Assert.IsFalse(detailRenderer.enabled);
                Assert.IsFalse(detailCollider.enabled);
                Assert.IsFalse(brakeLight.activeSelf);
                Assert.IsFalse(angryMark.activeSelf);
                foreach (Light headlight in
                         vehicle.GetComponentsInChildren<Light>(true))
                {
                    Assert.IsFalse(headlight.enabled);
                }

                setPresentation.Invoke(
                    null,
                    new[] { routeVehicle, (object)true });
                Assert.IsTrue(renderer.enabled);
                Assert.IsTrue(collider.enabled);
                Assert.IsTrue(detailRenderer.enabled);
                Assert.IsTrue(detailCollider.enabled);
            }
            finally
            {
                Object.DestroyImmediate(vehicle);
            }
        }

        [Test]
        public void RecoveryDistance_DoesNotCrossAuthoritativeStopCorridor()
        {
            Assert.That(
                MainCityView.ResolveVehicleViewRecoveryDistance(
                    previousDistance: 0.1f,
                    recoveredDistance: 5.355f,
                    authoritativeDistance: 0.7f,
                    polylineLength: 6f),
                Is.EqualTo(0.7f).Within(0.0001f));
            Assert.That(
                MainCityView.ResolveVehicleViewRecoveryDistance(
                    previousDistance: 0.8f,
                    recoveredDistance: 0.4f,
                    authoritativeDistance: 0.5f,
                    polylineLength: 6f),
                Is.EqualTo(0.8f).Within(0.0001f),
                "복구는 차량을 뒤로 순간이동시키지 않아야 합니다.");
        }

        private static VehicleViewRecoveryReason Observe(
            VehicleViewRecoveryMonitor monitor,
            float deltaTime,
            bool stopPending = false)
        {
            return monitor.Observe(
                currentDistance: 0f,
                authoritativeDistance: 2f,
                tileSize: 1f,
                unscaledDeltaTime: deltaTime,
                currentRouteVersion: 1,
                eligible: true,
                stopPresentationPending: stopPending,
                profile: null);
        }
    }
}
