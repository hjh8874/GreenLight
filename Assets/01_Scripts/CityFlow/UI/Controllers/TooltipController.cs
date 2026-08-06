using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using DG.Tweening;
using CityFlow.Configs;
using CityFlow.Contracts;
using CityFlow.UI.Data;

namespace CityFlow.UI
{
    public class TooltipController : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TextMeshProUGUI txtName;
        [SerializeField] private TextMeshProUGUI txtCategory;
        [SerializeField] private TextMeshProUGUI txtCost;
        [SerializeField] private TextMeshProUGUI txtIncome;
        [SerializeField] private TextMeshProUGUI txtEffect;
        [SerializeField] private TextMeshProUGUI txtDescription;

        [Header("Settings")]
        [Tooltip("마우스 커서 위치에서 툴팁을 얼마나 떨어뜨릴지 결정합니다.")]
        [SerializeField] private Vector2 offset = new Vector2(30f, 30f);

        private RectTransform tooltipRect;
        private Canvas rootCanvas;

        private void Awake()
        {
            // 화면 하단에서 툴팁이 잘리지 않도록 기준점(Pivot)을 좌하단(0, 0)으로 강제 고정
            tooltipRect = GetComponent<RectTransform>();
            rootCanvas = GetComponentInParent<Canvas>();
            if (tooltipRect != null)
            {
                tooltipRect.pivot = new Vector2(0f, 0f);
            }

            // 유저 지시에 따라 안정도와 수입 텍스트 라인을 완전히 숨김
            if (txtIncome != null) txtIncome.gameObject.SetActive(false);
            if (txtEffect != null) txtEffect.gameObject.SetActive(false);

            // 기본적으로 숨겨둠
            gameObject.SetActive(false);
            transform.localScale = Vector3.zero;
        }

        private void Update()
        {
            // 켜져 있을 때만 마우스 커서를 따라다님
            if (Mouse.current != null)
            {
                UpdateTooltipPosition();
            }
        }

        public void ShowTooltip(TileDataSO tileData)
        {
            if (tileData == null) return;
            
            gameObject.SetActive(true);
            if (txtIncome != null) txtIncome.gameObject.SetActive(false);
            if (txtEffect != null) txtEffect.gameObject.SetActive(false);
            
            if (txtName != null) txtName.text = tileData.BuildingName;
            if (txtCategory != null) txtCategory.text = $"분류: {tileData.Category}";
            if (txtCost != null) txtCost.text = $"비용: <color=#FFD700>{tileData.BuildCost}</color> 코인";
            if (txtDescription != null) txtDescription.text = tileData.BuildingDescription;

            RefreshLayoutAndPosition();

            // DOTween 팝업 애니메이션
            transform.DOKill();
            transform.DOScale(Vector3.one, 0.2f).SetEase(Ease.OutBack);
        }

        public void ShowTooltip(InfrastructureDataSO infraData)
        {
            if (infraData == null) return;
            
            gameObject.SetActive(true);
            if (txtIncome != null) txtIncome.gameObject.SetActive(false);
            if (txtEffect != null) txtEffect.gameObject.SetActive(false);
            
            if (txtName != null) txtName.text = infraData.InfrastructureName;
            if (txtCategory != null) txtCategory.text = $"분류: {infraData.Kind}";
            if (txtCost != null) txtCost.text = $"비용: <color=#FFD700>{infraData.Cost}</color> 코인";
            if (txtDescription != null) txtDescription.text = infraData.Description;

            RefreshLayoutAndPosition();

            transform.DOKill();
            transform.DOScale(Vector3.one, 0.2f).SetEase(Ease.OutBack);
        }

        public void ShowTooltip(SpecialBuildingBuildOption option)
        {
            if (string.IsNullOrEmpty(option.BuildingId))
            {
                return;
            }

            gameObject.SetActive(true);
            if (txtIncome != null) txtIncome.gameObject.SetActive(false);
            if (txtEffect != null) txtEffect.gameObject.SetActive(false);

            if (txtName != null) txtName.text = option.DisplayName;
            if (txtCategory != null)
                txtCategory.text = $"분류: {option.CategoryName}";
            if (txtCost != null)
                txtCost.text = $"비용: <color=#FFD700>{option.BuildCost:N0}</color> 코인";
            if (txtDescription != null)
                txtDescription.text = option.Description;

            RefreshLayoutAndPosition();

            transform.DOKill();
            transform.DOScale(Vector3.one, 0.2f).SetEase(Ease.OutBack);
        }

        public void HideTooltip()
        {
            // 부드럽게 축소 후 비활성화
            transform.DOKill();
            transform.DOScale(Vector3.zero, 0.15f).SetEase(Ease.InBack).OnComplete(() => 
            {
                gameObject.SetActive(false);
            });
        }

        private void RefreshLayoutAndPosition()
        {
            if (tooltipRect == null)
            {
                tooltipRect = GetComponent<RectTransform>();
            }

            Canvas.ForceUpdateCanvases();
            if (tooltipRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipRect);
            }

            UpdateTooltipPosition();
        }

        private void UpdateTooltipPosition()
        {
            if (Mouse.current == null || tooltipRect == null)
            {
                return;
            }

            Vector2 position = Mouse.current.position.ReadValue() + offset;
            float scaleFactor = rootCanvas != null
                ? Mathf.Max(0.01f, rootCanvas.scaleFactor)
                : 1f;
            Vector2 size = tooltipRect.rect.size * scaleFactor;
            const float screenMargin = 8f;
            position.x = Mathf.Clamp(
                position.x,
                screenMargin,
                Mathf.Max(screenMargin, Screen.width - size.x - screenMargin));
            position.y = Mathf.Clamp(
                position.y,
                screenMargin,
                Mathf.Max(screenMargin, Screen.height - size.y - screenMargin));
            transform.position = position;
        }
    }
}
