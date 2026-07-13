using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CityFlow.UI.Data;
using TMPro;
using DG.Tweening;

namespace CityFlow.UI.Controllers
{
    public class InfrastructureSlotController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [Header("Data")]
        [SerializeField] private InfrastructureDataSO infraData;
        
        [Header("UI References (Self)")]
        [SerializeField] private Image iconImage;
        [SerializeField] private TextMeshProUGUI costText;
        [SerializeField] private Button btnBuy;

        private InfrastructurePlacementCoordinator _coordinator;
        private TooltipController _tooltipController;

        private void Start()
        {
            // 에디터 씬에 있는 코디네이터를 자동으로 찾아서 연결합니다.
            _coordinator = FindFirstObjectByType<InfrastructurePlacementCoordinator>();
            _tooltipController = FindFirstObjectByType<TooltipController>(FindObjectsInactive.Include);

            // UI 자동 세팅
            if (infraData != null)
            {
                if (iconImage != null && infraData.Icon != null)
                {
                    iconImage.sprite = infraData.Icon;
                }
                
                if (costText != null)
                {
                    costText.text = infraData.Cost.ToString();
                }
            }

            if (btnBuy != null)
            {
                // 인스펙터 OnClick 외에 코드에서도 클릭 처리 및 DOTween 효과 추가
                btnBuy.onClick.RemoveAllListeners();
                btnBuy.onClick.AddListener(OnBuyClicked);

                EventTrigger trigger = btnBuy.gameObject.GetComponent<EventTrigger>();
                if (trigger == null) trigger = btnBuy.gameObject.AddComponent<EventTrigger>();
                trigger.triggers.Clear();
                
                EventTrigger.Entry enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enterEntry.callback.AddListener((data) => {
                    btnBuy.transform.DOKill();
                    btnBuy.transform.DOScale(1.1f, 0.2f).SetEase(Ease.OutBack);
                });
                trigger.triggers.Add(enterEntry);

                EventTrigger.Entry exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                exitEntry.callback.AddListener((data) => {
                    btnBuy.transform.DOKill();
                    btnBuy.transform.DOScale(1f, 0.15f).SetEase(Ease.OutQuad);
                });
                trigger.triggers.Add(exitEntry);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_tooltipController != null && infraData != null)
            {
                _tooltipController.ShowTooltip(infraData);
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
            transform.DOKill();
            transform.localScale = Vector3.one; 
            transform.DOPunchScale(new Vector3(-0.05f, -0.05f, 0f), 0.15f, 2, 0.5f);

            if (btnBuy == null) OnBuyClicked();
        }

        private void OnBuyClicked()
        {
            if (btnBuy != null)
            {
                btnBuy.transform.DOPunchScale(new Vector3(-0.2f, -0.2f, 0f), 0.2f, 5, 1f);
            }

            if (_coordinator != null && infraData != null)
            {
                _coordinator.StartPlacement(infraData);
            }
        }
    }
}
