using System;
using System.Collections.Generic;
using System.IO;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.Save;
using NUnit.Framework;
using UnityEngine;

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
        public void Save_RoundTripsSchoolBusTripHistory()
        {
            var repository =
                new JsonSaveRepository(savePath, backupPath);
            var source = new FakeSchoolBus
            {
                Current = new SchoolBusSaveData
                {
                    HasTripHistory = true,
                    LastMorningTripDay = 12L,
                    LastAfternoonTripDay = 11L
                }
            };
            var service = new SaveService(
                new FakeSim(new List<string>()),
                repository,
                new FakeClock());
            service.RegisterSchoolBusSaveSource(source);

            Assert.IsTrue(service.Save());
            Assert.IsTrue(
                repository.TryLoad(out GameSaveData loaded));
            Assert.IsTrue(loaded.SchoolBus.HasTripHistory);
            Assert.AreEqual(
                12L,
                loaded.SchoolBus.LastMorningTripDay);
            Assert.AreEqual(
                11L,
                loaded.SchoolBus.LastAfternoonTripDay);

            source.Current = new SchoolBusSaveData();
            service.RestoreSnapshot(loaded);

            Assert.AreEqual(
                12L,
                source.Current.LastMorningTripDay);
            Assert.AreEqual(
                11L,
                source.Current.LastAfternoonTripDay);
        }

        [Test]
        public void RegisterSchoolBusSaveSource_AfterLoad_RestoresTripHistory()
        {
            var repository =
                new JsonSaveRepository(savePath, backupPath);
            var service = new SaveService(
                new FakeSim(new List<string>()),
                repository,
                new FakeClock());

            Assert.IsTrue(
                repository.TrySave(
                    new GameSaveData
                    {
                        SaveVersion =
                            SaveConstants.CurrentSaveVersion,
                        Simulation = new SimSaveData(),
                        SchoolBus = new SchoolBusSaveData
                        {
                            HasTripHistory = true,
                            LastMorningTripDay = 7L,
                            LastAfternoonTripDay = 6L
                        }
                    }));
            Assert.IsTrue(service.TryLoadAndRestore());

            var source = new FakeSchoolBus();
            service.RegisterSchoolBusSaveSource(source);

            Assert.IsTrue(source.Current.HasTripHistory);
            Assert.AreEqual(
                7L,
                source.Current.LastMorningTripDay);
            Assert.AreEqual(
                6L,
                source.Current.LastAfternoonTripDay);
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
        public void Save_RoundTripsSpecialBuildingIdentity()
        {
            var repository = new JsonSaveRepository(savePath, backupPath);
            var buildings = new FakeSpecialBuildings
            {
                Current = new SpecialBuildingSaveData
                {
                    Buildings = new[]
                    {
                        new SpecialBuildingInstanceSaveData
                        {
                            BuildingId = "cinema",
                            X = 4,
                            Y = 7,
                            Direction = PlacementDirection.East
                        }
                    }
                }
            };
            var service = new SaveService(
                new FakeSim(new List<string>()),
                repository,
                new FakeClock());
            service.RegisterSpecialBuildingSaveSource(buildings);

            Assert.IsTrue(service.Save());
            Assert.IsTrue(repository.TryLoad(out GameSaveData loaded));
            Assert.AreEqual(1, loaded.SpecialBuildings.Buildings.Length);
            Assert.AreEqual(
                "cinema",
                loaded.SpecialBuildings.Buildings[0].BuildingId);
            Assert.AreEqual(
                PlacementDirection.East,
                loaded.SpecialBuildings.Buildings[0].Direction);
        }

        [Test]
        public void RegisterSpecialBuildingSaveSource_AfterLoad_RestoresRetainedState()
        {
            var repository = new JsonSaveRepository(savePath, backupPath);
            var service = new SaveService(
                new FakeSim(new List<string>()),
                repository,
                new FakeClock());

            Assert.IsTrue(repository.TrySave(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                Simulation = new SimSaveData(),
                SpecialBuildings = new SpecialBuildingSaveData
                {
                    Buildings = new[]
                    {
                        new SpecialBuildingInstanceSaveData
                        {
                            BuildingId = "mall",
                            X = 2,
                            Y = 3
                        }
                    }
                }
            }));
            Assert.IsTrue(service.TryLoadAndRestore());

            var buildings = new FakeSpecialBuildings();
            service.RegisterSpecialBuildingSaveSource(buildings);

            Assert.AreEqual(1, buildings.Current.Buildings.Length);
            Assert.AreEqual(
                "mall",
                buildings.Current.Buildings[0].BuildingId);
        }

        [Test]
        public void Save_RoundTripsSpecialBuildingVisitStatistics()
        {
            var repository = new JsonSaveRepository(savePath, backupPath);
            var visits = new FakeSpecialBuildingVisits
            {
                Current = new SpecialBuildingVisitSaveData
                {
                    HasState = true,
                    LastProcessedTotalDay = 42L,
                    Statistics = new[]
                    {
                        new SpecialBuildingVisitStatisticsSaveData
                        {
                            BuildingId = "cinema",
                            X = 4,
                            Y = 7,
                            Day = 42L,
                            PlannedToday = 3,
                            TotalPlannedVisits = 18
                        }
                    }
                }
            };
            var service = new SaveService(
                new FakeSim(new List<string>()),
                repository,
                new FakeClock());
            service.RegisterSpecialBuildingVisitSaveSource(visits);

            Assert.IsTrue(service.Save());
            Assert.IsTrue(repository.TryLoad(out GameSaveData loaded));
            Assert.IsTrue(loaded.SpecialBuildingVisits.HasState);
            Assert.AreEqual(
                42L,
                loaded.SpecialBuildingVisits.LastProcessedTotalDay);
            Assert.AreEqual(
                18,
                loaded.SpecialBuildingVisits.Statistics[0]
                    .TotalPlannedVisits);
        }

        [Test]
        public void LegacySpecialBuildingState_MigratesForLateRegisteredSources()
        {
            var repository = new JsonSaveRepository(savePath, backupPath);
            var service = new SaveService(
                new FakeSim(new List<string>()),
                repository,
                new FakeClock(),
                worldGridAccess: new FakeWorldGridAccess());

            Assert.IsTrue(repository.TrySave(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                GridWidth = 20,
                GridHeight = 20,
                Simulation = new SimSaveData
                {
                    GridWidth = 20,
                    GridHeight = 20
                },
                SpecialBuildings = new SpecialBuildingSaveData
                {
                    Buildings = new[]
                    {
                        new SpecialBuildingInstanceSaveData
                        {
                            BuildingId = "cinema",
                            X = 2,
                            Y = 3,
                            Direction = PlacementDirection.East
                        }
                    }
                },
                SpecialBuildingVisits = new SpecialBuildingVisitSaveData
                {
                    HasState = true,
                    LastProcessedTotalDay = 12L,
                    Statistics = new[]
                    {
                        new SpecialBuildingVisitStatisticsSaveData
                        {
                            BuildingId = "cinema",
                            X = 2,
                            Y = 3,
                            Day = 12L,
                            PlannedToday = 4,
                            TotalPlannedVisits = 27L
                        }
                    }
                }
            }));
            Assert.IsTrue(service.TryLoadAndRestore());

            var buildings = new FakeSpecialBuildings();
            var visits = new FakeSpecialBuildingVisits();
            service.RegisterSpecialBuildingSaveSource(buildings);
            service.RegisterSpecialBuildingVisitSaveSource(visits);

            SpecialBuildingInstanceSaveData building =
                buildings.Current.Buildings[0];
            Assert.AreEqual("cinema", building.BuildingId);
            Assert.AreEqual(92, building.X);
            Assert.AreEqual(93, building.Y);
            Assert.AreEqual(PlacementDirection.East, building.Direction);

            SpecialBuildingVisitStatisticsSaveData statistics =
                visits.Current.Statistics[0];
            Assert.AreEqual(92, statistics.X);
            Assert.AreEqual(93, statistics.Y);
            Assert.AreEqual(12L, statistics.Day);
            Assert.AreEqual(4, statistics.PlannedToday);
            Assert.AreEqual(27L, statistics.TotalPlannedVisits);
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
        public void TryLoadAndRestore_RestoreCompletedHandlerFailure_ReturnsFalse()
        {
            var calls = new List<string>();
            var repository = new JsonSaveRepository(savePath, backupPath);
            var service = new SaveService(
                new FakeSim(calls),
                repository,
                new FakeClock());
            service.RegisterWeeklySettlementSaveSource(new FakeWeekly(calls));

            Assert.IsTrue(repository.TrySave(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                SavedAtUtcTicks = DateTime.UtcNow.Ticks,
                Simulation = new SimSaveData(),
                WeeklySettlement = new WeeklySettlementSaveData()
            }));

            service.RestoreCompleted += _ =>
                throw new InvalidOperationException("Test restore callback failure.");

            Assert.IsFalse(service.TryLoadAndRestore());
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
            IWeeklySettlementSaveSource
        {
            readonly List<string> calls;
            public WeeklySettlementSaveData Current = new WeeklySettlementSaveData();
            public WeeklySettlementSaveData Restored { get; private set; }

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

        }

        sealed class FakeProgression : IProgressionSaveSource
        {
            public ProgressionSaveData Current = new ProgressionSaveData();
            public ProgressionSaveData CreateSnapshot() => Current;
            public void RestoreSnapshot(ProgressionSaveData snapshot) => Current = snapshot;
        }

        sealed class FakeSchoolBus : ISchoolBusSaveSource
        {
            public SchoolBusSaveData Current =
                new SchoolBusSaveData();

            public SchoolBusSaveData CreateSnapshot() => Current;

            public void RestoreSnapshot(
                SchoolBusSaveData snapshot)
            {
                Current = snapshot;
            }
        }

        sealed class FakeTerrainDecorations :
            ITerrainDecorationSaveSource
        {
            public TerrainDecorationSaveData Current =
                new TerrainDecorationSaveData();
            public event Action StateChanged
            {
                add { }
                remove { }
            }

            public bool IsCleared(UnityEngine.Vector2Int tile) => false;

            public TerrainDecorationSaveData CreateSnapshot() =>
                Current;

            public void RestoreSnapshot(
                TerrainDecorationSaveData snapshot)
            {
                Current = snapshot;
            }
        }

        sealed class FakeSpecialBuildings :
            ISpecialBuildingSaveSource
        {
            public SpecialBuildingSaveData Current =
                new SpecialBuildingSaveData();

            public SpecialBuildingSaveData CreateSnapshot() =>
                Current;

            public void RestoreSnapshot(
                SpecialBuildingSaveData snapshot)
            {
                Current = snapshot;
            }
        }

        sealed class FakeSpecialBuildingVisits :
            ISpecialBuildingVisitSaveSource
        {
            public SpecialBuildingVisitSaveData Current =
                new SpecialBuildingVisitSaveData();

            public SpecialBuildingVisitSaveData CreateSnapshot() =>
                Current;

            public void RestoreSnapshot(
                SpecialBuildingVisitSaveData snapshot)
            {
                Current = snapshot;
            }
        }

        sealed class FakeWorldGridAccess : IWorldGridAccess
        {
            public int WorldWidth => 200;
            public int WorldHeight => 200;
            public int ChunkSize => 10;
            public int ChunkColumns => 20;
            public int ChunkRows => 20;
            public Vector2Int InitialPlayableOrigin => new(90, 90);
            public Vector2Int InitialPlayableSize => new(20, 20);

            public event Action<GridChunkId> ChunkUnlocked
            {
                add { }
                remove { }
            }

            public event Action AccessRestored
            {
                add { }
                remove { }
            }

            public bool IsInsideWorld(Vector2Int tile) =>
                tile.x >= 0 && tile.x < WorldWidth &&
                tile.y >= 0 && tile.y < WorldHeight;

            public bool IsTileUnlocked(Vector2Int tile) =>
                IsAreaUnlocked(tile, Vector2Int.one);

            public bool IsChunkUnlocked(GridChunkId chunk) =>
                chunk.X is 9 or 10 && chunk.Y is 9 or 10;

            public bool IsAreaUnlocked(
                Vector2Int anchor,
                Vector2Int footprint)
            {
                Vector2Int max = anchor + footprint;
                return anchor.x >= 90 && anchor.y >= 90 &&
                       max.x <= 110 && max.y <= 110;
            }

            public bool TryGetChunkId(
                Vector2Int tile,
                out GridChunkId chunk)
            {
                if (!IsInsideWorld(tile))
                {
                    chunk = default;
                    return false;
                }

                chunk = new GridChunkId(
                    tile.x / ChunkSize,
                    tile.y / ChunkSize);
                return true;
            }
        }
    }
}
