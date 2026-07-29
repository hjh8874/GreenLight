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
        public InfrastructureDataSO InfraData => infraData;
        
        [Header("UI References (Self)")]
        [SerializeField] private Image iconImage;
        [SerializeField] private TextMeshProUGUI costText;
        [SerializeField] private Button btnBuy;

        private InfrastructurePlacementCoordinator _coordinator;
        private TooltipController _tooltipController;

        public void Configure(InfrastructureDataSO data)
        {
            infraData = data;
            ResolveReferences();
            NormalizeLayout();
            ApplyData();
        }

        private void Start()
        {
            // 에디터 씬에 있는 코디네이터를 자동으로 찾아서 연결합니다.
            _coordinator = FindAnyObjectByType<InfrastructurePlacementCoordinator>();
            _tooltipController = FindAnyObjectByType<TooltipController>(FindObjectsInactive.Include);

            ResolveReferences();
            NormalizeLayout();
            ApplyData();

            // UI 자동 세팅
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

        private void ResolveReferences()
        {
            if (btnBuy == null)
            {
                Transform buyTransform = transform.Find("Btn_Buy");
                if (buyTransform != null) btnBuy = buyTransform.GetComponent<Button>();
                else btnBuy = GetComponent<Button>();
            }

            Transform iconTransform = transform.Find("Icon");
            Transform costTransform = transform.Find("CostText");

            if (iconTransform != null)
            {
                iconImage = iconTransform.GetComponent<Image>();
            }

            if (costTransform != null)
            {
                costText = costTransform.GetComponent<TextMeshProUGUI>();
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

        private void ApplyData()
        {
            if (infraData == null)
            {
                return;
            }

            if (iconImage != null)
            {
                iconImage.sprite = infraData.Icon;
            }

            if (costText != null)
            {
                costText.text = infraData.Cost.ToString();
            }

            if (btnBuy != null)
            {
                TextMeshProUGUI buttonLabel = btnBuy.GetComponentInChildren<TextMeshProUGUI>(true);
                if (buttonLabel != null)
                {
                    buttonLabel.text = infraData.InfrastructureName;
                }
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

            OnBuyClicked();
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
