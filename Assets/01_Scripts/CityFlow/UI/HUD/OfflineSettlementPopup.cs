using System.Collections;
using CityFlow.Bootstrap;
using CityFlow.Contracts.Save;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    /// <summary>
    /// 씬에 남아 있는 오프라인 정산 리포트의 표시와 닫기 동작을 담당합니다.
    /// 현재 정산 시스템과는 결합하지 않으며, 리포트가 제거되기 전까지 닫기 전 입력을 차단합니다.
    /// </summary>
    public sealed class OfflineSettlementPopup :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        public static bool IsInteractionBlocked { get; private set; }

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
                services.Save.OfflineSettlementCompleted +=
                    OnOfflineSettlementCompleted;
            }
        }

        private void Awake()
        {
            ResolveReferences();
            BindCloseButton();
            ApplyHiddenState();
        }

        private void OnEnable()
        {
            ResolveReferences();
            BindCloseButton();
        }

        private void OnDisable()
        {
            IsInteractionBlocked = false;
            closeButton?.onClick.RemoveListener(Hide);
        }

        private void OnDestroy()
        {
            if (services?.Save != null)
            {
                services.Save.OfflineSettlementCompleted -=
                    OnOfflineSettlementCompleted;
            }
        }

        private void BindCloseButton()
        {
            if (closeButton != null)
            {
                closeButton.interactable = true;
                closeButton.onClick.RemoveListener(Hide);
                closeButton.onClick.AddListener(Hide);
            }
        }

        private void ResolveReferences()
        {
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }

            if (reportCard == null)
            {
                reportCard = transform.Find("OfflineReportCard") as RectTransform;
            }

            Button[] buttons = GetComponentsInChildren<Button>(true);
            TextMeshProUGUI[] texts =
                GetComponentsInChildren<TextMeshProUGUI>(true);

            foreach (Button button in buttons)
            {
                if (closeButton == null && button.name == "CloseButton")
                {
                    closeButton = button;
                }
            }

            foreach (TextMeshProUGUI text in texts)
            {
                switch (text.name)
                {
                    case "InitialCoinsValue":
                        initialCoinsText ??= text;
                        break;
                    case "EarnedCoinsValue":
                        earnedCoinsText ??= text;
                        break;
                    case "CurrentCoinsValue":
                        currentCoinsText ??= text;
                        break;
                }
            }
        }

        private void OnOfflineSettlementCompleted(
            OfflineSettlementCompletedEvent settlement)
        {
            SetText(
                initialCoinsText,
                $"{settlement.InitialCoins:N0}");
            SetText(
                earnedCoinsText,
                $"+{settlement.EarnedCoins:N0}");
            SetText(
                currentCoinsText,
                $"{settlement.CurrentCoins:N0}");

            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            transform.SetAsLastSibling();
            IsInteractionBlocked = true;
            StartTransition(true);
        }

        public void Hide()
        {
            StartTransition(false);
        }

        private void StartTransition(bool show)
        {
            if (transitionRoutine != null)
            {
                StopCoroutine(transitionRoutine);
            }

            transitionRoutine = StartCoroutine(
                TransitionRoutine(show));
        }

        private IEnumerator TransitionRoutine(bool show)
        {
            SetInteractionEnabled(show);

            float startAlpha =
                canvasGroup != null ? canvasGroup.alpha : 0f;
            float targetAlpha = show ? 1f : 0f;
            Vector3 startScale = reportCard != null ? reportCard.localScale : Vector3.one;
            Vector3 targetScale =
                Vector3.one * (show ? 1f : hiddenScale);
            float elapsed = 0f;

            while (elapsed < transitionDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = transitionDuration <= 0f
                    ? 1f
                    : Mathf.Clamp01(elapsed / transitionDuration);

                if (canvasGroup != null)
                {
                    canvasGroup.alpha = Mathf.Lerp(
                        startAlpha,
                        targetAlpha,
                        progress);
                }

                if (reportCard != null)
                {
                    reportCard.localScale = Vector3.Lerp(startScale, targetScale, progress);
                }

                yield return null;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = targetAlpha;
            }

            if (reportCard != null)
            {
                reportCard.localScale = targetScale;
            }

            transitionRoutine = null;

            if (!show)
            {
                IsInteractionBlocked = false;
                gameObject.SetActive(false);
            }
        }

        private void ApplyHiddenState()
        {
            IsInteractionBlocked = false;

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }

            if (reportCard != null)
            {
                reportCard.localScale =
                    Vector3.one * hiddenScale;
            }

            SetInteractionEnabled(false);
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

        private static void SetText(
            TextMeshProUGUI target,
            string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }

        // Unity setup: Bake this UI from Tools > GreenLight > UI while the target scene is open.
    }
}
