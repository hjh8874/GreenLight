using System;
using System.Collections.Generic;
using System.IO;
using CityFlow.Contracts;
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
            DeleteTestPath(savePath);
            DeleteTestPath(backupPath);
            DeleteTestPath($"{savePath}.tmp");
            DeleteTestPath($"{backupPath}.fallback");
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

        [Test]
        public void Save_RoundTripsQuestAndDeliveredProgress()
        {
            var calls = new List<string>();
            var repository = new JsonSaveRepository(savePath, backupPath);
            var progression = new FakeProgression
            {
                Current = new ProgressionSaveData
                {
                    CurrentStage = 5,
                    CompletedObjectiveIds = new[] { "BuildRoad", "BuildHouse" },
                    TutorialCompleted = true,
                    HasQuestProgress = true,
                    HasHarvested = true,
                    LifetimeDeliveredTotal = 123L
                }
            };
            var service = new SaveService(
                new FakeSim(calls),
                repository,
                new FakeClock(),
                progressionSaveSource: progression);

            Assert.IsTrue(service.Save());
            Assert.IsTrue(repository.TryLoad(out GameSaveData loaded));
            Assert.IsTrue(loaded.Progression.HasQuestProgress);
            Assert.IsTrue(loaded.Progression.HasHarvested);
            Assert.AreEqual(5, loaded.Progression.CurrentStage);
            Assert.AreEqual(123L, loaded.Progression.LifetimeDeliveredTotal);
        }

        [Test]
        public void Save_RoundTripsClearedTerrainDecorationTiles()
        {
            var calls = new List<string>();
            var repository = new JsonSaveRepository(savePath, backupPath);
            var terrainDecorations = new FakeTerrainDecorations
            {
                Current = new TerrainDecorationSaveData
                {
                    ClearedTileIndices = new[] { 2, 17, 399 }
                }
            };
            var service = new SaveService(
                new FakeSim(calls),
                repository,
                new FakeClock());
            service.RegisterTerrainDecorationSaveSource(
                terrainDecorations);

            Assert.IsTrue(service.Save());
            Assert.IsTrue(repository.TryLoad(out GameSaveData loaded));
            CollectionAssert.AreEqual(
                new[] { 2, 17, 399 },
                loaded.TerrainDecorations.ClearedTileIndices);

            terrainDecorations.Current =
                new TerrainDecorationSaveData();
            service.RestoreSnapshot(loaded);

            CollectionAssert.AreEqual(
                new[] { 2, 17, 399 },
                terrainDecorations.Current.ClearedTileIndices);
        }

        [Test]
        public void RestoreSnapshot_LegacyTerrainDecorationSection_UsesEmptyState()
        {
            var terrainDecorations = new FakeTerrainDecorations();
            var service = new SaveService(
                new FakeSim(new List<string>()),
                new JsonSaveRepository(savePath, backupPath),
                new FakeClock());
            service.RegisterTerrainDecorationSaveSource(
                terrainDecorations);

            service.RestoreSnapshot(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                Simulation = new SimSaveData()
            });

            Assert.NotNull(terrainDecorations.Current);
            CollectionAssert.IsEmpty(
                terrainDecorations.Current.ClearedTileIndices);
        }

        [Test]
        public void RegisterTerrainDecorationSaveSource_AfterLoad_RestoresRetainedState()
        {
            var repository = new JsonSaveRepository(
                savePath,
                backupPath);
            var service = new SaveService(
                new FakeSim(new List<string>()),
                repository,
                new FakeClock());

            Assert.IsTrue(repository.TrySave(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                Simulation = new SimSaveData(),
                TerrainDecorations = new TerrainDecorationSaveData
                {
                    ClearedTileIndices = new[] { 4, 28 }
                }
            }));
            Assert.IsTrue(service.TryLoadAndRestore());

            var terrainDecorations = new FakeTerrainDecorations();
            service.RegisterTerrainDecorationSaveSource(
                terrainDecorations);

            CollectionAssert.AreEqual(
                new[] { 4, 28 },
                terrainDecorations.Current.ClearedTileIndices);
        }

        [Test]
        public void Repository_TrySaveAtomically_PreservesPreviousPrimaryAsBackup()
        {
            var repository =
                new JsonSaveRepository(savePath, backupPath);

            Assert.IsTrue(repository.TrySave(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                SavedAtUtcTicks = 10L
            }));
            Assert.IsTrue(repository.TrySave(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                SavedAtUtcTicks = 20L
            }));

            Assert.IsTrue(repository.TryLoad(out GameSaveData current));
            Assert.AreEqual(20L, current.SavedAtUtcTicks);

            string backupJson =
                File.ReadAllText(backupPath);
            GameSaveData backup =
                UnityEngine.JsonUtility.FromJson<GameSaveData>(
                    backupJson);

            Assert.NotNull(backup);
            Assert.AreEqual(10L, backup.SavedAtUtcTicks);
            Assert.IsFalse(File.Exists($"{savePath}.tmp"));
        }

        [Test]
        public void Repository_CorruptedPrimary_DoesNotOverwriteValidBackup()
        {
            var repository =
                new JsonSaveRepository(savePath, backupPath);

            Assert.IsTrue(repository.TrySave(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                SavedAtUtcTicks = 10L
            }));
            Assert.IsTrue(repository.TrySave(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                SavedAtUtcTicks = 20L
            }));

            File.WriteAllText(savePath, "{ invalid json");

            Assert.IsTrue(repository.TrySave(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                SavedAtUtcTicks = 30L
            }));
            Assert.IsTrue(repository.TryLoad(out GameSaveData current));
            Assert.AreEqual(30L, current.SavedAtUtcTicks);

            string backupJson =
                File.ReadAllText(backupPath);
            GameSaveData backup =
                UnityEngine.JsonUtility.FromJson<GameSaveData>(
                    backupJson);

            Assert.NotNull(backup);
            Assert.AreEqual(10L, backup.SavedAtUtcTicks);
        }

        [Test]
        public void SaveVersionPolicy_AcceptsSupportedRangeOnly()
        {
            Assert.IsTrue(
                SaveConstants.IsSupportedSaveVersion(
                    SaveConstants.MinimumSupportedSaveVersion));
            Assert.IsTrue(
                SaveConstants.IsSupportedSaveVersion(
                    SaveConstants.CurrentSaveVersion));
            Assert.IsFalse(
                SaveConstants.IsSupportedSaveVersion(
                    SaveConstants.MinimumSupportedSaveVersion - 1));
            Assert.IsFalse(
                SaveConstants.IsSupportedSaveVersion(
                    SaveConstants.CurrentSaveVersion + 1));
        }

        [Test]
        public void Repository_FutureVersionPrimary_LoadsSupportedBackup()
        {
            var repository =
                new JsonSaveRepository(savePath, backupPath);

            Assert.IsTrue(repository.TrySave(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                SavedAtUtcTicks = 10L
            }));
            Assert.IsTrue(repository.TrySave(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                SavedAtUtcTicks = 20L
            }));

            var futureSave = new GameSaveData
            {
                SaveVersion =
                    SaveConstants.CurrentSaveVersion + 1,
                SavedAtUtcTicks = 30L
            };
            File.WriteAllText(
                savePath,
                UnityEngine.JsonUtility.ToJson(futureSave));

            Assert.IsTrue(
                repository.TryLoad(out GameSaveData loaded));
            Assert.AreEqual(
                SaveConstants.CurrentSaveVersion,
                loaded.SaveVersion);
            Assert.AreEqual(10L, loaded.SavedAtUtcTicks);
        }

        [Test]
        public void TryLoadAndRestore_ClampsOfflineProgressAndSavesSettlement()
        {
            var calls = new List<string>();
            var repository =
                new JsonSaveRepository(savePath, backupPath);
            DateTime savedAt =
                new DateTime(
                    2026,
                    7,
                    18,
                    0,
                    0,
                    0,
                    DateTimeKind.Utc);
            var clock = new FakeClock
            {
                Now = savedAt.AddHours(20)
            };
            var weekly = new FakeWeekly(calls)
            {
                OfflineMaximumSeconds = 8.0 * 3600.0,
                CoinsPerOfflineHour = 10L
            };
            var service = new SaveService(
                new FakeSim(calls),
                repository,
                clock);
            service.RegisterWeeklySettlementSaveSource(weekly);

            Assert.IsTrue(repository.TrySave(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                SavedAtUtcTicks = savedAt.Ticks,
                Simulation = new SimSaveData(),
                WeeklySettlement =
                    new WeeklySettlementSaveData()
            }));

            OfflineSettlementCompletedEvent summary = default;
            int summaries = 0;
            service.OfflineSettlementCompleted += value =>
            {
                summary = value;
                summaries++;
            };

            Assert.IsTrue(service.TryLoadAndRestore());
            Assert.AreEqual(
                8.0 * 3600.0,
                weekly.LastOfflineSeconds);
            Assert.AreEqual(80L, weekly.Current.PendingCoins);
            Assert.AreEqual(1, summaries);
            Assert.AreEqual(80L, summary.EarnedCoins);

            Assert.IsTrue(
                repository.TryLoad(out GameSaveData settled));
            Assert.AreEqual(
                clock.UtcNow.Ticks,
                settled.SavedAtUtcTicks);
            Assert.AreEqual(
                80L,
                settled.WeeklySettlement.PendingCoins);

            Assert.IsTrue(service.TryLoadAndRestore());
            Assert.AreEqual(1, weekly.OfflineSettlementCalls);
            Assert.AreEqual(1, summaries);
        }

        [Test]
        public void TryLoadAndRestore_SystemClockMovedBackward_SkipsOfflineProgress()
        {
            var calls = new List<string>();
            var repository =
                new JsonSaveRepository(savePath, backupPath);
            DateTime savedAt =
                new DateTime(
                    2026,
                    7,
                    18,
                    12,
                    0,
                    0,
                    DateTimeKind.Utc);
            var weekly = new FakeWeekly(calls);
            var service = new SaveService(
                new FakeSim(calls),
                repository,
                new FakeClock
                {
                    Now = savedAt.AddMinutes(-5)
                });
            service.RegisterWeeklySettlementSaveSource(weekly);

            Assert.IsTrue(repository.TrySave(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                SavedAtUtcTicks = savedAt.Ticks,
                Simulation = new SimSaveData(),
                WeeklySettlement =
                    new WeeklySettlementSaveData()
            }));

            RestoreCompletedEvent completed = default;
            service.RestoreCompleted += value => completed = value;

            Assert.IsTrue(service.TryLoadAndRestore());
            Assert.AreEqual(0, weekly.OfflineSettlementCalls);
            Assert.IsFalse(completed.IncludesOfflineProgression);
            Assert.AreEqual(0.0, completed.SettledOfflineSeconds);
        }

        [Test]
        public void TryLoadAndRestore_SettledSaveFails_RollsBackReward()
        {
            var repository =
                new JsonSaveRepository(savePath, backupPath);
            DateTime savedAt =
                new DateTime(
                    2026,
                    7,
                    18,
                    0,
                    0,
                    0,
                    DateTimeKind.Utc);
            var weekly = new FakeWeekly(new List<string>())
            {
                CoinsPerOfflineHour = 10L
            };
            var service = new SaveService(
                new FakeSim(new List<string>()),
                repository,
                new FakeClock
                {
                    Now = savedAt.AddHours(2)
                });
            service.RegisterWeeklySettlementSaveSource(weekly);

            Assert.IsTrue(repository.TrySave(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                SavedAtUtcTicks = savedAt.Ticks,
                Simulation = new SimSaveData(),
                WeeklySettlement =
                    new WeeklySettlementSaveData()
            }));

            Directory.CreateDirectory(backupPath);

            int summaries = 0;
            RestoreCompletedEvent lastCompleted = default;
            service.OfflineSettlementCompleted += _ => summaries++;
            service.RestoreCompleted += value =>
                lastCompleted = value;

            Assert.IsTrue(service.TryLoadAndRestore());
            Assert.AreEqual(1, weekly.OfflineSettlementCalls);
            Assert.AreEqual(0L, weekly.Current.PendingCoins);
            Assert.AreEqual(0, summaries);
            Assert.IsFalse(
                lastCompleted.IncludesOfflineProgression);

            Assert.IsTrue(
                repository.TryLoad(out GameSaveData persisted));
            Assert.AreEqual(
                savedAt.Ticks,
                persisted.SavedAtUtcTicks);
            Assert.AreEqual(
                0L,
                persisted.WeeklySettlement.PendingCoins);
        }

        [Test]
        public void TryLoadAndRestore_RestoreCompletedFails_RollsBackReward()
        {
            var repository =
                new JsonSaveRepository(savePath, backupPath);
            DateTime savedAt =
                new DateTime(
                    2026,
                    7,
                    18,
                    0,
                    0,
                    0,
                    DateTimeKind.Utc);
            var weekly = new FakeWeekly(new List<string>())
            {
                CoinsPerOfflineHour = 10L
            };
            var service = new SaveService(
                new FakeSim(new List<string>()),
                repository,
                new FakeClock
                {
                    Now = savedAt.AddHours(2)
                });
            service.RegisterWeeklySettlementSaveSource(weekly);

            Assert.IsTrue(repository.TrySave(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                SavedAtUtcTicks = savedAt.Ticks,
                Simulation = new SimSaveData(),
                WeeklySettlement =
                    new WeeklySettlementSaveData()
            }));

            service.RestoreCompleted += value =>
            {
                if (value.IncludesOfflineProgression)
                {
                    throw new InvalidOperationException(
                        "Test restore callback failure.");
                }
            };

            Assert.IsTrue(service.TryLoadAndRestore());
            Assert.AreEqual(1, weekly.OfflineSettlementCalls);
            Assert.AreEqual(0L, weekly.Current.PendingCoins);

            Assert.IsTrue(
                repository.TryLoad(out GameSaveData persisted));
            Assert.AreEqual(
                savedAt.Ticks,
                persisted.SavedAtUtcTicks);
            Assert.AreEqual(
                0L,
                persisted.WeeklySettlement.PendingCoins);
        }

        [TestCase(600L, 60.0, 3600.0, 100, 36000L)]
        [TestCase(600L, 60.0, 3600.0, 50, 18000L)]
        [TestCase(600L, 60.0, 3600.0, 0, 0L)]
        [TestCase(0L, 0.0, 3600.0, 100, 0L)]
        public void CalculateOfflineIncome_UsesObservedAverageAndPercent(
            long observedCoins,
            double observedSeconds,
            double offlineSeconds,
            int incomePercent,
            long expected)
        {
            Assert.AreEqual(
                expected,
                OfflineSettlementMath.CalculateIncome(
                    observedCoins,
                    observedSeconds,
                    offlineSeconds,
                    incomePercent));
        }

        static void DeleteTestPath(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        sealed class FakeClock : ISaveClock
        {
            public DateTime Now =
                new DateTime(
                    2026,
                    7,
                    18,
                    0,
                    0,
                    0,
                    DateTimeKind.Utc);

            public DateTime UtcNow => Now;
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

        sealed class FakeWeekly :
            IWeeklySettlementSaveSource,
            IOfflineSettlementSource
        {
            readonly List<string> calls;
            public WeeklySettlementSaveData Current = new WeeklySettlementSaveData();
            public WeeklySettlementSaveData Restored { get; private set; }
            public double OfflineMaximumSeconds { get; set; } =
                8.0 * 3600.0;
            public long CoinsPerOfflineHour { get; set; }
            public double LastOfflineSeconds { get; private set; }
            public int OfflineSettlementCalls { get; private set; }
            public double MaximumOfflineSeconds =>
                OfflineMaximumSeconds;

            public FakeWeekly(List<string> calls) => this.calls = calls;
            public WeeklySettlementSaveData CreateSnapshot() => Current;
            public void RestoreSnapshot(WeeklySettlementSaveData snapshot)
            {
                Restored = snapshot;
                Current = new WeeklySettlementSaveData
                {
                    PendingCoins = snapshot.PendingCoins,
                    DaysIntoCurrentWeek =
                        snapshot.DaysIntoCurrentWeek,
                    LastProcessedTotalDays =
                        snapshot.LastProcessedTotalDays,
                    HasCycleProgress =
                        snapshot.HasCycleProgress,
                    ObservedOnlineIncomeCoins =
                        snapshot.ObservedOnlineIncomeCoins,
                    ObservedOnlineSeconds =
                        snapshot.ObservedOnlineSeconds
                };
                calls.Add("weekly");
            }

            public long SettleOffline(double elapsedSeconds)
            {
                OfflineSettlementCalls++;
                LastOfflineSeconds = elapsedSeconds;
                long reward = (long)Math.Floor(
                    elapsedSeconds /
                    3600.0 *
                    CoinsPerOfflineHour);
                Current.PendingCoins += reward;
                calls.Add("offline");
                return reward;
            }
        }

        sealed class FakeProgression : IProgressionSaveSource
        {
            public ProgressionSaveData Current = new ProgressionSaveData();
            public ProgressionSaveData CreateSnapshot() => Current;
            public void RestoreSnapshot(ProgressionSaveData snapshot) => Current = snapshot;
        }

        sealed class FakeTerrainDecorations :
            ITerrainDecorationSaveSource
        {
            public TerrainDecorationSaveData Current =
                new TerrainDecorationSaveData();

            public TerrainDecorationSaveData CreateSnapshot() =>
                Current;

            public void RestoreSnapshot(
                TerrainDecorationSaveData snapshot)
            {
                Current = snapshot;
            }
        }
    }
}
