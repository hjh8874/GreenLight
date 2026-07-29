using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.View;
using UnityEngine;

namespace CityFlow.Gameplay.Save
{
    [DefaultExecutionOrder(1000)]
    public sealed class GameSaveLifecycleService : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("Startup")]
        [SerializeField] private bool loadOnStart = true;

        [Header("Automatic Save")]
        [SerializeField] private bool saveOnFocusLost = true;
        [SerializeField] private bool saveOnApplicationPause = true;
        [SerializeField] private bool saveOnFloatingModeEntered = true;
        [SerializeField] private bool saveOnApplicationQuit = true;

        private CityFlowServices services;
        private bool initialLoadAttempted;
        private bool inactiveSaveCompleted;
        private bool applicationPaused;
        private bool applicationFocused = true;
        private bool applicationQuitting;
        private BasicEconomySaveAdapter weeklySettlementSaveAdapter;
        private FloatingWindowService floatingWindowService;

        public void Initialize(CityFlowServices services)
        {
            this.services = services;
            RegisterWeeklySettlementSaveSource();
        }

        private void RegisterWeeklySettlementSaveSource()
        {
            if (services?.Save == null)
            {
                return;
            }

            if (services.Save.WeeklySettlementSaveSource != null)
            {
                return;
            }

            BasicEconomySystem economySystem =
                FindAnyObjectByType<BasicEconomySystem>(
                    FindObjectsInactive.Include);

            if (economySystem == null)
            {
                Debug.Log(
                    "[GameSaveLifecycleService] Weekly settlement save source was not found.");
                return;
            }

            weeklySettlementSaveAdapter =
                new BasicEconomySaveAdapter(economySystem);

            services.RegisterWeeklySettlementSaveSource(
                weeklySettlementSaveAdapter);

            Debug.Log(
                "[GameSaveLifecycleService] Weekly settlement save source registered.");
        }

        private void Start()
        {
            TryLoadInitialSave();
            BindFloatingWindowSaveTrigger();
        }

        private void OnDestroy()
        {
            if (floatingWindowService != null)
            {
                floatingWindowService.OnFloatingStateChanged -= OnFloatingStateChanged;
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            applicationFocused = hasFocus;

            if (!hasFocus && saveOnFocusLost)
            {
                TrySaveInactiveState("application focus lost");
                return;
            }

            ResetInactiveSaveGuard();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            applicationPaused = pauseStatus;

            if (pauseStatus && saveOnApplicationPause)
            {
                TrySaveInactiveState("application paused");
                return;
            }

            ResetInactiveSaveGuard();
        }

        private void OnApplicationQuit()
        {
            applicationQuitting = true;

            if (!saveOnApplicationQuit)
            {
                return;
            }

            TrySave("application quit");
        }

        private void TryLoadInitialSave()
        {
            if (initialLoadAttempted)
            {
                return;
            }

            initialLoadAttempted = true;

            if (!loadOnStart)
            {
                Debug.Log("[GameSaveLifecycleService] Initial load is disabled.");
                return;
            }

            if (services?.Save == null)
            {
                Debug.LogWarning("[GameSaveLifecycleService] Initial load skipped because SaveService is not connected.");
                return;
            }

            if (!services.Save.Repository.HasSave())
            {
                Debug.Log("[GameSaveLifecycleService] No save file found. Starting a new game.");
                return;
            }

            bool loaded = services.Save.TryLoadAndRestore();
            Debug.Log(loaded
                ? "[GameSaveLifecycleService] Initial save loaded and offline progress settled."
                : "[GameSaveLifecycleService] Initial save could not be loaded.");
        }

        private void BindFloatingWindowSaveTrigger()
        {
            floatingWindowService = FindFirstObjectByType<FloatingWindowService>();

            if (floatingWindowService == null)
            {
                Debug.Log("[GameSaveLifecycleService] Floating window save trigger was not found.");
                return;
            }

            floatingWindowService.OnFloatingStateChanged += OnFloatingStateChanged;
        }

        private void OnFloatingStateChanged(bool isFloating)
        {
            if (isFloating && saveOnFloatingModeEntered)
            {
                TrySaveInactiveState("floating mode entered");
                return;
            }

            ResetInactiveSaveGuard();
        }

        private void TrySaveInactiveState(string reason)
        {
            if (inactiveSaveCompleted || !initialLoadAttempted)
            {
                return;
            }

            inactiveSaveCompleted = TrySave(reason);
        }

        private bool TrySave(string reason)
        {
            if (services?.Save == null)
            {
                Debug.LogWarning($"[GameSaveLifecycleService] Save skipped because SaveService is not connected. Reason: {reason}.");
                return false;
            }

            if (services.Save.IsRestoring)
            {
                Debug.Log($"[GameSaveLifecycleService] Save skipped while restoring. Reason: {reason}.");
                return false;
            }

            bool saved = services.Save.Save();
            Debug.Log(saved
                ? $"[GameSaveLifecycleService] Game saved because of {reason}."
                : $"[GameSaveLifecycleService] Game save failed because of {reason}.");

            return saved;
        }

        private void ResetInactiveSaveGuard()
        {
            if (!applicationPaused
                && applicationFocused
                && !applicationQuitting)
            {
                inactiveSaveCompleted = false;
            }
        }

        // Unity setup: Add this component to the Services object in the integrated gameplay scene.
        // CityBootstrap initializes it automatically through ICityFlowServiceConsumer.
    }
}
