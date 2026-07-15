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
        private string savePath;
        private string backupPath;

        [SetUp]
        public void SetUp()
        {
            string testId = Guid.NewGuid().ToString("N");
            savePath = Path.Combine(Path.GetTempPath(), $"greenlight_{testId}.json");
            backupPath = Path.Combine(Path.GetTempPath(), $"greenlight_{testId}_backup.json");
        }

        [TearDown]
        public void TearDown()
        {
            DeleteIfPresent(savePath);
            DeleteIfPresent(backupPath);
        }

        [Test]
        public void TryLoadAndRestore_PublishesOnceAfterAllRestoreWork()
        {
            var calls = new List<string>();
            DateTime now = new DateTime(2026, 7, 13, 9, 0, 0, DateTimeKind.Utc);
            var repository = new JsonSaveRepository(savePath, backupPath);
            var simulation = new FakeSimulationSaveSource(calls, 60.0);
            var calendar = new FakeCalendarSaveSource(calls);
            var weekly = new FakeWeeklySettlementSaveSource(calls);
            var radio = new FakeRadioSaveSource(calls);
            var service = new SaveService(
                simulation,
                repository,
                new FakeSaveClock(now));

            service.RegisterGameCalendarSaveSource(calendar);
            service.RegisterWeeklySettlementSaveSource(weekly);
            service.RegisterRadioSaveSource(radio);

            bool written = repository.TrySave(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                SavedAtUtcTicks = now.AddSeconds(-120.0).Ticks,
                Simulation = new SimSaveData(),
                WeeklySettlement = new WeeklySettlementSaveData
                {
                    PendingCoins = 250L
                },
                Calendar = new GameCalendarSaveData(),
                Radio = new RadioSaveData
                {
                    Slots = Array.Empty<RadioSlotSaveData>(),
                    CurrentSlotIndex = -1
                }
            });

            Assert.That(written, Is.True);

            int completedCount = 0;
            double settledSeconds = -1.0;
            bool wasRestoringDuringEvent = true;

            service.RestoreCompleted += restoreEvent =>
            {
                completedCount++;
                settledSeconds = restoreEvent.SettledOfflineSeconds;
                wasRestoringDuringEvent = service.IsRestoring;
                calls.Add("completed");
            };

            bool loaded = service.TryLoadAndRestore();

            Assert.That(loaded, Is.True);
            Assert.That(completedCount, Is.EqualTo(1));
            Assert.That(settledSeconds, Is.EqualTo(60.0));
            Assert.That(wasRestoringDuringEvent, Is.False);
            Assert.That(weekly.RestoredCoins, Is.EqualTo(250L));
            CollectionAssert.AreEqual(
                new[]
                {
                    "simulation",
                    "weekly",
                    "calendar",
                    "radio",
                    "settlement",
                    "calendar-offline",
                    "completed"
                },
                calls);
        }

        [Test]
        public void TryLoadAndRestore_PublishesOfflineCoinSummaryAfterRestoreCompleted()
        {
            var calls = new List<string>();
            DateTime now = new DateTime(2026, 7, 14, 9, 0, 0, DateTimeKind.Utc);
            var repository = new JsonSaveRepository(savePath, backupPath);
            var economy = new FakeEconomySaveSource();
            var weekly = new FakeWeeklySettlementSaveSource(calls);
            var simulation = new FakeSimulationSaveSource(
                calls,
                60.0,
                () => economy.AddCoins(40L));
            var service = new SaveService(
                simulation,
                repository,
                new FakeSaveClock(now),
                economy);

            service.RegisterWeeklySettlementSaveSource(weekly);

            bool written = repository.TrySave(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                SavedAtUtcTicks = now.AddSeconds(-120.0).Ticks,
                Simulation = new SimSaveData(),
                Economy = new EconomySaveData
                {
                    Coins = 100L
                },
                WeeklySettlement = new WeeklySettlementSaveData
                {
                    PendingCoins = 50L
                }
            });

            Assert.That(written, Is.True);

            bool restoreCompleted = false;
            bool summaryPublishedAfterRestore = false;
            int summaryCount = 0;
            OfflineSettlementCompletedEvent summary = default;

            service.RestoreCompleted += _ =>
            {
                economy.AddCoins(weekly.ClaimCoins());
                restoreCompleted = true;
            };
            service.OfflineSettlementCompleted += settlement =>
            {
                summaryCount++;
                summary = settlement;
                summaryPublishedAfterRestore = restoreCompleted;
            };

            Assert.That(service.TryLoadAndRestore(), Is.True);
            Assert.That(summaryCount, Is.EqualTo(1));
            Assert.That(summaryPublishedAfterRestore, Is.True);
            Assert.That(summary.SettledOfflineSeconds, Is.EqualTo(60.0));
            Assert.That(summary.InitialCoins, Is.EqualTo(100L));
            Assert.That(summary.EarnedCoins, Is.EqualTo(90L));
            Assert.That(summary.CurrentCoins, Is.EqualTo(190L));
            Assert.That(repository.TryLoad(out GameSaveData persisted), Is.True);
            Assert.That(persisted.Economy.Coins, Is.EqualTo(190L));
            Assert.That(persisted.WeeklySettlement.PendingCoins, Is.Zero);
            Assert.That(persisted.SavedAtUtcTicks, Is.EqualTo(now.Ticks));
        }

        [Test]
        public void TryLoadAndRestore_RestoredPendingCoinsPublishSummaryWithoutOfflineTime()
        {
            var calls = new List<string>();
            DateTime now = new DateTime(2026, 7, 14, 9, 0, 0, DateTimeKind.Utc);
            var repository = new JsonSaveRepository(savePath, backupPath);
            var economy = new FakeEconomySaveSource();
            var weekly = new FakeWeeklySettlementSaveSource(calls);
            var service = new SaveService(
                new FakeSimulationSaveSource(calls, 0.0),
                repository,
                new FakeSaveClock(now),
                economy);

            service.RegisterWeeklySettlementSaveSource(weekly);

            Assert.That(repository.TrySave(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                SavedAtUtcTicks = now.AddSeconds(-1.0).Ticks,
                Simulation = new SimSaveData(),
                Economy = new EconomySaveData
                {
                    Coins = 100L
                },
                WeeklySettlement = new WeeklySettlementSaveData
                {
                    PendingCoins = 50L
                }
            }), Is.True);

            service.RestoreCompleted += _ =>
                economy.AddCoins(weekly.ClaimCoins());

            int summaryCount = 0;
            OfflineSettlementCompletedEvent summary = default;
            service.OfflineSettlementCompleted += settlement =>
            {
                summaryCount++;
                summary = settlement;
            };

            Assert.That(service.TryLoadAndRestore(), Is.True);
            Assert.That(summaryCount, Is.EqualTo(1));
            Assert.That(summary.SettledOfflineSeconds, Is.Zero);
            Assert.That(summary.InitialCoins, Is.EqualTo(100L));
            Assert.That(summary.EarnedCoins, Is.EqualTo(50L));
            Assert.That(summary.CurrentCoins, Is.EqualTo(150L));
        }

        [Test]
        public void RestoreSnapshot_LegacyDataWithoutOptionalSections_RestoresDefaults()
        {
            var calls = new List<string>();
            var weekly = new FakeWeeklySettlementSaveSource(calls);
            var radio = new FakeRadioSaveSource(calls);
            var service = new SaveService(
                new FakeSimulationSaveSource(calls, 0.0),
                new JsonSaveRepository(savePath, backupPath),
                new FakeSaveClock(DateTime.UtcNow));

            service.RegisterWeeklySettlementSaveSource(weekly);
            service.RegisterRadioSaveSource(radio);

            service.RestoreSnapshot(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                Simulation = new SimSaveData()
            });

            CollectionAssert.AreEqual(
                new[] { "simulation", "weekly", "radio" },
                calls);
            Assert.That(weekly.RestoreCount, Is.EqualTo(1));
            Assert.That(weekly.RestoredCoins, Is.Zero);
            Assert.That(radio.RestoreCount, Is.EqualTo(1));
        }

        [Test]
        public void TryLoadAndRestore_UnregisteredOptionalSources_PreservesLoadedData()
        {
            var calls = new List<string>();
            DateTime now = new DateTime(2026, 7, 13, 9, 0, 0, DateTimeKind.Utc);
            var repository = new JsonSaveRepository(savePath, backupPath);
            var service = new SaveService(
                new FakeSimulationSaveSource(calls, 30.0),
                repository,
                new FakeSaveClock(now));

            bool written = repository.TrySave(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                SavedAtUtcTicks = now.AddSeconds(-60.0).Ticks,
                Simulation = new SimSaveData(),
                WeeklySettlement = new WeeklySettlementSaveData
                {
                    PendingCoins = 777L,
                    DaysIntoCurrentWeek = 5,
                    LastProcessedTotalDays = 125L,
                    HasCycleProgress = true
                },
                Radio = new RadioSaveData
                {
                    Slots = new[]
                    {
                        new RadioSlotSaveData
                        {
                            SlotIndex = 0,
                            IsUnlocked = true,
                            YoutubeVideoId = "video-id"
                        }
                    },
                    CurrentSlotIndex = 0
                }
            });

            Assert.That(written, Is.True);
            Assert.That(service.TryLoadAndRestore(), Is.True);
            Assert.That(repository.TryLoad(out GameSaveData reloaded), Is.True);
            Assert.That(reloaded.WeeklySettlement.PendingCoins, Is.EqualTo(777L));
            Assert.That(reloaded.WeeklySettlement.DaysIntoCurrentWeek, Is.EqualTo(5));
            Assert.That(reloaded.WeeklySettlement.LastProcessedTotalDays, Is.EqualTo(125L));
            Assert.That(reloaded.WeeklySettlement.HasCycleProgress, Is.True);
            Assert.That(reloaded.Radio.CurrentSlotIndex, Is.EqualTo(0));
            Assert.That(reloaded.Radio.Slots[0].YoutubeVideoId, Is.EqualTo("video-id"));
        }

        private static void DeleteIfPresent(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private sealed class FakeSaveClock : ISaveClock
        {
            public DateTime UtcNow { get; }

            public FakeSaveClock(DateTime utcNow)
            {
                UtcNow = utcNow;
            }
        }

        private sealed class FakeSimulationSaveSource :
            ISimSaveSource,
            IOfflineSettlementSource
        {
            private readonly List<string> calls;
            private readonly double settledSeconds;
            private readonly Action onSettle;

            public int GridWidth => 20;
            public int GridHeight => 20;

            public FakeSimulationSaveSource(
                List<string> calls,
                double settledSeconds,
                Action onSettle = null)
            {
                this.calls = calls;
                this.settledSeconds = settledSeconds;
                this.onSettle = onSettle;
            }

            public SimSaveData CreateSnapshot()
            {
                return new SimSaveData();
            }

            public void RestoreSnapshot(SimSaveData snapshot)
            {
                calls.Add("simulation");
            }

            public double SettleOffline(double elapsedSeconds)
            {
                calls.Add("settlement");
                onSettle?.Invoke();
                return settledSeconds;
            }
        }

        private sealed class FakeEconomySaveSource : IEconomySaveSource
        {
            public long Coins { get; private set; }

            public EconomySaveData CreateSnapshot()
            {
                return new EconomySaveData
                {
                    Coins = Coins
                };
            }

            public void RestoreSnapshot(EconomySaveData snapshot)
            {
                Coins = Math.Max(0L, snapshot?.Coins ?? 0L);
            }

            public void AddCoins(long amount)
            {
                Coins += Math.Max(0L, amount);
            }
        }

        private sealed class FakeCalendarSaveSource :
            IGameCalendarSaveSource,
            IOfflineCalendarProgressionSource
        {
            private readonly List<string> calls;

            public FakeCalendarSaveSource(List<string> calls)
            {
                this.calls = calls;
            }

            public GameCalendarSaveData CreateSnapshot()
            {
                return new GameCalendarSaveData();
            }

            public void RestoreSnapshot(GameCalendarSaveData snapshot)
            {
                calls.Add("calendar");
            }

            public void AdvanceOffline(double settledRealSeconds)
            {
                calls.Add("calendar-offline");
            }
        }

        private sealed class FakeWeeklySettlementSaveSource :
            IWeeklySettlementSaveSource
        {
            private readonly List<string> calls;

            public int RestoreCount { get; private set; }
            public long RestoredCoins { get; private set; }

            public FakeWeeklySettlementSaveSource(List<string> calls)
            {
                this.calls = calls;
            }

            public WeeklySettlementSaveData CreateSnapshot()
            {
                return new WeeklySettlementSaveData
                {
                    PendingCoins = RestoredCoins
                };
            }

            public void RestoreSnapshot(WeeklySettlementSaveData snapshot)
            {
                RestoreCount++;
                RestoredCoins = snapshot.PendingCoins;
                calls.Add("weekly");
            }

            public void AddCoins(long amount)
            {
                RestoredCoins += Math.Max(0L, amount);
            }

            public long ClaimCoins()
            {
                long claimedCoins = RestoredCoins;
                RestoredCoins = 0L;
                return claimedCoins;
            }
        }

        private sealed class FakeRadioSaveSource : IRadioSaveSource
        {
            private readonly List<string> calls;

            public int RestoreCount { get; private set; }

            public FakeRadioSaveSource(List<string> calls)
            {
                this.calls = calls;
            }

            public RadioSaveData CreateSnapshot()
            {
                return new RadioSaveData
                {
                    Slots = Array.Empty<RadioSlotSaveData>(),
                    CurrentSlotIndex = -1
                };
            }

            public void RestoreSnapshot(RadioSaveData snapshot)
            {
                RestoreCount++;
                calls.Add("radio");
            }
        }
    }
}
