using System;

namespace CityFlow.Contracts
{
    public interface IWorldGridExpansionService
    {
        int CurrentStageIndex { get; }
        string CurrentStageId { get; }
        bool CanUnlockNextStage { get; }

        event Action<WorldGridStageChangedEvent> StageChanged;

        bool TryUnlockNextStage();
        bool TryUnlockStage(string stageId);
        bool TryResetToInitialStage();
    }
}
