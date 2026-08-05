using System.Collections.Generic;
using CityFlow.Bootstrap;
using UnityEngine;
using UnityEngine.UI;
using CityFlow.Contracts;
using CityFlow.UI.Controllers;
using CityFlow.UI.Data;
using DG.Tweening;
namespace CityFlow.UI
{
    public class BuildPanelController :
        MonoBehaviour,
        ICityFlowServiceConsumer
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
        [SerializeField] private InfrastructureDataSO highwayData;
        [SerializeField] private InfrastructureDataSO busStopData;

        private bool _isBound;
        private readonly Dictionary<string, BuildSlotController>
            _specialBuildingSlots = new();
        private CityFlowServices _services;
        private ISpecialBuildingService _specialBuildings;
        private IResearchUnlockService _research;
        private bool _started;

        public void Initialize(CityFlowServices services)
        {
            if (_services != null)
            {
                _services.SpecialBuildingsRegistered -=
                    OnSpecialBuildingsRegistered;
                _services.ResearchRegistered -=
                    OnResearchRegistered;
            }

            _services = services;
            if (_services == null)
            {
                BindSpecialBuildings(null);
                BindResearch(null);
                return;
            }

            _services.SpecialBuildingsRegistered +=
                OnSpecialBuildingsRegistered;
            _services.ResearchRegistered +=
                OnResearchRegistered;
            BindSpecialBuildings(_services.SpecialBuildings);
            BindResearch(_services.Research);
            RefreshSpecialBuildingSlots();
            RefreshResearchLockedSlots();
        }
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
            _started = true;
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
                placementController = FindAnyObjectByType<PlacementController>(FindObjectsInactive.Include);
            }
            if (tooltipController == null)
            {
                tooltipController = FindAnyObjectByType<TooltipController>(FindObjectsInactive.Include);
            }

            if (placementController == null)
            {
                Debug.LogError("[BuildPanelController] PlacementController가 할당되지 않았으며 씬에서도 찾을 수 없습니다.");
                return;
            }
            ConfigureInfrastructureSlots();
            EnsureBusStopSlot();
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
            RefreshResearchLockedSlots();

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
            RefreshSpecialBuildingSlots();
        }

        private void OnDestroy()
        {
            if (_services != null)
            {
                _services.SpecialBuildingsRegistered -=
                    OnSpecialBuildingsRegistered;
                _services.ResearchRegistered -=
                    OnResearchRegistered;
            }

            BindSpecialBuildings(null);
            BindResearch(null);
        }

        private void OnSpecialBuildingsRegistered(
            ISpecialBuildingService service)
        {
            BindSpecialBuildings(service);
            RefreshSpecialBuildingSlots();
        }

        private void BindSpecialBuildings(
            ISpecialBuildingService service)
        {
            if (ReferenceEquals(_specialBuildings, service))
            {
                return;
            }

            if (_specialBuildings != null)
            {
                _specialBuildings.BuildOptionsChanged -=
                    RefreshSpecialBuildingSlots;
            }

            _specialBuildings = service;
            if (_specialBuildings != null)
            {
                _specialBuildings.BuildOptionsChanged +=
                    RefreshSpecialBuildingSlots;
            }
        }

        private void OnResearchRegistered(
            IResearchUnlockService service)
        {
            BindResearch(service);
            RefreshResearchLockedSlots();
        }

        private void BindResearch(IResearchUnlockService service)
        {
            if (ReferenceEquals(_research, service))
            {
                return;
            }

            if (_research != null)
            {
                _research.ResearchUnlocked -=
                    OnResearchUnlocked;
                _research.ResearchStateRestored -=
                    RefreshResearchLockedSlots;
            }

            _research = service;
            if (_research != null)
            {
                _research.ResearchUnlocked +=
                    OnResearchUnlocked;
                _research.ResearchStateRestored +=
                    RefreshResearchLockedSlots;
            }
        }

        private void OnResearchUnlocked(string _) =>
            RefreshResearchLockedSlots();

        private void RefreshResearchLockedSlots()
        {
            if (!_started || buildSlots == null)
            {
                return;
            }

            for (int index = 0; index < buildSlots.Length; index++)
            {
                buildSlots[index]?.RefreshAvailability();
            }
        }

        private void RefreshSpecialBuildingSlots()
        {
            if (!_started || placementController == null ||
                _specialBuildings == null || categoryPages == null)
            {
                return;
            }

            BuildSlotController template = FindBuildSlotTemplate();
            if (template == null)
            {
                Debug.LogWarning(
                    "[BuildPanelController] A build slot template is required " +
                    "for special building slots.",
                    this);
                return;
            }

            SpecialBuildingBuildOption[] options =
                _specialBuildings.CreateBuildOptionSnapshot();
            var activeIds = new HashSet<string>();

            for (int index = 0; index < options.Length; index++)
            {
                SpecialBuildingBuildOption option = options[index];
                activeIds.Add(option.BuildingId);

                if (_specialBuildingSlots.TryGetValue(
                        option.BuildingId,
                        out BuildSlotController existing))
                {
                    existing.RefreshSpecialBuilding(option);
                    continue;
                }

                Transform page = ResolveSpecialBuildingPage(
                    option.MenuCategory);
                if (page == null)
                {
                    continue;
                }

                BuildSlotController slot = Instantiate(template, page, false);
                slot.name = $"SpecialBuilding_{option.BuildingId}_Slot";
                slot.gameObject.SetActive(true);
                slot.ConfigureSpecialBuilding(
                    option,
                    placementController,
                    tooltipController);
                _specialBuildingSlots.Add(option.BuildingId, slot);
            }

            var removedIds = new List<string>();
            foreach (KeyValuePair<string, BuildSlotController> pair in
                     _specialBuildingSlots)
            {
                if (activeIds.Contains(pair.Key))
                {
                    continue;
                }

                if (pair.Value != null)
                {
                    Destroy(pair.Value.gameObject);
                }
                removedIds.Add(pair.Key);
            }

            for (int index = 0; index < removedIds.Count; index++)
            {
                _specialBuildingSlots.Remove(removedIds[index]);
            }
        }

        private BuildSlotController FindBuildSlotTemplate()
        {
            if (buildSlots != null)
            {
                for (int index = 0; index < buildSlots.Length; index++)
                {
                    if (buildSlots[index] != null &&
                        string.IsNullOrEmpty(
                            buildSlots[index].SpecialBuildingId))
                    {
                        return buildSlots[index];
                    }
                }
            }

            BuildSlotController[] candidates =
                GetComponentsInChildren<BuildSlotController>(true);
            for (int index = 0; index < candidates.Length; index++)
            {
                if (candidates[index] != null &&
                    string.IsNullOrEmpty(
                        candidates[index].SpecialBuildingId))
                {
                    return candidates[index];
                }
            }

            return null;
        }

        private Transform ResolveSpecialBuildingPage(
            SpecialBuildingMenuCategory category)
        {
            int index = category == SpecialBuildingMenuCategory.Public
                ? 3
                : 2;
            return index >= 0 && index < categoryPages.Length &&
                   categoryPages[index] != null
                ? categoryPages[index].transform
                : null;
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

            InfrastructureSlotController priorityRoadSlot = Instantiate(template, infraPage, false);
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
            if (categoryPages == null || categoryPages.Length == 0 || categoryPages[0] == null) return;
            Transform page = categoryPages[0].transform;
            InfrastructureSlotController template = null;
            foreach (InfrastructureSlotController slot in page.GetComponentsInChildren<InfrastructureSlotController>(true))
            {
                if (slot.InfraData != null && slot.InfraData.Kind == InfrastructureKind.Highway)
                { slot.gameObject.SetActive(true); return; }
                if (template == null && slot.InfraData != null && slot.InfraData.Kind == InfrastructureKind.Signal)
                    template = slot;
            }
            if (template == null || highwayData == null) return;
            InfrastructureSlotController clone = Instantiate(template, page, false);
            clone.name = "Highway_Slot";
            clone.Configure(highwayData);
            clone.gameObject.SetActive(true);
            clone.transform.SetSiblingIndex(Mathf.Min(3, page.childCount - 1));
        }

        public void ShowCategory(int index)
        {
            if (categoryPages == null) return;

            RefreshBalancePresentation();

            for (int i = 0; i < categoryPages.Length; i++)
            {
                if (categoryPages[i] != null)
                {
                    bool isActive = (i == index);
                    categoryPages[i].SetActive(isActive);
                }
            }
        }

        public void RefreshBalancePresentation()
        {
            RefreshSpecialBuildingSlots();

            if (buildSlots == null)
            {
                return;
            }

            foreach (BuildSlotController slot in buildSlots)
            {
                slot?.RefreshAvailability();
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

        private void EnsureBusStopSlot()
        {
            if (categoryPages == null ||
                categoryPages.Length == 0 ||
                categoryPages[0] == null)
            {
                return;
            }

            if (FindAnyObjectByType<
                    CityFlow.Content.Transit.BusStopRegistry>() == null)
            {
                return;
            }

            busStopData ??= Resources.Load<InfrastructureDataSO>(
                "CityFlow/InfrastructureData/BusStopData");

            if (busStopData == null)
            {
                Debug.LogWarning(
                    "[BuildPanelController] BusStopData asset was not found in Resources.");
                return;
            }

            Transform infraPage = categoryPages[0].transform;
            InfrastructureSlotController template = null;

            foreach (InfrastructureSlotController slot in
                     infraPage.GetComponentsInChildren<InfrastructureSlotController>(true))
            {
                if (slot.InfraData != null &&
                    slot.InfraData.Kind == InfrastructureKind.BusStop)
                {
                    slot.gameObject.SetActive(true);
                    return;
                }

                if (template == null &&
                    slot.InfraData != null &&
                    slot.InfraData.Kind == InfrastructureKind.Signal)
                {
                    template = slot;
                }
            }

            if (template == null)
            {
                Debug.LogWarning(
                    "[BuildPanelController] Bus-stop slot template was not found.");
                return;
            }

            InfrastructureSlotController clone =
                Instantiate(template, infraPage, false);
            clone.name = "BusStop_Slot";
            clone.Configure(busStopData);
            clone.gameObject.SetActive(true);
            clone.transform.SetSiblingIndex(
                Mathf.Min(3, infraPage.childCount - 1));
        }
    }
}
