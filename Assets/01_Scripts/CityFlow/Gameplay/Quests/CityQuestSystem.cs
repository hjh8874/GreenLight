using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Contracts;
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

    public sealed class CityQuestSystem : MonoBehaviour
    {
        private const float EvaluationInterval = 0.5f;

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
            jamTiles.Clear();

            if (services == null)
            {
                PublishViewState();
                return;
            }

            services.Events.Arrival += OnArrival;
            services.Events.CongestionChanged += OnCongestionChanged;
            services.WeeklyEconomyRegistered += OnWeeklyEconomyRegistered;

            if (services.WeeklyEconomy != null)
            {
                BindWeeklyEconomy(services.WeeklyEconomy);
            }

            BindDeliveredProgress();
            RefreshJamTiles();
            Evaluate(EvaluationInterval);
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

            for (int y = 0; y < GridUtil.DefaultHeight; y++)
            {
                for (int x = 0; x < GridUtil.DefaultWidth; x++)
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

            int usedRoadTiles = services.Stats?.RoadTileCount ?? roadCount;
            int maxRoadTiles = services.Stats?.MaxRoadTiles ?? 0;
            long deliveredTotal = deliveredProgress?.LifetimeDeliveredTotal ?? totalArrivals;

            return new CityQuestSnapshot(
                roadCount,
                houseCount,
                officeCount,
                schoolCount,
                Math.Max(totalArrivals, deliveredTotal),
                pendingCoins,
                hasHarvested,
                jamTiles.Count,
                services.TileData.Stability01,
                usedRoadTiles,
                maxRoadTiles);
        }

        private void RefreshJamTiles()
        {
            jamTiles.Clear();

            for (int y = 0; y < GridUtil.DefaultHeight; y++)
            {
                for (int x = 0; x < GridUtil.DefaultWidth; x++)
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
            DeliveredProgressSystem progress = FindAnyObjectByType<DeliveredProgressSystem>();

            if (progress == null)
            {
                return;
            }

            deliveredProgress = progress;
            deliveredProgress.LifetimeDeliveredChanged += OnDeliveredProgressChanged;
            totalArrivals = Math.Max(totalArrivals, deliveredProgress.LifetimeDeliveredTotal);
            hasHarvested = hasHarvested
                || (deliveredProgress.LifetimeDeliveredTotal > 0L && pendingCoins == 0L);
        }

        private void OnDeliveredProgressChanged(long value)
        {
            totalArrivals = Math.Max(totalArrivals, value);
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
