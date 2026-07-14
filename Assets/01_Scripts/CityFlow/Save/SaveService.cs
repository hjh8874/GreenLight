using System;
using CityFlow.Contracts.Save;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Save
{
    public sealed class SaveService
    {
        public ISimSaveSource SimSaveSource { get; private set; }
        public IEconomySaveSource EconomySaveSource { get; private set; }
        public IWeeklySettlementSaveSource WeeklySettlementSaveSource { get; private set; }
        public IResearchSaveSource ResearchSaveSource { get; private set; }
        public IProgressionSaveSource ProgressionSaveSource { get; private set; }
        public IGameCalendarSaveSource GameCalendarSaveSource { get; private set; }
        public IRadioSaveSource RadioSaveSource { get; private set; }
        public IOfflineCalendarProgressionSource OfflineCalendarProgressionSource { get; private set; }
        public JsonSaveRepository Repository { get; private set; }
        public ISaveClock Clock { get; private set; }
        public bool IsRestoring { get; private set; }
        public bool IsSavingEnabled { get; private set; } = true;

        private WeeklySettlementSaveData retainedWeeklySettlement;
        private ResearchSaveData retainedResearch;
        private ProgressionSaveData retainedProgression;
        private RadioSaveData retainedRadio;
        private bool hasLoadedSave;

        public event Action<RestoreCompletedEvent> RestoreCompleted;
        public event Action<OfflineSettlementCompletedEvent> OfflineSettlementCompleted;

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

        public void RegisterWeeklySettlementSaveSource(
            IWeeklySettlementSaveSource weeklySettlementSaveSource)
        {
            WeeklySettlementSaveSource = weeklySettlementSaveSource;

            if (hasLoadedSave)
            {
                WeeklySettlementSaveSource?.RestoreSnapshot(
                    retainedWeeklySettlement ?? new WeeklySettlementSaveData());
            }
        }

        public void RegisterResearchSaveSource(IResearchSaveSource researchSaveSource)
        {
            ResearchSaveSource = researchSaveSource;

            if (hasLoadedSave)
            {
                ResearchSaveSource?.RestoreSnapshot(
                    retainedResearch ?? new ResearchSaveData());
            }
        }

        public void RegisterProgressionSaveSource(IProgressionSaveSource progressionSaveSource)
        {
            ProgressionSaveSource = progressionSaveSource;

            if (hasLoadedSave)
            {
                ProgressionSaveSource?.RestoreSnapshot(
                    retainedProgression ?? new ProgressionSaveData());
            }
        }

        public void RegisterGameCalendarSaveSource(IGameCalendarSaveSource gameCalendarSaveSource)
        {
            GameCalendarSaveSource = gameCalendarSaveSource;
            OfflineCalendarProgressionSource = gameCalendarSaveSource as IOfflineCalendarProgressionSource;
        }

        public void RegisterRadioSaveSource(IRadioSaveSource radioSaveSource)
        {
            RadioSaveSource = radioSaveSource;

            if (hasLoadedSave)
            {
                RadioSaveSource?.RestoreSnapshot(
                    retainedRadio ?? CreateEmptyRadioSaveData());
            }
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
                WeeklySettlement = WeeklySettlementSaveSource?.CreateSnapshot()
                    ?? retainedWeeklySettlement,
                Research = ResearchSaveSource?.CreateSnapshot()
                    ?? retainedResearch,
                Progression = ProgressionSaveSource?.CreateSnapshot()
                    ?? retainedProgression,
                Calendar = GameCalendarSaveSource?.CreateSnapshot(),
                Radio = RadioSaveSource?.CreateSnapshot()
                    ?? retainedRadio
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

            if (WeeklySettlementSaveSource != null)
            {
                WeeklySettlementSaveSource.RestoreSnapshot(
                    saveData.WeeklySettlement ?? new WeeklySettlementSaveData());
            }

            if (ResearchSaveSource != null)
            {
                ResearchSaveSource.RestoreSnapshot(
                    saveData.Research ?? new ResearchSaveData());
            }

            if (ProgressionSaveSource != null)
            {
                ProgressionSaveSource.RestoreSnapshot(
                    saveData.Progression ?? new ProgressionSaveData());
            }

            if (saveData.Calendar != null)
            {
                GameCalendarSaveSource?.RestoreSnapshot(saveData.Calendar);
            }

            if (RadioSaveSource != null)
            {
                RadioSaveSource.RestoreSnapshot(
                    saveData.Radio ?? CreateEmptyRadioSaveData());
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
            bool saved = Repository.TrySave(saveData);

            if (saved)
            {
                RetainOptionalSections(saveData);
            }

            return saved;
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

            RetainOptionalSections(saveData);
            hasLoadedSave = true;
            IsRestoring = true;
            double settledOfflineSeconds;
            GameSaveData restoredSnapshot;

            try
            {
                RestoreSnapshot(saveData);
                restoredSnapshot = CreateSnapshot();
                settledOfflineSeconds = SettleOfflineProgress(saveData);
            }
            finally
            {
                IsRestoring = false;
            }

            RestoreCompleted?.Invoke(
                new RestoreCompletedEvent(settledOfflineSeconds));

            if (settledOfflineSeconds > 0.0)
            {
                OfflineSettlementCompleted?.Invoke(
                    CreateOfflineSettlementSummary(
                        restoredSnapshot,
                        CreateSnapshot(),
                        settledOfflineSeconds));
            }

            Debug.Log("Game save loaded and restored.");
            return true;
        }

        private static OfflineSettlementCompletedEvent CreateOfflineSettlementSummary(
            GameSaveData beforeSettlement,
            GameSaveData afterSettlement,
            double settledOfflineSeconds)
        {
            long initialCoins = Math.Max(0L, beforeSettlement?.Economy?.Coins ?? 0L);
            long currentCoins = Math.Max(0L, afterSettlement?.Economy?.Coins ?? initialCoins);
            decimal totalBefore = initialCoins +
                Math.Max(0L, beforeSettlement?.WeeklySettlement?.PendingCoins ?? 0L);
            decimal totalAfter = currentCoins +
                Math.Max(0L, afterSettlement?.WeeklySettlement?.PendingCoins ?? 0L);
            decimal earnedDifference = Math.Max(0m, totalAfter - totalBefore);
            long earnedCoins = earnedDifference >= long.MaxValue
                ? long.MaxValue
                : (long)earnedDifference;

            return new OfflineSettlementCompletedEvent(
                settledOfflineSeconds,
                initialCoins,
                earnedCoins,
                currentCoins);
        }

        private double SettleOfflineProgress(GameSaveData saveData)
        {
            if (saveData == null || saveData.SavedAtUtcTicks <= 0L)
            {
                return 0.0;
            }

            if (!(SimSaveSource is IOfflineSettlementSource offlineSettlementSource))
            {
                return 0.0;
            }

            System.DateTime savedAtUtc;

            try
            {
                savedAtUtc = new System.DateTime(saveData.SavedAtUtcTicks, System.DateTimeKind.Utc);
            }
            catch (System.ArgumentOutOfRangeException)
            {
                Debug.LogWarning("Offline settlement skipped because saved UTC ticks are invalid.");
                return 0.0;
            }

            double elapsedSeconds = (Clock.UtcNow - savedAtUtc).TotalSeconds;

            if (elapsedSeconds <= 0.0)
            {
                return 0.0;
            }

            double settledSeconds = offlineSettlementSource.SettleOffline(elapsedSeconds);
            OfflineCalendarProgressionSource?.AdvanceOffline(settledSeconds);

            bool savedAfterSettlement = Save();
            Debug.Log(savedAfterSettlement
                ? $"Offline settlement completed and saved for {settledSeconds:0.##} of {elapsedSeconds:0.##} elapsed seconds."
                : $"Offline settlement completed for {settledSeconds:0.##} of {elapsedSeconds:0.##} elapsed seconds, but the updated save could not be written.");

            return settledSeconds;
        }

        private void RetainOptionalSections(GameSaveData saveData)
        {
            retainedWeeklySettlement = saveData?.WeeklySettlement;
            retainedResearch = saveData?.Research;
            retainedProgression = saveData?.Progression;
            retainedRadio = saveData?.Radio;
        }

        private static RadioSaveData CreateEmptyRadioSaveData()
        {
            return new RadioSaveData
            {
                Slots = Array.Empty<RadioSlotSaveData>(),
                CurrentSlotIndex = -1
            };
        }
    }
}
