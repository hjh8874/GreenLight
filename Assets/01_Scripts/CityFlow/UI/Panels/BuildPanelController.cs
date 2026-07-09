using UnityEngine;
using UnityEngine.UI;
using CityFlow.Contracts;

namespace CityFlow.UI
{
    public class BuildPanelController : MonoBehaviour
    {
        [Header("System References")]
        [Tooltip("씬에 배치된 PlacementManager (PlacementController) 오브젝트 연결")]
        [SerializeField] private PlacementController placementController;

        [Header("UI System References")]
        [Tooltip("호버 시 설명을 띄워줄 공통 툴팁 컨트롤러 연결")]
        [SerializeField] private TooltipController tooltipController;

        [Header("Slots")]
        [Tooltip("하단 수평 패널에 배치된 슬롯들 연결 (인스펙터 할당 또는 자동 검색)")]
        [SerializeField] private BuildSlotController[] buildSlots;
        
        [Header("Remove Action")]
        [Tooltip("철거 기능은 데이터(SO)가 없으므로 별도의 버튼으로 유지")]
        [SerializeField] private Button btnRemove;

        private void Start()
        {
            if (placementController == null)
            {
                Debug.LogError("[BuildPanelController] PlacementController가 할당되지 않았습니다. 인스펙터를 확인해주세요.");
                return;
            }

            // 인스펙터에서 할당 안 했으면 자식 오브젝트에서 자동으로 찾기
            if (buildSlots == null || buildSlots.Length == 0)
            {
                buildSlots = GetComponentsInChildren<BuildSlotController>(true);
            }

            // 슬롯 초기화 및 컨트롤러 주입
            foreach (var slot in buildSlots)
            {
                slot.Initialize(placementController, tooltipController);
            }

            // 철거 기능은 Empty 타일 타입으로 전달
            if (btnRemove != null) btnRemove.onClick.AddListener(() => placementController.SetBuildType(TileType.Empty)); 
        }
    }
}
