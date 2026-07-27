using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.WorldGrid
{
    [CreateAssetMenu(
        fileName = "WorldGridUnlockProfile",
        menuName = "CityFlow/World Grid Unlock Profile")]
    public sealed class WorldGridUnlockProfileSO : ScriptableObject
    {
        [SerializeField] private List<WorldGridUnlockStage> stages = new();

        public IReadOnlyList<WorldGridUnlockStage> Stages => stages;
        public int StageCount => stages?.Count ?? 0;

        public bool TryGetStage(
            int stageIndex,
            out WorldGridUnlockStage stage)
        {
            stage = null;
            if (stages == null ||
                stageIndex < 0 ||
                stageIndex >= stages.Count)
            {
                return false;
            }

            stage = stages[stageIndex];
            return stage != null;
        }

        public int FindStageIndex(string stageId)
        {
            if (string.IsNullOrWhiteSpace(stageId) || stages == null)
            {
                return -1;
            }

            for (int index = 0; index < stages.Count; index++)
            {
                WorldGridUnlockStage stage = stages[index];
                if (stage != null && stage.StageId == stageId)
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
