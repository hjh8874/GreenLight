using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Gameplay.Save
{
    public sealed class AutoSaveService : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private bool saveOnMonthChanged = true;

        private CityFlowServices services;
        private IGameCalendarService gameCalendar;

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            this.services = services;
            services.GameCalendarRegistered += OnGameCalendarRegistered;

            if (services.GameCalendar != null)
            {
                BindGameCalendar(services.GameCalendar);
            }

            Debug.Log("[AutoSaveService] Auto save service initialized.");
        }

        private void OnDestroy()
        {
            if (services != null)
            {
                services.GameCalendarRegistered -= OnGameCalendarRegistered;
            }

            if (gameCalendar != null)
            {
                gameCalendar.MonthChanged -= OnMonthChanged;
            }
        }

        private void OnGameCalendarRegistered(IGameCalendarService calendar)
        {
            BindGameCalendar(calendar);
        }

        private void BindGameCalendar(IGameCalendarService calendar)
        {
            if (gameCalendar == calendar)
            {
                return;
            }

            if (gameCalendar != null)
            {
                gameCalendar.MonthChanged -= OnMonthChanged;
            }

            gameCalendar = calendar;
            gameCalendar.MonthChanged += OnMonthChanged;
        }

        private void OnMonthChanged(int totalMonths)
        {
            if (!saveOnMonthChanged)
            {
                return;
            }

            if (services?.Save?.IsRestoring == true)
            {
                Debug.Log("[AutoSaveService] Month auto save skipped while save data is being restored.");
                return;
            }

            if (services?.Save == null)
            {
                Debug.LogWarning("[AutoSaveService] Month auto save skipped because SaveService is not connected.");
                return;
            }

            bool saved = services.Save.Save(createAutomaticSlot: true);
            Debug.Log(saved
                ? $"[AutoSaveService] Auto saved at game month {totalMonths}."
                : $"[AutoSaveService] Auto save failed at game month {totalMonths}.");
        }

        // Unity setup: Attach this beside GameCalendarService. One automatic slot is created per game month.
    }
}
