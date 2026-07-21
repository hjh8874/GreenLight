using UnityEngine;
using UnityEngine.UI;
using CityFlow.Contracts;
using CityFlow.UI.Controllers;
using CityFlow.UI.Data;
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

        [Header("Categories")]
        [Tooltip("카테고리 탭 버튼들 (인프라, 주거, 상업, 공공 순서 권장)")]
        [SerializeField] private Button[] categoryTabs;
        [Tooltip("각 탭에 해당하는 페이지 오브젝트들 (카테고리 버튼과 동일한 인덱스 매핑)")]
        [SerializeField] private GameObject[] categoryPages;

        [Header("Infrastructure")]
        [Tooltip("Infra 탭에 별도 슬롯으로 추가할 우선도로 데이터")]
        [SerializeField] private InfrastructureDataSO priorityRoadData;
        [Tooltip("Infra 탭에 별도 슬롯으로 추가할 고속도로 데이터")]
        [SerializeField] private InfrastructureDataSO highwayData;

        private InfrastructureDataSO _runtimeHighwayData;

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

            // 테스트 씬용 임시 런타임 버튼 연결 (우리 1차 빌드 본 게임 UI와는 별개로 동작)
            if (road != null) road.onClick.AddListener(() => placementController.SetBuildType(TileType.Road));
            if (house != null) house.onClick.AddListener(() => placementController.SetBuildType(TileType.House));
            if (office != null) office.onClick.AddListener(() => placementController.SetBuildType(TileType.Office));
            if (remove != null) remove.onClick.AddListener(() => placementController.SetBuildType(TileType.Empty));
            if (school != null) school.onClick.AddListener(() => placementController.SetBuildType(TileType.School));
            BindButtons();
        }
        private void Start()
        {
            LocalizeCategoryTabs();

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

            ConfigureInfrastructureSlots();
            EnsureHighwaySlot();

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

        private void EnsurePriorityRoadSlot()
        {
            if (priorityRoadData == null || categoryPages == null || categoryPages.Length == 0 || categoryPages[0] == null)
            {
                return;
            }

            Transform infraPage = categoryPages[0].transform;
            InfrastructureSlotController[] infrastructureSlots =
                infraPage.GetComponentsInChildren<InfrastructureSlotController>(true);

            InfrastructureSlotController template = null;
            foreach (InfrastructureSlotController slot in infrastructureSlots)
            {
                if (slot.InfraData != null && slot.InfraData.Kind == InfrastructureKind.PriorityRoad)
                {
                    return;
                }

                if (template == null)
                {
                    template = slot;
                }
            }

            if (template == null)
            {
                Debug.LogWarning("[BuildPanelController] 우선도로 슬롯을 복제할 인프라 슬롯이 없습니다.");
                return;
            }

            InfrastructureSlotController priorityRoadSlot = Instantiate(template, infraPage);
            priorityRoadSlot.name = "PriorityRoad_Slot";
            priorityRoadSlot.Configure(priorityRoadData);
            priorityRoadSlot.transform.SetSiblingIndex(Mathf.Min(5, infraPage.childCount - 1));
        }

        private void ConfigureInfrastructureSlots()
        {
            if (categoryPages == null || categoryPages.Length == 0 || categoryPages[0] == null)
            {
                return;
            }

            Transform infraPage = categoryPages[0].transform;
            BuildSlotController roadSlot = null;
            BuildSlotController[] buildSlots = infraPage.GetComponentsInChildren<BuildSlotController>(true);
            foreach (BuildSlotController slot in buildSlots)
            {
                bool isRoad = roadSlot == null &&
                              slot.TileData != null &&
                              slot.TileData.Category == TileType.Road;
                slot.gameObject.SetActive(isRoad);

                if (isRoad)
                {
                    roadSlot = slot;
                }
            }

            InfrastructureSlotController signalSlot = null;
            InfrastructureSlotController roundaboutSlot = null;
            InfrastructureSlotController[] infrastructureSlots =
                infraPage.GetComponentsInChildren<InfrastructureSlotController>(true);

            foreach (InfrastructureSlotController slot in infrastructureSlots)
            {
                bool isSignal = signalSlot == null &&
                                slot.InfraData != null &&
                                slot.InfraData.Kind == InfrastructureKind.Signal;
                bool isRoundabout = roundaboutSlot == null &&
                                    slot.InfraData != null &&
                                    slot.InfraData.Kind == InfrastructureKind.Roundabout;
                slot.gameObject.SetActive(isSignal || isRoundabout);

                if (isSignal)
                {
                    signalSlot = slot;
                }
                else if (isRoundabout)
                {
                    roundaboutSlot = slot;
                }
            }

            int siblingIndex = 0;
            if (roadSlot != null)
            {
                roadSlot.transform.SetSiblingIndex(siblingIndex++);
            }
            if (signalSlot != null)
            {
                signalSlot.transform.SetSiblingIndex(siblingIndex++);
            }
            if (roundaboutSlot != null)
            {
                roundaboutSlot.transform.SetSiblingIndex(siblingIndex);
            }
        }

        private void EnsureHighwaySlot()
        {
            if (categoryPages == null || categoryPages.Length == 0 || categoryPages[0] == null)
            {
                return;
            }

            Transform infraPage = categoryPages[0].transform;
            InfrastructureSlotController[] slots =
                infraPage.GetComponentsInChildren<InfrastructureSlotController>(true);
            InfrastructureSlotController template = null;

            foreach (InfrastructureSlotController slot in slots)
            {
                if (slot.InfraData != null && slot.InfraData.Kind == InfrastructureKind.Highway)
                {
                    slot.gameObject.SetActive(true);
                    return;
                }

                if (template == null && slot.gameObject.activeSelf)
                {
                    template = slot;
                }
            }

            if (template == null)
            {
                Debug.LogWarning("[BuildPanelController] 고속도로 슬롯을 복제할 인프라 슬롯이 없습니다.");
                return;
            }

            InfrastructureDataSO data = highwayData != null ? highwayData : CreateRuntimeHighwayData();
            InfrastructureSlotController highwaySlot = Instantiate(template, infraPage);
            highwaySlot.name = "Highway_Slot";
            highwaySlot.Configure(data);
            highwaySlot.gameObject.SetActive(true);
            highwaySlot.transform.SetSiblingIndex(Mathf.Min(3, infraPage.childCount - 1));
        }

        private InfrastructureDataSO CreateRuntimeHighwayData()
        {
            if (_runtimeHighwayData != null)
            {
                return _runtimeHighwayData;
            }

            _runtimeHighwayData = ScriptableObject.CreateInstance<InfrastructureDataSO>();
            _runtimeHighwayData.hideFlags = HideFlags.HideAndDontSave;
            _runtimeHighwayData.Kind = InfrastructureKind.Highway;
            _runtimeHighwayData.InfrastructureName = "Highway";
            _runtimeHighwayData.Description = "직선 구간을 빠르게 이동합니다. 진출입은 양 끝에서만 가능합니다.";
            _runtimeHighwayData.Cost = 150;
            return _runtimeHighwayData;
        }

        private void OnDestroy()
        {
            if (_runtimeHighwayData != null)
            {
                Destroy(_runtimeHighwayData);
            }
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
            _isBound = true;
        }

        /// <summary>
        /// 씬에 하드코딩된 영문 카테고리 탭 텍스트를 한글로 일괄 설정합니다.
        /// </summary>
        private void LocalizeCategoryTabs()
        {
            if (categoryTabs == null) return;
            
            string[] titles = { "인프라", "주거", "상업", "공공장소" };
            for (int i = 0; i < categoryTabs.Length && i < titles.Length; i++)
            {
                if (categoryTabs[i] != null)
                {
                    var label = categoryTabs[i].GetComponentInChildren<TMPro.TMP_Text>(true);
                    if (label != null)
                    {
                        label.text = titles[i];
                    }
                }
            }
        }
    }
}
