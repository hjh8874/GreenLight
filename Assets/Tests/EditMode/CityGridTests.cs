using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public class CityGridTests
    {
        // 5x4 비정사각 → x/y 뒤바뀐 버그가 있으면 바로 드러남.
        static CityGrid NewGrid() => new CityGrid(5, 4);

        [Test]
        public void NewGrid_AllEmpty_VersionZero_NotDirty()
        {
            var g = NewGrid();
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 5; x++)
                    Assert.AreEqual(TileType.Empty, g.GetTile(new Vector2Int(x, y)));
            Assert.AreEqual(0, g.TopologyVersion);
            Assert.IsFalse(g.TopologyDirty);
        }

        [Test]
        public void Place_OnEmpty_Succeeds_SetsTile()
        {
            var g = NewGrid();
            var t = new Vector2Int(2, 3);
            Assert.IsTrue(g.Place(t, TileType.Road));
            Assert.AreEqual(TileType.Road, g.GetTile(t));
        }

        [Test]
        public void Place_OutOfBounds_Fails_NoStateChange()
        {
            var g = NewGrid();
            Assert.IsFalse(g.CanPlace(new Vector2Int(-1, 0), TileType.Road));
            Assert.IsFalse(g.Place(new Vector2Int(-1, 0), TileType.Road));
            Assert.IsFalse(g.Place(new Vector2Int(5, 0), TileType.Road)); // x == W
            Assert.IsFalse(g.Place(new Vector2Int(0, 4), TileType.Road)); // y == H
            Assert.AreEqual(0, g.TopologyVersion);
            Assert.IsFalse(g.TopologyDirty);
        }

        [Test]
        public void Place_OnOccupied_Fails_KeepsOriginal()
        {
            var g = NewGrid();
            var t = new Vector2Int(1, 1);
            Assert.IsTrue(g.Place(t, TileType.Road));
            Assert.IsFalse(g.CanPlace(t, TileType.House));
            Assert.IsFalse(g.Place(t, TileType.House));
            Assert.AreEqual(TileType.Road, g.GetTile(t));
        }

        [Test]
        public void Remove_Placed_Succeeds_BecomesEmpty()
        {
            var g = NewGrid();
            var t = new Vector2Int(2, 2);
            g.Place(t, TileType.House);
            Assert.IsTrue(g.Remove(t));
            Assert.AreEqual(TileType.Empty, g.GetTile(t));
        }

        [Test]
        public void Remove_Empty_Fails()
        {
            var g = NewGrid();
            Assert.IsFalse(g.Remove(new Vector2Int(0, 0)));
        }

        [Test]
        public void ReplaceAfterRemove_Succeeds()
        {
            var g = NewGrid();
            var t = new Vector2Int(3, 1);
            g.Place(t, TileType.Road);
            g.Remove(t);
            Assert.IsTrue(g.Place(t, TileType.House));
            Assert.AreEqual(TileType.House, g.GetTile(t));
        }

        [Test]
        public void DirtyAndVersion_TrackSuccessfulMutationsOnly()
        {
            var g = NewGrid();
            var t = new Vector2Int(0, 0);

            Assert.IsTrue(g.Place(t, TileType.Road));
            Assert.AreEqual(1, g.TopologyVersion);
            Assert.IsTrue(g.TopologyDirty);

            g.ClearTopologyDirty();
            Assert.IsFalse(g.TopologyDirty);
            Assert.AreEqual(1, g.TopologyVersion); // Clear는 Version 불변

            Assert.IsFalse(g.Place(t, TileType.House)); // 중복 실패
            Assert.AreEqual(1, g.TopologyVersion);
            Assert.IsFalse(g.TopologyDirty);

            Assert.IsTrue(g.Remove(t));
            Assert.AreEqual(2, g.TopologyVersion);
            Assert.IsTrue(g.TopologyDirty);

            g.ClearTopologyDirty();
            Assert.IsFalse(g.Remove(t)); // 빈 칸 제거 실패
            Assert.AreEqual(2, g.TopologyVersion);
            Assert.IsFalse(g.TopologyDirty);
        }
    }
}
