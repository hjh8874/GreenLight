using UnityEngine;

namespace CityFlow.Contracts
{
    public enum WorldGridStageChangeReason
    {
        Unlocked = 0,
        Restored = 1
    }

    public readonly struct WorldGridStageChangedEvent
    {
        public WorldGridStageChangedEvent(
            string stageId,
            int stageIndex,
            RectInt unlockedBounds,
            WorldGridStageChangeReason reason)
        {
            StageId = stageId;
            StageIndex = stageIndex;
            UnlockedBounds = unlockedBounds;
            Reason = reason;
        }

        public string StageId { get; }
        public int StageIndex { get; }
        public RectInt UnlockedBounds { get; }
        public WorldGridStageChangeReason Reason { get; }
    }
}
