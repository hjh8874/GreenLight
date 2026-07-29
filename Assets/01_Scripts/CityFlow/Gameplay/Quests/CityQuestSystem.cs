using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.Sim.Quests;
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

        private static readonly string[] TutorialObjectiveIds =
        {
            CityQuestId.BuildRoad.ToString(),
            CityQuestId.BuildHouse.ToString(),
            CityQuestId.BuildOffice.ToString(),
            CityQuestId.ConnectCommute.ToString(),
            CityQuestId.HarvestFirstIncome.ToString()
        };

        private readonly HashSet<Vector2Int> jamTiles = new();

        private CityQuestDirector director = new();
        private CityFlowServices services;
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
        private int gridWidth = GridUtil.DefaultWidth;
        private int gridHeight = GridUtil.DefaultHeight;

        public event Action<CityQuestViewState> ViewStateChanged;

        public CityQuestViewState CurrentViewState =>
            new CityQuestViewState(director.ActiveQuest, director.IsMinimized);

        public void Initialize(CityFlowServices cityFlowServices)
        {
            if (ReferenceEquals(services, cityFlowServices))
            {
                PublishViewState();
                return;
            }

            UnbindServices();
            services = cityFlowServices;
            director = new CityQuestDirector();
            evaluationElapsed = EvaluationInterval;
            totalArrivals = 0L;
            pendingCoins = 0L;
            previousPendingCoins = 0L;
            hasHarvested = false;
            needsLegacyProgressionMigration = false;
            hasRestoredLifetimeDeliveredTotal = false;
            restoredLifetimeDeliveredTotal = 0L;
            jamTiles.Clear();

            if (services == null)
            {
                PublishViewState();
                return;
            }

            if (services.WorldGrid != null)
            {
                gridWidth = Math.Max(1, services.WorldGrid.WorldWidth);
                gridHeight = Math.Max(1, services.WorldGrid.WorldHeight);
            }

            services.Events.Arrival += OnArrival;
            services.Events.CongestionChanged += OnCongestionChanged;
            services.WeeklyEconomyRegistered += OnWeeklyEconomyRegistered;

            if (services.WeeklyEconomy != null)
            {
                BindWeeklyEconomy(services.WeeklyEconomy);
            }

            BindDeliveredProgress();
            services.RegisterProgressionSaveSource(this);
            TryMigrateLegacyProgression();
            RefreshJamTiles();
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
            evaluationElapsed += Time.unscaledDeltaTime;

            if (evaluationElapsed < EvaluationInterval)
            {
                return;
            }

            float elapsed = evaluationElapsed;
            evaluationElapsed = 0f;
            RefreshJamTiles();
            Evaluate(elapsed);
        }

        private void OnDestroy()
        {
            UnbindServices();
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
            int roadCount = 0;
            int houseCount = 0;
            int officeCount = 0;
            int schoolCount = 0;

            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    TileType type = services.TileData.GetTileType(new Vector2Int(x, y));

                    switch (type)
                    {
                        case TileType.Road:
                            roadCount++;
                            break;
                        case TileType.House:
                            houseCount++;
                            break;
                        case TileType.Office:
                            officeCount++;
                            break;
                        case TileType.School:
                            schoolCount++;
                            break;
                    }
                }
            }

            long deliveredTotal = deliveredProgress?.LifetimeDeliveredTotal ?? totalArrivals;

            return new CityQuestSnapshot(
                roadCount,
                houseCount,
                officeCount,
                schoolCount,
                Math.Max(totalArrivals, deliveredTotal),
                pendingCoins,
                hasHarvested,
                jamTiles.Count);
        }

        private void RefreshJamTiles()
        {
            jamTiles.Clear();

            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    Vector2Int tile = new Vector2Int(x, y);

                    if (services.TileData.GetCongestion(tile) == CongestionLevel.Jam)
                    {
                        jamTiles.Add(tile);
                    }
                }
            }
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
                services.WeeklyEconomyRegistered -= OnWeeklyEconomyRegistered;
            }

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
        }

        private void PublishViewState()
        {
            ViewStateChanged?.Invoke(CurrentViewState);
        }
    }
}
