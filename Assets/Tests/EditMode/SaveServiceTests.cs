using System;
using System.Collections.Generic;
using System.IO;
using CityFlow.Contracts.Save;
using CityFlow.Save;
using NUnit.Framework;

namespace CityFlow.Sim.Tests
{
    public sealed class SaveServiceTests
    {
        string savePath;
        string backupPath;

        [SetUp]
        public void SetUp()
        {
            string id = Guid.NewGuid().ToString("N");
            savePath = Path.Combine(Path.GetTempPath(), $"greenlight_{id}.json");
            backupPath = Path.Combine(Path.GetTempPath(), $"greenlight_{id}_backup.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(savePath)) File.Delete(savePath);
            if (File.Exists(backupPath)) File.Delete(backupPath);
        }

        [Test]
        public void TryLoadAndRestore_RestoresSectionsThenPublishesOnce()
        {
            var calls = new List<string>();
            var repository = new JsonSaveRepository(savePath, backupPath);
            var sim = new FakeSim(calls);
            var weekly = new FakeWeekly(calls);
            var service = new SaveService(sim, repository, new FakeClock());
            service.RegisterWeeklySettlementSaveSource(weekly);

            Assert.IsTrue(repository.TrySave(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                Simulation = new SimSaveData(),
                WeeklySettlement = new WeeklySettlementSaveData { PendingCoins = 250L }
            }));

            int completed = 0;
            service.RestoreCompleted += _ => { calls.Add("completed"); completed++; };

            Assert.IsTrue(service.TryLoadAndRestore());
            Assert.AreEqual(1, completed);
            Assert.AreEqual(250L, weekly.Restored.PendingCoins);
            CollectionAssert.AreEqual(new[] { "sim", "weekly", "completed" }, calls);
        }

        [Test]
        public void RestoreSnapshot_LegacyOptionalSection_UsesDefault()
        {
            var calls = new List<string>();
            var weekly = new FakeWeekly(calls);
            var service = new SaveService(
                new FakeSim(calls),
                new JsonSaveRepository(savePath, backupPath),
                new FakeClock());
            service.RegisterWeeklySettlementSaveSource(weekly);

            service.RestoreSnapshot(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                Simulation = new SimSaveData()
            });

            Assert.NotNull(weekly.Restored);
            Assert.AreEqual(0L, weekly.Restored.PendingCoins);
        }

        [Test]
        public void Save_RoundTripsSimulationAndWeeklyData()
        {
            var calls = new List<string>();
            var repository = new JsonSaveRepository(savePath, backupPath);
            var weekly = new FakeWeekly(calls)
            {
                Current = new WeeklySettlementSaveData
                {
                    PendingCoins = 77L,
                    DaysIntoCurrentWeek = 3,
                    HasCycleProgress = true
                }
            };
            var service = new SaveService(new FakeSim(calls), repository, new FakeClock());
            service.RegisterWeeklySettlementSaveSource(weekly);

            Assert.IsTrue(service.Save());
            Assert.IsTrue(repository.TryLoad(out GameSaveData loaded));
            Assert.AreEqual(77L, loaded.WeeklySettlement.PendingCoins);
            Assert.AreEqual(3, loaded.WeeklySettlement.DaysIntoCurrentWeek);
        }

        sealed class FakeClock : ISaveClock
        {
            public DateTime UtcNow => new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);
        }

        sealed class FakeSim : ISimSaveSource
        {
            readonly List<string> calls;
            public int GridWidth => 20;
            public int GridHeight => 20;
            public FakeSim(List<string> calls) => this.calls = calls;
            public SimSaveData CreateSnapshot() => new SimSaveData();
            public void RestoreSnapshot(SimSaveData snapshot) => calls.Add("sim");
        }

        sealed class FakeWeekly : IWeeklySettlementSaveSource
        {
            readonly List<string> calls;
            public WeeklySettlementSaveData Current = new WeeklySettlementSaveData();
            public WeeklySettlementSaveData Restored { get; private set; }
            public FakeWeekly(List<string> calls) => this.calls = calls;
            public WeeklySettlementSaveData CreateSnapshot() => Current;
            public void RestoreSnapshot(WeeklySettlementSaveData snapshot)
            {
                Restored = snapshot;
                calls.Add("weekly");
            }
        }
    }
}
