using System;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class ProgressionSaveData
    {
        public int CurrentStage;
        public string[] CompletedObjectiveIds;
        public bool TutorialCompleted;
        public bool HasQuestProgress;
        public bool HasHarvested;
        public long LifetimeDeliveredTotal;
    }
}
