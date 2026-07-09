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
        private bool _isBound;
        // [통합 테스트 호환용] 
        // 팀원이 추가한 Configure 함수를 유지하여 테스트 씬(Runtime) 에러를 방지합니다.
        public void Configure(
            PlacementController placement,
            Button road,
            Button house,
            Button office,
            Button remove)
        {
            Configure(placement, road, house, office, remove, null);
        }
        public void Configure(
            PlacementController placement,
            Button road,
            Button house,
            Button office,
            Button remove,
            Button school)
        {
            placementController = placement;
            btnRemove = remove;

            // 테스트 씬용 임시 런타임 버튼 연결 (우리 1차 빌드 본 게임 UI와는 별개로 동작)
            if (road != null) road.onClick.AddListener(() => placementController.SetBuildType(TileType.Road));
            if (house != null) house.onClick.AddListener(() => placementController.SetBuildType(TileType.House));
            if (office != null) office.onClick.AddListener(() => placementController.SetBuildType(TileType.Office));
            if (school != null) school.onClick.AddListener(() => placementController.SetBuildType(TileType.School));
            BindButtons();
        }
        private void Start()
        {
            if (placementController == null)
            {
                Debug.LogError("[BuildPanelController] PlacementController가 할당되지 않았습니다. 인스펙터를 확인해주세요.");
                return;
            }
            // 인스펙터에서 할당 안 했으면 자식 오브젝트에서 자동으로 찾기 (우리의 새로운 슬롯 로직)
            if (buildSlots == null || buildSlots.Length == 0)
            {
                buildSlots = GetComponentsInChildren<BuildSlotController>(true);
            }
            // 슬롯 초기화 및 컨트롤러 주입
            foreach (var slot in buildSlots)
            {
                slot.Initialize(placementController, tooltipController);
            }
            BindButtons();
        }
        private void BindButtons()
        {
            if (_isBound || placementController == null)
            {
                return;
            }
            // 철거 기능은 Empty 타일 타입으로 전달
            if (btnRemove != null) btnRemove.onClick.AddListener(() => placementController.SetBuildType(TileType.Empty));
            _isBound = true;
        }
    }
}