using CityFlow.Contracts;
using CityFlow.View;
using NUnit.Framework;

namespace CityFlow.Sim.Tests
{
    public sealed class SchoolBusRoadExitRecoveryTests
    {
        [Test]
        public void OffRoadExitPresentation_SpacingBlockUsesRecoveryDeadline()
        {
            bool eligible =
                BusWorldView.IsRoadPresentationRecoveryEligible(
                    isVisible: true,
                    state: RoadTrafficAgentState.HoldingAtDestination,
                    spacingBlocked: true,
                    stopPresentationPending: false,
                    offRoadExitPresentationPending: true);

            Assert.IsTrue(
                eligible,
                "A school bus holding the road queue for its parking transition " +
                "must remain eligible for the presentation timeout.");

            var monitor = new VehicleViewRecoveryMonitor();
            Assert.AreEqual(
                VehicleViewRecoveryReason.None,
                Observe(monitor, eligible, 0f));
            Assert.AreEqual(
                VehicleViewRecoveryReason.None,
                Observe(monitor, eligible, 0.5f));
            Assert.AreEqual(
                VehicleViewRecoveryReason.None,
                Observe(monitor, eligible, 0.5f));
            Assert.AreEqual(
                VehicleViewRecoveryReason.StopPresentationTimeout,
                Observe(monitor, eligible, 0.5f));
        }

        [Test]
        public void OrdinarySpacingBlock_DoesNotBypassLeaderClearance()
        {
            Assert.IsFalse(
                BusWorldView.IsRoadPresentationRecoveryEligible(
                    isVisible: true,
                    state: RoadTrafficAgentState.Moving,
                    spacingBlocked: true,
                    stopPresentationPending: false,
                    offRoadExitPresentationPending: false));
        }

        private static VehicleViewRecoveryReason Observe(
            VehicleViewRecoveryMonitor monitor,
            bool eligible,
            float deltaTime) =>
            monitor.Observe(
                currentDistance: 0f,
                authoritativeDistance: 2f,
                tileSize: 1f,
                unscaledDeltaTime: deltaTime,
                currentRouteVersion: 1,
                eligible: eligible,
                stopPresentationPending: true,
                profile: null);
    }
}
