using CityFlow.Bootstrap;
using CityFlow.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public sealed class CoinHarvestButton : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("UI References")]
        [SerializeField] private Button harvestButton;
        [SerializeField] private TextMeshProUGUI pendingText;

        private CityFlowServices services;
        private IWeeklyEconomyService weeklyEconomy;

        public long DisplayedPendingCoins { get; private set; }

        public void Configure(Button button, TextMeshProUGUI label)
        {
            harvestButton = button;
            pendingText = label;
        }

        public void Initialize(CityFlowServices cityFlowServices)
        {
            services = cityFlowServices;
            services.WeeklyEconomyRegistered += OnWeeklyEconomyRegistered;

            if (services.WeeklyEconomy != null)
            {
                BindWeeklyEconomy(services.WeeklyEconomy);
            }
            else
            {
                Refresh(0L);
            }
        }

        private void Awake()
        {
            harvestButton?.onClick.AddListener(Harvest);
            Refresh(0L);
        }

        private void OnDestroy()
        {
            harvestButton?.onClick.RemoveListener(Harvest);

            if (services != null)
            {
                services.WeeklyEconomyRegistered -= OnWeeklyEconomyRegistered;
            }

            if (weeklyEconomy != null)
            {
                weeklyEconomy.PendingCoinsChanged -= Refresh;
            }
        }

        public void Harvest()
        {
            weeklyEconomy?.TryHarvestPendingCoins();
        }

        private void OnWeeklyEconomyRegistered(IWeeklyEconomyService service)
        {
            BindWeeklyEconomy(service);
        }

        private void BindWeeklyEconomy(IWeeklyEconomyService service)
        {
            if (weeklyEconomy == service)
            {
                return;
            }

            if (weeklyEconomy != null)
            {
                weeklyEconomy.PendingCoinsChanged -= Refresh;
            }

            weeklyEconomy = service;
            weeklyEconomy.PendingCoinsChanged += Refresh;
            Refresh(weeklyEconomy.PendingCoins);
        }

        private void Refresh(long pendingCoins)
        {
            DisplayedPendingCoins = System.Math.Max(0L, pendingCoins);

            if (pendingText != null)
            {
                pendingText.text = $"HARVEST  {DisplayedPendingCoins:N0}";
            }

            if (harvestButton != null)
            {
                harvestButton.interactable = DisplayedPendingCoins > 0L;
            }
        }

        // Unity setup: Bake from Tools > GreenLight > UI > Bake Manual Coin Harvest UI.
    }
}
