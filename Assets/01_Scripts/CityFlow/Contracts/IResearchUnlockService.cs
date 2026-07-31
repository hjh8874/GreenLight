using System;

namespace CityFlow.Contracts
{
    public interface IResearchUnlockService
    {
        int UnlockedCount { get; }
        string ActiveResearchId { get; }

        event Action<string> ResearchUnlocked;
        event Action ResearchProgressChanged;
        event Action ResearchStateRestored;

        bool IsUnlocked(string researchId);
        bool IsReady(string researchId);
        bool IsResearching(string researchId);
        int GetRemainingResearchHours(string researchId);
        bool TryStartResearch(string researchId);
        bool TryUnlock(string researchId);
    }
}
