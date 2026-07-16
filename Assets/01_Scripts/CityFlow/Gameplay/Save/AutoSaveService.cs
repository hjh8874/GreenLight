using System.Collections;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Gameplay.Save
{
    public sealed class AutoSaveService : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private bool saveOnMonthChanged = true;
        [SerializeField] private bool saveOnPlacementChanged = true;
        [SerializeField, Min(0f)] private float placementSaveDelaySeconds = 0.5f;

        private CityFlowServices services;
        private IGameCalendarService gameCalendar;
        private Coroutine placementSaveCoroutine;

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            this.services = services;
            services.GameCalendarRegistered += OnGameCalendarRegistered;
            services.Events.Placed += OnPlaced;
            services.Events.InfrastructureChanged += OnInfrastructureChanged;

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
                services.Events.Placed -= OnPlaced;
                services.Events.InfrastructureChanged -= OnInfrastructureChanged;
            }

            if (gameCalendar != null)
            {
                gameCalendar.MonthChanged -= OnMonthChanged;
            }

            if (placementSaveCoroutine != null)
            {
                StopCoroutine(placementSaveCoroutine);
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

            bool saved = services.Save.Save();
            Debug.Log(saved
                ? $"[AutoSaveService] Auto saved at game month {totalMonths}."
                : $"[AutoSaveService] Auto save failed at game month {totalMonths}.");
        }

        private void OnPlaced(PlacedEvent placedEvent)
        {
            if (!saveOnPlacementChanged
                || services?.Save == null
                || services.Save.IsRestoring)
            {
                return;
            }

            if (placementSaveCoroutine != null)
            {
                StopCoroutine(placementSaveCoroutine);
            }

            placementSaveCoroutine = StartCoroutine(
                SaveAfterPlacementDelay(placedEvent));
        }

        private IEnumerator SaveAfterPlacementDelay(PlacedEvent placedEvent)
        {
            if (placementSaveDelaySeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(placementSaveDelaySeconds);
            }

            placementSaveCoroutine = null;

            if (services?.Save == null || services.Save.IsRestoring)
            {
                yield break;
            }

            bool saved = services?.Save?.Save() == true;
            string action = placedEvent.IsRemove ? "removed" : "placed";

            Debug.Log(saved
                ? $"[AutoSaveService] Auto saved after {action} {placedEvent.Type} at {placedEvent.Tile}."
                : $"[AutoSaveService] Auto save failed after {action} {placedEvent.Type} at {placedEvent.Tile}.");
        }

        private void OnInfrastructureChanged(InfrastructureChangedEvent e)
        {
            if (!saveOnPlacementChanged
                || services?.Save == null
                || services.Save.IsRestoring)
            {
                return;
            }

            if (placementSaveCoroutine != null)
            {
                StopCoroutine(placementSaveCoroutine);
            }

            placementSaveCoroutine = StartCoroutine(
                SaveAfterInfrastructureDelay(e));
        }

        private IEnumerator SaveAfterInfrastructureDelay(InfrastructureChangedEvent e)
        {
            if (placementSaveDelaySeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(placementSaveDelaySeconds);
            }

            placementSaveCoroutine = null;

            if (services?.Save == null || services.Save.IsRestoring)
            {
                yield break;
            }

            bool saved = services?.Save?.Save() == true;
            string action = e.IsRemove ? "removed" : "placed";

            Debug.Log(saved
                ? $"[AutoSaveService] Auto saved after {action} {e.Kind} at {e.Tile}."
                : $"[AutoSaveService] Auto save failed after {action} {e.Kind} at {e.Tile}.");
        }

        // Unity setup: Attach this beside GameCalendarService. Saves update the single default save file.
    }
}
