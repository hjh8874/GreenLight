using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class HighwayPlacementTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static SimEngine Build()
        {
            var c = SimConfig.Default();
            c.TickInterval = 0.25f;
            c.GridWidth = 10;
            c.GridHeight = 4;
            c.AutoDetectSignals = false;
            var e = new SimEngine(c, new SimEventHub());
            for (int x = 1; x <= 8; x++) e.Place(V(x, 1), TileType.Road);
            e.Tick(0.25f);
            return e;
        }

        [Test]
        public void Place_OnStraightRoad_Works_AndListsFlatSorted()
        {
            var e = Build();

            Assert.IsTrue(e.TryPlaceHighway(V(6, 1)));
            Assert.IsTrue(e.TryPlaceHighway(V(5, 1)));
            Assert.IsTrue(e.IsHighway(V(5, 1)));
            Assert.AreEqual(new[] { V(5, 1), V(6, 1) }, e.HighwayTiles);
        }

        [Test]
        public void Place_RejectsNonRoadIntersectionBuildingFrontageAndDevice()
        {
            var e = Build();
            Assert.IsFalse(e.TryPlaceHighway(V(0, 0)));               // Empty

            e.Place(V(3, 2), TileType.Road);
            e.Place(V(3, 0), TileType.Road);
            Assert.IsFalse(e.TryPlaceHighway(V(3, 1)));               // 3-way+ intersection

            e.Place(V(7, 2), TileType.House);
            Assert.IsFalse(e.TryPlaceHighway(V(7, 1)));               // direct building frontage

            Assert.IsTrue(e.TryPlaceOneway(V(2, 1), Vector2Int.right));
            Assert.IsFalse(e.TryPlaceHighway(V(2, 1)));               // traffic device overlap
        }

        [Test]
        public void Place_RejectsCurveAndBranch_ButAllowsStraightExtension()
        {
            var c = SimConfig.Default();
            c.GridWidth = 8; c.GridHeight = 4; c.AutoDetectSignals = false;
            var e = new SimEngine(c, new SimEventHub());
            for (int x = 1; x <= 5; x++) e.Place(V(x, 1), TileType.Road);
            e.Place(V(5, 2), TileType.Road); // 도로 자체는 L자지만 교차로는 아님

            Assert.IsTrue(e.TryPlaceHighway(V(3, 1)));
            Assert.IsTrue(e.TryPlaceHighway(V(4, 1)));
            Assert.IsTrue(e.TryPlaceHighway(V(5, 1)));
            Assert.IsFalse(e.TryPlaceHighway(V(5, 2)), "endpoint curve must be rejected");

            var straight = Build();
            Assert.IsTrue(straight.TryPlaceHighway(V(3, 1)));
            Assert.IsTrue(straight.TryPlaceHighway(V(4, 1)));
            Assert.IsTrue(straight.TryPlaceHighway(V(5, 1)));
            Assert.IsTrue(straight.TryPlaceHighway(V(6, 1)), "straight extension must remain valid");
        }

        [Test]
        public void Remove_LeavesRoad_AndBothMutationsMarkTopologyDirty()
        {
            var e = Build();
            Assert.IsFalse(e.TopologyDirtyForTest);

            Assert.IsTrue(e.TryPlaceHighway(V(4, 1)));
            Assert.IsTrue(e.TopologyDirtyForTest);
            e.Tick(0.25f);

            Assert.IsTrue(e.TryRemoveHighway(V(4, 1)));
            Assert.IsTrue(e.TopologyDirtyForTest);
            Assert.AreEqual(TileType.Road, e.GetTileType(V(4, 1)));
            Assert.IsFalse(e.TryRemoveHighway(V(4, 1)));
        }

        [Test]
        public void ExistingHighway_BlocksAdjacentBuildingAndSideRoad()
        {
            var e = Build();
            Assert.IsTrue(e.TryPlaceHighway(V(3, 1)));
            Assert.IsTrue(e.TryPlaceHighway(V(4, 1)));
            Assert.IsTrue(e.TryPlaceHighway(V(5, 1)));

            Assert.IsFalse(e.CanPlace(V(4, 2), TileType.House));
            Assert.IsFalse(e.Place(V(4, 2), TileType.House));
            Assert.IsFalse(e.CanPlace(V(4, 2), TileType.Road));
            Assert.IsFalse(e.Place(V(4, 2), TileType.Road));
        }
    }
}
