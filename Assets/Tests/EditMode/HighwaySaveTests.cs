using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;

namespace CityFlow.Sim.Tests
{
    public class HighwaySaveTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static SimEngine Build()
        {
            var c = SimConfig.Default();
            c.TickInterval = 0.25f;
            c.GridWidth = 9; c.GridHeight = 3;
            c.AutoDetectSignals = false;
            var e = new SimEngine(c, new SimEventHub());
            for (int x = 1; x <= 7; x++) e.Place(V(x, 1), TileType.Road);
            e.Tick(0.25f);
            return e;
        }

        [Test]
        public void SaveRoundtrip_PreservesFlatSortedHighways()
        {
            var e = Build();
            Assert.IsTrue(e.TryPlaceHighway(V(5, 1)));
            Assert.IsTrue(e.TryPlaceHighway(V(4, 1)));
            Assert.IsTrue(e.TryPlaceHighway(V(3, 1)));

            SimSaveData save = e.CreateSnapshot();
            Assert.AreEqual(3, save.Highways.Length);
            Assert.AreEqual(3, save.Highways[0].X);
            Assert.AreEqual(4, save.Highways[1].X);
            Assert.AreEqual(5, save.Highways[2].X);

            var restored = Build();
            restored.RestoreSnapshot(save);
            restored.Tick(0.25f);
            CollectionAssert.AreEqual(e.HighwayTiles, restored.HighwayTiles);
        }

        [Test]
        public void LegacyNull_ClearsPreviousHighways()
        {
            var source = Build();
            SimSaveData legacy = source.CreateSnapshot();
            legacy.Highways = null;

            var restored = Build();
            restored.TryPlaceHighway(V(3, 1));
            Assert.DoesNotThrow(() => restored.RestoreSnapshot(legacy));
            Assert.IsEmpty(restored.HighwayTiles);
        }

        [Test]
        public void Restore_SkipsNonRoadAndBuildingFrontageEntries()
        {
            var source = Build();
            SimSaveData save = source.CreateSnapshot();
            var tiles = new List<TileSaveData>(save.PlacedTiles)
            {
                new TileSaveData { X = 2, Y = 2, Type = TileType.House },
            };
            save.PlacedTiles = tiles.ToArray();
            save.Highways = new[]
            {
                new HighwaySaveData { X = 2, Y = 1 }, // building frontage -> reject
                new HighwaySaveData { X = 3, Y = 1 },
                new HighwaySaveData { X = 4, Y = 1 },
                new HighwaySaveData { X = 5, Y = 1 },
                new HighwaySaveData { X = 0, Y = 0 }, // non-road -> reject
            };

            var restored = Build();
            Assert.DoesNotThrow(() => restored.RestoreSnapshot(save));
            CollectionAssert.AreEqual(new[] { V(3, 1), V(4, 1), V(5, 1) }, restored.HighwayTiles);
        }

        [Test]
        public void RoadRemoval_PrunesHighwayOnNextTopologyRebuild()
        {
            var e = Build();
            Assert.IsTrue(e.TryPlaceHighway(V(3, 1)));
            Assert.IsTrue(e.Remove(V(3, 1)));

            e.Tick(0.25f);

            Assert.IsFalse(e.IsHighway(V(3, 1)));
            Assert.IsEmpty(e.HighwayTiles);
        }
    }
}
