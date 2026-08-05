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
        public ISchoolBusSaveSource SchoolBusSaveSource { get; private set; }
        public IRadioSaveSource RadioSaveSource { get; private set; }
        public ITerrainDecorationSaveSource TerrainDecorationSaveSource { get; private set; }
        public IWorldGridSaveSource WorldGridSaveSource { get; private set; }
        public ISpecialBuildingSaveSource SpecialBuildingSaveSource { get; private set; }
        public ISpecialBuildingVisitSaveSource SpecialBuildingVisitSaveSource
        {
            get;
            private set;
        }
        public IEmergencyIncidentSaveSource EmergencyIncidentSaveSource
        {
            get;
            private set;
        }
        public ICitizenFeedSaveSource CitizenFeedSaveSource { get; private set; }
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
        private SchoolBusSaveData retainedSchoolBus;
        private RadioSaveData retainedRadio;
        private TerrainDecorationSaveData retainedTerrainDecorations;
        private WorldGridSaveData retainedWorldGrid;
        private SpecialBuildingSaveData retainedSpecialBuildings;
        private SpecialBuildingVisitSaveData retainedSpecialBuildingVisits;
        private EmergencyIncidentSaveData retainedEmergencyIncidents;
        private CitizenFeedSaveData retainedCitizenFeed;
        private readonly IWorldGridAccess worldGridAccess;
        private bool hasLoadedSave;

        public event Action<RestoreCompletedEvent> RestoreCompleted;
        public event Action<SaveSlotMetadata> AutomaticSaveSlotCreated;

        public SaveService(
            ISimSaveSource simSaveSource,
            JsonSaveRepository repository,
            ISaveClock clock,
            IEconomySaveSource economySaveSource = null,
            IResearchSaveSource researchSaveSource = null,
            IProgressionSaveSource progressionSaveSource = null,
            IWorldGridAccess worldGridAccess = null)
        {
            SimSaveSource = simSaveSource;
            Repository = repository ?? new JsonSaveRepository();
            Clock = clock ?? new SystemSaveClock();
            EconomySaveSource = economySaveSource;
            ResearchSaveSource = researchSaveSource;
            ProgressionSaveSource = progressionSaveSource;
            this.worldGridAccess = worldGridAccess;
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

        public void RegisterSchoolBusSaveSource(
            ISchoolBusSaveSource schoolBusSaveSource)
        {
            SchoolBusSaveSource = schoolBusSaveSource;

            if (hasLoadedSave)
            {
                SchoolBusSaveSource?.RestoreSnapshot(
                    retainedSchoolBus ?? new SchoolBusSaveData());
            }
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

        public void RegisterEmergencyIncidentSaveSource(
            IEmergencyIncidentSaveSource emergencyIncidentSaveSource)
        {
            EmergencyIncidentSaveSource =
                emergencyIncidentSaveSource;

            if (hasLoadedSave)
            {
                EmergencyIncidentSaveSource?.RestoreSnapshot(
                    retainedEmergencyIncidents ??
                    new EmergencyIncidentSaveData());
            }
        }

        public void RegisterCitizenFeedSaveSource(
            ICitizenFeedSaveSource citizenFeedSaveSource)
        {
            CitizenFeedSaveSource = citizenFeedSaveSource;

            if (hasLoadedSave)
            {
                CitizenFeedSaveSource?.RestoreSnapshot(
                    retainedCitizenFeed ?? new CitizenFeedSaveData());
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
                SchoolBus = SchoolBusSaveSource?.CreateSnapshot()
                    ?? retainedSchoolBus,
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
                    ?? retainedSpecialBuildingVisits,
                EmergencyIncidents =
                    EmergencyIncidentSaveSource?.CreateSnapshot()
                    ?? retainedEmergencyIncidents,
                CitizenFeed = CitizenFeedSaveSource?.CreateSnapshot()
                    ?? retainedCitizenFeed
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

            if (SchoolBusSaveSource != null)
            {
                SchoolBusSaveSource.RestoreSnapshot(
                    saveData.SchoolBus ?? new SchoolBusSaveData());
            }

            if (saveData.Calendar != null)
            {
                GameCalendarSaveSource?.RestoreSnapshot(saveData.Calendar);
            }

            // 반드시 달력 복원 뒤에 온다. 피드 장부는 복원 즉시 현재 게임 시각으로
            // 24시간 만료를 판정하는데, 달력이 아직 이전 런타임 값이면 저장 시점과
            // 무관한 기준으로 항목을 버리거나 남긴다.
            if (CitizenFeedSaveSource != null)
            {
                CitizenFeedSaveSource.RestoreSnapshot(
                    saveData.CitizenFeed ?? new CitizenFeedSaveData());
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
                    CreateRestoredSpecialBuildingData(saveData));
            }

            if (SpecialBuildingVisitSaveSource != null)
            {
                SpecialBuildingVisitSaveSource.RestoreSnapshot(
                    CreateRestoredSpecialBuildingVisitData(saveData));
            }

            if (EmergencyIncidentSaveSource != null)
            {
                EmergencyIncidentSaveSource.RestoreSnapshot(
                    saveData.EmergencyIncidents ??
                    new EmergencyIncidentSaveData());
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
            try
            {
                RestoreSnapshot(saveData);
            }
            finally
            {
                IsRestoring = false;
            }

            try
            {
                PublishRestoreCompleted();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Save restore completion failed.\n{exception.Message}");
                return false;
            }

            Debug.Log("Game save loaded and restored.");
            return true;
        }

        private void PublishRestoreCompleted()
        {
            RestoreCompleted?.Invoke(new RestoreCompletedEvent());
        }

        private void RetainOptionalSections(GameSaveData saveData)
        {
            retainedWeeklySettlement = saveData?.WeeklySettlement;
            retainedResearch = saveData?.Research;
            retainedProgression = saveData?.Progression;
            retainedSchoolBus = saveData?.SchoolBus;
            retainedRadio = saveData?.Radio;
            retainedTerrainDecorations = saveData?.TerrainDecorations;
            retainedWorldGrid = saveData?.WorldGrid;
            retainedSpecialBuildings =
                CreateRestoredSpecialBuildingData(saveData);
            retainedSpecialBuildingVisits =
                CreateRestoredSpecialBuildingVisitData(saveData);
            retainedEmergencyIncidents =
                saveData?.EmergencyIncidents;
            retainedCitizenFeed = saveData?.CitizenFeed;
        }

        private SpecialBuildingSaveData CreateRestoredSpecialBuildingData(
            GameSaveData saveData)
        {
            SpecialBuildingSaveData source = saveData?.SpecialBuildings;
            SpecialBuildingInstanceSaveData[] entries = source?.Buildings;

            if (entries == null || entries.Length == 0)
            {
                return CreateEmptySpecialBuildingSaveData();
            }

            Vector2Int offset = GetLegacyWorldOffset(saveData);
            var migrated = new SpecialBuildingInstanceSaveData[entries.Length];

            for (int index = 0; index < entries.Length; index++)
            {
                SpecialBuildingInstanceSaveData entry = entries[index];
                migrated[index] = entry == null
                    ? null
                    : new SpecialBuildingInstanceSaveData
                    {
                        BuildingId = entry.BuildingId,
                        X = entry.X + offset.x,
                        Y = entry.Y + offset.y,
                        Direction = entry.Direction
                    };
            }

            return new SpecialBuildingSaveData
            {
                Buildings = migrated
            };
        }

        private SpecialBuildingVisitSaveData
            CreateRestoredSpecialBuildingVisitData(GameSaveData saveData)
        {
            SpecialBuildingVisitSaveData source =
                saveData?.SpecialBuildingVisits;

            if (source == null)
            {
                return CreateEmptySpecialBuildingVisitSaveData();
            }

            SpecialBuildingVisitStatisticsSaveData[] entries =
                source.Statistics ??
                Array.Empty<SpecialBuildingVisitStatisticsSaveData>();
            Vector2Int offset = GetLegacyWorldOffset(saveData);
            var migrated =
                new SpecialBuildingVisitStatisticsSaveData[entries.Length];

            for (int index = 0; index < entries.Length; index++)
            {
                SpecialBuildingVisitStatisticsSaveData entry = entries[index];
                migrated[index] = entry == null
                    ? null
                    : new SpecialBuildingVisitStatisticsSaveData
                    {
                        BuildingId = entry.BuildingId,
                        X = entry.X + offset.x,
                        Y = entry.Y + offset.y,
                        Day = entry.Day,
                        PlannedToday = entry.PlannedToday,
                        TotalPlannedVisits = entry.TotalPlannedVisits
                    };
            }

            return new SpecialBuildingVisitSaveData
            {
                HasState = source.HasState,
                LastProcessedTotalDay = source.LastProcessedTotalDay,
                Statistics = migrated
            };
        }

        private Vector2Int GetLegacyWorldOffset(GameSaveData saveData)
        {
            if (worldGridAccess == null || saveData == null)
            {
                return Vector2Int.zero;
            }

            int savedWidth = saveData.Simulation?.GridWidth > 0
                ? saveData.Simulation.GridWidth
                : saveData.GridWidth;
            int savedHeight = saveData.Simulation?.GridHeight > 0
                ? saveData.Simulation.GridHeight
                : saveData.GridHeight;

            return savedWidth == worldGridAccess.InitialPlayableSize.x &&
                   savedHeight == worldGridAccess.InitialPlayableSize.y &&
                   (savedWidth != worldGridAccess.WorldWidth ||
                    savedHeight != worldGridAccess.WorldHeight)
                ? worldGridAccess.InitialPlayableOrigin
                : Vector2Int.zero;
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
