using UnityEngine;
using UnityEngine.UI;
using CityFlow.Contracts;
using DG.Tweening;
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

        [Header("Undo Action")]
        [Tooltip("최근 건설/철거 작업을 되돌리는 버튼")]
        [SerializeField] private Button btnUndo;

        [Header("Categories")]
        [Tooltip("카테고리 탭 버튼들 (인프라, 주거, 상업, 공공 순서 권장)")]
        [SerializeField] private Button[] categoryTabs;
        [Tooltip("각 탭에 해당하는 페이지 오브젝트들 (카테고리 버튼과 동일한 인덱스 매핑)")]
        [SerializeField] private GameObject[] categoryPages;
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
            Button undo,
            Button school)
        {
            placementController = placement;
            btnUndo = undo;

            // 테스트 씬용 임시 런타임 버튼 연결 (우리 1차 빌드 본 게임 UI와는 별개로 동작)
            if (road != null) road.onClick.AddListener(() => placementController.SetBuildType(TileType.Road));
            if (house != null) house.onClick.AddListener(() => placementController.SetBuildType(TileType.House));
            if (office != null) office.onClick.AddListener(() => placementController.SetBuildType(TileType.Office));
            if (school != null) school.onClick.AddListener(() => placementController.SetBuildType(TileType.School));
            BindButtons();
        }
        private void Start()
        {
            // DOTween 등장 팝업 슬라이드 인 애니메이션
            RectTransform rect = GetComponent<RectTransform>();
            if (rect != null)
            {
                float originalY = rect.anchoredPosition.y;
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, originalY - 200f);
                rect.DOAnchorPosY(originalY, 0.5f).SetEase(Ease.OutBack).SetDelay(0.2f);
            }

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

            // 카테고리 버튼 연결
            for (int i = 0; i < categoryTabs.Length; i++)
            {
                int index = i; // 클로저 이슈 방지
                if (categoryTabs[i] != null)
                {
                    categoryTabs[i].onClick.AddListener(() => ShowCategory(index));
                }
            }

            // 초기 탭 활성화 (0번 인덱스)
            if (categoryPages != null && categoryPages.Length > 0)
            {
                ShowCategory(0);
            }

            BindButtons();
        }

        public void ShowCategory(int index)
        {
            if (categoryPages == null) return;

            for (int i = 0; i < categoryPages.Length; i++)
            {
                if (categoryPages[i] != null)
                {
                    bool isActive = (i == index);
                    categoryPages[i].SetActive(isActive);
                }
            }
        }
        private void BindButtons()
        {
            if (_isBound || placementController == null)
            {
                return;
            }
            // 철거 버튼 ➡️ 되돌리기(Undo) 기능으로 변경
            if (btnUndo != null) btnUndo.onClick.AddListener(() => placementController.UndoLastAction());
            _isBound = true;
        }
    }
}