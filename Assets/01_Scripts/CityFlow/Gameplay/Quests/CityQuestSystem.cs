using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.Gameplay.Research;
using CityFlow.Sim.Quests;
using CityFlow.WorldGrid;
using UnityEngine;

namespace CityFlow.Gameplay.Quests
{
    public readonly struct CityQuestViewState
    {
        public readonly CityQuestPresentation Quest;
        public readonly bool IsMinimized;

        public CityQuestViewState(CityQuestPresentation quest, bool isMinimized)
        {
            Quest = quest;
            IsMinimized = isMinimized;
        }
    }

    public sealed class CityQuestSystem : MonoBehaviour, IProgressionSaveSource
    {
        private const float EvaluationInterval = 0.5f;
        private const int TutorialQuestCount = 5;
        private const int QuestSaveVersion = 1;
        private const string SchoolResearchId = "research_building_school";
        private const string HospitalResearchId = "research_building_hospital";

        private static readonly string[] TutorialObjectiveIds =
        {
            CityQuestId.BuildRoad.ToString(),
            CityQuestId.BuildHouse.ToString(),
            CityQuestId.BuildOffice.ToString(),
            CityQuestId.ConnectCommute.ToString(),
            CityQuestId.HarvestFirstIncome.ToString()
        };

        private readonly HashSet<Vector2Int> jamTiles = new();
        private readonly Dictionary<Vector2Int, TileType> trackedQuestTiles =
            new();

        // Initialize 전에도 CurrentViewState 가 director 를 읽으므로 항상 살아 있어야 한다.
        // 구독은 ReplaceDirector 가 책임진다(Awake 에서 최초 1회).
        private CityQuestDirector director;
        private CityFlowServices services;
        private IWorldGridService worldGrid;
        private IWeeklyEconomyService weeklyEconomy;
        private IReadOnlyDeliveredProgress deliveredProgress;
        private float evaluationElapsed;
        private long totalArrivals;
        private long pendingCoins;
        private long previousPendingCoins;
        private bool hasHarvested;
        private bool needsLegacyProgressionMigration;
        private bool hasRestoredLifetimeDeliveredTotal;
        private long restoredLifetimeDeliveredTotal;
        private int roadCount;
        private int houseCount;
        private int officeCount;
        private int schoolCount;
        private int hospitalCount;
        private CityBusService cityBusService;
        private bool gridStateRebuildPending;
        private IReadOnlyList<ResearchEntry> researchEntries =
            Array.Empty<ResearchEntry>();

        public event Action<CityQuestViewState> ViewStateChanged;
        // 퀘스트가 실제로 달성된 순간만 울린다. ViewStateChanged 와 구분해야 하는 이유는
        // CityQuestDirector.QuestCompleted 주석 참조.
        public event Action<CityQuestId> QuestCompleted;

        public CityQuestViewState CurrentViewState =>
            new CityQuestViewState(director.ActiveQuest, director.IsMinimized);

        private void Awake()
        {
            if (director == null)
            {
                ReplaceDirector(new CityQuestDirector(showShortcutGuide: true));
            }
        }

        public void Initialize(CityFlowServices cityFlowServices)
        {
            if (ReferenceEquals(services, cityFlowServices))
            {
                PublishViewState();
                return;
            }

            UnbindServices();
            services = cityFlowServices;
            ReplaceDirector(new CityQuestDirector(showShortcutGuide: true));
            evaluationElapsed = EvaluationInterval;
            totalArrivals = 0L;
            pendingCoins = 0L;
            previousPendingCoins = 0L;
            hasHarvested = false;
            needsLegacyProgressionMigration = false;
            hasRestoredLifetimeDeliveredTotal = false;
            restoredLifetimeDeliveredTotal = 0L;
            jamTiles.Clear();
            trackedQuestTiles.Clear();
            ResetGridCounts();
            gridStateRebuildPending = false;
            ResearchCatalogSO researchCatalog = ResearchCatalogSO.LoadDefault();
            researchEntries = researchCatalog != null
                ? researchCatalog.ValidEntries()
                : Array.Empty<ResearchEntry>();

            if (services == null)
            {
                PublishViewState();
                return;
            }

            services.Events.Arrival += OnArrival;
            services.Events.CongestionChanged += OnCongestionChanged;
            services.Events.Placed += OnPlaced;
            services.WeeklyEconomyRegistered += OnWeeklyEconomyRegistered;
            services.WorldGridRegistered += OnWorldGridRegistered;
            BindWorldGrid(services.WorldGrid);

            if (services.Save != null)
            {
                services.Save.RestoreCompleted += OnRestoreCompleted;
            }

            if (services.WeeklyEconomy != null)
            {
                BindWeeklyEconomy(services.WeeklyEconomy);
            }

            BindDeliveredProgress();
            services.RegisterProgressionSaveSource(this);
            TryMigrateLegacyProgression();
            RebuildGridState();
            Evaluate(EvaluationInterval);
        }

        public ProgressionSaveData CreateSnapshot()
        {
            int completedStage = Math.Max(
                director.TutorialStage,
                hasHarvested ? TutorialQuestCount : 0);
            int safeStage = Math.Max(0, Math.Min(TutorialQuestCount, completedStage));
            var completedObjectiveIds = new string[safeStage];

            Array.Copy(
                TutorialObjectiveIds,
                completedObjectiveIds,
                safeStage);

            return new ProgressionSaveData
            {
                QuestSaveVersion = QuestSaveVersion,
                ShortcutGuideStage = director.ShortcutGuideStage,
                ShortcutGuideCompleted = director.IsShortcutGuideComplete,
                CurrentStage = safeStage,
                CompletedObjectiveIds = completedObjectiveIds,
                TutorialCompleted = safeStage >= TutorialQuestCount,
                HasQuestProgress = true,
                HasHarvested = hasHarvested,
                LifetimeDeliveredTotal = Math.Max(
                    totalArrivals,
                    deliveredProgress?.LifetimeDeliveredTotal ?? 0L)
            };
        }

        public void RestoreSnapshot(ProgressionSaveData snapshot)
        {
            int restoredStage = GetRestoredTutorialStage(snapshot);
            director.SetResumeMode(true);
            director.RestoreShortcutGuideStage(
                GetRestoredShortcutGuideStage(snapshot));
            director.RestoreTutorialStage(restoredStage);
            hasHarvested = snapshot?.HasHarvested == true
                || restoredStage >= TutorialQuestCount;
            hasRestoredLifetimeDeliveredTotal = snapshot != null;
            restoredLifetimeDeliveredTotal = Math.Max(
                0L,
                snapshot?.HasQuestProgress == true
                    ? snapshot.LifetimeDeliveredTotal
                    : 0L);
            totalArrivals = restoredLifetimeDeliveredTotal;

            ApplyRestoredDeliveredProgress();

            needsLegacyProgressionMigration =
                snapshot != null
                && !snapshot.HasQuestProgress
                && restoredStage == 0
                && !snapshot.TutorialCompleted
                && (snapshot.CompletedObjectiveIds == null
                    || snapshot.CompletedObjectiveIds.Length == 0);

            TryMigrateLegacyProgression();
            PublishViewState();
        }

        public void MinimizeCurrentQuest()
        {
            if (director.Minimize())
            {
                PublishViewState();
            }
        }

        public void RestoreCurrentQuest()
        {
            if (director.Restore())
            {
                PublishViewState();
            }
        }

        public void AcknowledgeCurrentQuest()
        {
            if (director.Acknowledge())
            {
                Evaluate(0f);
                PublishViewState();
            }
        }

        private void Update()
        {
            if (services == null)
            {
                return;
            }

            if (deliveredProgress == null)
            {
                BindDeliveredProgress();
            }

            TryMigrateLegacyProgression();
            if (gridStateRebuildPending)
            {
                RebuildGridState();
            }

            evaluationElapsed += Time.unscaledDeltaTime;

            if (evaluationElapsed < EvaluationInterval)
            {
                return;
            }

            float elapsed = evaluationElapsed;
            evaluationElapsed = 0f;
            Evaluate(elapsed);
        }

        private void OnDestroy()
        {
            UnbindServices();
        }

        // director 는 Initialize() 마다 새로 만들어진다(L81). 지연 구독 + bool 가드로는
        // 두 번째 인스턴스부터 구독이 안 붙어 연출이 영구 무음이 된다
        // (리뷰 #251 — 세 리뷰어가 독립적으로 지적).
        // 그래서 "생성과 구독을 한곳에서" 처리한다. 교체 시 이전 구독도 끊는다.
        private void ReplaceDirector(CityQuestDirector next)
        {
            if (director != null)
            {
                director.QuestCompleted -= OnDirectorQuestCompleted;
            }

            director = next;

            if (director != null)
            {
                director.QuestCompleted += OnDirectorQuestCompleted;
            }
        }

        private void OnDirectorQuestCompleted(CityQuestId id)
        {
            QuestCompleted?.Invoke(id);
        }

        private void Evaluate(float elapsed)
        {
            CityQuestSnapshot snapshot = CaptureSnapshot();

            if (director.Tick(snapshot, elapsed))
            {
                PublishViewState();
            }
        }

        private CityQuestSnapshot CaptureSnapshot()
        {
            long deliveredTotal = deliveredProgress?.LifetimeDeliveredTotal ?? totalArrivals;
            IIntersectionFacilityService intersectionFacilities =
                services?.Placement as IIntersectionFacilityService;
            IBusStopInfrastructureService busStops =
                services?.Placement as IBusStopInfrastructureService;

            return new CityQuestSnapshot(
                roadCount,
                houseCount,
                officeCount,
                schoolCount,
                Math.Max(totalArrivals, deliveredTotal),
                pendingCoins,
                hasHarvested,
                jamTiles.Count,
                HasConnectedCommute(),
                hospitalCount,
                GetReadyResearchId(),
                services?.Research?.ActiveResearchId,
                GetUnbuiltSpecialBuildingId(),
                services?.Research?.IsUnlocked(SchoolResearchId) == true,
                services?.Research?.IsUnlocked(HospitalResearchId) == true,
                intersectionFacilities?.SignalTiles?.Count ?? 0,
                intersectionFacilities?.RoundaboutTiles?.Count ?? 0,
                busStops?.BusStopTiles?.Count ?? 0,
                intersectionFacilities != null,
                busStops != null,
                IsCityBusOperating());
        }

        private bool IsCityBusOperating()
        {
            if (cityBusService == null)
            {
                cityBusService = FindAnyObjectByType<CityBusService>(
                    FindObjectsInactive.Include);
            }

            IReadOnlyList<CityBusVehicleAgent> activeVehicles =
                cityBusService?.ActiveVehicles;
            if (activeVehicles == null)
            {
                return false;
            }

            for (int index = 0; index < activeVehicles.Count; index++)
            {
                CityBusVehicleAgent vehicle = activeVehicles[index];
                if (vehicle != null && vehicle.IsOperating)
                {
                    return true;
                }
            }

            return false;
        }

        private string GetReadyResearchId()
        {
            IResearchUnlockService research = services?.Research;
            if (research == null || researchEntries == null)
            {
                return string.Empty;
            }

            for (int index = 0; index < researchEntries.Count; index++)
            {
                string researchId = researchEntries[index]?.researchId?.Trim();
                if (!string.IsNullOrEmpty(researchId) &&
                    research.IsReady(researchId))
                {
                    return researchId;
                }
            }

            return string.Empty;
        }

        private string GetUnbuiltSpecialBuildingId()
        {
            ISpecialBuildingService specialBuildings =
                services?.SpecialBuildings;
            if (specialBuildings == null)
            {
                return string.Empty;
            }

            SpecialBuildingBuildOption[] options =
                specialBuildings.CreateBuildOptionSnapshot();
            SpecialBuildingInstance[] instances =
                specialBuildings.CreateBuildingSnapshot();

            for (int optionIndex = 0;
                 optionIndex < options.Length;
                 optionIndex++)
            {
                SpecialBuildingBuildOption option = options[optionIndex];
                if (!option.IsUnlocked ||
                    string.IsNullOrWhiteSpace(option.RequiredResearchId))
                {
                    continue;
                }

                bool isBuilt = false;
                for (int instanceIndex = 0;
                     instanceIndex < instances.Length;
                     instanceIndex++)
                {
                    if (string.Equals(
                            instances[instanceIndex].BuildingId,
                            option.BuildingId,
                            StringComparison.Ordinal))
                    {
                        isBuilt = true;
                        break;
                    }
                }

                if (!isBuilt)
                {
                    return option.BuildingId;
                }
            }

            return string.Empty;
        }

        private bool HasConnectedCommute()
        {
            IReadOnlyCityStats stats = services?.Stats;
            if (stats == null)
            {
                return false;
            }

            foreach (KeyValuePair<Vector2Int, TileType> trackedTile in
                     trackedQuestTiles)
            {
                if (trackedTile.Value != TileType.Office)
                {
                    continue;
                }

                IReadOnlyList<CommuterHomeCount> commuterHomes =
                    stats.GetCompanyCommuterHomes(trackedTile.Key);
                if (commuterHomes != null && commuterHomes.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void RebuildGridState()
        {
            trackedQuestTiles.Clear();
            jamTiles.Clear();
            ResetGridCounts();
            gridStateRebuildPending = false;

            if (services?.TileData == null)
            {
                return;
            }

            int scannedTileCount = UnlockedGridTileScanner.VisitUnlockedTiles(
                worldGrid,
                GridUtil.DefaultWidth,
                GridUtil.DefaultHeight,
                ScanGridTile);

            Debug.Log(
                $"[CityQuestSystem] Rebuilt grid cache from " +
                $"{scannedTileCount} unlocked tiles.",
                this);
        }

        private void ScanGridTile(Vector2Int tile)
        {
            TileType type = services.TileData.GetTileType(tile);
            if (type == TileType.Road)
            {
                TrackQuestTile(tile, type);
                if (services.TileData.GetCongestion(tile) ==
                    CongestionLevel.Jam)
                {
                    jamTiles.Add(tile);
                }
            }
            else if (IsTrackedBuilding(type) &&
                     services.TileData.IsFootprintAnchor(tile))
            {
                TrackQuestTile(tile, type);
            }
        }

        private void TrackQuestTile(Vector2Int tile, TileType type)
        {
            if (!IsTrackedType(type) || trackedQuestTiles.ContainsKey(tile))
            {
                return;
            }

            trackedQuestTiles.Add(tile, type);
            ApplyCountDelta(type, 1);
        }

        private void UntrackQuestTile(Vector2Int tile)
        {
            if (!trackedQuestTiles.TryGetValue(tile, out TileType type))
            {
                return;
            }

            trackedQuestTiles.Remove(tile);
            ApplyCountDelta(type, -1);
        }

        private void ApplyCountDelta(TileType type, int delta)
        {
            switch (type)
            {
                case TileType.Road:
                    roadCount = Math.Max(0, roadCount + delta);
                    break;
                case TileType.House:
                    houseCount = Math.Max(0, houseCount + delta);
                    break;
                case TileType.Office:
                    officeCount = Math.Max(0, officeCount + delta);
                    break;
                case TileType.School:
                    schoolCount = Math.Max(0, schoolCount + delta);
                    break;
                case TileType.Hospital:
                    hospitalCount = Math.Max(0, hospitalCount + delta);
                    break;
            }
        }

        private void ResetGridCounts()
        {
            roadCount = 0;
            houseCount = 0;
            officeCount = 0;
            schoolCount = 0;
            hospitalCount = 0;
        }

        private static bool IsTrackedType(TileType type) =>
            type == TileType.Road || IsTrackedBuilding(type);

        private static bool IsTrackedBuilding(TileType type) =>
            type == TileType.House ||
            type == TileType.Office ||
            type == TileType.School ||
            type == TileType.Hospital;

        private void OnPlaced(PlacedEvent placed)
        {
            if (placed.IsRemove)
            {
                UntrackQuestTile(placed.Tile);
                jamTiles.Remove(placed.Tile);
                return;
            }

            TrackQuestTile(placed.Tile, placed.Type);
        }

        private void OnChunkUnlocked(GridChunkId chunk)
        {
            if (worldGrid == null || services?.TileData == null)
            {
                return;
            }

            UnlockedGridTileScanner.VisitChunk(
                worldGrid,
                chunk,
                ScanGridTile);
        }

        private void OnWorldGridRegistered(IWorldGridService service)
        {
            BindWorldGrid(service);
            RebuildGridState();
        }

        private void BindWorldGrid(IWorldGridService service)
        {
            if (ReferenceEquals(worldGrid, service))
            {
                return;
            }

            if (worldGrid != null)
            {
                worldGrid.ChunkUnlocked -= OnChunkUnlocked;
                worldGrid.AccessRestored -= OnWorldGridAccessRestored;
            }

            worldGrid = service;
            if (worldGrid != null)
            {
                worldGrid.ChunkUnlocked += OnChunkUnlocked;
                worldGrid.AccessRestored += OnWorldGridAccessRestored;
            }
        }

        private void OnWorldGridAccessRestored()
        {
            gridStateRebuildPending = true;
        }

        private void OnRestoreCompleted(RestoreCompletedEvent _)
        {
            gridStateRebuildPending = true;
        }

        private void OnArrival(ArrivalEvent arrival)
        {
            totalArrivals++;
        }

        private void OnCongestionChanged(CongestionEvent congestion)
        {
            if (congestion.Level == CongestionLevel.Jam)
            {
                jamTiles.Add(congestion.Tile);
            }
            else
            {
                jamTiles.Remove(congestion.Tile);
            }
        }

        private void OnWeeklyEconomyRegistered(IWeeklyEconomyService service)
        {
            BindWeeklyEconomy(service);
        }

        private void BindWeeklyEconomy(IWeeklyEconomyService service)
        {
            if (ReferenceEquals(weeklyEconomy, service))
            {
                return;
            }

            if (weeklyEconomy != null)
            {
                weeklyEconomy.PendingCoinsChanged -= OnPendingCoinsChanged;
            }

            weeklyEconomy = service;
            pendingCoins = Math.Max(0L, weeklyEconomy?.PendingCoins ?? 0L);
            previousPendingCoins = pendingCoins;

            if (weeklyEconomy != null)
            {
                weeklyEconomy.PendingCoinsChanged += OnPendingCoinsChanged;
            }
        }

        private void OnPendingCoinsChanged(long value)
        {
            long next = Math.Max(0L, value);

            if (previousPendingCoins > 0L && next == 0L)
            {
                hasHarvested = true;
            }

            previousPendingCoins = next;
            pendingCoins = next;
        }

        private void BindDeliveredProgress()
        {
            if (deliveredProgress != null)
            {
                return;
            }

            DeliveredProgressSystem progress = FindAnyObjectByType<DeliveredProgressSystem>();

            if (progress == null)
            {
                return;
            }

            deliveredProgress = progress;
            deliveredProgress.LifetimeDeliveredChanged += OnDeliveredProgressChanged;
            ApplyRestoredDeliveredProgress();
            totalArrivals = Math.Max(totalArrivals, deliveredProgress.LifetimeDeliveredTotal);
        }

        private void OnDeliveredProgressChanged(long value)
        {
            totalArrivals = Math.Max(totalArrivals, value);
        }

        private void ApplyRestoredDeliveredProgress()
        {
            if (!hasRestoredLifetimeDeliveredTotal
                || deliveredProgress is not DeliveredProgressSystem progress)
            {
                return;
            }

            progress.RestoreLifetimeDeliveredTotal(restoredLifetimeDeliveredTotal);
            hasRestoredLifetimeDeliveredTotal = false;
        }

        private static int GetRestoredTutorialStage(ProgressionSaveData snapshot)
        {
            if (snapshot == null)
            {
                return 0;
            }

            if (snapshot.TutorialCompleted)
            {
                return TutorialQuestCount;
            }

            int restoredStage = Math.Max(
                0,
                Math.Min(TutorialQuestCount, snapshot.CurrentStage));

            if (snapshot.CompletedObjectiveIds == null)
            {
                return restoredStage;
            }

            for (int i = 0; i < TutorialObjectiveIds.Length; i++)
            {
                if (Array.IndexOf(
                        snapshot.CompletedObjectiveIds,
                        TutorialObjectiveIds[i]) < 0)
                {
                    break;
                }

                restoredStage = Math.Max(restoredStage, i + 1);
            }

            return restoredStage;
        }

        private static int GetRestoredShortcutGuideStage(
            ProgressionSaveData snapshot)
        {
            if (snapshot == null)
            {
                return 0;
            }

            // 이 필드가 없던 저장은 이미 시작된 게임이다. 새 안내를 뒤늦게
            // 끼워 넣지 않고 완료 상태로 마이그레이션한다.
            if (snapshot.QuestSaveVersion < QuestSaveVersion ||
                snapshot.ShortcutGuideCompleted)
            {
                return CityQuestDirector.ShortcutGuideCount;
            }

            return Math.Max(
                0,
                Math.Min(
                    CityQuestDirector.ShortcutGuideCount,
                    snapshot.ShortcutGuideStage));
        }

        private void TryMigrateLegacyProgression()
        {
            if (!needsLegacyProgressionMigration || services == null)
            {
                return;
            }

            long playedDays = services.GameCalendar?.TotalDays ?? 0L;
            int activeVehicles = services.Stats?.ActiveVehicleCount ?? 0;

            if (playedDays <= 0L || activeVehicles <= 0)
            {
                return;
            }

            director.RestoreTutorialStage(TutorialQuestCount);
            hasHarvested = true;
            needsLegacyProgressionMigration = false;
        }

        private void UnbindServices()
        {
            if (services != null)
            {
                services.Events.Arrival -= OnArrival;
                services.Events.CongestionChanged -= OnCongestionChanged;
                services.Events.Placed -= OnPlaced;
                services.WeeklyEconomyRegistered -= OnWeeklyEconomyRegistered;
                services.WorldGridRegistered -= OnWorldGridRegistered;

                if (services.Save != null)
                {
                    services.Save.RestoreCompleted -= OnRestoreCompleted;
                }
            }

            BindWorldGrid(null);

            if (weeklyEconomy != null)
            {
                weeklyEconomy.PendingCoinsChanged -= OnPendingCoinsChanged;
            }

            if (deliveredProgress != null)
            {
                deliveredProgress.LifetimeDeliveredChanged -= OnDeliveredProgressChanged;
            }

            services = null;
            weeklyEconomy = null;
            deliveredProgress = null;
            cityBusService = null;
        }

        private void PublishViewState()
        {
            ViewStateChanged?.Invoke(CurrentViewState);
        }
    }
}
