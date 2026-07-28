using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Buildings
{
    [DisallowMultipleComponent]
    public sealed class SpecialBuildingVisitService :
        MonoBehaviour,
        ICityFlowServiceConsumer,
        ISpecialBuildingVisitService,
        ISpecialBuildingVisitSaveSource
    {
        private const int MaximumCatchUpDays = 4096;

        private sealed class VisitRecord
        {
            public string BuildingId = string.Empty;
            public Vector2Int Anchor;
            public long Day;
            public int PlannedToday;
            public long TotalPlannedVisits;

            public SpecialBuildingVisitStatistics CreateSnapshot() =>
                new(
                    BuildingId,
                    Anchor,
                    Day,
                    PlannedToday,
                    TotalPlannedVisits);
        }

        private readonly Dictionary<Vector2Int, VisitRecord> records = new();
        private CityFlowServices services;
        private ISpecialBuildingService buildings;
        private IGameCalendarService calendar;
        private IReadOnlyPopulationData population;
        private long lastProcessedTotalDay = -1L;
        private bool hasRestoredState;
        private bool initialized;

        public long LastProcessedTotalDay => lastProcessedTotalDay;

        public event Action<SpecialBuildingVisitDemandPlannedEvent>
            DemandPlanned;

        public void Initialize(CityFlowServices cityServices)
        {
            if (!isActiveAndEnabled || initialized || cityServices == null)
            {
                return;
            }

            services = cityServices;
            services.SpecialBuildingsRegistered += OnBuildingsRegistered;
            services.GameCalendarRegistered += OnCalendarRegistered;
            services.PopulationRegistered += OnPopulationRegistered;

            BindBuildings(services.SpecialBuildings);
            BindCalendar(services.GameCalendar);
            population = services.Population;

            if (services.Save != null)
            {
                services.Save.RestoreCompleted += OnRestoreCompleted;
            }

            initialized = services.RegisterSpecialBuildingVisits(this);
            if (!initialized)
            {
                Debug.LogWarning(
                    "[SpecialBuildingVisitService] Another visit service is registered.",
                    this);
                Unsubscribe();
                services = null;
                return;
            }

            EnsureBuildingRecords();
            Debug.Log(
                "[SpecialBuildingVisitService] Deterministic daily visits registered.",
                this);
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        public bool TryGetStatistics(
            Vector2Int tile,
            out SpecialBuildingVisitStatistics statistics)
        {
            statistics = default;
            if (buildings == null ||
                !buildings.TryGetBuilding(tile, out SpecialBuildingInstance building))
            {
                return false;
            }

            VisitRecord record = EnsureRecord(building);
            statistics = record.CreateSnapshot();
            return true;
        }

        public SpecialBuildingVisitSaveData CreateSnapshot()
        {
            var entries = new List<SpecialBuildingVisitStatisticsSaveData>(
                records.Count);
            foreach (VisitRecord record in records.Values)
            {
                entries.Add(new SpecialBuildingVisitStatisticsSaveData
                {
                    BuildingId = record.BuildingId,
                    X = record.Anchor.x,
                    Y = record.Anchor.y,
                    Day = record.Day,
                    PlannedToday = record.PlannedToday,
                    TotalPlannedVisits = record.TotalPlannedVisits
                });
            }

            entries.Sort(CompareSavedStatistics);
            return new SpecialBuildingVisitSaveData
            {
                HasState = true,
                LastProcessedTotalDay = Math.Max(0L, lastProcessedTotalDay),
                Statistics = entries.ToArray()
            };
        }

        public void RestoreSnapshot(SpecialBuildingVisitSaveData snapshot)
        {
            records.Clear();
            hasRestoredState = true;

            if (snapshot == null || !snapshot.HasState)
            {
                lastProcessedTotalDay = calendar?.TotalDays ?? 0L;
                EnsureBuildingRecords();
                return;
            }

            lastProcessedTotalDay = Math.Max(
                0L,
                snapshot.LastProcessedTotalDay);
            SpecialBuildingVisitStatisticsSaveData[] entries =
                snapshot.Statistics;

            if (entries != null)
            {
                for (int index = 0; index < entries.Length; index++)
                {
                    SpecialBuildingVisitStatisticsSaveData saved = entries[index];
                    if (saved == null ||
                        string.IsNullOrWhiteSpace(saved.BuildingId))
                    {
                        continue;
                    }

                    Vector2Int anchor = new(saved.X, saved.Y);
                    records[anchor] = new VisitRecord
                    {
                        BuildingId = saved.BuildingId.Trim(),
                        Anchor = anchor,
                        Day = Math.Max(0L, saved.Day),
                        PlannedToday = Math.Max(0, saved.PlannedToday),
                        TotalPlannedVisits = Math.Max(
                            0L,
                            saved.TotalPlannedVisits)
                    };
                }
            }

            EnsureBuildingRecords(pruneMissing: true);
        }

        private void ProcessThrough(long targetTotalDay)
        {
            if (!initialized || calendar == null || population == null ||
                buildings == null || services?.Save?.IsRestoring == true)
            {
                return;
            }

            if (lastProcessedTotalDay < 0L)
            {
                lastProcessedTotalDay = targetTotalDay;
                return;
            }

            if (targetTotalDay <= lastProcessedTotalDay)
            {
                return;
            }

            long firstDay = lastProcessedTotalDay + 1L;
            long pendingDays = targetTotalDay - lastProcessedTotalDay;
            if (pendingDays > MaximumCatchUpDays)
            {
                firstDay = targetTotalDay - MaximumCatchUpDays + 1L;
                Debug.LogWarning(
                    "[SpecialBuildingVisitService] Visit catch-up was capped " +
                    $"at {MaximumCatchUpDays} days.",
                    this);
            }

            for (long day = firstDay; day <= targetTotalDay; day++)
            {
                ProcessDay(day);
            }

            lastProcessedTotalDay = targetTotalDay;
        }

        private void ProcessDay(long totalDay)
        {
            SpecialBuildingInstance[] instances =
                buildings.CreateBuildingSnapshot();
            ResetToday(instances, totalDay);

            var groups = new SortedDictionary<
                string,
                List<SpecialBuildingInstance>>(StringComparer.Ordinal);
            for (int index = 0; index < instances.Length; index++)
            {
                SpecialBuildingInstance building = instances[index];
                if (!groups.TryGetValue(
                        building.BuildingId,
                        out List<SpecialBuildingInstance> group))
                {
                    group = new List<SpecialBuildingInstance>();
                    groups.Add(building.BuildingId, group);
                }

                group.Add(building);
            }

            foreach (KeyValuePair<string, List<SpecialBuildingInstance>> pair
                     in groups)
            {
                if (!buildings.TryGetBuildOption(
                        pair.Key,
                        out SpecialBuildingBuildOption option) ||
                    !option.CanReceiveVisitors)
                {
                    continue;
                }

                int demand = DeterministicVisitDemand.CalculateDailyDemand(
                    population.CurrentPopulation,
                    option.VisitsPerPeriod,
                    option.PeriodDays,
                    totalDay,
                    option.BuildingId);
                demand = ScaleByAttraction(
                    demand,
                    option.AttractionWeight,
                    totalDay,
                    option.BuildingId);

                int[] allocations = AllocateAcrossBuildings(
                    demand,
                    pair.Value.Count,
                    option.VisitorCapacity,
                    option.BuildingId,
                    totalDay);

                for (int index = 0; index < pair.Value.Count; index++)
                {
                    PlanVisits(
                        pair.Value[index],
                        allocations[index],
                        totalDay);
                }
            }
        }

        private void PlanVisits(
            SpecialBuildingInstance building,
            int visits,
            long totalDay)
        {
            VisitRecord record = EnsureRecord(building);
            int safeVisits = Math.Max(0, visits);

            record.Day = totalDay;
            record.PlannedToday = safeVisits;
            record.TotalPlannedVisits = SaturatingAdd(
                record.TotalPlannedVisits,
                safeVisits);

            DemandPlanned?.Invoke(
                new SpecialBuildingVisitDemandPlannedEvent(
                    record.CreateSnapshot()));
        }

        private void ResetToday(
            SpecialBuildingInstance[] buildingsSnapshot,
            long totalDay)
        {
            for (int index = 0; index < buildingsSnapshot.Length; index++)
            {
                VisitRecord record = EnsureRecord(buildingsSnapshot[index]);
                record.Day = totalDay;
                record.PlannedToday = 0;
            }
        }

        private void EnsureBuildingRecords(bool pruneMissing = false)
        {
            if (buildings == null)
            {
                return;
            }

            SpecialBuildingInstance[] snapshot =
                buildings.CreateBuildingSnapshot();
            HashSet<Vector2Int> active = pruneMissing
                ? new HashSet<Vector2Int>()
                : null;

            for (int index = 0; index < snapshot.Length; index++)
            {
                EnsureRecord(snapshot[index]);
                active?.Add(snapshot[index].Anchor);
            }

            if (active == null)
            {
                return;
            }

            var stale = new List<Vector2Int>();
            foreach (Vector2Int anchor in records.Keys)
            {
                if (!active.Contains(anchor))
                {
                    stale.Add(anchor);
                }
            }

            for (int index = 0; index < stale.Count; index++)
            {
                records.Remove(stale[index]);
            }
        }

        private VisitRecord EnsureRecord(SpecialBuildingInstance building)
        {
            if (!records.TryGetValue(
                    building.Anchor,
                    out VisitRecord record) ||
                !string.Equals(
                    record.BuildingId,
                    building.BuildingId,
                    StringComparison.Ordinal))
            {
                record = new VisitRecord
                {
                    BuildingId = building.BuildingId,
                    Anchor = building.Anchor,
                    Day = Math.Max(0L, calendar?.TotalDays ?? 0L)
                };
                records[building.Anchor] = record;
            }

            return record;
        }

        private void OnBuildingsRegistered(ISpecialBuildingService service)
        {
            BindBuildings(service);
            EnsureBuildingRecords();
        }

        private void BindBuildings(ISpecialBuildingService service)
        {
            if (ReferenceEquals(buildings, service))
            {
                return;
            }

            if (buildings != null)
            {
                buildings.BuildingChanged -= OnBuildingChanged;
                buildings.BuildingsRestored -= OnBuildingsRestored;
            }

            buildings = service;
            if (buildings != null)
            {
                buildings.BuildingChanged += OnBuildingChanged;
                buildings.BuildingsRestored += OnBuildingsRestored;
            }
        }

        private void OnBuildingChanged(SpecialBuildingChangedEvent changed)
        {
            if (changed.IsRemove)
            {
                records.Remove(changed.Building.Anchor);
                return;
            }

            EnsureRecord(changed.Building);
        }

        private void OnBuildingsRestored()
        {
            EnsureBuildingRecords(pruneMissing: true);
        }

        private void OnCalendarRegistered(IGameCalendarService service)
        {
            BindCalendar(service);
        }

        private void BindCalendar(IGameCalendarService service)
        {
            if (ReferenceEquals(calendar, service))
            {
                return;
            }

            if (calendar != null)
            {
                calendar.DayChanged -= OnDayChanged;
            }

            calendar = service;
            if (calendar != null)
            {
                calendar.DayChanged += OnDayChanged;
                if (!hasRestoredState && lastProcessedTotalDay < 0L)
                {
                    lastProcessedTotalDay = calendar.TotalDays;
                }
            }
        }

        private void OnPopulationRegistered(IReadOnlyPopulationData service)
        {
            population = service;
        }

        private void OnDayChanged(int _)
        {
            ProcessThrough(calendar?.TotalDays ?? lastProcessedTotalDay);
        }

        private void OnRestoreCompleted(RestoreCompletedEvent _)
        {
            EnsureBuildingRecords(pruneMissing: true);
            ProcessThrough(calendar?.TotalDays ?? lastProcessedTotalDay);
        }

        private void Unsubscribe()
        {
            BindBuildings(null);
            BindCalendar(null);

            if (services == null)
            {
                return;
            }

            services.SpecialBuildingsRegistered -= OnBuildingsRegistered;
            services.GameCalendarRegistered -= OnCalendarRegistered;
            services.PopulationRegistered -= OnPopulationRegistered;
            if (services.Save != null)
            {
                services.Save.RestoreCompleted -= OnRestoreCompleted;
            }
        }

        private static int ScaleByAttraction(
            int demand,
            float attractionWeight,
            long totalDay,
            string buildingId)
        {
            if (demand <= 0 || attractionWeight <= 0f)
            {
                return 0;
            }

            double weighted = demand * (double)attractionWeight;
            int whole = weighted >= int.MaxValue
                ? int.MaxValue
                : (int)Math.Floor(weighted);
            double fraction = weighted - whole;
            if (whole < int.MaxValue && fraction > 0d)
            {
                int threshold = (int)Math.Round(fraction * 1000d);
                int sample = DeterministicVisitDemand.StableRotation(
                    buildingId,
                    totalDay,
                    1000);
                if (sample < threshold)
                {
                    whole++;
                }
            }

            return whole;
        }

        private static int[] AllocateAcrossBuildings(
            int demand,
            int buildingCount,
            int capacityPerBuilding,
            string buildingId,
            long totalDay)
        {
            int safeCount = Math.Max(0, buildingCount);
            var result = new int[safeCount];
            if (demand <= 0 || safeCount == 0)
            {
                return result;
            }

            long totalCapacity = capacityPerBuilding <= 0
                ? long.MaxValue
                : (long)capacityPerBuilding * safeCount;
            int distributable = totalCapacity >= demand
                ? demand
                : (int)Math.Min(int.MaxValue, totalCapacity);
            int each = distributable / safeCount;
            int remainder = distributable % safeCount;
            int rotation = DeterministicVisitDemand.StableRotation(
                buildingId,
                totalDay,
                safeCount);

            for (int index = 0; index < safeCount; index++)
            {
                result[index] = each;
            }

            for (int index = 0; index < remainder; index++)
            {
                result[(rotation + index) % safeCount]++;
            }

            return result;
        }

        private static int CompareSavedStatistics(
            SpecialBuildingVisitStatisticsSaveData left,
            SpecialBuildingVisitStatisticsSaveData right)
        {
            int y = left.Y.CompareTo(right.Y);
            return y != 0 ? y : left.X.CompareTo(right.X);
        }

        private static long SaturatingAdd(long left, long right)
        {
            if (right <= 0L)
            {
                return Math.Max(0L, left);
            }

            return left > long.MaxValue - right
                ? long.MaxValue
                : left + right;
        }
    }
}

// Unity setup: This component is prewired in SpecialBuildingSystem.prefab.
