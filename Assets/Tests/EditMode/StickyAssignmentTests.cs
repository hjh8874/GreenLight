using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class StickyAssignmentTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static CityGrid MakeGrid(int w, int h, params (Vector2Int pos, TileType type)[] tiles)
        {
            var g = new CityGrid(w, h);
            foreach (var (pos, type) in tiles) g.Place(pos, type);
            return g;
        }

        static SimConfig NearestCfg()
        {
            var c = SimConfig.Default();
            c.DemandChoicePool = 1;
            return c;
        }

        [Test]
        public void AddingCloserOffice_KeepsAssignmentUntilExplicitHomeRebalance()
        {
            var g = MakeGrid(9, 3,
                (V(0, 0), TileType.House),
                (V(1, 0), TileType.Road), (V(2, 0), TileType.Road), (V(3, 0), TileType.Road),
                (V(4, 0), TileType.Road), (V(5, 0), TileType.Road), (V(6, 0), TileType.Road),
                (V(7, 0), TileType.Road), (V(8, 0), TileType.Office));
            var net = new RoadNetwork(g);
            var dm = new DemandMap(NearestCfg());
            dm.Reassign(g, net);
            Vector2Int firstSink = FindSink(dm, V(0, 0), TileType.Office);

            g.Place(V(2, 1), TileType.Office);
            var net2 = new RoadNetwork(g);
            dm.Reassign(g, net2);
            Vector2Int afterSink = FindSink(dm, V(0, 0), TileType.Office);

            Assert.AreEqual(firstSink, afterSink, "기존 집 배정은 위상 변경에도 불변이어야 한다");

            dm.ClearStickyAssignments();
            dm.Reassign(g, net2);

            Assert.AreEqual(V(2, 1), FindSink(dm, V(0, 0), TileType.Office),
                "귀가 안전시점에 sticky를 풀면 새 가까운 회사가 후보가 되어야 한다");
        }

        [Test]
        public void RemovingAssignedSink_ReassignsToRemaining()
        {
            var g = MakeGrid(9, 3,
                (V(0, 0), TileType.House),
                (V(1, 0), TileType.Road), (V(2, 0), TileType.Road), (V(3, 0), TileType.Road),
                (V(4, 0), TileType.Road), (V(5, 0), TileType.Road), (V(6, 0), TileType.Road),
                (V(7, 0), TileType.Road), (V(8, 0), TileType.Office));
            g.Place(V(2, 1), TileType.Office);
            var dm = new DemandMap(NearestCfg());
            dm.Reassign(g, new RoadNetwork(g));
            Vector2Int assigned = FindSink(dm, V(0, 0), TileType.Office);

            g.Remove(assigned);
            dm.Reassign(g, new RoadNetwork(g));
            Vector2Int after = FindSink(dm, V(0, 0), TileType.Office);

            Assert.AreNotEqual(assigned, after, "철거된 sink에 남아있으면 안 된다");
        }

        static Vector2Int FindSink(DemandMap dm, Vector2Int home, TileType sinkType)
        {
            foreach (var d in dm.Demands)
            {
                if (d.Source == home && d.Sink != default)
                {
                    return d.Sink;
                }
            }

            Assert.Fail($"집 {home} 배정 없음");
            return default;
        }
    }
}
