using System;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class ResearchSaveData
    {
        public string[] UnlockedResearchIds;
        public string[] PurchasedUpgradeIds;
        public string ActiveResearchId;
        public long ResearchCompletionGameHour;
    }
}
