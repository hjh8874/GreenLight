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
        public JsonSaveRepository Repository { get; private set; }
        public ISaveClock Clock { get; private set; }
        public SaveSlotRepository SaveSlots { get; private set; }
        public bool IsRestoring { get; private set; }
        public bool IsSavingEnabled { get; private set; } = true;

        // 디버그 씬 등에서 라이브 세이브 쓰기를 명시적으로 끄고/켜기 위한 공개 훅.
        // (컴파일러 생성 백킹필드를 리플렉션으로 우회하면 필드명 변경·IL2CPP 스트리핑 시
        //  조용히 실패해 저장이 켜진 채로 남으므로, 명시적 API로 그 사일런트 실패를 제거.)
        public void SetSavingEnabled(bool enabled) => IsSavingEnabled = enabled;

        private WeeklySettlementSaveData retainedWeeklySettlement;
        private ResearchSaveData retainedResearch;
        private ProgressionSaveData retainedProgression;
        private RadioSaveData retainedRadio;
        private bool hasLoadedSave;

        public event Action<RestoreCompletedEvent> RestoreCompleted;
        public event Action<SaveSlotMetadata> AutomaticSaveSlotCreated;

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
            SaveSlots = new SaveSlotRepository();
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

        public bool Save(bool createAutomaticSlot = false)
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

                if (createAutomaticSlot
                    && SaveSlots.TryCreateAutomatic(saveData, out SaveSlotMetadata metadata))
                {
                    AutomaticSaveSlotCreated?.Invoke(metadata);
                }
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

            return TryRestoreLoadedSnapshot(saveData);
        }

        public bool TryCreateManualSave(
            string displayName,
            byte[] previewPng,
            out SaveSlotMetadata metadata)
        {
            metadata = null;

            if (!IsSavingEnabled)
            {
                Debug.LogWarning("Manual save skipped because saving is disabled.");
                return false;
            }

            return SaveSlots.TryCreateManual(
                CreateSnapshot(),
                displayName,
                previewPng,
                out metadata);
        }

        public bool TryLoadSaveSlot(string slotId)
        {
            return SaveSlots.TryLoad(slotId, out GameSaveData saveData)
                && TryRestoreLoadedSnapshot(saveData);
        }

        public bool TryDeleteSaveSlot(string slotId)
        {
            return SaveSlots.TryDelete(slotId);
        }

        private bool TryRestoreLoadedSnapshot(GameSaveData saveData)
        {
            if (saveData == null)
            {
                Debug.LogWarning("Save restore skipped because save data is null.");
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
            try
            {
                RestoreSnapshot(saveData);
            }
            finally
            {
                IsRestoring = false;
            }

            RestoreCompleted?.Invoke(new RestoreCompletedEvent(0.0, false));

            Debug.Log("Game save loaded and restored.");
            return true;
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
