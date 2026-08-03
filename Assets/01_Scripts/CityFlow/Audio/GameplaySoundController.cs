using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Gameplay.Quests;
using CityFlow.Managers;
using CityFlow.UI;
using CityFlow.UI.Controllers;
using UnityEngine;

namespace CityFlow.Audio
{
    public sealed class GameplaySoundController :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        [SerializeField] private SoundManager soundManager;

        private readonly HashSet<PlacementController> placements = new();
        private readonly HashSet<InfrastructurePlacementCoordinator>
            infrastructurePlacements = new();

        private CityFlowServices services;
        private IEconomyService economy;
        private IWorldGridExpansionService worldGridExpansion;
        private CityQuestSystem questSystem;
        private bool coinChangePending;
        private long settledCoins;
        private long observedCoins;
        private bool hadActiveQuest;
        private float nextSceneBindingTime;

        public void Initialize(CityFlowServices cityFlowServices)
        {
            if (ReferenceEquals(services, cityFlowServices))
            {
                return;
            }

            UnbindServices();
            services = cityFlowServices;
            if (services == null)
            {
                return;
            }

            services.EconomyRegistered += BindEconomy;
            services.WorldGridExpansionRegistered += BindWorldGridExpansion;
            BindEconomy(services.Economy);
            BindWorldGridExpansion(services.WorldGridExpansion);
            BindSceneControllers();
        }

        private void Awake()
        {
            soundManager ??= GetComponent<SoundManager>();
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextSceneBindingTime)
            {
                BindSceneControllers();
                nextSceneBindingTime = Time.unscaledTime + 1f;
            }

            FlushCoinChange();
        }

        private void OnDestroy()
        {
            UnbindServices();
            UnbindSceneControllers();
        }

        private void BindEconomy(IEconomyService service)
        {
            if (ReferenceEquals(economy, service))
            {
                return;
            }

            if (economy != null)
            {
                economy.CoinsChanged -= OnCoinsChanged;
            }

            economy = service;
            if (economy == null)
            {
                return;
            }

            settledCoins = economy.Coins;
            observedCoins = economy.Coins;
            coinChangePending = false;
            economy.CoinsChanged += OnCoinsChanged;
        }

        private void BindWorldGridExpansion(IWorldGridExpansionService service)
        {
            if (ReferenceEquals(worldGridExpansion, service))
            {
                return;
            }

            if (worldGridExpansion != null)
            {
                worldGridExpansion.StageChanged -= OnWorldGridStageChanged;
            }

            worldGridExpansion = service;
            if (worldGridExpansion != null)
            {
                worldGridExpansion.StageChanged += OnWorldGridStageChanged;
            }
        }

        private void BindSceneControllers()
        {
            PlacementController[] placementControllers =
                FindObjectsByType<PlacementController>(
                    FindObjectsInactive.Include);
            for (int index = 0; index < placementControllers.Length; index++)
            {
                PlacementController controller = placementControllers[index];
                if (controller == null || !placements.Add(controller))
                {
                    continue;
                }

                controller.PlacementSucceeded += OnPlacementSucceeded;
                controller.PlacementRejected += OnPlacementRejected;
                controller.DemolitionSucceeded += OnDemolitionSucceeded;
            }

            InfrastructurePlacementCoordinator[] coordinators =
                FindObjectsByType<InfrastructurePlacementCoordinator>(
                    FindObjectsInactive.Include);
            for (int index = 0; index < coordinators.Length; index++)
            {
                InfrastructurePlacementCoordinator coordinator = coordinators[index];
                if (coordinator == null ||
                    !infrastructurePlacements.Add(coordinator))
                {
                    continue;
                }

                coordinator.PlacementSucceeded += OnPlacementSucceeded;
                coordinator.PlacementRejected += OnPlacementRejected;
                coordinator.DemolitionSucceeded += OnDemolitionSucceeded;
            }

            if (questSystem == null)
            {
                questSystem = FindAnyObjectByType<CityQuestSystem>(
                    FindObjectsInactive.Include);
                if (questSystem != null)
                {
                    hadActiveQuest = questSystem.CurrentViewState.Quest != null;
                    questSystem.ViewStateChanged += OnQuestViewStateChanged;
                }
            }
        }

        private void UnbindSceneControllers()
        {
            foreach (PlacementController controller in placements)
            {
                if (controller == null)
                {
                    continue;
                }

                controller.PlacementSucceeded -= OnPlacementSucceeded;
                controller.PlacementRejected -= OnPlacementRejected;
                controller.DemolitionSucceeded -= OnDemolitionSucceeded;
            }
            placements.Clear();

            foreach (InfrastructurePlacementCoordinator coordinator in
                     infrastructurePlacements)
            {
                if (coordinator == null)
                {
                    continue;
                }

                coordinator.PlacementSucceeded -= OnPlacementSucceeded;
                coordinator.PlacementRejected -= OnPlacementRejected;
                coordinator.DemolitionSucceeded -= OnDemolitionSucceeded;
            }
            infrastructurePlacements.Clear();

            if (questSystem != null)
            {
                questSystem.ViewStateChanged -= OnQuestViewStateChanged;
                questSystem = null;
            }
        }

        private void OnPlacementSucceeded()
        {
            soundManager?.PlaySfx(SoundIds.PlacementSuccess);
        }

        private void OnPlacementRejected()
        {
            soundManager?.PlaySfx(SoundIds.PlacementRejected);
        }

        private void OnDemolitionSucceeded()
        {
            soundManager?.PlaySfx(SoundIds.DemolitionSuccess);
        }

        private void OnCoinsChanged(long coins)
        {
            observedCoins = coins;
            coinChangePending = true;
        }

        private void FlushCoinChange()
        {
            if (!coinChangePending)
            {
                return;
            }

            coinChangePending = false;
            bool changed = observedCoins != settledCoins;
            settledCoins = observedCoins;
            if (changed && services?.Save?.IsRestoring != true)
            {
                soundManager?.PlaySfx(SoundIds.CoinTransaction);
            }
        }

        private void OnWorldGridStageChanged(WorldGridStageChangedEvent stage)
        {
            if (stage.Reason == WorldGridStageChangeReason.Unlocked)
            {
                soundManager?.PlaySfx(SoundIds.PositiveNotification);
            }
        }

        private void OnQuestViewStateChanged(CityQuestViewState state)
        {
            bool hasActiveQuest = state.Quest != null;
            if (hadActiveQuest &&
                !hasActiveQuest &&
                services?.Save?.IsRestoring != true)
            {
                soundManager?.PlaySfx(SoundIds.PositiveNotification);
            }

            hadActiveQuest = hasActiveQuest;
        }

        private void UnbindServices()
        {
            if (services != null)
            {
                services.EconomyRegistered -= BindEconomy;
                services.WorldGridExpansionRegistered -= BindWorldGridExpansion;
            }

            BindEconomy(null);
            BindWorldGridExpansion(null);
            services = null;
        }

#if UNITY_EDITOR
        public void EditorConfigure(SoundManager manager)
        {
            soundManager = manager;
        }
#endif

        // Unity setup:
        // The baked prefab discovers placement and quest controllers at runtime.
    }
}
