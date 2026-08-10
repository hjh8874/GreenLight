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
        [SerializeField] private TextMeshProUGUI nameText;
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
            _coordinator = FindFirstObjectByType<InfrastructurePlacementCoordinator>();
            _tooltipController = FindFirstObjectByType<TooltipController>(FindObjectsInactive.Include);

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
            Transform nameTransform = transform.Find("NameText");
            Transform costTransform = transform.Find("CostText");

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

        private void ApplyData()
        {
            if (infraData == null)
            {
                if (nameText != null)
                {
                    nameText.text = string.Empty;
                }

                return;
            }

            if (nameText != null)
            {
                nameText.text = infraData.InfrastructureName;
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
                    buttonLabel.text = "건설";
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
