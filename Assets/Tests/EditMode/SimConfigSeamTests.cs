using CityFlow.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public class SimConfigSeamTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static SimConfig BaseConfig()
        {
            SimConfig cfg = SimConfig.Default();
            cfg.TickInterval = 0.25f;
            cfg.GridWidth = 10;
            cfg.GridHeight = 2;
            cfg.AutoDetectSignals = false;
            return cfg;
        }

        static SimEngine BuildWithIntersection(SimConfig cfg)
        {
            var engine = new SimEngine(cfg, new SimEventHub());
            for (int x = 0; x <= 9; x++) engine.Place(V(x, 0), TileType.Road);
            engine.Place(V(3, 1), TileType.Road);
            engine.Place(V(6, 1), TileType.Road);
            engine.Place(V(0, 1), TileType.House);
            engine.Place(V(9, 1), TileType.Office);
            engine.Tick(cfg.TickInterval);
            return engine;
        }

        [Test]
        public void ApplyConfig_PreservesStructuralFields_AndAppliesRuntimeTuning()
        {
            SimConfig cfg = BaseConfig();
            SimEngine engine = BuildWithIntersection(cfg);
            Assert.IsTrue(engine.TryPlaceSignal(V(3, 0), 8));

            SimConfig next = cfg;
            next.GridWidth = 999;
            next.GridHeight = 999;
            next.AutoDetectSignals = true;
            next.QueueSlowRatio = 0.25f;

            Assert.IsTrue(engine.ApplyConfig(next));
            engine.Tick(cfg.TickInterval);

            Assert.AreEqual(10, engine.CurrentConfig.GridWidth);
            Assert.AreEqual(2, engine.CurrentConfig.GridHeight);
            Assert.IsFalse(engine.CurrentConfig.AutoDetectSignals);
            Assert.AreEqual(0.25f, engine.CurrentConfig.QueueSlowRatio);
            Assert.AreEqual(1, engine.SignalTiles.Count);
            Assert.IsTrue(engine.CanPlaceSignal(V(6, 0)));
        }

        [Test]
        public void ApplyConfig_RejectsDegenerateConfig()
        {
            SimConfig cfg = BaseConfig();
            SimEngine engine = BuildWithIntersection(cfg);
            SimConfig before = engine.CurrentConfig;
            SimConfig degenerate = cfg;
            degenerate.TickInterval = 0f;

            Assert.IsFalse(engine.ApplyConfig(degenerate));
            Assert.AreEqual(before.TickInterval, engine.CurrentConfig.TickInterval);
            Assert.AreEqual(before.QueueSlowRatio, engine.CurrentConfig.QueueSlowRatio);
        }
    }
}
