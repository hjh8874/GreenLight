using CityFlow.Configs;
using CityFlow.Contracts;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public class BuildSlotController :
        MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerClickHandler
    {
        [Header("Data")]
        [SerializeField] private TileDataSO tileData;

        [Header("UI References (Self)")]
        [SerializeField] private Image iconImage;
        [SerializeField] private TextMeshProUGUI costText;
        [SerializeField] private Button btnBuy;

        private PlacementController placementController;
        private TooltipController tooltipController;
        private bool isInitialized;
        private bool isInteractable = true;

        public TileDataSO TileData => tileData;

        public void Configure(TileDataSO data)
        {
            tileData = data;
            RefreshContent();
        }

        public void Initialize(
            PlacementController placement,
            TooltipController tooltip
        )
        {
            placementController = placement;
            tooltipController = tooltip;

            ResolveReferences();
            NormalizeLayout();
            BindButton();
            RefreshContent();

            isInitialized = true;
        }

        public void RefreshContent()
        {
            ResolveReferences();

            if (tileData == null)
            {
                if (iconImage != null)
                {
                    iconImage.enabled = false;
                }

                if (costText != null)
                {
                    costText.text = "-";
                }

                SetInteractable(false);
                return;
            }

            if (iconImage != null)
            {
                iconImage.sprite = tileData.BuildingIcon;
                iconImage.enabled = tileData.BuildingIcon != null;
                iconImage.preserveAspect = true;
            }

            if (costText != null)
            {
                costText.text = tileData.BuildCost.ToString("N0");
            }

            SetInteractable(true);
        }

        public void SetInteractable(bool interactable)
        {
            isInteractable = interactable && tileData != null;

            if (btnBuy != null)
            {
                btnBuy.interactable = isInteractable;
            }

            if (iconImage != null)
            {
                Color color = iconImage.color;
                color.a = isInteractable ? 1f : 0.45f;
                iconImage.color = color;
            }

            if (costText != null)
            {
                Color color = costText.color;
                color.a = isInteractable ? 1f : 0.55f;
                costText.color = color;
            }
        }

        private void BindButton()
        {
            if (btnBuy == null)
            {
                return;
            }

            btnBuy.onClick.RemoveListener(OnBuyClicked);
            btnBuy.onClick.AddListener(OnBuyClicked);

            EventTrigger trigger = btnBuy.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = btnBuy.gameObject.AddComponent<EventTrigger>();
            }

            trigger.triggers.Clear();

            EventTrigger.Entry enterEntry = new EventTrigger.Entry
            {
                eventID = EventTriggerType.PointerEnter
            };
            enterEntry.callback.AddListener(_ =>
            {
                if (!isInteractable)
                {
                    return;
                }

                btnBuy.transform.DOKill();
                btnBuy.transform.DOScale(1.1f, 0.2f).SetEase(Ease.OutBack);
            });
            trigger.triggers.Add(enterEntry);

            EventTrigger.Entry exitEntry = new EventTrigger.Entry
            {
                eventID = EventTriggerType.PointerExit
            };
            exitEntry.callback.AddListener(_ =>
            {
                btnBuy.transform.DOKill();
                btnBuy.transform.DOScale(1f, 0.15f).SetEase(Ease.OutQuad);
            });
            trigger.triggers.Add(exitEntry);
        }

        private void ResolveReferences()
        {
            if (iconImage == null)
            {
                Transform iconTransform = transform.Find("Icon");
                if (iconTransform != null)
                {
                    iconImage = iconTransform.GetComponent<Image>();
                }
            }

            if (costText == null)
            {
                Transform costTransform = transform.Find("CostText");
                if (costTransform != null)
                {
                    costText = costTransform.GetComponent<TextMeshProUGUI>();
                }
            }

            if (btnBuy == null)
            {
                Transform buyTransform = transform.Find("Btn_Buy");
                if (buyTransform != null)
                {
                    btnBuy = buyTransform.GetComponent<Button>();
                }
            }
        }

        private void NormalizeLayout()
        {
            if (costText != null)
            {
                RectTransform costRect = costText.rectTransform;
                costRect.anchorMin = new Vector2(0.5f, 1f);
                costRect.anchorMax = new Vector2(0.5f, 1f);
                costRect.pivot = new Vector2(0.5f, 1f);
                costRect.anchoredPosition = new Vector2(0f, -8f);
                costRect.sizeDelta = new Vector2(100f, 30f);
                costText.enableAutoSizing = true;
                costText.fontSizeMin = 14f;
                costText.fontSizeMax = 22f;
                costText.alignment = TextAlignmentOptions.Center;
                costText.textWrappingMode = TextWrappingModes.NoWrap;
            }

            if (iconImage != null)
            {
                RectTransform iconRect = iconImage.rectTransform;
                iconRect.anchorMin = new Vector2(0.5f, 1f);
                iconRect.anchorMax = new Vector2(0.5f, 1f);
                iconRect.pivot = new Vector2(0.5f, 1f);
                iconRect.anchoredPosition = new Vector2(0f, -42f);
                iconRect.sizeDelta = new Vector2(84f, 72f);
                iconImage.preserveAspect = true;
            }

            if (btnBuy != null)
            {
                RectTransform buttonRect = btnBuy.GetComponent<RectTransform>();
                buttonRect.anchorMin = new Vector2(0.5f, 0f);
                buttonRect.anchorMax = new Vector2(0.5f, 0f);
                buttonRect.pivot = new Vector2(0.5f, 0f);
                buttonRect.anchoredPosition = new Vector2(0f, 8f);
                buttonRect.sizeDelta = new Vector2(100f, 38f);

                TextMeshProUGUI buttonLabel =
                    btnBuy.GetComponentInChildren<TextMeshProUGUI>(true);

                if (buttonLabel != null)
                {
                    buttonLabel.enableAutoSizing = true;
                    buttonLabel.fontSizeMin = 14f;
                    buttonLabel.fontSizeMax = 18f;
                    buttonLabel.alignment = TextAlignmentOptions.Center;
                    buttonLabel.textWrappingMode = TextWrappingModes.NoWrap;
                }
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (tooltipController != null && tileData != null)
            {
                tooltipController.ShowTooltip(tileData);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            tooltipController?.HideTooltip();
        }

        private void OnDisable()
        {
            tooltipController?.HideTooltip();

            transform.DOKill();
            if (btnBuy != null)
            {
                btnBuy.transform.DOKill();
                btnBuy.transform.localScale = Vector3.one;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!isInitialized || !isInteractable)
            {
                return;
            }

            // 버튼 자신이나 버튼의 자식을 클릭한 경우에는 Button.onClick이 처리합니다.
            // 슬롯의 빈 영역을 클릭했을 때만 여기서 구매 처리를 호출하여 중복 실행을 막습니다.
            if (btnBuy != null &&
                eventData.pointerPress != null &&
                (eventData.pointerPress == btnBuy.gameObject ||
                 eventData.pointerPress.transform.IsChildOf(btnBuy.transform)))
            {
                return;
            }

            transform.DOKill();
            transform.localScale = Vector3.one;
            transform.DOPunchScale(
                new Vector3(-0.05f, -0.05f, 0f),
                0.15f,
                2,
                0.5f
            );

            OnBuyClicked();
        }

        private void OnBuyClicked()
        {
            if (!isInteractable || placementController == null || tileData == null)
            {
                return;
            }

            if (btnBuy != null)
            {
                btnBuy.transform.DOKill();
                btnBuy.transform.DOPunchScale(
                    new Vector3(-0.2f, -0.2f, 0f),
                    0.2f,
                    5,
                    1f
                );
            }

            placementController.SetBuildType(tileData.Category);
            Debug.Log(
                $"[BuildSlot] {tileData.BuildingName} 건설 모드 활성화",
                this
            );
        }
    }
}
