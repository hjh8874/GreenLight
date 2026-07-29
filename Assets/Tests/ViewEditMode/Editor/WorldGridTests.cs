using System.Reflection;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Content.Transit;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.Gameplay.Quests;
using CityFlow.Save;
using CityFlow.Sim;
using CityFlow.UI;
using CityFlow.View;
using CityFlow.WorldGrid;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CityFlow.Tests
{
    public sealed class WorldGridTests
    {
        private const string ConfigPath =
            "Assets/05_ScriptableObjects/WorldGridConfig.asset";
        private const string UnlockProfilePath =
            "Assets/05_ScriptableObjects/WorldGridUnlockProfile.asset";
        private const string PrefabPath =
            "Assets/02_Prefabs/WorldGrid/WorldGridSystem.prefab";

        [Test]
        public void Partition_MapsWorldEdgesToExpectedChunks()
        {
            var partition = new GridChunkPartition(200, 200, 20);

            Assert.AreEqual(10, partition.ChunkColumns);
            Assert.AreEqual(10, partition.ChunkRows);
            Assert.AreEqual(100, partition.ChunkCount);
            AssertChunk(partition, new Vector2Int(0, 0), 0, 0);
            AssertChunk(partition, new Vector2Int(19, 19), 0, 0);
            AssertChunk(partition, new Vector2Int(20, 0), 1, 0);
            AssertChunk(partition, new Vector2Int(199, 199), 9, 9);
            Assert.IsFalse(
                partition.TryGetChunkId(
                    new Vector2Int(200, 0),
                    out _));
        }

        [Test]
        public void State_InitiallyUnlocksCenteredTwentyByTwentyArea()
        {
            var state = CreateDefaultState();

            Assert.IsTrue(state.IsTileUnlocked(new Vector2Int(90, 90)));
            Assert.IsTrue(state.IsTileUnlocked(new Vector2Int(109, 109)));
            Assert.IsFalse(state.IsTileUnlocked(new Vector2Int(89, 90)));
            Assert.IsFalse(state.IsTileUnlocked(new Vector2Int(110, 90)));
            CollectionAssert.AreEqual(
                new[] { 189, 190, 209, 210 },
                state.CreateSnapshot().UnlockedChunkIndices);
        }

        [Test]
        public void AreaUnlock_RejectsFootprintCrossingLockedChunk()
        {
            var state = CreateDefaultState();
            Vector2Int anchor = new Vector2Int(109, 95);
            Vector2Int footprint = new Vector2Int(2, 1);

            Assert.IsFalse(state.IsAreaUnlocked(anchor, footprint));
            Assert.IsTrue(state.TryUnlockChunk(new GridChunkId(11, 9)));
            Assert.IsTrue(state.IsAreaUnlocked(anchor, footprint));
        }

        [Test]
        public void Unlock_IsIdempotentAndSnapshotIsDeterministic()
        {
            var state = CreateDefaultState();

            Assert.IsTrue(state.TryUnlockChunk(new GridChunkId(11, 9)));
            Assert.IsFalse(state.TryUnlockChunk(new GridChunkId(11, 9)));
            Assert.IsTrue(state.TryUnlockChunk(new GridChunkId(9, 11)));

            CollectionAssert.AreEqual(
                new[] { 189, 190, 191, 209, 210, 229 },
                state.CreateSnapshot().UnlockedChunkIndices);
        }

        [Test]
        public void Restore_IgnoresInvalidIndicesAndRejectsConfigMismatch()
        {
            var state = CreateDefaultState();

            Assert.IsTrue(state.RestoreSnapshot(new WorldGridSaveData
            {
                WorldWidth = 200,
                WorldHeight = 200,
                ChunkSize = 10,
                UnlockedChunkIndices = new[] { -1, 2, 2, 399, 400 }
            }));
            CollectionAssert.AreEqual(
                new[] { 2, 399 },
                state.CreateSnapshot().UnlockedChunkIndices);

            Assert.IsFalse(state.RestoreSnapshot(new WorldGridSaveData
            {
                WorldWidth = 200,
                WorldHeight = 200,
                ChunkSize = 20,
                UnlockedChunkIndices = new[] { 0 }
            }));
            CollectionAssert.AreEqual(
                new[] { 189, 190, 209, 210 },
                state.CreateSnapshot().UnlockedChunkIndices);
        }

        [Test]
        public void SystemPrefab_HasConfigAndRegistersAsSaveSource()
        {
            GameObject instance = null;

            try
            {
                WorldGridConfigSO config =
                    AssetDatabase.LoadAssetAtPath<WorldGridConfigSO>(ConfigPath);
                WorldGridUnlockProfileSO profile =
                    AssetDatabase.LoadAssetAtPath<WorldGridUnlockProfileSO>(
                        UnlockProfilePath);
                GameObject prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                Assert.NotNull(config);
                Assert.NotNull(profile);
                Assert.NotNull(prefab);
                Assert.AreEqual(200, config.WorldWidth);
                Assert.AreEqual(200, config.WorldHeight);
                Assert.AreEqual(10, config.ChunkSize);
                Assert.AreEqual(2, config.InitialUnlockedColumns);
                Assert.AreEqual(2, config.InitialUnlockedRows);
                Assert.AreEqual(
                    new Vector2Int(90, 90),
                    config.InitialPlayableOrigin);
                Assert.AreEqual(
                    new Vector2Int(20, 20),
                    config.InitialPlayableSize);

                instance = Object.Instantiate(prefab);
                WorldGridService worldGrid =
                    instance.GetComponent<WorldGridService>();
                WorldGridExpansionService expansion =
                    instance.GetComponent<WorldGridExpansionService>();
                WorldGridVisualStreamer visualStreamer =
                    instance.GetComponent<WorldGridVisualStreamer>();
                TerrainDecorationView terrainDecorations =
                    instance.GetComponent<TerrainDecorationView>();
                Assert.NotNull(worldGrid);
                Assert.NotNull(expansion);
                Assert.NotNull(visualStreamer);
                Assert.NotNull(terrainDecorations);
                Assert.AreSame(config, worldGrid.Config);
                Assert.AreSame(worldGrid, expansion.WorldGrid);
                Assert.AreSame(profile, expansion.Profile);
                Assert.AreSame(worldGrid, visualStreamer.WorldGrid);
                Assert.NotNull(visualStreamer.FieldTilePrefab);
                Assert.AreSame(
                    visualStreamer.DecorationCatalog,
                    terrainDecorations.Catalog);

                var save = new SaveService(null, null, null);
                var services = new CityFlowServices(
                    new SimEventHub(),
                    null,
                    null,
                    save);
                expansion.Initialize(services);

                Assert.AreSame(worldGrid, services.WorldGrid);
                Assert.AreSame(expansion, services.WorldGridExpansion);
                Assert.AreSame(worldGrid, save.WorldGridSaveSource);
                Assert.IsTrue(
                    worldGrid.IsTileUnlocked(new Vector2Int(90, 90)));
                Assert.IsFalse(
                    worldGrid.IsTileUnlocked(new Vector2Int(89, 90)));

                int unlockEventCount = 0;
                worldGrid.ChunkUnlocked += _ => unlockEventCount++;
                Assert.IsTrue(
                    services.WorldGrid.TryUnlockChunk(
                        new GridChunkId(11, 9)));
                Assert.IsFalse(
                    services.WorldGrid.TryUnlockChunk(
                        new GridChunkId(11, 9)));
                Assert.AreEqual(1, unlockEventCount);
                Assert.IsTrue(
                    worldGrid.IsTileUnlocked(new Vector2Int(110, 90)));
            }
            finally
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void UnlockProfile_DefinesTwentyTileStepsToFullWorld()
        {
            WorldGridUnlockProfileSO profile =
                AssetDatabase.LoadAssetAtPath<WorldGridUnlockProfileSO>(
                    UnlockProfilePath);

            Assert.NotNull(profile);
            Assert.AreEqual(10, profile.StageCount);
            for (int index = 0; index < profile.StageCount; index++)
            {
                Assert.IsTrue(profile.TryGetStage(index, out var stage));
                int expectedSize = (index + 1) * 20;
                Assert.AreEqual($"center_{expectedSize:000}", stage.StageId);
                Assert.AreEqual(expectedSize, stage.UnlockedWidth);
                Assert.AreEqual(expectedSize, stage.UnlockedHeight);
            }
        }

        [Test]
        public void Expansion_UnlocksCenteredFortyByFortyArea()
        {
            GameObject instance = null;

            try
            {
                WorldGridExpansionService expansion =
                    CreateInitializedSystem(out instance, out var worldGrid);
                int chunkEventCount = 0;
                int stageEventCount = 0;
                WorldGridStageChangedEvent lastEvent = default;
                worldGrid.ChunkUnlocked += _ => chunkEventCount++;
                expansion.StageChanged += stageEvent =>
                {
                    stageEventCount++;
                    lastEvent = stageEvent;
                };

                Assert.AreEqual(0, expansion.CurrentStageIndex);
                Assert.AreEqual("center_020", expansion.CurrentStageId);
                Assert.IsTrue(expansion.TryUnlockNextStage());

                Assert.AreEqual(1, expansion.CurrentStageIndex);
                Assert.AreEqual("center_040", expansion.CurrentStageId);
                Assert.AreEqual(12, chunkEventCount);
                Assert.AreEqual(1, stageEventCount);
                Assert.AreEqual(
                    new RectInt(80, 80, 40, 40),
                    lastEvent.UnlockedBounds);
                Assert.AreEqual(
                    WorldGridStageChangeReason.Unlocked,
                    lastEvent.Reason);
                Assert.IsTrue(
                    worldGrid.IsTileUnlocked(new Vector2Int(80, 80)));
                Assert.IsTrue(
                    worldGrid.IsTileUnlocked(new Vector2Int(119, 119)));
                Assert.IsFalse(
                    worldGrid.IsTileUnlocked(new Vector2Int(79, 80)));
                Assert.IsFalse(
                    worldGrid.IsTileUnlocked(new Vector2Int(120, 80)));
                Assert.AreEqual(
                    16,
                    worldGrid.CreateSnapshot().UnlockedChunkIndices.Length);
            }
            finally
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void Expansion_AllowsTriggerAgnosticStageSelection()
        {
            GameObject instance = null;

            try
            {
                WorldGridExpansionService expansion =
                    CreateInitializedSystem(out instance, out var worldGrid);

                Assert.IsTrue(expansion.TryUnlockStage("center_060"));
                Assert.AreEqual(2, expansion.CurrentStageIndex);
                Assert.IsTrue(
                    worldGrid.IsAreaUnlocked(
                        new Vector2Int(70, 70),
                        new Vector2Int(60, 60)));
                Assert.AreEqual(
                    36,
                    worldGrid.CreateSnapshot().UnlockedChunkIndices.Length);
                Assert.IsFalse(expansion.TryUnlockStage("center_040"));
                Assert.IsFalse(expansion.TryUnlockStage("missing_stage"));
            }
            finally
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void Expansion_RestoreSynchronizesStageAndInitialMinimum()
        {
            GameObject instance = null;

            try
            {
                WorldGridExpansionService expansion =
                    CreateInitializedSystem(out instance, out var worldGrid);
                Assert.IsTrue(expansion.TryUnlockNextStage());

                int restoredEventCount = 0;
                expansion.StageChanged += stageEvent =>
                {
                    if (stageEvent.Reason ==
                        WorldGridStageChangeReason.Restored)
                    {
                        restoredEventCount++;
                    }
                };

                worldGrid.RestoreSnapshot(new WorldGridSaveData
                {
                    WorldWidth = 200,
                    WorldHeight = 200,
                    ChunkSize = 10,
                    UnlockedChunkIndices = new int[0]
                });

                Assert.AreEqual(0, expansion.CurrentStageIndex);
                Assert.AreEqual("center_020", expansion.CurrentStageId);
                Assert.AreEqual(1, restoredEventCount);
                CollectionAssert.AreEqual(
                    new[] { 189, 190, 209, 210 },
                    worldGrid.CreateSnapshot().UnlockedChunkIndices);
            }
            finally
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void Expansion_ResetReturnsToInitialStage()
        {
            GameObject instance = null;

            try
            {
                WorldGridExpansionService expansion =
                    CreateInitializedSystem(out instance, out var worldGrid);
                Assert.IsTrue(expansion.TryUnlockStage("center_060"));

                int restoredEventCount = 0;
                expansion.StageChanged += stageEvent =>
                {
                    if (stageEvent.Reason ==
                        WorldGridStageChangeReason.Restored)
                    {
                        restoredEventCount++;
                    }
                };

                Assert.IsTrue(expansion.TryResetToInitialStage());

                Assert.AreEqual(0, expansion.CurrentStageIndex);
                Assert.AreEqual("center_020", expansion.CurrentStageId);
                Assert.AreEqual(1, restoredEventCount);
                CollectionAssert.AreEqual(
                    new[] { 189, 190, 209, 210 },
                    worldGrid.CreateSnapshot().UnlockedChunkIndices);
            }
            finally
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void VisualStreamer_RendersOnlyExpandedTilesAndResyncsRestore()
        {
            GameObject cityObject = null;
            GameObject systemInstance = null;

            try
            {
                cityObject = new GameObject("WorldGridVisualTestCity");
                MainCityView cityView =
                    cityObject.AddComponent<MainCityView>();
                GameObject prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                Assert.NotNull(prefab);

                systemInstance = Object.Instantiate(prefab);
                WorldGridService worldGrid =
                    systemInstance.GetComponent<WorldGridService>();
                WorldGridExpansionService expansion =
                    systemInstance.GetComponent<WorldGridExpansionService>();
                WorldGridVisualStreamer visualStreamer =
                    systemInstance.GetComponent<WorldGridVisualStreamer>();
                Assert.NotNull(worldGrid);
                Assert.NotNull(expansion);
                Assert.NotNull(visualStreamer);
                Assert.IsTrue(visualStreamer.TryInstall(cityView));

                var services = new CityFlowServices(
                    new SimEventHub(),
                    null,
                    null,
                    new SaveService(null, null, null));
                expansion.Initialize(services);
                visualStreamer.Initialize(services);
                visualStreamer.RefreshVisuals();

                Assert.AreEqual(0, visualStreamer.RenderedTileCount);
                Assert.AreEqual(0, visualStreamer.RenderBatchCount);

                Assert.IsTrue(expansion.TryUnlockNextStage());
                visualStreamer.RefreshVisuals();
                Assert.AreEqual(1200, visualStreamer.RenderedTileCount);
                Assert.AreEqual(2, visualStreamer.RenderBatchCount);

                visualStreamer.RefreshVisuals();
                Assert.AreEqual(1200, visualStreamer.RenderedTileCount);

                worldGrid.RestoreSnapshot(null);
                visualStreamer.RefreshVisuals();
                Assert.AreEqual(0, visualStreamer.RenderedTileCount);
                Assert.AreEqual(0, visualStreamer.RenderBatchCount);
            }
            finally
            {
                if (systemInstance != null)
                {
                    Object.DestroyImmediate(systemInstance);
                }

                if (cityObject != null)
                {
                    Object.DestroyImmediate(cityObject);
                }
            }
        }

        [Test]
        public void MainCityView_RendersCentralLogicalBuildingOnBaseBoard()
        {
            GameObject cityObject = null;
            GameObject systemInstance = null;

            try
            {
                GameObject prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                Assert.NotNull(prefab);
                systemInstance = Object.Instantiate(prefab);
                WorldGridService worldGrid =
                    systemInstance.GetComponent<WorldGridService>();
                Assert.NotNull(worldGrid);

                CityFlow.Sim.SimConfig config =
                    CityFlow.Sim.SimConfig.Default();
                var hub = new SimEventHub();
                var engine = new CityFlow.Sim.SimEngine(
                    config,
                    hub,
                    worldGrid);
                var services = new CityFlowServices(
                    hub,
                    engine,
                    engine,
                    new SaveService(engine, null, null));
                worldGrid.Initialize(services);
                Assert.IsTrue(
                    engine.Place(
                        new Vector2Int(90, 90),
                        TileType.House));

                cityObject = new GameObject("WorldGridCentralBuildingTest");
                MainCityView cityView =
                    cityObject.AddComponent<MainCityView>();
                cityView.Initialize(services);

                Transform building = null;
                Transform[] children =
                    cityObject.GetComponentsInChildren<Transform>(true);
                for (int index = 0; index < children.Length; index++)
                {
                    if (children[index].name == "House_90_90")
                    {
                        building = children[index];
                        break;
                    }
                }

                Assert.NotNull(building);
                Assert.That(building.localPosition.x, Is.InRange(0f, 20f));
                Assert.That(building.localPosition.y, Is.InRange(0f, 20f));
            }
            finally
            {
                if (cityObject != null)
                {
                    Object.DestroyImmediate(cityObject);
                }

                if (systemInstance != null)
                {
                    Object.DestroyImmediate(systemInstance);
                }
            }
        }

        [Test]
        public void RuntimeConsumers_UseWorldBoundsForCentralContent()
        {
            GameObject systemInstance = null;
            GameObject consumerObject = null;
            GameObject statsObject = null;

            try
            {
                GameObject prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                Assert.NotNull(prefab);
                systemInstance = Object.Instantiate(prefab);
                WorldGridService worldGrid =
                    systemInstance.GetComponent<WorldGridService>();
                Assert.NotNull(worldGrid);

                SimConfig config = SimConfig.Default();
                var events = new SimEventHub();
                var engine = new SimEngine(config, events, worldGrid);
                var save = new SaveService(
                    engine,
                    null,
                    null,
                    worldGridAccess: worldGrid);
                var services = new CityFlowServices(
                    events,
                    engine,
                    engine,
                    save,
                    stats: engine);
                worldGrid.Initialize(services);

                Vector2Int house = new(90, 90);
                Vector2Int school = new(94, 90);
                Vector2Int hospital = new(90, 94);
                Vector2Int firstStop = new(99, 100);
                Vector2Int secondStop = new(99, 102);

                Assert.IsTrue(engine.Place(house, TileType.House));
                Assert.IsTrue(engine.Place(school, TileType.School));
                Assert.IsTrue(engine.Place(hospital, TileType.Hospital));
                Assert.IsTrue(engine.Place(new Vector2Int(100, 100), TileType.Road));
                Assert.IsTrue(engine.Place(new Vector2Int(100, 101), TileType.Road));
                Assert.IsTrue(engine.Place(new Vector2Int(100, 102), TileType.Road));
                Assert.IsTrue(engine.TryPlaceBusStop(firstStop));
                Assert.IsTrue(engine.TryPlaceBusStop(secondStop));

                consumerObject = new GameObject("WorldGridConsumerBoundsTest");
                PopulationSystem population =
                    consumerObject.AddComponent<PopulationSystem>();
                SetPrivateField(
                    population,
                    "populationConfig",
                    AssetDatabase.LoadAssetAtPath<PopulationConfigSO>(
                        "Assets/05_ScriptableObjects/CityFlow/PopulationConfig.asset"));
                population.Initialize(services);
                Assert.IsTrue(population.IsPopulationTile(house));

                HospitalSystem hospitalSystem =
                    consumerObject.AddComponent<HospitalSystem>();
                SetPrivateField(
                    hospitalSystem,
                    "hospitalDefinition",
                    AssetDatabase.LoadAssetAtPath<BuildingDefinitionSO>(
                        "Assets/05_ScriptableObjects/CityFlow/TileData/HospitalTileData.asset"));
                hospitalSystem.Initialize(services);
                Assert.Greater(
                    hospitalSystem.CurrentHospitalStabilityBonus,
                    0);

                BusStopRegistry registry =
                    consumerObject.AddComponent<BusStopRegistry>();
                registry.Initialize(services);
                Assert.IsTrue(registry.ContainsSchool(school));
                Assert.IsTrue(registry.ContainsResidentialStop(house));
                Assert.IsTrue(registry.ContainsBusStop(firstStop));

                EmergencyIncidentSystem emergency =
                    consumerObject.AddComponent<EmergencyIncidentSystem>();
                SetPrivateField(
                    emergency,
                    "config",
                    AssetDatabase.LoadAssetAtPath<EmergencyIncidentConfigSO>(
                        "Assets/05_ScriptableObjects/CityFlow/Emergency/EmergencyIncidentConfig.asset"));
                emergency.Initialize(services);
                Assert.IsTrue(emergency.TryCreateIncidentAt(house));

                BusRoute route = consumerObject.AddComponent<BusRoute>();
                route.Initialize(services);
                Assert.IsTrue(route.ConfigureRoute(
                    new[] { firstStop, secondStop },
                    shouldLoop: false));
                Assert.IsTrue(route.StartRoute());

                CityQuestSystem quests =
                    consumerObject.AddComponent<CityQuestSystem>();
                quests.Initialize(services);
                Assert.AreEqual(200, GetPrivateField<int>(quests, "gridWidth"));
                Assert.AreEqual(200, GetPrivateField<int>(quests, "gridHeight"));

                statsObject = new GameObject("WorldGridStatsBoundsTest");
                statsObject.SetActive(false);
                StatsPanelController stats =
                    statsObject.AddComponent<StatsPanelController>();
                stats.Initialize(services);
                Assert.AreEqual(200, GetPrivateField<int>(stats, "_gridWidth"));
                Assert.AreEqual(200, GetPrivateField<int>(stats, "_gridHeight"));
            }
            finally
            {
                if (statsObject != null)
                {
                    Object.DestroyImmediate(statsObject);
                }

                if (consumerObject != null)
                {
                    Object.DestroyImmediate(consumerObject);
                }

                if (systemInstance != null)
                {
                    Object.DestroyImmediate(systemInstance);
                }
            }
        }

        private static WorldGridState CreateDefaultState()
        {
            return new WorldGridState(
                new GridChunkPartition(200, 200, 10),
                initialUnlockedColumns: 2,
                initialUnlockedRows: 2);
        }

        private static WorldGridExpansionService CreateInitializedSystem(
            out GameObject instance,
            out WorldGridService worldGrid)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.NotNull(prefab);

            instance = Object.Instantiate(prefab);
            worldGrid = instance.GetComponent<WorldGridService>();
            WorldGridExpansionService expansion =
                instance.GetComponent<WorldGridExpansionService>();
            Assert.NotNull(worldGrid);
            Assert.NotNull(expansion);

            var services = new CityFlowServices(
                new SimEventHub(),
                null,
                null,
                new SaveService(null, null, null));
            expansion.Initialize(services);

            Assert.AreSame(worldGrid, services.WorldGrid);
            Assert.AreSame(expansion, services.WorldGridExpansion);
            return expansion;
        }

        private static void AssertChunk(
            GridChunkPartition partition,
            Vector2Int tile,
            int expectedX,
            int expectedY)
        {
            Assert.IsTrue(partition.TryGetChunkId(tile, out GridChunkId chunk));
            Assert.AreEqual(new GridChunkId(expectedX, expectedY), chunk);
        }

        private static void SetPrivateField<T>(
            object target,
            string fieldName,
            T value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, fieldName);
            Assert.NotNull(value, fieldName);
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(
            object target,
            string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, fieldName);
            return (T)field.GetValue(target);
        }
    }
}
