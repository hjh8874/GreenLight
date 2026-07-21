using CityFlow.Contracts;
using CityFlow.UI.Controllers;
using CityFlow.UI.Data;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

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

        private bool isInitialized;

        public void Configure(
            PlacementController placement,
            Button road,
            Button house,
            Button office,
            Button remove
        )
        {
            Configure(placement, road, house, office, remove, null);
        }

        public void Configure(
            PlacementController placement,
            Button road,
            Button house,
            Button office,
            Button remove,
            Button school
        )
        {
            placementController = placement;

            BindRuntimeButton(road, TileType.Road);
            BindRuntimeButton(house, TileType.House);
            BindRuntimeButton(office, TileType.Office);
            BindRuntimeButton(remove, TileType.Empty);
            BindRuntimeButton(school, TileType.School);

            InitializePanel();
        }

        private void Start()
        {
            InitializePanel();
        }

        private void OnDisable()
        {
            RectTransform rect = GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.DOKill();
            }

            tooltipController?.HideTooltip();
        }

        private void InitializePanel()
        {
            if (isInitialized)
            {
                RefreshSlots();
                return;
            }

            LocalizeCategoryTabs();
            PlayOpenAnimation();

            if (placementController == null)
            {
                placementController = FindAnyObjectByType<PlacementController>(
                    FindObjectsInactive.Include
                );
            }

            if (placementController == null)
            {
                Debug.LogError(
                    "[BuildPanelController] PlacementController를 찾을 수 없습니다.",
                    this
                );
                return;
            }

            ConfigureInfrastructureSlots();
            RefreshSlots();
            BindCategoryTabs();

            if (categoryPages != null && categoryPages.Length > 0)
            {
                ShowCategory(0);
            }

            isInitialized = true;
        }

        private void RefreshSlots()
        {
            if (buildSlots == null || buildSlots.Length == 0)
            {
                buildSlots = GetComponentsInChildren<BuildSlotController>(true);
            }

            if (buildSlots == null)
            {
                return;
            }

            for (int i = 0; i < buildSlots.Length; i++)
            {
                BuildSlotController slot = buildSlots[i];
                if (slot != null)
                {
                    slot.Initialize(placementController, tooltipController);
                }
            }
        }

        private void BindCategoryTabs()
        {
            if (categoryTabs == null)
            {
                return;
            }

            for (int i = 0; i < categoryTabs.Length; i++)
            {
                Button tab = categoryTabs[i];
                if (tab == null)
                {
                    continue;
                }

                int index = i;
                tab.onClick.RemoveAllListeners();
                tab.onClick.AddListener(() => ShowCategory(index));
            }
        }

        private void BindRuntimeButton(Button button, TileType type)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                if (placementController != null)
                {
                    placementController.SetBuildType(type);
                }
            });
        }

        private void PlayOpenAnimation()
        {
            RectTransform rect = GetComponent<RectTransform>();
            if (rect == null)
            {
                return;
            }

            rect.DOKill();

            float originalY = rect.anchoredPosition.y;
            rect.anchoredPosition = new Vector2(
                rect.anchoredPosition.x,
                originalY - 200f
            );
            rect.DOAnchorPosY(originalY, 0.5f)
                .SetEase(Ease.OutBack)
                .SetDelay(0.2f);
        }

        private void ConfigureInfrastructureSlots()
        {
            if (categoryPages == null ||
                categoryPages.Length == 0 ||
                categoryPages[0] == null)
            {
                return;
            }

            Transform infraPage = categoryPages[0].transform;
            BuildSlotController roadSlot = null;
            BuildSlotController[] tileSlots =
                infraPage.GetComponentsInChildren<BuildSlotController>(true);

            for (int i = 0; i < tileSlots.Length; i++)
            {
                BuildSlotController slot = tileSlots[i];
                bool isRoad = roadSlot == null &&
                              slot != null &&
                              slot.TileData != null &&
                              slot.TileData.Category == TileType.Road;

                if (slot != null)
                {
                    slot.gameObject.SetActive(isRoad);
                }

                if (isRoad)
                {
                    roadSlot = slot;
                }
            }

            InfrastructureSlotController signalSlot = null;
            InfrastructureSlotController roundaboutSlot = null;
            InfrastructureSlotController[] infrastructureSlots =
                infraPage.GetComponentsInChildren<InfrastructureSlotController>(true);

            for (int i = 0; i < infrastructureSlots.Length; i++)
            {
                InfrastructureSlotController slot = infrastructureSlots[i];
                if (slot == null)
                {
                    continue;
                }

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

        public void ShowCategory(int index)
        {
            if (categoryPages == null || categoryPages.Length == 0)
            {
                return;
            }

            int safeIndex = Mathf.Clamp(index, 0, categoryPages.Length - 1);

            for (int i = 0; i < categoryPages.Length; i++)
            {
                if (categoryPages[i] != null)
                {
                    categoryPages[i].SetActive(i == safeIndex);
                }
            }
        }

        private void LocalizeCategoryTabs()
        {
            if (categoryTabs == null)
            {
                return;
            }

            string[] titles = { "인프라", "주거", "상업", "공공장소" };

            for (int i = 0; i < categoryTabs.Length && i < titles.Length; i++)
            {
                if (categoryTabs[i] == null)
                {
                    continue;
                }

                TMPro.TMP_Text label =
                    categoryTabs[i].GetComponentInChildren<TMPro.TMP_Text>(true);

                if (label != null)
                {
                    label.text = titles[i];
                }
            }
        }
    }
}
