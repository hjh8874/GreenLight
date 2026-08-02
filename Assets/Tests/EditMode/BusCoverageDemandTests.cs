using System;
using System.Collections.Generic;
using CityFlow.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    public class BusCoverageDemandTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static CityGrid MakeCommuteGrid()
        {
            var grid = new CityGrid(16, 6);
            for (int x = 0; x < 14; x++)
                Assert.IsTrue(grid.Place(V(x, 2), TileType.Road));
            Assert.IsTrue(grid.Place(V(0, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(3, 0), TileType.House));
            Assert.IsTrue(grid.Place(V(10, 0), TileType.Office));
            return grid;
        }

        static SimConfig DemandConfig()
        {
            SimConfig config = SimConfig.Default();
            config.CarsPerHouse = 2;
            config.DemandChoicePool = 1;
            config.OfficeCapacity = 8;
            return config;
        }

        static int DemandCountFrom(DemandMap demandMap, Vector2Int home)
        {
            int count = 0;
            foreach (Demand demand in demandMap.Demands)
                if (demand.Source == home) count++;
            return count;
        }

        [Test]
        public void Reduction_CutsCommutersPerHouse()
        {
            CityGrid grid = MakeCommuteGrid();
            DemandMap demandMap = new DemandMap(DemandConfig());
            demandMap.SetCommuterReduction(home => home == V(0, 0) ? 1 : 0);

            demandMap.Reassign(grid, new RoadNetwork(grid));

            Assert.AreEqual(1, DemandCountFrom(demandMap, V(0, 0)));
            Assert.AreEqual(2, DemandCountFrom(demandMap, V(3, 0)));
        }

        [Test]
        public void Reduction_FloorsAtZero()
        {
            CityGrid grid = MakeCommuteGrid();
            DemandMap demandMap = new DemandMap(DemandConfig());
            demandMap.SetCommuterReduction(_ => 5);

            demandMap.Reassign(grid, new RoadNetwork(grid));

            Assert.AreEqual(0, demandMap.Demands.Count);
        }

        [Test]
        public void NullDelegate_BitIdentical()
        {
            CityGrid grid = MakeCommuteGrid();
            SimConfig config = DemandConfig();
            DemandMap implicitNull = new DemandMap(config);
            DemandMap explicitNull = new DemandMap(config);
            explicitNull.SetCommuterReduction(null);

            implicitNull.Reassign(grid, new RoadNetwork(grid));
            explicitNull.Reassign(grid, new RoadNetwork(grid));

            Assert.AreEqual(implicitNull.Demands.Count, explicitNull.Demands.Count);
            for (int i = 0; i < implicitNull.Demands.Count; i++)
            {
                Demand expected = implicitNull.Demands[i];
                Demand actual = explicitNull.Demands[i];
                Assert.AreEqual(expected.Source, actual.Source);
                Assert.AreEqual(expected.Sink, actual.Sink);
                Assert.AreEqual(expected.SourceRoad, actual.SourceRoad);
                Assert.AreEqual(expected.SinkRoad, actual.SinkRoad);
                Assert.AreEqual(expected.SinkType, actual.SinkType);
            }
        }

        static SimConfig EngineConfig()
        {
            SimConfig config = DemandConfig();
            config.GridWidth = 16;
            config.GridHeight = 6;
            config.BusCoverageRadius = 3;
            config.MaxSimCars = 32;
            config.MaxPendingVehicleTrips = 32;
            config.QueueCapacityPerTile = 8;
            config.CompanyHiringSlotsPerGameHour = 100f;
            return config;
        }

        static SimEngine MakeEngine()
        {
            SimConfig config = EngineConfig();
            var engine = new SimEngine(config, new SimEventHub());
            for (int x = 0; x < 14; x++)
                Assert.IsTrue(engine.Place(V(x, 2), TileType.Road));
            Assert.IsTrue(engine.Place(V(0, 0), TileType.House));
            Assert.IsTrue(engine.Place(V(10, 0), TileType.Office));
            engine.Tick(config.TickInterval);
            return engine;
        }

        static void PlaceStops(SimEngine engine)
        {
            Assert.IsTrue(engine.TryPlaceBusStop(V(1, 3)));
            Assert.IsTrue(engine.TryPlaceBusStop(V(2, 3)));
        }

        [Test]
        public void PlaceBusStop_TriggersRebuild()
        {
            SimEngine engine = MakeEngine();
            Assert.AreEqual(2, engine.CarSimVehicleStorageCount);

            PlaceStops(engine);
            engine.Tick(engine.TickInterval);

            Assert.AreEqual(
                1,
                engine.CarSimVehicleStorageCount,
                "정류장 2개 배치 후 다음 Step에서 수요가 재배정되어야 한다");
        }

        [Test]
        public void TwoStops_CoveredHouseLosesOneCar()
        {
            SimEngine engine = MakeEngine();
            Assert.AreEqual(2, engine.CarSimVehicleStorageCount);

            PlaceStops(engine);
            engine.Tick(engine.TickInterval);

            Assert.AreEqual(1, engine.CarSimVehicleStorageCount);
        }

        [Test]
        public void OneStop_NoReduction()
        {
            SimEngine engine = MakeEngine();
            Assert.AreEqual(2, engine.CarSimVehicleStorageCount);

            Assert.IsTrue(engine.TryPlaceBusStop(V(2, 3)));
            engine.Tick(engine.TickInterval);

            Assert.AreEqual(2, engine.CarSimVehicleStorageCount);
        }

        [Test]
        public void RemoveToOneStop_RestoresCars()
        {
            SimEngine engine = MakeEngine();
            PlaceStops(engine);
            engine.Tick(engine.TickInterval);
            Assert.AreEqual(1, engine.CarSimVehicleStorageCount);

            Assert.IsTrue(engine.TryRemoveBusStop(V(2, 3)));
            engine.Tick(engine.TickInterval);

            Assert.AreEqual(2, engine.CarSimVehicleStorageCount);
        }
    }
}
