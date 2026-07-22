using System.Linq;
using CityFlow.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public class CompanyCapacityTests
    {
        static Vector2Int V(int x, int y) =>
            new Vector2Int(x, y);

        static SimConfig CapacityConfig()
        {
            SimConfig config = SimConfig.Default();
            config.DemandChoicePool = 1;
            config.OfficeCapacity = 6;
            config.CompanyHiringSlotsPerGameHour = 2f;
            config.DayLengthSeconds = 120f;
            return config;
        }

        [Test]
        public void EffectiveCapacity_OpensFromZeroToTotalOverTime()
        {
            const double builtAt = 100d;
            const float dayLengthSeconds = 120f;

            Assert.AreEqual(
                0,
                CompanyCapacityCalculator.EffectiveCapacity(
                    totalCapacity: 6,
                    builtAtSimSeconds: builtAt,
                    currentSimSeconds: builtAt,
                    slotsPerGameHour: 2f,
                    dayLengthSeconds: dayLengthSeconds
                )
            );

            double twoGameHoursLater =
                builtAt +
                dayLengthSeconds * 2d / 24d;
            Assert.AreEqual(
                4,
                CompanyCapacityCalculator.EffectiveCapacity(
                    totalCapacity: 6,
                    builtAtSimSeconds: builtAt,
                    currentSimSeconds: twoGameHoursLater,
                    slotsPerGameHour: 2f,
                    dayLengthSeconds: dayLengthSeconds
                )
            );

            double fourGameHoursLater =
                builtAt +
                dayLengthSeconds * 4d / 24d;
            Assert.AreEqual(
                6,
                CompanyCapacityCalculator.EffectiveCapacity(
                    totalCapacity: 6,
                    builtAtSimSeconds: builtAt,
                    currentSimSeconds: fourGameHoursLater,
                    slotsPerGameHour: 2f,
                    dayLengthSeconds: dayLengthSeconds
                )
            );
        }

        [Test]
        public void SimEngine_NewCompanyFillsFromZeroAsCapacityOpens()
        {
            SimConfig config = CapacityConfig();
            config.GridWidth = 14;
            config.GridHeight = 3;
            config.TickInterval = 0.25f;
            config.MaxStepsPerFrame = 20;
            config.DayLengthSeconds = 24f;

            var engine = new SimEngine(
                config,
                new SimEventHub()
            );

            for (int x = 0; x < config.GridWidth; x++)
            {
                Assert.IsTrue(engine.Place(V(x, 2), TileType.Road));
            }

            for (int x = 0; x < 6; x++)
            {
                Assert.IsTrue(engine.Place(V(x * 2, 0), TileType.House));
            }

            Vector2Int office = V(12, 0);
            Assert.IsTrue(engine.Place(office, TileType.Office));
            engine.SetGameHour(13f);

            Assert.IsTrue(
                engine.TryGetCompanyStaffing(
                    office,
                    out CompanyStaffing justBuilt
                )
            );
            Assert.AreEqual(0, justBuilt.Filled);
            Assert.AreEqual(6, justBuilt.Capacity);

            for (int step = 0; step < 12; step++)
            {
                engine.Tick(config.TickInterval);
            }

            Assert.IsTrue(
                engine.TryGetCompanyStaffing(
                    office,
                    out CompanyStaffing fullyOpen
                )
            );
            Assert.AreEqual(6, fullyOpen.Filled);
            Assert.AreEqual(6, fullyOpen.Capacity);
        }

        [Test]
        public void PerCompanyCapacity_LimitsAssignmentsIndependently()
        {
            SimConfig config = CapacityConfig();
            var grid = new CityGrid(16, 6);
            Vector2Int smallOffice = V(4, 0);
            Vector2Int largeOffice = V(10, 0);

            Assert.IsTrue(grid.Place(smallOffice, TileType.Office));
            Assert.IsTrue(grid.Place(largeOffice, TileType.Office));
            Assert.IsTrue(grid.Place(V(0, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(14, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(0, 2), TileType.House));
            Assert.IsTrue(grid.Place(V(14, 2), TileType.House));
            Assert.IsTrue(grid.Place(V(0, 4), TileType.House));
            Assert.IsTrue(grid.Place(V(14, 4), TileType.House));

            var demand = new DemandMap(config);
            demand.RegisterCompany(
                smallOffice,
                TileType.Office,
                builtAtSimSeconds: 0d,
                capacityOverride: 1
            );
            demand.RegisterCompany(
                largeOffice,
                TileType.Office,
                builtAtSimSeconds: 0d,
                capacityOverride: 3
            );
            demand.AdvanceCompanyCapacities(1000d);
            demand.Reassign(grid, new RoadNetwork(grid));

            Assert.AreEqual(
                1,
                demand.Demands.Count(d =>
                    d.Sink == smallOffice)
            );
            Assert.AreEqual(
                3,
                demand.Demands.Count(d =>
                    d.Sink == largeOffice)
            );

            Assert.IsTrue(
                demand.TryGetCompanyStaffing(
                    smallOffice,
                    out int smallFilled,
                    out int smallCapacity
                )
            );
            Assert.AreEqual(1, smallFilled);
            Assert.AreEqual(1, smallCapacity);
        }

        [Test]
        public void Assignment_DoesNotExceedCompanyCapacity()
        {
            SimConfig config = CapacityConfig();
            var grid = new CityGrid(14, 2);
            Vector2Int office = V(12, 0);
            Assert.IsTrue(grid.Place(office, TileType.Office));

            for (int x = 0; x < 5; x++)
            {
                Assert.IsTrue(grid.Place(V(x * 2, 0), TileType.House));
            }

            var demand = new DemandMap(config);
            demand.RegisterCompany(
                office,
                TileType.Office,
                builtAtSimSeconds: 0d,
                capacityOverride: 2
            );
            demand.AdvanceCompanyCapacities(1000d);
            demand.Reassign(grid, new RoadNetwork(grid));

            Assert.AreEqual(2, demand.Demands.Count);
            Assert.AreEqual(
                2,
                demand.Demands.Count(d =>
                    d.Sink == office)
            );
        }
    }
}
