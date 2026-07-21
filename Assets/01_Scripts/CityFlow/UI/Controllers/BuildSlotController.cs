using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CityFlow.Contracts;
using CityFlow.Configs;
using TMPro;
using DG.Tweening;

namespace CityFlow.UI
{
    public class BuildSlotController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [Header("Data")]
        [SerializeField] private TileDataSO tileData;
        
        [Header("UI References (Self)")]
        [SerializeField] private Image iconImage;
        [SerializeField] private TextMeshProUGUI costText;
        [SerializeField] private Button btnBuy;

        private PlacementController _placementController;
        private TooltipController _tooltipController;

        public TileDataSO TileData => tileData;

        public void Initialize(PlacementController placement, TooltipController tooltip)
        {
            _placementController = placement;
            _tooltipController = tooltip;

            ResolveReferences();
            NormalizeLayout();

            // UI 초기화
            if (tileData != null)
            {
                if (iconImage != null && tileData.BuildingIcon != null)
                {
                    iconImage.sprite = tileData.BuildingIcon;
                }
                
                if (costText != null)
                {
                    costText.text = tileData.BuildCost.ToString();
                }
            }

            if (btnBuy != null)
            {
                btnBuy.onClick.RemoveAllListeners();
                btnBuy.onClick.AddListener(OnBuyClicked);

                // Add EventTrigger for DOTween Hover on btnBuy only
                EventTrigger trigger = btnBuy.gameObject.GetComponent<EventTrigger>();
                if (trigger == null) trigger = btnBuy.gameObject.AddComponent<EventTrigger>();
                trigger.triggers.Clear();
                
                EventTrigger.Entry enterEntry = new EventTrigger.Entry();
                enterEntry.eventID = EventTriggerType.PointerEnter;
                enterEntry.callback.AddListener((data) => {
                    btnBuy.transform.DOKill();
                    btnBuy.transform.DOScale(1.1f, 0.2f).SetEase(Ease.OutBack);
                });
                trigger.triggers.Add(enterEntry);

                EventTrigger.Entry exitEntry = new EventTrigger.Entry();
                exitEntry.eventID = EventTriggerType.PointerExit;
                exitEntry.callback.AddListener((data) => {
                    btnBuy.transform.DOKill();
                    btnBuy.transform.DOScale(1f, 0.15f).SetEase(Ease.OutQuad);
                });
                trigger.triggers.Add(exitEntry);
            }
        }

        private void ResolveReferences()
        {
            Transform iconTransform = transform.Find("Icon");
            Transform costTransform = transform.Find("CostText");
            Transform buyTransform = transform.Find("Btn_Buy");

            if (iconTransform != null)
            {
                iconImage = iconTransform.GetComponent<Image>();
            }

            if (costTransform != null)
            {
                costText = costTransform.GetComponent<TextMeshProUGUI>();
            }

            if (buyTransform != null)
            {
                btnBuy = buyTransform.GetComponent<Button>();
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
            }

            if (btnBuy != null)
            {
                RectTransform buttonRect = btnBuy.GetComponent<RectTransform>();
                buttonRect.anchorMin = new Vector2(0.5f, 0f);
                buttonRect.anchorMax = new Vector2(0.5f, 0f);
                buttonRect.pivot = new Vector2(0.5f, 0f);
                buttonRect.anchoredPosition = new Vector2(0f, 8f);
                buttonRect.sizeDelta = new Vector2(100f, 38f);

                TextMeshProUGUI buttonLabel = btnBuy.GetComponentInChildren<TextMeshProUGUI>(true);
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
            // Tooltip 표시 (DOTween 애니메이션은 btnBuy의 EventTrigger로 이동)

            if (_tooltipController != null && tileData != null)
            {
                _tooltipController.ShowTooltip(tileData);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // Tooltip 숨기기 (DOTween 애니메이션은 btnBuy의 EventTrigger로 이동)

            if (_tooltipController != null)
            {
                _tooltipController.HideTooltip();
            }
        }

        private void OnDisable()
        {
            if (_tooltipController != null)
            {
                _tooltipController.HideTooltip();
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // 전체 슬롯 클릭 시 가벼운 선택 애니메이션
            transform.DOKill();
            transform.localScale = Vector3.one; 
            transform.DOPunchScale(new Vector3(-0.05f, -0.05f, 0f), 0.15f, 2, 0.5f);

            OnBuyClicked();
        }

        private void OnBuyClicked()
        {
            if (btnBuy != null)
            {
                btnBuy.transform.DOPunchScale(new Vector3(-0.2f, -0.2f, 0f), 0.2f, 5, 1f);
            }

            if (_placementController != null && tileData != null)
            {
                _placementController.SetBuildType(tileData.Category);
                Debug.Log($"[BuildSlot] {tileData.BuildingName} 건설 모드 활성화");
            }
        }
    }
}
