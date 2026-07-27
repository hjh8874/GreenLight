using System;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.WorldGrid
{
    [DisallowMultipleComponent]
    public sealed class WorldGridService :
        MonoBehaviour,
        ICityFlowServiceConsumer,
        IWorldGridService,
        IWorldGridSaveSource
    {
        [SerializeField] private WorldGridConfigSO config;

        private WorldGridState state;
        private bool initialized;

        public WorldGridConfigSO Config => config;
        public int WorldWidth => EnsureState() ? state.Partition.WorldWidth : 0;
        public int WorldHeight => EnsureState() ? state.Partition.WorldHeight : 0;
        public int ChunkSize => EnsureState() ? state.Partition.ChunkSize : 0;
        public int ChunkColumns => EnsureState() ? state.Partition.ChunkColumns : 0;
        public int ChunkRows => EnsureState() ? state.Partition.ChunkRows : 0;

        public event Action<GridChunkId> ChunkUnlocked;

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled || initialized)
            {
                return;
            }

            if (services == null || !EnsureState())
            {
                Debug.LogWarning(
                    "[WorldGridService] Services or configuration is missing. " +
                    "World grid registration was skipped.",
                    this);
                return;
            }

            if (!services.RegisterWorldGrid(this))
            {
                Debug.LogWarning(
                    "[WorldGridService] Another world grid service is already registered.",
                    this);
                return;
            }

            initialized = true;
            Debug.Log(
                $"[WorldGridService] Registered {WorldWidth}x{WorldHeight} world " +
                $"as {ChunkColumns}x{ChunkRows} chunks of {ChunkSize} tiles.",
                this);
        }

        public bool IsInsideWorld(Vector2Int tile) =>
            EnsureState() && state.Partition.IsInsideWorld(tile);

        public bool IsTileUnlocked(Vector2Int tile) =>
            EnsureState() && state.IsTileUnlocked(tile);

        public bool IsChunkUnlocked(GridChunkId chunk) =>
            EnsureState() && state.IsChunkUnlocked(chunk);

        public bool IsAreaUnlocked(Vector2Int anchor, Vector2Int footprint) =>
            EnsureState() && state.IsAreaUnlocked(anchor, footprint);

        public bool TryGetChunkId(Vector2Int tile, out GridChunkId chunk)
        {
            chunk = default;
            return EnsureState() &&
                   state.Partition.TryGetChunkId(tile, out chunk);
        }

        public bool TryUnlockChunk(GridChunkId chunk)
        {
            if (!EnsureState() || !state.TryUnlockChunk(chunk))
            {
                return false;
            }

            Debug.Log($"[WorldGridService] Unlocked chunk {chunk}.", this);
            ChunkUnlocked?.Invoke(chunk);
            return true;
        }

        public WorldGridSaveData CreateSnapshot()
        {
            return EnsureState()
                ? state.CreateSnapshot()
                : new WorldGridSaveData();
        }

        public void RestoreSnapshot(WorldGridSaveData snapshot)
        {
            if (!EnsureState())
            {
                return;
            }

            if (!state.RestoreSnapshot(snapshot))
            {
                Debug.LogWarning(
                    "[WorldGridService] Saved world dimensions do not match " +
                    "the current configuration. Initial access was restored.",
                    this);
            }
        }

        private bool EnsureState()
        {
            if (state != null)
            {
                return true;
            }

            if (config == null)
            {
                return false;
            }

            var partition = new GridChunkPartition(
                config.WorldWidth,
                config.WorldHeight,
                config.ChunkSize);
            state = new WorldGridState(
                partition,
                config.InitialUnlockedColumns,
                config.InitialUnlockedRows);
            return true;
        }
    }
}
