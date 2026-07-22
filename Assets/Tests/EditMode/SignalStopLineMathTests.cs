using CityFlow.ViewKit;
using NUnit.Framework;

namespace CityFlow.Sim.Tests
{
    public class SignalStopLineMathTests
    {
        [Test]
        public void CalculateStopDistance_LeavesVehicleFrontBeforeEntryBoundary()
        {
            float stop = SignalStopLineMath.CalculateStopDistance(
                approachCenterDistance: 0f,
                signalCenterDistance: 1f,
                vehicleLength: 0.4f,
                safetyMargin: 0.05f);

            Assert.AreEqual(0.25f, stop, 1e-4f);
        }

        [Test]
        public void CalculateStopDistance_LongerVehicleStopsEarlier()
        {
            float shortVehicle = SignalStopLineMath.CalculateStopDistance(0f, 1f, 0.3f, 0.05f);
            float longVehicle = SignalStopLineMath.CalculateStopDistance(0f, 1f, 0.5f, 0.05f);

            Assert.Less(longVehicle, shortVehicle);
        }
    }
}
