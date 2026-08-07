using System;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Content
{
    public sealed class PolicePatrolScheduler :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        [SerializeField]
        private PoliceDispatchConfigSO config;

        private CityFlowServices services;
        private IGameCalendarService calendar;
        private bool initialized;

        public long LastScheduledTotalDay { get; private set; } = -1L;

        internal event Action<long> PatrolDue;

        public void Initialize(CityFlowServices cityServices)
        {
            if (!isActiveAndEnabled || initialized)
            {
                return;
            }

            config ??= GetComponent<PoliceCallSystem>()?.Config;
            if (cityServices == null || config == null)
            {
                Debug.LogError(
                    "[PolicePatrolScheduler] Services and config are required.",
                    this);
                return;
            }

            services = cityServices;
            initialized = true;
            services.GameCalendarRegistered += BindCalendar;
            if (services.Save != null)
            {
                services.Save.RestoreCompleted += HandleRestoreCompleted;
            }

            BindCalendar(services.GameCalendar);
        }

        public void RestoreLastScheduledDay(
            bool hasSavedDay,
            long totalDay)
        {
            LastScheduledTotalDay = hasSavedDay
                ? Math.Max(0L, totalDay)
                : -1L;
        }

        private void OnEnable()
        {
            if (!initialized)
            {
                return;
            }

            services.GameCalendarRegistered += BindCalendar;
            if (services.Save != null)
            {
                services.Save.RestoreCompleted += HandleRestoreCompleted;
            }

            BindCalendar(services.GameCalendar);
        }

        private void OnDisable()
        {
            if (services != null)
            {
                services.GameCalendarRegistered -= BindCalendar;
                if (services.Save != null)
                {
                    services.Save.RestoreCompleted -=
                        HandleRestoreCompleted;
                }
            }

            BindCalendar(null);
        }

        private void OnDestroy()
        {
            if (services != null)
            {
                services.GameCalendarRegistered -= BindCalendar;
                if (services.Save != null)
                {
                    services.Save.RestoreCompleted -=
                        HandleRestoreCompleted;
                }
            }

            BindCalendar(null);
        }

        private void BindCalendar(IGameCalendarService service)
        {
            if (ReferenceEquals(calendar, service))
            {
                return;
            }

            if (calendar != null)
            {
                calendar.HourChanged -= HandleHourChanged;
            }

            calendar = service;
            if (calendar != null)
            {
                calendar.HourChanged += HandleHourChanged;
                TryScheduleCurrentHour();
            }
        }

        private void HandleHourChanged(int _)
        {
            TryScheduleCurrentHour();
        }

        private void HandleRestoreCompleted(RestoreCompletedEvent _)
        {
            TryScheduleCurrentHour();
        }

        private void TryScheduleCurrentHour()
        {
            if (!config.EnableDailyPatrol ||
                calendar == null ||
                services?.Save?.IsRestoring == true ||
                calendar.Hour != config.PatrolStartHour ||
                calendar.TotalDays <= LastScheduledTotalDay)
            {
                return;
            }

            LastScheduledTotalDay = calendar.TotalDays;
            PatrolDue?.Invoke(LastScheduledTotalDay);
        }

        // Unity setup: this component is prewired in PoliceContent.prefab.
    }
}
