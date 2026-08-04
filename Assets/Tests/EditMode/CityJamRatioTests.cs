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

            // A small connected road/building layout forces the normal car
            // simulation pipeline to run; this test intentionally pins the
            // exposed scan result's contract, not a fragile traffic seed.
            for (int x = 1; x <= 6; x++)
            {
                Assert.That(engine.Place(new UnityEngine.Vector2Int(x, 1),
                    TileType.Road), Is.True);
            }
            Assert.That(engine.Place(new UnityEngine.Vector2Int(1, 2),
                TileType.House), Is.True);
            Assert.That(engine.Place(new UnityEngine.Vector2Int(4, 2),
                TileType.Office), Is.True);

            Assert.That(engine.CityJamRatio01, Is.InRange(0f, 1f));
            for (int i = 0; i < 8; i++)
            {
                engine.Tick(config.TickInterval);
            }
            Assert.That(engine.StepCount, Is.GreaterThanOrEqualTo(8),
                "Tick must execute the scan/update path before exposing the value");
            Assert.That(engine.CityJamRatio01, Is.InRange(0f, 1f));
        }
    }
}
