using System.Collections;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public sealed class WeeklySettlementPopup : MonoBehaviour, ICityFlowServiceConsumer
    {
        public static bool IsInteractionBlocked { get; private set; }
        [Header("UI References")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform settlementCard;
        [SerializeField] private Button backdropButton;
        [SerializeField] private TextMeshProUGUI periodText;
        [SerializeField] private TextMeshProUGUI amountText;
        [SerializeField] private TextMeshProUGUI balanceText;

        [Header("Presentation")]
        [SerializeField, Min(0f)] private float visibleDuration = 2.5f;
        [SerializeField, Min(0f)] private float fadeInDuration = 0.18f;
        [SerializeField, Min(0f)] private float fadeOutDuration = 0.25f;
        [SerializeField, Range(0.8f, 1f)] private float hiddenScale = 0.94f;

        private CityFlowServices services;
        private IWeeklyEconomyService weeklyEconomy;
        private Coroutine presentationRoutine;

        public void Configure(
            CanvasGroup group,
            RectTransform card,
            Button backdrop,
            TextMeshProUGUI period,
            TextMeshProUGUI amount,
            TextMeshProUGUI balance)
        {
            canvasGroup = group;
            settlementCard = card;
            backdropButton = backdrop;
            periodText = period;
            amountText = amount;
            balanceText = balance;
        }

        public void Initialize(CityFlowServices cityFlowServices)
        {
            services = cityFlowServices;
            services.WeeklyEconomyRegistered += OnWeeklyEconomyRegistered;

            if (services.WeeklyEconomy != null)
            {
                BindWeeklyEconomy(services.WeeklyEconomy);
            }
        }

        private void Awake()
        {
            backdropButton?.onClick.AddListener(Hide);
            ApplyHiddenState();
        }

        private void OnDestroy()
        {
            backdropButton?.onClick.RemoveListener(Hide);

            if (services != null)
            {
                services.WeeklyEconomyRegistered -= OnWeeklyEconomyRegistered;
            }

            if (weeklyEconomy != null)
            {
                weeklyEconomy.SettlementCompleted -= OnSettlementCompleted;
            }
        }

        public void Hide()
        {
            if (presentationRoutine != null)
            {
                StopCoroutine(presentationRoutine);
            }

            presentationRoutine = StartCoroutine(HideRoutine());
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
                weeklyEconomy.SettlementCompleted -= OnSettlementCompleted;
            }

            weeklyEconomy = service;
            weeklyEconomy.SettlementCompleted += OnSettlementCompleted;
        }

        private void OnSettlementCompleted(WeeklySettlementCompletedEvent settlement)
        {
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            periodText.text = "HARVEST COMPLETE";
            amountText.text = $"+{settlement.Amount:N0} COINS";
            balanceText.text = $"BALANCE  {settlement.BalanceAfterSettlement:N0}";

            if (presentationRoutine != null)
            {
                StopCoroutine(presentationRoutine);
            }

            presentationRoutine = StartCoroutine(ShowRoutine());
        }

        private IEnumerator ShowRoutine()
        {
            SetInteractionEnabled(true);
            float elapsed = 0f;

            while (elapsed < fadeInDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = NormalizedProgress(elapsed, fadeInDuration);
                canvasGroup.alpha = progress;
                settlementCard.localScale = Vector3.one * Mathf.Lerp(hiddenScale, 1f, EaseOutCubic(progress));
                yield return null;
            }

            canvasGroup.alpha = 1f;
            settlementCard.localScale = Vector3.one;

            elapsed = 0f;

            while (elapsed < visibleDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            yield return HideRoutine();
        }

        private IEnumerator HideRoutine()
        {
            SetInteractionEnabled(false);
            float startAlpha = canvasGroup.alpha;
            Vector3 startScale = settlementCard.localScale;
            float elapsed = 0f;

            while (elapsed < fadeOutDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = NormalizedProgress(elapsed, fadeOutDuration);
                canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, progress);
                settlementCard.localScale = Vector3.Lerp(
                    startScale,
                    Vector3.one * hiddenScale,
                    progress);
                yield return null;
            }

            ApplyHiddenState();
            presentationRoutine = null;
            gameObject.SetActive(false);
        }

        private void ApplyHiddenState()
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                SetInteractionEnabled(false);
            }

            if (settlementCard != null)
            {
                settlementCard.localScale = Vector3.one * hiddenScale;
            }
        }

        private void SetInteractionEnabled(bool enabled)
        {
            IsInteractionBlocked = enabled;
            canvasGroup.interactable = enabled;
            canvasGroup.blocksRaycasts = enabled;
        }

        private static float NormalizedProgress(float elapsed, float duration)
        {
            return duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
        }

        private static float EaseOutCubic(float value)
        {
            float inverse = 1f - value;
            return 1f - (inverse * inverse * inverse);
        }

        // Unity setup: Bake this UI from Tools > GreenLight > UI while the target scene is open.
    }
}
