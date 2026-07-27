using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.Save;
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
        public void State_InitiallyUnlocksOnlyFirstTwentyByTwentyChunk()
        {
            var state = CreateDefaultState();

            Assert.IsTrue(state.IsTileUnlocked(new Vector2Int(0, 0)));
            Assert.IsTrue(state.IsTileUnlocked(new Vector2Int(19, 19)));
            Assert.IsFalse(state.IsTileUnlocked(new Vector2Int(20, 0)));
            CollectionAssert.AreEqual(
                new[] { 0 },
                state.CreateSnapshot().UnlockedChunkIndices);
        }

        [Test]
        public void AreaUnlock_RejectsFootprintCrossingLockedChunk()
        {
            var state = CreateDefaultState();
            Vector2Int anchor = new Vector2Int(19, 5);
            Vector2Int footprint = new Vector2Int(2, 1);

            Assert.IsFalse(state.IsAreaUnlocked(anchor, footprint));
            Assert.IsTrue(state.TryUnlockChunk(new GridChunkId(1, 0)));
            Assert.IsTrue(state.IsAreaUnlocked(anchor, footprint));
        }

        [Test]
        public void Unlock_IsIdempotentAndSnapshotIsDeterministic()
        {
            var state = CreateDefaultState();

            Assert.IsTrue(state.TryUnlockChunk(new GridChunkId(1, 0)));
            Assert.IsFalse(state.TryUnlockChunk(new GridChunkId(1, 0)));
            Assert.IsTrue(state.TryUnlockChunk(new GridChunkId(0, 1)));

            CollectionAssert.AreEqual(
                new[] { 0, 1, 10 },
                state.CreateSnapshot().UnlockedChunkIndices);
        }

        [Test]
        public void Restore_IgnoresInvalidIndicesAndRejectsDimensionMismatch()
        {
            var state = CreateDefaultState();

            Assert.IsTrue(state.RestoreSnapshot(new WorldGridSaveData
            {
                WorldWidth = 200,
                WorldHeight = 200,
                ChunkSize = 20,
                UnlockedChunkIndices = new[] { -1, 2, 2, 99, 100 }
            }));
            CollectionAssert.AreEqual(
                new[] { 2, 99 },
                state.CreateSnapshot().UnlockedChunkIndices);

            Assert.IsFalse(state.RestoreSnapshot(new WorldGridSaveData
            {
                WorldWidth = 20,
                WorldHeight = 20,
                ChunkSize = 20,
                UnlockedChunkIndices = new[] { 0 }
            }));
            CollectionAssert.AreEqual(
                new[] { 0 },
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
                GameObject prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                Assert.NotNull(config);
                Assert.NotNull(prefab);
                Assert.AreEqual(200, config.WorldWidth);
                Assert.AreEqual(200, config.WorldHeight);
                Assert.AreEqual(20, config.ChunkSize);

                instance = Object.Instantiate(prefab);
                WorldGridService worldGrid =
                    instance.GetComponent<WorldGridService>();
                Assert.NotNull(worldGrid);
                Assert.AreSame(config, worldGrid.Config);

                var save = new SaveService(null, null, null);
                var services = new CityFlowServices(
                    new SimEventHub(),
                    null,
                    null,
                    save);
                worldGrid.Initialize(services);

                Assert.AreSame(worldGrid, services.WorldGrid);
                Assert.AreSame(worldGrid, save.WorldGridSaveSource);
                Assert.IsTrue(
                    worldGrid.IsTileUnlocked(new Vector2Int(19, 19)));
                Assert.IsFalse(
                    worldGrid.IsTileUnlocked(new Vector2Int(20, 0)));

                int unlockEventCount = 0;
                worldGrid.ChunkUnlocked += _ => unlockEventCount++;
                Assert.IsTrue(
                    services.WorldGrid.TryUnlockChunk(
                        new GridChunkId(1, 0)));
                Assert.IsFalse(
                    services.WorldGrid.TryUnlockChunk(
                        new GridChunkId(1, 0)));
                Assert.AreEqual(1, unlockEventCount);
                Assert.IsTrue(
                    worldGrid.IsTileUnlocked(new Vector2Int(20, 0)));
            }
            finally
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        private static WorldGridState CreateDefaultState()
        {
            return new WorldGridState(
                new GridChunkPartition(200, 200, 20),
                initialUnlockedColumns: 1,
                initialUnlockedRows: 1);
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
    }
}
