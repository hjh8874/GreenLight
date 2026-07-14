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
            // 전체 슬롯 클릭 시 가벼운 선택 애니메이션 (실제 건설은 btnBuy가 담당)
            transform.DOKill();
            transform.localScale = Vector3.one; 
            transform.DOPunchScale(new Vector3(-0.05f, -0.05f, 0f), 0.15f, 2, 0.5f);

            // 만약 별도의 구매 버튼(btnBuy)이 연결되지 않은 기존 프리팹이라면 호환성을 위해 여기서 처리합니다.
            if (btnBuy == null)
            {
                OnBuyClicked();
            }
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
                FindFirstObjectByType<UIDockController>()?.CollapseBuildPanelForPlacement();
                Debug.Log($"[BuildSlot] {tileData.BuildingName} 건설 모드 활성화");
            }
        }
    }
}
