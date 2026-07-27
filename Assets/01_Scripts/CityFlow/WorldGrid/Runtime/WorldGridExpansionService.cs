using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.WorldGrid
{
    [DisallowMultipleComponent]
    public sealed class WorldGridExpansionService :
        MonoBehaviour,
        ICityFlowServiceConsumer,
        IWorldGridExpansionService
    {
        [SerializeField] private WorldGridService worldGrid;
        [SerializeField] private WorldGridUnlockProfileSO profile;

#if UNITY_EDITOR
        [Header("Play Mode Test")]
        [SerializeField, Min(0)] private int testStageIndex = 1;
#endif

        private bool initialized;
        private int currentStageIndex = -1;

        public WorldGridService WorldGrid => worldGrid;
        public WorldGridUnlockProfileSO Profile => profile;
        public int CurrentStageIndex => currentStageIndex;
        public string CurrentStageId =>
            TryGetCurrentStage(out WorldGridUnlockStage stage)
                ? stage.StageId
                : string.Empty;
        public bool CanUnlockNextStage =>
            initialized &&
            profile != null &&
            currentStageIndex + 1 < profile.StageCount;

        public event Action<WorldGridStageChangedEvent> StageChanged;

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled || initialized)
            {
                return;
            }

            if (services == null || worldGrid == null || profile == null)
            {
                Debug.LogWarning(
                    "[WorldGridExpansionService] Required references are missing.",
                    this);
                return;
            }

            worldGrid.Initialize(services);
            if (!ReferenceEquals(services.WorldGrid, worldGrid) ||
                !ValidateProfile())
            {
                Debug.LogWarning(
                    "[WorldGridExpansionService] World grid registration or " +
                    "unlock profile validation failed.",
                    this);
                return;
            }

            if (!services.RegisterWorldGridExpansion(this))
            {
                Debug.LogWarning(
                    "[WorldGridExpansionService] Another expansion service " +
                    "is already registered.",
                    this);
                return;
            }

            worldGrid.AccessRestored += OnAccessRestored;
            initialized = true;
            EnsureInitialStage();
            SynchronizeCurrentStage();

            Debug.Log(
                $"[WorldGridExpansionService] Ready at stage " +
                $"{CurrentStageId} ({CurrentStageIndex}).",
                this);
        }

        public bool TryUnlockNextStage()
        {
            return CanUnlockNextStage &&
                   TryApplyStage(
                       currentStageIndex + 1,
                       publishEvent: true);
        }

        public bool TryUnlockStage(string stageId)
        {
            if (!initialized || profile == null)
            {
                return false;
            }

            int stageIndex = profile.FindStageIndex(stageId);
            return stageIndex > currentStageIndex &&
                   TryApplyStage(stageIndex, publishEvent: true);
        }

        public bool TryResetToInitialStage()
        {
            if (!initialized || worldGrid == null)
            {
                return false;
            }

            worldGrid.RestoreSnapshot(null);
            return currentStageIndex == 0 && IsStageFullyUnlocked(0);
        }

#if UNITY_EDITOR
        [ContextMenu("Play Mode Test/Unlock Next Stage")]
        private void UnlockNextStageForPlayModeTest()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning(
                    "[WorldGridExpansionService] Enter Play Mode before " +
                    "using the test unlock command.",
                    this);
                return;
            }

            if (!TryUnlockNextStage())
            {
                Debug.LogWarning(
                    "[WorldGridExpansionService] The next stage could not " +
                    "be unlocked.",
                    this);
            }
        }

        [ContextMenu("Play Mode Test/Unlock Selected Stage")]
        private void UnlockSelectedStageForPlayModeTest()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning(
                    "[WorldGridExpansionService] Enter Play Mode before " +
                    "using the test unlock command.",
                    this);
                return;
            }

            if (profile == null ||
                !profile.TryGetStage(
                    testStageIndex,
                    out WorldGridUnlockStage stage) ||
                !TryUnlockStage(stage.StageId))
            {
                Debug.LogWarning(
                    $"[WorldGridExpansionService] Test stage index " +
                    $"{testStageIndex} could not be unlocked.",
                    this);
            }
        }

        [ContextMenu("Play Mode Test/Reset To Initial Stage")]
        private void ResetToInitialStageForPlayModeTest()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning(
                    "[WorldGridExpansionService] Enter Play Mode before " +
                    "using the test reset command.",
                    this);
                return;
            }

            if (!TryResetToInitialStage())
            {
                Debug.LogWarning(
                    "[WorldGridExpansionService] The world grid could not " +
                    "be reset to its initial stage.",
                    this);
                return;
            }

            Debug.Log(
                "[WorldGridExpansionService] Reset to initial stage " +
                $"{CurrentStageId} ({CurrentStageIndex}).",
                this);
        }
#endif

        private void OnDestroy()
        {
            if (worldGrid != null)
            {
                worldGrid.AccessRestored -= OnAccessRestored;
            }
        }

        private void EnsureInitialStage()
        {
            if (profile.StageCount > 0 &&
                !IsStageFullyUnlocked(0))
            {
                TryApplyStage(0, publishEvent: false);
            }
        }

        private bool TryApplyStage(int stageIndex, bool publishEvent)
        {
            if (!TryGetStageBounds(
                    stageIndex,
                    out WorldGridUnlockStage stage,
                    out RectInt bounds))
            {
                return false;
            }

            Vector2Int firstTile = new Vector2Int(
                bounds.xMin,
                bounds.yMin);
            Vector2Int lastTile = new Vector2Int(
                bounds.xMax - 1,
                bounds.yMax - 1);
            if (!worldGrid.TryGetChunkId(firstTile, out GridChunkId first) ||
                !worldGrid.TryGetChunkId(lastTile, out GridChunkId last))
            {
                return false;
            }

            for (int chunkY = first.Y; chunkY <= last.Y; chunkY++)
            {
                for (int chunkX = first.X; chunkX <= last.X; chunkX++)
                {
                    GridChunkId chunk = new GridChunkId(chunkX, chunkY);
                    if (!worldGrid.IsChunkUnlocked(chunk))
                    {
                        worldGrid.TryUnlockChunk(chunk);
                    }
                }
            }

            currentStageIndex = stageIndex;
            if (publishEvent)
            {
                StageChanged?.Invoke(new WorldGridStageChangedEvent(
                    stage.StageId,
                    stageIndex,
                    bounds,
                    WorldGridStageChangeReason.Unlocked));
            }

            Debug.Log(
                $"[WorldGridExpansionService] Applied stage {stage.StageId} " +
                $"with bounds {bounds}.",
                this);
            return true;
        }

        private void OnAccessRestored()
        {
            EnsureInitialStage();
            SynchronizeCurrentStage();
            if (TryGetStageBounds(
                    currentStageIndex,
                    out WorldGridUnlockStage stage,
                    out RectInt bounds))
            {
                StageChanged?.Invoke(new WorldGridStageChangedEvent(
                    stage.StageId,
                    currentStageIndex,
                    bounds,
                    WorldGridStageChangeReason.Restored));
            }
        }

        private void SynchronizeCurrentStage()
        {
            currentStageIndex = -1;
            for (int stageIndex = 0;
                 stageIndex < profile.StageCount;
                 stageIndex++)
            {
                if (!IsStageFullyUnlocked(stageIndex))
                {
                    break;
                }

                currentStageIndex = stageIndex;
            }
        }

        private bool IsStageFullyUnlocked(int stageIndex)
        {
            return TryGetStageBounds(
                       stageIndex,
                       out _,
                       out RectInt bounds) &&
                   worldGrid.IsAreaUnlocked(
                       new Vector2Int(bounds.xMin, bounds.yMin),
                       new Vector2Int(bounds.width, bounds.height));
        }

        private bool TryGetCurrentStage(out WorldGridUnlockStage stage)
        {
            stage = null;
            return profile != null &&
                   profile.TryGetStage(currentStageIndex, out stage);
        }

        private bool TryGetStageBounds(
            int stageIndex,
            out WorldGridUnlockStage stage,
            out RectInt bounds)
        {
            stage = null;
            bounds = default;
            if (worldGrid == null ||
                profile == null ||
                !profile.TryGetStage(stageIndex, out stage))
            {
                return false;
            }

            int width = stage.UnlockedWidth;
            int height = stage.UnlockedHeight;
            if (width > worldGrid.WorldWidth ||
                height > worldGrid.WorldHeight ||
                width % worldGrid.ChunkSize != 0 ||
                height % worldGrid.ChunkSize != 0 ||
                (worldGrid.WorldWidth - width) % 2 != 0 ||
                (worldGrid.WorldHeight - height) % 2 != 0)
            {
                return false;
            }

            int minX = (worldGrid.WorldWidth - width) / 2;
            int minY = (worldGrid.WorldHeight - height) / 2;
            if (minX % worldGrid.ChunkSize != 0 ||
                minY % worldGrid.ChunkSize != 0)
            {
                return false;
            }

            bounds = new RectInt(minX, minY, width, height);
            return true;
        }

        private bool ValidateProfile()
        {
            if (profile.StageCount == 0)
            {
                return false;
            }

            var stageIds = new HashSet<string>();
            int previousWidth = 0;
            int previousHeight = 0;
            for (int stageIndex = 0;
                 stageIndex < profile.StageCount;
                 stageIndex++)
            {
                if (!TryGetStageBounds(
                        stageIndex,
                        out WorldGridUnlockStage stage,
                        out _) ||
                    string.IsNullOrWhiteSpace(stage.StageId) ||
                    !stageIds.Add(stage.StageId) ||
                    stage.UnlockedWidth <= previousWidth ||
                    stage.UnlockedHeight <= previousHeight)
                {
                    return false;
                }

                previousWidth = stage.UnlockedWidth;
                previousHeight = stage.UnlockedHeight;
            }

            return true;
        }
    }
}
