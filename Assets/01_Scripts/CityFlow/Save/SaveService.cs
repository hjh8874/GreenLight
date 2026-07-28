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
        public ITerrainDecorationSaveSource TerrainDecorationSaveSource { get; private set; }
        public IWorldGridSaveSource WorldGridSaveSource { get; private set; }
        public ISpecialBuildingSaveSource SpecialBuildingSaveSource { get; private set; }
        public ISpecialBuildingVisitSaveSource SpecialBuildingVisitSaveSource
        {
            get;
            private set;
        }
        public IOfflineSettlementSource OfflineSettlementSource { get; private set; }
        public IOfflineCalendarProgressionSource OfflineCalendarProgressionSource { get; private set; }
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
        private TerrainDecorationSaveData retainedTerrainDecorations;
        private WorldGridSaveData retainedWorldGrid;
        private SpecialBuildingSaveData retainedSpecialBuildings;
        private SpecialBuildingVisitSaveData retainedSpecialBuildingVisits;
        private bool hasLoadedSave;

        public event Action<RestoreCompletedEvent> RestoreCompleted;
        public event Action<OfflineSettlementCompletedEvent> OfflineSettlementCompleted;
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
            OfflineSettlementSource =
                weeklySettlementSaveSource as IOfflineSettlementSource;

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
            OfflineCalendarProgressionSource =
                gameCalendarSaveSource as IOfflineCalendarProgressionSource;
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

        public void RegisterTerrainDecorationSaveSource(
            ITerrainDecorationSaveSource terrainDecorationSaveSource)
        {
            TerrainDecorationSaveSource = terrainDecorationSaveSource;

            if (hasLoadedSave)
            {
                TerrainDecorationSaveSource?.RestoreSnapshot(
                    retainedTerrainDecorations ??
                    CreateEmptyTerrainDecorationSaveData());
            }
        }

        public void RegisterWorldGridSaveSource(
            IWorldGridSaveSource worldGridSaveSource)
        {
            WorldGridSaveSource = worldGridSaveSource;

            if (hasLoadedSave)
            {
                WorldGridSaveSource?.RestoreSnapshot(retainedWorldGrid);
            }
        }

        public void RegisterSpecialBuildingSaveSource(
            ISpecialBuildingSaveSource specialBuildingSaveSource)
        {
            SpecialBuildingSaveSource = specialBuildingSaveSource;

            if (hasLoadedSave)
            {
                SpecialBuildingSaveSource?.RestoreSnapshot(
                    retainedSpecialBuildings ??
                    CreateEmptySpecialBuildingSaveData());
            }
        }

        public void RegisterSpecialBuildingVisitSaveSource(
            ISpecialBuildingVisitSaveSource specialBuildingVisitSaveSource)
        {
            SpecialBuildingVisitSaveSource = specialBuildingVisitSaveSource;

            if (hasLoadedSave)
            {
                SpecialBuildingVisitSaveSource?.RestoreSnapshot(
                    retainedSpecialBuildingVisits ??
                    CreateEmptySpecialBuildingVisitSaveData());
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
                    ?? retainedRadio,
                TerrainDecorations =
                    TerrainDecorationSaveSource?.CreateSnapshot()
                    ?? retainedTerrainDecorations,
                WorldGrid = WorldGridSaveSource?.CreateSnapshot()
                    ?? retainedWorldGrid,
                SpecialBuildings =
                    SpecialBuildingSaveSource?.CreateSnapshot()
                    ?? retainedSpecialBuildings,
                SpecialBuildingVisits =
                    SpecialBuildingVisitSaveSource?.CreateSnapshot()
                    ?? retainedSpecialBuildingVisits
            };
        }

        public void RestoreSnapshot(GameSaveData saveData)
        {
            if (saveData == null)
            {
                Debug.LogWarning("Save restore skipped because save data is null.");
                return;
            }

            if (!SaveConstants.IsSupportedSaveVersion(
                    saveData.SaveVersion))
            {
                Debug.LogWarning(
                    $"Save restore skipped because version " +
                    $"{saveData.SaveVersion} is not supported. " +
                    $"Supported versions: " +
                    $"{SaveConstants.MinimumSupportedSaveVersion}-" +
                    $"{SaveConstants.CurrentSaveVersion}.");
                return;
            }

            if (saveData.Simulation != null)
            {
                if (saveData.Simulation.GridWidth <= 0)
                {
                    saveData.Simulation.GridWidth = saveData.GridWidth;
                }

                if (saveData.Simulation.GridHeight <= 0)
                {
                    saveData.Simulation.GridHeight = saveData.GridHeight;
                }

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

            if (TerrainDecorationSaveSource != null)
            {
                TerrainDecorationSaveSource.RestoreSnapshot(
                    saveData.TerrainDecorations ??
                    CreateEmptyTerrainDecorationSaveData());
            }

            WorldGridSaveSource?.RestoreSnapshot(saveData.WorldGrid);

            if (SpecialBuildingSaveSource != null)
            {
                SpecialBuildingSaveSource.RestoreSnapshot(
                    saveData.SpecialBuildings ??
                    CreateEmptySpecialBuildingSaveData());
            }

            if (SpecialBuildingVisitSaveSource != null)
            {
                SpecialBuildingVisitSaveSource.RestoreSnapshot(
                    saveData.SpecialBuildingVisits ??
                    CreateEmptySpecialBuildingVisitSaveData());
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

            return TryRestoreLoadedSnapshot(
                saveData,
                includeOfflineProgression: true);
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
                && TryRestoreLoadedSnapshot(
                    saveData,
                    includeOfflineProgression: false);
        }

        public bool TryDeleteSaveSlot(string slotId)
        {
            return SaveSlots.TryDelete(slotId);
        }

        private bool TryRestoreLoadedSnapshot(
            GameSaveData saveData,
            bool includeOfflineProgression)
        {
            if (saveData == null)
            {
                Debug.LogWarning("Save restore skipped because save data is null.");
                return false;
            }

            if (!SaveConstants.IsSupportedSaveVersion(
                    saveData.SaveVersion))
            {
                Debug.LogWarning(
                    $"Save restore skipped because version " +
                    $"{saveData.SaveVersion} is not supported. " +
                    $"Supported versions: " +
                    $"{SaveConstants.MinimumSupportedSaveVersion}-" +
                    $"{SaveConstants.CurrentSaveVersion}.");
                return false;
            }

            RetainOptionalSections(saveData);
            hasLoadedSave = true;
            IsRestoring = true;
            double settledOfflineSeconds = 0.0;
            try
            {
                RestoreSnapshot(saveData);

                if (includeOfflineProgression)
                {
                    settledOfflineSeconds =
                        SettleOfflineProgress(saveData);
                }
            }
            finally
            {
                IsRestoring = false;
            }

            bool includesOfflineProgression =
                settledOfflineSeconds > 0.0;

            try
            {
                PublishRestoreCompleted(
                    settledOfflineSeconds,
                    includesOfflineProgression);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Save restore completion failed.\n{exception.Message}");

                if (!includesOfflineProgression)
                {
                    return false;
                }

                return TryRollbackOfflineSettlement(
                    saveData,
                    "a restore completion handler failed");
            }

            if (includesOfflineProgression)
            {
                GameSaveData settledSnapshot = CreateSnapshot();
                OfflineSettlementCompletedEvent summary =
                    CreateOfflineSettlementSummary(
                        saveData,
                        settledSnapshot,
                        settledOfflineSeconds);
                bool savedAfterSettlement = Save();

                if (!savedAfterSettlement)
                {
                    return TryRollbackOfflineSettlement(
                        saveData,
                        "the settled save could not be written");
                }

                Debug.Log(
                    $"Offline settlement completed and saved for {settledOfflineSeconds:0.##} seconds.");
                OfflineSettlementCompleted?.Invoke(summary);
            }

            Debug.Log("Game save loaded and restored.");
            return true;
        }

        private double SettleOfflineProgress(GameSaveData saveData)
        {
            if (saveData == null ||
                saveData.SavedAtUtcTicks <= 0L ||
                OfflineSettlementSource == null)
            {
                return 0.0;
            }

            DateTime savedAtUtc;

            try
            {
                savedAtUtc = new DateTime(
                    saveData.SavedAtUtcTicks,
                    DateTimeKind.Utc);
            }
            catch (ArgumentOutOfRangeException)
            {
                Debug.LogWarning(
                    "Offline settlement skipped because saved UTC ticks are invalid.");
                return 0.0;
            }

            double elapsedSeconds =
                (Clock.UtcNow.ToUniversalTime() - savedAtUtc).TotalSeconds;

            if (elapsedSeconds <= 0.0 ||
                double.IsNaN(elapsedSeconds) ||
                double.IsInfinity(elapsedSeconds))
            {
                return 0.0;
            }

            double maximumSeconds = Math.Max(
                0.0,
                OfflineSettlementSource.MaximumOfflineSeconds);
            double settledSeconds = Math.Min(
                elapsedSeconds,
                maximumSeconds);

            if (settledSeconds <= 0.0)
            {
                return 0.0;
            }

            OfflineSettlementSource.SettleOffline(settledSeconds);
            OfflineCalendarProgressionSource?.AdvanceOffline(
                settledSeconds);

            return settledSeconds;
        }

        private bool TryRollbackOfflineSettlement(
            GameSaveData saveData,
            string reason)
        {
            try
            {
                IsRestoring = true;
                RestoreSnapshot(saveData);
                RetainOptionalSections(saveData);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Offline settlement rollback failed after {reason}.\n" +
                    exception.Message);
                return false;
            }
            finally
            {
                IsRestoring = false;
            }

            try
            {
                PublishRestoreCompleted(
                    settledOfflineSeconds: 0.0,
                    includesOfflineProgression: false);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Offline settlement state was restored after {reason}, " +
                    $"but restore completion failed.\n{exception.Message}");
                return false;
            }

            Debug.LogWarning(
                $"Offline settlement was rolled back because {reason}. " +
                "The previously saved game state remains active.");
            return true;
        }

        private void PublishRestoreCompleted(
            double settledOfflineSeconds,
            bool includesOfflineProgression)
        {
            RestoreCompleted?.Invoke(
                new RestoreCompletedEvent(
                    settledOfflineSeconds,
                    includesOfflineProgression));
        }

        private static OfflineSettlementCompletedEvent
            CreateOfflineSettlementSummary(
                GameSaveData beforeSettlement,
                GameSaveData afterSettlement,
                double settledOfflineSeconds)
        {
            long initialCoins = Math.Max(
                0L,
                beforeSettlement?.Economy?.Coins ?? 0L);
            long currentCoins = Math.Max(
                0L,
                afterSettlement?.Economy?.Coins ?? initialCoins);
            decimal totalBefore =
                initialCoins +
                Math.Max(
                    0L,
                    beforeSettlement?.WeeklySettlement?.PendingCoins ?? 0L);
            decimal totalAfter =
                currentCoins +
                Math.Max(
                    0L,
                    afterSettlement?.WeeklySettlement?.PendingCoins ?? 0L);
            decimal earnedDifference =
                Math.Max(0m, totalAfter - totalBefore);
            long earnedCoins =
                earnedDifference >= long.MaxValue
                    ? long.MaxValue
                    : (long)earnedDifference;

            return new OfflineSettlementCompletedEvent(
                settledOfflineSeconds,
                initialCoins,
                earnedCoins,
                currentCoins);
        }

        private void RetainOptionalSections(GameSaveData saveData)
        {
            retainedWeeklySettlement = saveData?.WeeklySettlement;
            retainedResearch = saveData?.Research;
            retainedProgression = saveData?.Progression;
            retainedRadio = saveData?.Radio;
            retainedTerrainDecorations = saveData?.TerrainDecorations;
            retainedWorldGrid = saveData?.WorldGrid;
            retainedSpecialBuildings = saveData?.SpecialBuildings;
            retainedSpecialBuildingVisits =
                saveData?.SpecialBuildingVisits;
        }

        private static RadioSaveData CreateEmptyRadioSaveData()
        {
            return new RadioSaveData
            {
                Slots = Array.Empty<RadioSlotSaveData>(),
                CurrentSlotIndex = -1
            };
        }

        private static TerrainDecorationSaveData
            CreateEmptyTerrainDecorationSaveData()
        {
            return new TerrainDecorationSaveData
            {
                ClearedTileIndices = Array.Empty<int>()
            };
        }

        private static SpecialBuildingSaveData
            CreateEmptySpecialBuildingSaveData()
        {
            return new SpecialBuildingSaveData
            {
                Buildings = Array.Empty<SpecialBuildingInstanceSaveData>()
            };
        }

        private static SpecialBuildingVisitSaveData
            CreateEmptySpecialBuildingVisitSaveData()
        {
            return new SpecialBuildingVisitSaveData
            {
                HasState = false,
                LastProcessedTotalDay = 0L,
                Statistics =
                    Array.Empty<SpecialBuildingVisitStatisticsSaveData>()
            };
        }
    }
}
