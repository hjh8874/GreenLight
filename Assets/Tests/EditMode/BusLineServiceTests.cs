using System.Collections.Generic;
using CityFlow.Content;
using CityFlow.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public sealed class BusLineServiceTests
    {
        private static Vector2Int V(int x, int y) =>
            new Vector2Int(x, y);

        [Test]
        public void LineManagement_KeepsRoutesSortedAndIsolated()
        {
            var service = new BusLineService();
            var routeTwoStops = new List<Vector2Int>
            {
                V(8, 2),
                V(8, 6)
            };

            Assert.IsTrue(service.TryCreateLine(2, routeTwoStops));
            Assert.IsTrue(service.TryCreateLine(
                1,
                new[] { V(1, 1), V(4, 1) }));
            Assert.IsFalse(service.TryCreateLine(
                1,
                new[] { V(2, 2), V(3, 2) }));

            routeTwoStops[0] = V(99, 99);

            Assert.AreEqual(2, service.LineCount);
            Assert.AreEqual(1, service.Lines[0].RouteId);
            Assert.AreEqual(2, service.Lines[1].RouteId);
            Assert.IsTrue(service.TryGetLine(2, out BusLineData routeTwo));
            CollectionAssert.AreEqual(
                new[] { V(8, 2), V(8, 6) },
                routeTwo.OrderedStops);

            Assert.IsTrue(service.TryUpdateLine(
                1,
                new[] { V(1, 1), V(4, 1), V(7, 1) }));
            Assert.IsTrue(service.TryRemoveLine(2));
            Assert.IsFalse(service.TryGetLine(2, out _));
            Assert.AreEqual(1, service.LineCount);
        }

        [Test]
        public void LineManagement_RejectsInvalidIdentityAndStops()
        {
            var service = new BusLineService();

            Assert.IsFalse(service.TryCreateLine(
                0,
                new[] { V(1, 1), V(2, 1) }));
            Assert.IsFalse(service.TryCreateLine(
                1,
                new[] { V(1, 1) }));
            Assert.IsFalse(service.TryCreateLine(
                1,
                new[] { V(1, 1), V(1, 1) }));
            Assert.AreEqual(0, service.LineCount);
        }

        [Test]
        public void DirectionalRoutes_UseOnlyTheSelectedLine()
        {
            var service = new BusLineService();
            Vector2Int[] routeOne =
            {
                V(1, 1),
                V(4, 1),
                V(7, 1)
            };
            Vector2Int[] routeTwo =
            {
                V(2, 8),
                V(6, 8)
            };

            Assert.IsTrue(service.TryCreateLine(1, routeOne));
            Assert.IsTrue(service.TryCreateLine(2, routeTwo));
            Assert.IsTrue(service.TryBuildDirectionalRoute(
                1,
                BusTravelDirection.Forward,
                out BusDirectionalRoute forward));
            Assert.IsTrue(service.TryBuildDirectionalRoute(
                1,
                BusTravelDirection.Reverse,
                out BusDirectionalRoute reverse));

            CollectionAssert.AreEqual(routeOne, forward.OrderedStops);
            CollectionAssert.AreEqual(
                new[] { V(7, 1), V(4, 1), V(1, 1) },
                reverse.OrderedStops);
            CollectionAssert.DoesNotContain(
                forward.OrderedStops,
                routeTwo[0]);
            Assert.AreEqual(1, forward.RouteId);
            Assert.AreEqual(
                BusTravelDirection.Reverse,
                reverse.Direction);
        }

        [Test]
        public void LineEvents_ReportCreatedUpdatedAndRemovedSnapshots()
        {
            var service = new BusLineService();
            int createdRouteId = 0;
            int updatedStopCount = 0;
            int removedRouteId = 0;

            service.LineCreated += line =>
                createdRouteId = line.RouteId;
            service.LineUpdated += line =>
                updatedStopCount = line.StopCount;
            service.LineRemoved += line =>
                removedRouteId = line.RouteId;

            Assert.IsTrue(service.TryCreateLine(
                3,
                new[] { V(3, 1), V(3, 4) }));
            Assert.IsTrue(service.TryUpdateLine(
                3,
                new[] { V(3, 1), V(3, 4), V(3, 7) }));
            Assert.IsTrue(service.TryRemoveLine(3));

            Assert.AreEqual(3, createdRouteId);
            Assert.AreEqual(3, updatedStopCount);
            Assert.AreEqual(3, removedRouteId);
        }

        // Unity integration: run as an EditMode test after script compilation.
    }
}
