using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public class ResearchPanelController : MonoBehaviour
    {
        [Header("Research Buttons")]
        [SerializeField] private Button btnUpgradeSpeed;
        [SerializeField] private Button btnReduceCooldown;

        private void Start()
        {
            if (btnUpgradeSpeed != null) btnUpgradeSpeed.onClick.AddListener(OnUpgradeSpeedClicked);
            if (btnReduceCooldown != null) btnReduceCooldown.onClick.AddListener(OnReduceCooldownClicked);
        }

        private void OnUpgradeSpeedClicked()
        {
            // TODO: 코어 엔진의 연구 시스템(IResearchService 등)이 완성되면 여기에 연결합니다.
            Debug.Log("[Research] 차량 주행 속도 향상 업그레이드 버튼 클릭됨!");
        }

        private void OnReduceCooldownClicked()
        {
            // TODO: 코어 엔진의 연구 시스템(IResearchService 등)이 완성되면 여기에 연결합니다.
            Debug.Log("[Research] 건물 쿨타임 감소 업그레이드 버튼 클릭됨!");
        }
    }
}
