using System;
using System.Collections.Generic;
using System.IO;
using CityFlow.Contracts.Save;
using CityFlow.Save;
using NUnit.Framework;

namespace CityFlow.Sim.Tests
{
    public sealed class WorldGridSaveServiceTests
    {
        private string savePath;
        private string backupPath;

        [SetUp]
        public void SetUp()
        {
            string id = Guid.NewGuid().ToString("N");
            savePath = Path.Combine(Path.GetTempPath(), $"world_grid_{id}.json");
            backupPath = Path.Combine(Path.GetTempPath(), $"world_grid_{id}.backup");
        }

        [TearDown]
        public void TearDown()
        {
            Delete(savePath);
            Delete(backupPath);
            Delete($"{savePath}.tmp");
            Delete($"{backupPath}.fallback");
        }

        [Test]
        public void Save_RoundTripsWorldGridSection()
        {
            var repository = new JsonSaveRepository(savePath, backupPath);
            var source = new FakeWorldGrid
            {
                Current = CreateSnapshot(0, 1, 10)
            };
            var service = new SaveService(
                new FakeSim(),
                repository,
                new FakeClock());
            service.RegisterWorldGridSaveSource(source);

            Assert.IsTrue(service.Save());
            Assert.IsTrue(repository.TryLoad(out GameSaveData loaded));
            CollectionAssert.AreEqual(
                new[] { 0, 1, 10 },
                loaded.WorldGrid.UnlockedChunkIndices);

            source.Current = CreateSnapshot();
            service.RestoreSnapshot(loaded);
            CollectionAssert.AreEqual(
                new[] { 0, 1, 10 },
                source.Current.UnlockedChunkIndices);
        }

        [Test]
        public void RegisterAfterLoad_RestoresRetainedWorldGridSection()
        {
            var repository = new JsonSaveRepository(savePath, backupPath);
            var service = new SaveService(
                new FakeSim(),
                repository,
                new FakeClock());

            Assert.IsTrue(repository.TrySave(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                Simulation = new SimSaveData(),
                WorldGrid = CreateSnapshot(0, 99)
            }));
            Assert.IsTrue(service.TryLoadAndRestore());

            var source = new FakeWorldGrid();
            service.RegisterWorldGridSaveSource(source);
            CollectionAssert.AreEqual(
                new[] { 0, 99 },
                source.Current.UnlockedChunkIndices);
        }

        [Test]
        public void LegacySave_RestoresNullForConfiguredInitialAccess()
        {
            var source = new FakeWorldGrid();
            var service = new SaveService(
                new FakeSim(),
                new JsonSaveRepository(savePath, backupPath),
                new FakeClock());
            service.RegisterWorldGridSaveSource(source);

            service.RestoreSnapshot(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                Simulation = new SimSaveData()
            });

            Assert.IsNull(source.Current);
        }

        private static WorldGridSaveData CreateSnapshot(params int[] indices)
        {
            return new WorldGridSaveData
            {
                WorldWidth = 200,
                WorldHeight = 200,
                ChunkSize = 20,
                UnlockedChunkIndices = indices
            };
        }

        private static void Delete(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private sealed class FakeWorldGrid : IWorldGridSaveSource
        {
            public WorldGridSaveData Current { get; set; } =
                new WorldGridSaveData();

            public WorldGridSaveData CreateSnapshot() => Current;

            public void RestoreSnapshot(WorldGridSaveData snapshot)
            {
                Current = snapshot;
            }
        }

        private sealed class FakeClock : ISaveClock
        {
            public DateTime UtcNow =>
                new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc);
        }

        private sealed class FakeSim : ISimSaveSource
        {
            public int GridWidth => 20;
            public int GridHeight => 20;
            public SimSaveData CreateSnapshot() => new SimSaveData();
            public void RestoreSnapshot(SimSaveData snapshot) { }
        }
    }
}
