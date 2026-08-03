using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class AutoDirectionTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static SimEngine EngineWithRoads(params Vector2Int[] roads)
        {
            SimConfig config = SimConfig.Default();
            config.GridWidth = 8;
            config.GridHeight = 8;
            var engine = new SimEngine(config, new SimEventHub());
            foreach (Vector2Int road in roads)
            {
                Assert.IsTrue(engine.Place(road, TileType.Road));
            }

            return engine;
        }

        [TestCase(PlacementDirection.North, 3, 2)]
        [TestCase(PlacementDirection.East, 5, 3)]
        [TestCase(PlacementDirection.South, 3, 5)]
        [TestCase(PlacementDirection.West, 2, 3)]
        public void TryResolveAutoDirection_ChoosesRoadFacingDirection(
            PlacementDirection expected,
            int roadX,
            int roadY)
        {
            SimEngine engine = EngineWithRoads(V(roadX, roadY));

            Assert.IsTrue(engine.TryResolveAutoDirection(
                V(3, 3),
                TileType.House,
                out PlacementDirection actual));
            Assert.AreEqual(expected, actual);
        }

        // 우선순위 주입(카메라 기준 순서용): 여러 면이 도로일 때만 결과가 달라진다.
        [Test]
        public void TryResolveAutoDirection_CustomPriority_BreaksTieOnCorner()
        {
            SimEngine engine = EngineWithRoads(V(3, 2), V(5, 3));   // 북·동 두 면 모두 도로

            Assert.IsTrue(engine.TryResolveAutoDirection(
                V(3, 3), TileType.House, out PlacementDirection byDefault));
            Assert.AreEqual(PlacementDirection.North, byDefault, "기본 순서는 North 우선");

            var cameraOrder = new[]
            {
                PlacementDirection.East, PlacementDirection.South,
                PlacementDirection.West, PlacementDirection.North,
            };
            Assert.IsTrue(engine.TryResolveAutoDirection(
                V(3, 3), TileType.House, out PlacementDirection byPriority, cameraOrder));
            Assert.AreEqual(PlacementDirection.East, byPriority, "우선순위 주입이 타이브레이크를 바꾼다");
        }

        [Test]
        public void TryResolveAutoDirection_UsesFixedDirectionOrder()
        {
            SimEngine engine = EngineWithRoads(V(3, 2), V(5, 3));

            Assert.IsTrue(engine.TryResolveAutoDirection(
                V(3, 3),
                TileType.Office,
                out PlacementDirection actual));
            Assert.AreEqual(PlacementDirection.North, actual);
        }

        [Test]
        public void TryResolveAutoDirection_NoRoadFallsBackToNorth()
        {
            SimEngine engine = EngineWithRoads();

            Assert.IsFalse(engine.TryResolveAutoDirection(
                V(3, 3),
                TileType.School,
                out PlacementDirection actual));
            Assert.AreEqual(PlacementDirection.North, actual);
        }

        [Test]
        public void TryResolveAutoDirection_UsesRotatedFootprintSize()
        {
            SimEngine engine = EngineWithRoads(V(5, 3));

            Assert.AreEqual(
                new Vector2Int(2, 2),
                TileFootprint.GetRotatedSize(
                    TileType.Hospital,
                    PlacementDirection.North));
            Assert.AreEqual(
                new Vector2Int(2, 2),
                TileFootprint.GetRotatedSize(
                    TileType.Hospital,
                    PlacementDirection.East));
            Assert.IsTrue(engine.TryResolveAutoDirection(
                V(3, 3),
                TileType.Hospital,
                out PlacementDirection actual));
            Assert.AreEqual(PlacementDirection.East, actual);
        }
    }
}
