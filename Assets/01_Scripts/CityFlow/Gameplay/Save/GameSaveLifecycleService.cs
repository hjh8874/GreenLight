using CityFlow.Bootstrap;
using CityFlow.Content;
using UnityEngine;

namespace CityFlow.Gameplay.Save
{
    [DefaultExecutionOrder(1000)]
    public sealed class GameSaveLifecycleService : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("Startup")]
        [SerializeField] private bool loadOnStart = true;

        [Header("Automatic Save")]
        [SerializeField] private bool saveOnApplicationQuit = true;

        private CityFlowServices services;
        private bool initialLoadAttempted;
        private BasicEconomySaveAdapter weeklySettlementSaveAdapter;

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
        }

        private void OnApplicationQuit()
        {
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

        private bool TrySave(string reason)
        {
            if (services?.Save == null)
            {
                Debug.LogWarning($"[GameSaveLifecycleService] Save skipped because SaveService is not connected. Reason: {reason}.");
                return false;
            }

            bool saved = services.Save.Save(createAutomaticSlot: true);
            Debug.Log(saved
                ? $"[GameSaveLifecycleService] Game saved because of {reason}."
                : $"[GameSaveLifecycleService] Game save failed because of {reason}.");

            return saved;
        }

        // Unity setup: Add this component to the Services object in the integrated gameplay scene.
        // CityBootstrap initializes it automatically through ICityFlowServiceConsumer.
    }
}
