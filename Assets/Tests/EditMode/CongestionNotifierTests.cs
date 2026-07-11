using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class CongestionNotifierTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static CityGrid StraightCity()
        {
            var g = new CityGrid(5, 2);
            for (int x = 0; x <= 4; x++) g.Place(V(x, 0), TileType.Road);
            g.Place(V(0, 1), TileType.House);
            g.Place(V(4, 1), TileType.Office);
            return g;
        }

        [Test]
        public void PublishesOnlyOnLevelTransition()
        {
            var g = StraightCity();
            var cfg = SimConfig.Default();       // Slow 0.7 / Jam 1.0
            cfg.RoadCapacity = 10f;
            var dm = new DemandMap(cfg); dm.Reassign(g, new RoadNetwork(g));
            var net = new RoadNetwork(g);
            var planner = new RoutePlanner(g.Width, g.Height);
            planner.Plan(dm, net, g, cfg);
            var solver = new FlowSolver(g.Width, g.Height);
            var notifier = new CongestionNotifier(g.Width, g.Height);

            var hub = new SimEventHub();
            var received = new List<CongestionEvent>();
            hub.CongestionChanged += e => received.Add(e);
            var buffer = new SimEventBuffer(hub);

            void Tick(float demand)
            {
                cfg.DemandPerHouse = demand;
                solver.Assign(dm, planner, cfg);
                solver.Resolve(cfg);
                notifier.Scan(solver, buffer, cfg);
                buffer.Drain();
            }

            Tick(1f);                             // ratio 0.1 → Free 유지(초기값도 Free)
            Assert.AreEqual(0, received.Count);   // 변화 없음 = 무발행

            Tick(15f);                            // ratio 1.5 → 도로 5타일 Free→Jam
            Assert.AreEqual(5, received.Count);
            Assert.AreEqual(CongestionLevel.Jam, received[0].Level);
            Assert.AreEqual(V(0, 0), received[0].Tile);   // flat 순회 → 결정론적 순서

            Tick(15f);                            // Jam 유지 → 무발행
            Assert.AreEqual(5, received.Count);

            Tick(8f);                             // ratio 0.8 → Jam→Slow 강등 5타일
            Assert.AreEqual(10, received.Count);
            Assert.AreEqual(CongestionLevel.Slow, received[5].Level);
        }
    }
}
