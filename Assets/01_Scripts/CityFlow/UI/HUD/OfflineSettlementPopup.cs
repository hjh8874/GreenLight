using CityFlow.Bootstrap;
using CityFlow.Contracts.Save;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public sealed class OfflineSettlementPopup : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("UI References")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform reportCard;
        [SerializeField] private Button closeButton;
        [SerializeField] private TextMeshProUGUI initialCoinsText;
        [SerializeField] private TextMeshProUGUI earnedCoinsText;
        [SerializeField] private TextMeshProUGUI currentCoinsText;

        [Header("Presentation")]
        [SerializeField, Min(0f)] private float transitionDuration = 0.18f;
        [SerializeField, Range(0.8f, 1f)] private float hiddenScale = 0.96f;

        private CityFlowServices services;
        private Coroutine transitionRoutine;

        public void Configure(
            CanvasGroup group,
            RectTransform card,
            Button close,
            TextMeshProUGUI initial,
            TextMeshProUGUI earned,
            TextMeshProUGUI current)
        {
            canvasGroup = group;
            reportCard = card;
            closeButton = close;
            initialCoinsText = initial;
            earnedCoinsText = earned;
            currentCoinsText = current;
        }

        public void Initialize(CityFlowServices cityFlowServices)
        {
            services = cityFlowServices;

            if (services?.Save != null)
            {
                services.Save.OfflineSettlementCompleted += OnOfflineSettlementCompleted;
            }
        }

        private void Awake()
        {
            closeButton?.onClick.AddListener(Hide);
            ApplyHiddenState();
        }

        private void OnDestroy()
        {
            closeButton?.onClick.RemoveListener(Hide);

            if (services?.Save != null)
            {
                services.Save.OfflineSettlementCompleted -= OnOfflineSettlementCompleted;
            }
        }

        public void Hide()
        {
            StartTransition(false);
        }

        private void OnOfflineSettlementCompleted(
            OfflineSettlementCompletedEvent settlement)
        {
            initialCoinsText.text = $"{settlement.InitialCoins:N0}";
            earnedCoinsText.text = $"+{settlement.EarnedCoins:N0}";
            currentCoinsText.text = $"{settlement.CurrentCoins:N0}";
            StartTransition(true);
        }

        private void StartTransition(bool show)
        {
            if (transitionRoutine != null)
            {
                StopCoroutine(transitionRoutine);
            }

            transitionRoutine = StartCoroutine(TransitionRoutine(show));
        }

        private System.Collections.IEnumerator TransitionRoutine(bool show)
        {
            if (show)
            {
                SetInteractionEnabled(true);
            }
            else
            {
                SetInteractionEnabled(false);
            }

            float startAlpha = canvasGroup.alpha;
            float targetAlpha = show ? 1f : 0f;
            Vector3 startScale = reportCard.localScale;
            Vector3 targetScale = Vector3.one * (show ? 1f : hiddenScale);
            float elapsed = 0f;

            while (elapsed < transitionDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = transitionDuration <= 0f
                    ? 1f
                    : Mathf.Clamp01(elapsed / transitionDuration);
                canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, progress);
                reportCard.localScale = Vector3.Lerp(startScale, targetScale, progress);
                yield return null;
            }

            canvasGroup.alpha = targetAlpha;
            reportCard.localScale = targetScale;

            if (!show)
            {
                ApplyHiddenState();
            }

            transitionRoutine = null;
        }

        private void ApplyHiddenState()
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                SetInteractionEnabled(false);
            }

            if (reportCard != null)
            {
                reportCard.localScale = Vector3.one * hiddenScale;
            }
        }

        private void SetInteractionEnabled(bool enabled)
        {
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.interactable = enabled;
            canvasGroup.blocksRaycasts = enabled;
        }

        // Unity setup: Bake this UI from Tools > GreenLight > UI while the target scene is open.
    }
}
