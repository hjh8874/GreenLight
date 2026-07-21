using System.Collections.Generic;
using CityFlow.ViewKit;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public class HighwayVisualMathTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        [Test]
        public void Kind_ClassifiesEndpointsAndInteriorDeterministically()
        {
            var highways = new HashSet<Vector2Int> { V(2, 2), V(3, 2), V(4, 2) };

            Assert.AreEqual(HighwayMarkerKind.Endpoint, HighwayVisualMath.Kind(V(2, 2), highways));
            Assert.AreEqual(HighwayMarkerKind.Interior, HighwayVisualMath.Kind(V(3, 2), highways));
            Assert.AreEqual(HighwayMarkerKind.Endpoint, HighwayVisualMath.Kind(V(4, 2), highways));
        }

        [Test]
        public void Axis_UsesConnectedHighwayNeighbor()
        {
            var horizontal = new HashSet<Vector2Int> { V(2, 2), V(3, 2) };
            var vertical = new HashSet<Vector2Int> { V(2, 2), V(2, 3) };

            Assert.IsFalse(HighwayVisualMath.IsVertical(V(2, 2), horizontal));
            Assert.IsTrue(HighwayVisualMath.IsVertical(V(2, 2), vertical));
        }
    }
}
