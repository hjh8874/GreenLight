using CityFlow.Contracts.Save;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Save
{
    public sealed class SaveService
    {
        public ISimSaveSource SimSaveSource { get; private set; }
        public IEconomySaveSource EconomySaveSource { get; private set; }
        public IResearchSaveSource ResearchSaveSource { get; private set; }
        public IProgressionSaveSource ProgressionSaveSource { get; private set; }
        public IGameCalendarSaveSource GameCalendarSaveSource { get; private set; }
        public IOfflineCalendarProgressionSource OfflineCalendarProgressionSource { get; private set; }
        public JsonSaveRepository Repository { get; private set; }
        public ISaveClock Clock { get; private set; }
        public bool IsRestoring { get; private set; }
        public bool IsSavingEnabled { get; private set; } = true;

        public SaveService(
            ISimSaveSource simSaveSource,
            JsonSaveRepository repository,
            ISaveClock clock,
            IEconomySaveSource economySaveSource = null,
            IResearchSaveSource researchSaveSource = null,
            IProgressionSaveSource progressionSaveSource = null)
        {
            SimSaveSource = simSaveSource;
            Repository = repository ?? new JsonSaveRepository();
            Clock = clock ?? new SystemSaveClock();
            EconomySaveSource = economySaveSource;
            ResearchSaveSource = researchSaveSource;
            ProgressionSaveSource = progressionSaveSource;
        }

        public void RegisterEconomySaveSource(IEconomySaveSource economySaveSource)
        {
            EconomySaveSource = economySaveSource;
        }

        public void RegisterGameCalendarSaveSource(IGameCalendarSaveSource gameCalendarSaveSource)
        {
            GameCalendarSaveSource = gameCalendarSaveSource;
            OfflineCalendarProgressionSource = gameCalendarSaveSource as IOfflineCalendarProgressionSource;
        }

        public GameSaveData CreateSnapshot()
        {
            return new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                SavedAtUtcTicks = Clock.UtcNow.Ticks,
                GridWidth = SimSaveSource?.GridWidth ?? 0,
                GridHeight = SimSaveSource?.GridHeight ?? 0,
                Simulation = SimSaveSource?.CreateSnapshot(),
                Economy = EconomySaveSource?.CreateSnapshot(),
                Research = ResearchSaveSource?.CreateSnapshot(),
                Progression = ProgressionSaveSource?.CreateSnapshot(),
                Calendar = GameCalendarSaveSource?.CreateSnapshot()
            };
        }

        public void RestoreSnapshot(GameSaveData saveData)
        {
            if (saveData == null)
            {
                Debug.LogWarning("Save restore skipped because save data is null.");
                return;
            }

            if (saveData.SaveVersion != SaveConstants.CurrentSaveVersion)
            {
                Debug.LogWarning(
                    $"Save restore skipped because version {saveData.SaveVersion} is not supported. Current version is {SaveConstants.CurrentSaveVersion}.");
                return;
            }

            if (saveData.Simulation != null)
            {
                SimSaveSource?.RestoreSnapshot(saveData.Simulation);
            }

            if (saveData.Economy != null)
            {
                EconomySaveSource?.RestoreSnapshot(saveData.Economy);
            }

            if (saveData.Research != null)
            {
                ResearchSaveSource?.RestoreSnapshot(saveData.Research);
            }

            if (saveData.Progression != null)
            {
                ProgressionSaveSource?.RestoreSnapshot(saveData.Progression);
            }

            if (saveData.Calendar != null)
            {
                GameCalendarSaveSource?.RestoreSnapshot(saveData.Calendar);
            }
        }

        public bool Save()
        {
            if (!IsSavingEnabled)
            {
                Debug.Log("Game save skipped because saving is disabled for the current session.");
                return false;
            }

            GameSaveData saveData = CreateSnapshot();
            return Repository.TrySave(saveData);
        }

        public bool DeleteSaveAndSuspend()
        {
            try
            {
                Repository.DeleteSave();
                IsSavingEnabled = false;
                Debug.Log("Game save data deleted. Saving is disabled until the next game session.");
                return true;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"Game save data could not be deleted.\n{exception.Message}");
                return false;
            }
        }

        public bool TryLoadAndRestore()
        {
            if (!Repository.TryLoad(out GameSaveData saveData))
            {
                Debug.LogWarning("Save restore skipped because no save data could be loaded.");
                return false;
            }

            if (saveData.SaveVersion != SaveConstants.CurrentSaveVersion)
            {
                Debug.LogWarning(
                    $"Save restore skipped because version {saveData.SaveVersion} is not supported. Current version is {SaveConstants.CurrentSaveVersion}.");
                return false;
            }

            IsRestoring = true;

            try
            {
                RestoreSnapshot(saveData);
                SettleOfflineProgress(saveData);
            }
            finally
            {
                IsRestoring = false;
            }

            Debug.Log("Game save loaded and restored.");
            return true;
        }

        private void SettleOfflineProgress(GameSaveData saveData)
        {
            if (saveData == null || saveData.SavedAtUtcTicks <= 0L)
            {
                return;
            }

            if (!(SimSaveSource is IOfflineSettlementSource offlineSettlementSource))
            {
                return;
            }

            System.DateTime savedAtUtc;

            try
            {
                savedAtUtc = new System.DateTime(saveData.SavedAtUtcTicks, System.DateTimeKind.Utc);
            }
            catch (System.ArgumentOutOfRangeException)
            {
                Debug.LogWarning("Offline settlement skipped because saved UTC ticks are invalid.");
                return;
            }

            double elapsedSeconds = (Clock.UtcNow - savedAtUtc).TotalSeconds;

            if (elapsedSeconds <= 0.0)
            {
                return;
            }

            double settledSeconds = offlineSettlementSource.SettleOffline(elapsedSeconds);
            OfflineCalendarProgressionSource?.AdvanceOffline(settledSeconds);

            bool savedAfterSettlement = Save();
            Debug.Log(savedAfterSettlement
                ? $"Offline settlement completed and saved for {settledSeconds:0.##} of {elapsedSeconds:0.##} elapsed seconds."
                : $"Offline settlement completed for {settledSeconds:0.##} of {elapsedSeconds:0.##} elapsed seconds, but the updated save could not be written.");
        }
    }
}
