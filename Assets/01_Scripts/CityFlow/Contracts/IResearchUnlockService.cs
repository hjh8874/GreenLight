using System;

namespace CityFlow.Contracts
{
    public interface IResearchUnlockService
    {
        int UnlockedCount { get; }

        event Action<string> ResearchUnlocked;
        event Action ResearchStateRestored;

        bool IsUnlocked(string researchId);
        bool TryUnlock(string researchId);
    }
}
