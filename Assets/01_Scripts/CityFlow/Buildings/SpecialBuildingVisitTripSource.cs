using System;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Buildings
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpecialBuildingVisitService))]
    public sealed class SpecialBuildingVisitTripSource :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        [SerializeField]
        [Min(0)]
        [Tooltip("Maximum visual vehicle trips per building and day. Zero means unlimited.")]
        private int maximumVisualTripsPerBuildingPerDay = 64;

        private CityFlowServices services;
        private ISpecialBuildingVisitService visitService;
        private ISpecialBuildingService buildings;
        private IGameCalendarService calendar;
        private IVehicleTripService vehicleTrips;
        private bool initialized;

        public int MaximumVisualTripsPerBuildingPerDay =>
            maximumVisualTripsPerBuildingPerDay;

        public void Initialize(CityFlowServices cityServices)
        {
            if (!isActiveAndEnabled || initialized || cityServices == null)
            {
                return;
            }

            services = cityServices;
            services.SpecialBuildingVisitsRegistered += OnVisitsRegistered;
            services.SpecialBuildingsRegistered += OnBuildingsRegistered;
            services.GameCalendarRegistered += OnCalendarRegistered;
            services.VehicleTripsRegistered += OnVehicleTripsRegistered;
            BindVisits(services.SpecialBuildingVisits);
            buildings = services.SpecialBuildings;
            calendar = services.GameCalendar;
            vehicleTrips = services.VehicleTrips;
            if (services.Save != null)
            {
                services.Save.RestoreCompleted += OnRestoreCompleted;
            }
            initialized = true;

            Debug.Log(
                "[SpecialBuildingVisitTripSource] Visit demand is connected " +
                "to vehicle trips.",
                this);
        }

        private void OnDestroy()
        {
            BindVisits(null);
            if (services == null)
            {
                return;
            }

            services.SpecialBuildingVisitsRegistered -= OnVisitsRegistered;
            services.SpecialBuildingsRegistered -= OnBuildingsRegistered;
            services.GameCalendarRegistered -= OnCalendarRegistered;
            services.VehicleTripsRegistered -= OnVehicleTripsRegistered;
            if (services.Save != null)
            {
                services.Save.RestoreCompleted -= OnRestoreCompleted;
            }
        }

        private void OnVisitsRegistered(ISpecialBuildingVisitService service)
        {
            BindVisits(service);
        }

        private void OnBuildingsRegistered(ISpecialBuildingService service)
        {
            buildings = service;
        }

        private void OnCalendarRegistered(IGameCalendarService service)
        {
            calendar = service;
        }

        private void OnVehicleTripsRegistered(IVehicleTripService service)
        {
            vehicleTrips = service;
        }

        private void BindVisits(ISpecialBuildingVisitService service)
        {
            if (ReferenceEquals(visitService, service))
            {
                return;
            }

            if (visitService != null)
            {
                visitService.DemandPlanned -= OnDemandPlanned;
            }

            visitService = service;
            if (visitService != null)
            {
                visitService.DemandPlanned += OnDemandPlanned;
            }
        }

        private void OnDemandPlanned(
            SpecialBuildingVisitDemandPlannedEvent planned)
        {
            if (!isActiveAndEnabled || vehicleTrips == null)
            {
                return;
            }

            SpecialBuildingVisitStatistics statistics = planned.Statistics;
            if (statistics.PlannedToday <= 0 ||
                (calendar != null && statistics.Day != calendar.TotalDays))
            {
                return;
            }

            ScheduleStatistics(statistics, -1f);
        }

        private void OnRestoreCompleted(RestoreCompletedEvent _)
        {
            if (!isActiveAndEnabled || vehicleTrips == null ||
                visitService == null || buildings == null || calendar == null)
            {
                return;
            }

            SpecialBuildingInstance[] snapshot =
                buildings.CreateBuildingSnapshot();
            Array.Sort(snapshot, CompareBuildings);
            for (int index = 0; index < snapshot.Length; index++)
            {
                if (!visitService.TryGetStatistics(
                        snapshot[index].Anchor,
                        out SpecialBuildingVisitStatistics statistics) ||
                    statistics.Day != calendar.TotalDays)
                {
                    continue;
                }

                ScheduleStatistics(statistics, calendar.Hour);
            }
        }

        private void ScheduleStatistics(
            SpecialBuildingVisitStatistics statistics,
            float earliestHourExclusive)
        {
            if (statistics.PlannedToday <= 0 || vehicleTrips == null)
            {
                return;
            }

            int tripCount = maximumVisualTripsPerBuildingPerDay <= 0
                ? statistics.PlannedToday
                : Mathf.Min(
                    statistics.PlannedToday,
                    maximumVisualTripsPerBuildingPerDay);
            int scheduledCount = 0;
            int eligibleCount = 0;
            for (int visitIndex = 0; visitIndex < tripCount; visitIndex++)
            {
                float scheduledHour = 24f * (visitIndex + 0.5f) /
                    Mathf.Max(1, tripCount);
                if (scheduledHour <= earliestHourExclusive)
                {
                    continue;
                }

                eligibleCount++;
                if (vehicleTrips.TryScheduleSpecialBuildingVisit(
                        new SpecialBuildingVisitTripRequest(
                            statistics.BuildingId,
                            statistics.Anchor,
                            statistics.Day,
                            visitIndex,
                            scheduledHour)))
                {
                    scheduledCount++;
                }
            }

            if (scheduledCount < eligibleCount)
            {
                Debug.LogWarning(
                    $"[SpecialBuildingVisitTripSource] Scheduled " +
                    $"{scheduledCount}/{eligibleCount} visual trips for " +
                    $"{statistics.BuildingId} on day {statistics.Day}. " +
                    "The trip queue is full or duplicate requests were " +
                    "ignored.",
                    this);
            }
        }

        private static int CompareBuildings(
            SpecialBuildingInstance left,
            SpecialBuildingInstance right)
        {
            int buildingId = string.CompareOrdinal(
                left.BuildingId,
                right.BuildingId);
            if (buildingId != 0)
            {
                return buildingId;
            }

            int y = left.Anchor.y.CompareTo(right.Anchor.y);
            return y != 0
                ? y
                : left.Anchor.x.CompareTo(right.Anchor.x);
        }
    }
}

// Unity setup: Add this component beside SpecialBuildingVisitService.
// SpecialBuildingSystem.prefab includes it so scene integration needs no wiring.
