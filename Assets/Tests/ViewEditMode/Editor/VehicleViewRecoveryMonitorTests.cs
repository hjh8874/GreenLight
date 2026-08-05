using CityFlow.View;
using NUnit.Framework;

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
