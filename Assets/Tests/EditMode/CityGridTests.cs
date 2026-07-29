using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

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
        public void Building_Place_UsesTwoByTwoFootprint()
        {
            var g = NewGrid();
            var anchor = new Vector2Int(1, 1);

            Assert.IsTrue(g.Place(anchor, TileType.House));

            Assert.AreEqual(TileType.House, g.GetTile(new Vector2Int(1, 1)));
            Assert.AreEqual(TileType.House, g.GetTile(new Vector2Int(2, 1)));
            Assert.AreEqual(TileType.House, g.GetTile(new Vector2Int(1, 2)));
            Assert.AreEqual(TileType.House, g.GetTile(new Vector2Int(2, 2)));
            Assert.IsTrue(g.IsFootprintAnchor(anchor));
            Assert.IsFalse(g.IsFootprintAnchor(new Vector2Int(2, 2)));
        }

        [Test]
        public void Building_RemoveFromSecondaryTile_RemovesWholeFootprint()
        {
            var g = NewGrid();
            var anchor = new Vector2Int(1, 1);
            g.Place(anchor, TileType.Office);

            Assert.IsTrue(g.TryRemove(new Vector2Int(2, 2), out TileType removed, out Vector2Int removedAnchor));
            Assert.AreEqual(TileType.Office, removed);
            Assert.AreEqual(anchor, removedAnchor);

            for (int y = 1; y <= 2; y++)
                for (int x = 1; x <= 2; x++)
                    Assert.AreEqual(TileType.Empty, g.GetTile(new Vector2Int(x, y)));
        }

        [Test]
        public void Building_PlaceAcrossBoundary_Fails()
        {
            var g = NewGrid();

            Assert.IsFalse(g.CanPlace(new Vector2Int(4, 3), TileType.School));
            Assert.IsFalse(g.Place(new Vector2Int(4, 3), TileType.School));
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

        [Test]
        public void Clear_EmptiesAllTiles_MarksDirty_BumpsVersion()
        {
            // 세이브 복원용 seam: 저장된 타일 재배치 전에 도시를 통째로 비운다.
            var g = NewGrid();
            g.Place(new Vector2Int(0, 0), TileType.Road);
            g.Place(new Vector2Int(3, 2), TileType.House);   // 우상단까지 차지하는 2x2 건물 — 전체 범위 확인
            g.ClearTopologyDirty();
            int versionBefore = g.TopologyVersion;

            g.Clear();

            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 5; x++)
                    Assert.AreEqual(TileType.Empty, g.GetTile(new Vector2Int(x, y)));
            Assert.IsTrue(g.TopologyDirty);                       // 다음 Step이 경로·수요·신호 재구축
            Assert.Greater(g.TopologyVersion, versionBefore);     // RoadNetwork 캐시 무효화 키 갱신
        }

        [Test]
        public void RoadTileCount_CountsOnlyRoads()
        {
            var g = new CityGrid(5, 4);
            g.Place(new Vector2Int(0, 0), TileType.Road);
            g.Place(new Vector2Int(1, 0), TileType.Road);
            g.Place(new Vector2Int(2, 0), TileType.House);

            Assert.AreEqual(2, g.RoadTileCount);

            g.Remove(new Vector2Int(0, 0));

            Assert.AreEqual(1, g.RoadTileCount);
        }

        [Test]
        public void RoadTileIndices_StaySortedAcrossGridChanges()
        {
            var g = new CityGrid(5, 4);

            Assert.IsTrue(g.Place(new Vector2Int(4, 3), TileType.Road));
            Assert.IsTrue(g.Place(new Vector2Int(0, 0), TileType.Road));
            Assert.IsTrue(g.Place(new Vector2Int(2, 1), TileType.Road));

            Assert.AreEqual(3, g.RoadTileCount);
            Assert.AreEqual(0, g.GetRoadTileIndex(0));
            Assert.AreEqual(7, g.GetRoadTileIndex(1));
            Assert.AreEqual(19, g.GetRoadTileIndex(2));

            Assert.IsTrue(g.Remove(new Vector2Int(2, 1)));
            Assert.AreEqual(2, g.RoadTileCount);
            Assert.AreEqual(0, g.GetRoadTileIndex(0));
            Assert.AreEqual(19, g.GetRoadTileIndex(1));

            g.Clear();
            Assert.AreEqual(0, g.RoadTileCount);
        }
    }
}
