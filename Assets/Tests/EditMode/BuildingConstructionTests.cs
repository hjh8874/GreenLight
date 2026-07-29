using CityFlow.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public class BuildingConstructionTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // CarSimEngineTests.Cfg()와 같은 형태. DayLengthSeconds=24 → 1 게임시간 = 1 시뮬초.
        static SimConfig Cfg()
        {
            SimConfig cfg = SimConfig.Default();
            cfg.GridWidth = 8;
            cfg.GridHeight = 4;
            cfg.TickInterval = 0.25f;
            cfg.MaxStepsPerFrame = 20;
            cfg.DayLengthSeconds = 24f;
            cfg.CompanyHiringSlotsPerGameHour = 100f;
            return cfg;
        }

        [Test]
        public void Promote_ReplacesFootprintTypeAndKeepsAnchorAndDirection()
        {
            var grid = new CityGrid(8, 4);
            Assert.IsTrue(grid.Place(V(0, 0), TileType.UnderConstruction, PlacementDirection.East));

            Assert.IsTrue(grid.Promote(V(0, 0), TileType.House));

            // 2x2 풋프린트 전체가 교체된다
            Assert.AreEqual(TileType.House, grid.GetTile(V(0, 0)));
            Assert.AreEqual(TileType.House, grid.GetTile(V(1, 0)));
            Assert.AreEqual(TileType.House, grid.GetTile(V(0, 1)));
            Assert.AreEqual(TileType.House, grid.GetTile(V(1, 1)));
            // 방향과 앵커는 보존된다
            Assert.AreEqual(PlacementDirection.East, grid.GetDirection(V(1, 1)));
            Assert.IsTrue(grid.TryGetFootprintAnchor(V(1, 1), out Vector2Int anchor));
            Assert.AreEqual(V(0, 0), anchor);
        }

        [Test]
        public void Promote_ReturnsFalseForNonAnchorOrEmptyTile()
        {
            var grid = new CityGrid(8, 4);
            Assert.IsTrue(grid.Place(V(0, 0), TileType.UnderConstruction));

            Assert.IsFalse(grid.Promote(V(1, 1), TileType.House), "앵커가 아닌 타일은 거부");
            Assert.IsFalse(grid.Promote(V(5, 3), TileType.House), "빈 타일은 거부");
            Assert.IsFalse(grid.Promote(V(-1, 0), TileType.House), "격자 밖은 거부");
        }
    }
}
