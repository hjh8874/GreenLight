using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using DG.Tweening;
using CityFlow.Configs;
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

        private void Awake()
        {
            // 화면 하단에서 툴팁이 잘리지 않도록 기준점(Pivot)을 좌하단(0, 0)으로 강제 고정
            RectTransform rect = GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.pivot = new Vector2(0f, 0f);
            }

            // 기본적으로 숨겨둠
            gameObject.SetActive(false);
            transform.localScale = Vector3.zero;
        }

        private void Update()
        {
            // 켜져 있을 때만 마우스 커서를 따라다님
            if (Mouse.current != null)
            {
                Vector2 mousePos = Mouse.current.position.ReadValue();
                transform.position = mousePos + offset;
            }
        }

        public void ShowTooltip(TileDataSO tileData)
        {
            if (tileData == null) return;
            
            gameObject.SetActive(true);
            
            if (txtName != null) txtName.text = tileData.BuildingName;
            if (txtCategory != null) txtCategory.text = $"분류: {tileData.Category}";
            if (txtCost != null) txtCost.text = $"비용: {tileData.BuildCost} 코인";
            if (txtIncome != null) txtIncome.text = $"수입: 분당 +{tileData.DailyCoinValue}";
            if (txtEffect != null) txtEffect.text = $"안정도: +{tileData.ProsperityValue}";
            if (txtDescription != null) txtDescription.text = tileData.BuildingDescription;

            // 켜지는 순간 랙 방지를 위해 즉시 위치 동기화
            if (Mouse.current != null)
            {
                transform.position = Mouse.current.position.ReadValue() + offset;
            }

            // DOTween 팝업 애니메이션
            transform.DOKill();
            transform.DOScale(Vector3.one, 0.2f).SetEase(Ease.OutBack);
        }

        public void ShowTooltip(InfrastructureDataSO infraData)
        {
            if (infraData == null) return;
            
            gameObject.SetActive(true);
            
            if (txtName != null) txtName.text = infraData.InfrastructureName;
            if (txtCategory != null) txtCategory.text = $"분류: {infraData.Kind}";
            if (txtCost != null) txtCost.text = $"비용: {infraData.Cost} 코인";
            if (txtIncome != null) txtIncome.text = "유지비: 자동"; // 인프라는 별도 수입이 없으므로 안내 문구 대체
            if (txtEffect != null) txtEffect.text = "효과: 교통 흐름 제어"; // 인프라 범용 효과
            if (txtDescription != null) txtDescription.text = infraData.Description;

            if (Mouse.current != null)
            {
                transform.position = Mouse.current.position.ReadValue() + offset;
            }

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
    }
}
