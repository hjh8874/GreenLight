using CityFlow.Sim;
using CityFlow.Contracts;
using NUnit.Framework;

namespace CityFlow.Sim.Tests
{
    public sealed class CityJamRatioTests
    {
        [Test]
        public void CityJamRatio01_IsBoundedAndRefreshesAfterSimulationStep()
        {
            SimConfig config = SimConfig.Default();
            config.GridWidth = 8;
            config.GridHeight = 4;
            SimEngine engine = new SimEngine(config, new SimEventHub());

            Assert.That(engine.CityJamRatio01, Is.InRange(0f, 1f));
            engine.Tick(config.TickInterval);
            Assert.That(engine.CityJamRatio01, Is.InRange(0f, 1f));
        }
    }
}
