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
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI costText;
        [SerializeField] private Button btnBuy;

        private PlacementController _placementController;
        private TooltipController _tooltipController;
        private TextMeshProUGUI _buyText;
        private string _defaultBuyText;
        private SpecialBuildingBuildOption _specialBuilding;
        private bool _usesSpecialBuilding;

        public TileDataSO TileData => tileData;
        public string SpecialBuildingId => _usesSpecialBuilding
            ? _specialBuilding.BuildingId
            : string.Empty;

        public void Initialize(PlacementController placement, TooltipController tooltip)
        {
            _placementController = placement;
            _tooltipController = tooltip;

            ResolveReferences();
            NormalizeLayout();

            ApplyPresentation();

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

        public void ConfigureSpecialBuilding(
            SpecialBuildingBuildOption option,
            PlacementController placement,
            TooltipController tooltip)
        {
            _specialBuilding = option;
            _usesSpecialBuilding = true;
            tileData = null;
            Initialize(placement, tooltip);
        }

        public void RefreshSpecialBuilding(
            SpecialBuildingBuildOption option)
        {
            if (!_usesSpecialBuilding ||
                !string.Equals(
                    _specialBuilding.BuildingId,
                    option.BuildingId,
                    System.StringComparison.Ordinal))
            {
                return;
            }

            _specialBuilding = option;
            ApplyPresentation();
        }

        public void RefreshAvailability() => ApplyPresentation();

        private void ResolveReferences()
        {
            Transform iconTransform = transform.Find("Icon");
            Transform nameTransform = transform.Find("NameText");
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

            if (nameTransform != null)
            {
                nameText = nameTransform.GetComponent<TextMeshProUGUI>();
            }
            else if (costText != null)
            {
                GameObject nameObject = Instantiate(
                    costText.gameObject,
                    costText.transform.parent,
                    false);
                nameObject.name = "NameText";
                nameText = nameObject.GetComponent<TextMeshProUGUI>();
            }

            if (buyTransform != null)
            {
                btnBuy = buyTransform.GetComponent<Button>();
                _buyText = buyTransform.GetComponentInChildren<
                    TextMeshProUGUI>(true);
                if (_buyText != null && _defaultBuyText == null)
                {
                    _defaultBuyText = _buyText.text;
                }
            }
        }

        private void NormalizeLayout()
        {
            ConfigureTextLayout(nameText, 62f, 13f);
            ConfigureTextLayout(costText, 42f, 15f);

            if (iconImage != null)
            {
                RectTransform iconRect = iconImage.rectTransform;
                iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                iconRect.anchoredPosition = new Vector2(0f, -3f);
                iconRect.sizeDelta = new Vector2(68f, 68f);
            }
        }

        private static void ConfigureTextLayout(
            TextMeshProUGUI text,
            float verticalPosition,
            float maximumFontSize)
        {
            if (text == null)
            {
                return;
            }

            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, verticalPosition);
            rect.sizeDelta = new Vector2(96f, 20f);

            text.enableAutoSizing = true;
            text.fontSizeMin = 10f;
            text.fontSizeMax = maximumFontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
        }

        private void ApplyPresentation()
        {
            ResolveReferences();

            if (_usesSpecialBuilding)
            {
                if (nameText != null)
                {
                    nameText.text = _specialBuilding.DisplayName;
                }

                if (iconImage != null)
                {
                    iconImage.sprite = _specialBuilding.Icon;
                    iconImage.color = _specialBuilding.Icon != null
                        ? Color.white
                        : _specialBuilding.FallbackColor;
                }

                if (costText != null)
                {
                    costText.text = _specialBuilding.IsUnlocked
                        ? _specialBuilding.BuildCost.ToString()
                        : "잠김";
                }

                if (btnBuy != null)
                {
                    btnBuy.interactable = _specialBuilding.IsUnlocked;
                }

                if (_buyText != null)
                {
                    _buyText.text = _specialBuilding.IsUnlocked
                        ? (_defaultBuyText ?? "건설")
                        : "연구 필요";
                }

                return;
            }

            if (tileData == null)
            {
                if (nameText != null)
                {
                    nameText.text = string.Empty;
                }

                return;
            }

            bool isUnlocked =
                _placementController == null ||
                _placementController.IsTileTypeUnlocked(
                    tileData.Category);

            if (nameText != null)
            {
                nameText.text = tileData.BuildingName;
            }

            if (iconImage != null)
            {
                iconImage.sprite = tileData.BuildingIcon;
                iconImage.color = Color.white;
            }

            if (costText != null)
            {
                costText.text = isUnlocked
                    ? tileData.BuildCost.ToString()
                    : "잠김";
            }

            if (btnBuy != null)
            {
                btnBuy.interactable = isUnlocked;
            }

            if (_buyText != null)
            {
                _buyText.text = isUnlocked
                    ? (_defaultBuyText ?? "건설")
                    : "연구 필요";
            }
        }



        public void OnPointerEnter(PointerEventData eventData)
        {
            // Tooltip 표시 (DOTween 애니메이션은 btnBuy의 EventTrigger로 이동)

            if (_tooltipController != null && _usesSpecialBuilding)
            {
                _tooltipController.ShowTooltip(_specialBuilding);
            }
            else if (_tooltipController != null && tileData != null)
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

            if (_placementController != null && _usesSpecialBuilding)
            {
                if (!_specialBuilding.IsUnlocked)
                {
                    Debug.LogWarning(
                        $"[BuildSlot] {_specialBuilding.DisplayName} requires " +
                        $"research {_specialBuilding.RequiredResearchId}.",
                        this);
                    return;
                }

                if (_placementController.SetSpecialBuilding(
                        _specialBuilding.BuildingId))
                {
                    Debug.Log(
                        $"[BuildSlot] {_specialBuilding.DisplayName} 건설 모드 활성화",
                        this);
                }
            }
            else if (_placementController != null && tileData != null)
            {
                if (!_placementController.IsTileTypeUnlocked(
                        tileData.Category))
                {
                    Debug.LogWarning(
                        $"[BuildSlot] {tileData.BuildingName}은 연구 완료 후 건설할 수 있습니다.",
                        this);
                    return;
                }

                _placementController.SetBuildType(tileData);
                Debug.Log($"[BuildSlot] {tileData.BuildingName} 건설 모드 활성화");
            }
        }
    }
}
