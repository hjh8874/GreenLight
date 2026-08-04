using CityFlow.Bootstrap;
using CityFlow.Contracts;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public sealed class CoinHarvestButton : MonoBehaviour, ICityFlowServiceConsumer
    {
        private const float CountUpDuration = 0.48f;
        private const float CountUpAfterglowDuration = 0.8f;

        [Header("UI References")]
        [SerializeField] private Button harvestButton;
        [SerializeField] private TextMeshProUGUI pendingText;
        [SerializeField] private TextMeshProUGUI receiptText;

        private CityFlowServices services;
        private IWeeklyEconomyService weeklyEconomy;
        private Graphic buttonGraphic;
        private Color baseButtonColor = new Color(0.12f, 0.55f, 0.48f, 0.98f);
        private Coroutine countUpRoutine;
        private bool isCountingUp;

        public long DisplayedPendingCoins { get; private set; }

        public void Configure(Button button, TextMeshProUGUI label)
        {
            harvestButton = button;
            pendingText = label;
            buttonGraphic = harvestButton?.targetGraphic;
            if (buttonGraphic != null)
            {
                baseButtonColor = buttonGraphic.color;
            }
        }

        public void Configure(
            Button button,
            TextMeshProUGUI label,
            TextMeshProUGUI receipt)
        {
            Configure(button, label);
            receiptText = receipt;
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
            buttonGraphic = harvestButton?.targetGraphic;
            if (buttonGraphic != null)
            {
                baseButtonColor = buttonGraphic.color;
            }
            harvestButton?.onClick.AddListener(Harvest);
            if (receiptText != null)
            {
                receiptText.enabled = false;
            }
            Refresh(0L);
        }

        private void Update()
        {
            if (harvestButton == null || buttonGraphic == null)
            {
                return;
            }

            if (DisplayedPendingCoins <= 0L)
            {
                buttonGraphic.color = baseButtonColor;
                harvestButton.transform.localScale = Vector3.one;
                return;
            }

            float pulse = (Mathf.Sin(Time.unscaledTime * 5.5f) + 1f) * 0.5f;
            buttonGraphic.color = Color.Lerp(
                baseButtonColor,
                new Color(1f, 0.78f, 0.22f, 1f),
                0.2f + pulse * 0.25f);
            float scale = 1f + pulse * 0.035f;
            harvestButton.transform.localScale = new Vector3(scale, scale, 1f);
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
            if (weeklyEconomy == null || DisplayedPendingCoins <= 0L)
            {
                return;
            }

            long amount = DisplayedPendingCoins;
            var breakdown = new Dictionary<string, long>(
                weeklyEconomy.PendingBreakdown);

            if (!weeklyEconomy.TryHarvestPendingCoins())
            {
                return;
            }

            ShowReceipt(breakdown, amount);
            if (countUpRoutine != null)
            {
                StopCoroutine(countUpRoutine);
            }

            countUpRoutine = StartCoroutine(CountUp(amount));
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

            if (pendingText != null && !isCountingUp)
            {
                pendingText.text = $"HARVEST  {DisplayedPendingCoins:N0}";
            }

            if (harvestButton != null)
            {
                harvestButton.interactable = DisplayedPendingCoins > 0L;
            }
        }

        private IEnumerator CountUp(long amount)
        {
            isCountingUp = true;
            float elapsed = 0f;

            while (elapsed < CountUpDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                double progress = Mathf.Clamp01(elapsed / CountUpDuration);
                long shown = (long)System.Math.Min(
                    (double)amount,
                    System.Math.Round(progress * amount));
                if (pendingText != null)
                {
                    pendingText.text = $"HARVEST  +{shown:N0}";
                }

                yield return null;
            }

            if (pendingText != null)
            {
                pendingText.text = $"HARVEST  +{amount:N0}";
            }

            yield return new WaitForSecondsRealtime(CountUpAfterglowDuration);

            if (receiptText != null)
            {
                receiptText.enabled = false;
            }

            isCountingUp = false;
            countUpRoutine = null;
            Refresh(weeklyEconomy?.PendingCoins ?? 0L);
        }

        private void ShowReceipt(
            IReadOnlyDictionary<string, long> breakdown,
            long amount)
        {
            if (receiptText == null)
            {
                return;
            }

            var lines = new List<string> { $"RECEIPT  +{amount:N0}" };
            foreach (KeyValuePair<string, long> entry in breakdown)
            {
                if (entry.Value > 0L)
                {
                    lines.Add($"{entry.Key}  +{entry.Value:N0}");
                }
            }

            receiptText.text = string.Join("\n", lines);
            receiptText.enabled = true;
        }

        // Unity setup: Bake from Tools > GreenLight > UI > Bake Manual Coin Harvest UI.
    }
}
