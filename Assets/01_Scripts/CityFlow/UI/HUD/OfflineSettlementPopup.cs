using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    /// <summary>
    /// 씬에 남아 있는 오프라인 정산 리포트의 표시와 닫기 동작을 담당합니다.
    /// 현재 정산 시스템과는 결합하지 않으며, 리포트가 제거되기 전까지 닫기 전 입력을 차단합니다.
    /// </summary>
    public sealed class OfflineSettlementPopup : MonoBehaviour
    {
        public static bool IsInteractionBlocked { get; private set; }

        [Header("UI References")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform reportCard;
        [SerializeField] private Button closeButton;

        [Header("Presentation")]
        [SerializeField, Min(0f)] private float transitionDuration = 0.18f;
        [SerializeField, Range(0.8f, 1f)] private float hiddenScale = 0.96f;

        private Coroutine transitionRoutine;

        private void OnEnable()
        {
            IsInteractionBlocked = true;
            ResolveReferences();
            transform.SetAsLastSibling();
            if (closeButton != null)
            {
                closeButton.interactable = true;
                closeButton.onClick.RemoveListener(Hide);
                closeButton.onClick.AddListener(Hide);
            }

            ApplyVisibleState();
        }

        private void OnDisable()
        {
            IsInteractionBlocked = false;
            closeButton?.onClick.RemoveListener(Hide);
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

            if (closeButton != null)
            {
                return;
            }

            Button[] buttons = GetComponentsInChildren<Button>(true);
            foreach (Button button in buttons)
            {
                if (button.name == "CloseButton")
                {
                    closeButton = button;
                    break;
                }
            }
        }

        public void Hide()
        {
            if (transitionRoutine != null)
            {
                StopCoroutine(transitionRoutine);
            }

            transitionRoutine = StartCoroutine(HideRoutine());
        }

        private IEnumerator HideRoutine()
        {
            SetInteractionEnabled(false);

            float startAlpha = canvasGroup != null ? canvasGroup.alpha : 1f;
            Vector3 startScale = reportCard != null ? reportCard.localScale : Vector3.one;
            Vector3 targetScale = Vector3.one * hiddenScale;
            float elapsed = 0f;

            while (elapsed < transitionDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = transitionDuration <= 0f
                    ? 1f
                    : Mathf.Clamp01(elapsed / transitionDuration);

                if (canvasGroup != null)
                {
                    canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, progress);
                }

                if (reportCard != null)
                {
                    reportCard.localScale = Vector3.Lerp(startScale, targetScale, progress);
                }

                yield return null;
            }

            transitionRoutine = null;
            gameObject.SetActive(false);
        }

        private void ApplyVisibleState()
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
            }

            if (reportCard != null)
            {
                reportCard.localScale = Vector3.one;
            }

            SetInteractionEnabled(true);
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
    }
}
