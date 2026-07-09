using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CityFlow.Contracts;
using CityFlow.Configs;
using TMPro;

namespace CityFlow.UI
{
    public class BuildSlotController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [Header("Data")]
        [SerializeField] private TileDataSO tileData;
        
        [Header("UI References (Self)")]
        [SerializeField] private Image iconImage;
        [SerializeField] private TextMeshProUGUI costText;

        private PlacementController _placementController;
        private TooltipController _tooltipController;

        public void Initialize(PlacementController placement, TooltipController tooltip)
        {
            _placementController = placement;
            _tooltipController = tooltip;

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
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_tooltipController != null && tileData != null)
            {
                _tooltipController.ShowTooltip(tileData.BuildingName, tileData.BuildCost, tileData.BuildingDescription);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_tooltipController != null)
            {
                _tooltipController.HideTooltip();
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_placementController != null && tileData != null)
            {
                _placementController.SetBuildType(tileData.Category);
                Debug.Log($"[BuildSlot] {tileData.BuildingName} 건설 모드 활성화");
            }
        }
    }
}
