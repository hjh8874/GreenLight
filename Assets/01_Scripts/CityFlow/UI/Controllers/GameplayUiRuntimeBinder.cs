using CityFlow.Bootstrap;
using CityFlow.UI.Feed;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI.Controllers
{
    /// <summary>
    /// Prefab 에셋이 보존할 수 없는 Scene 런타임 참조를 자동 복구합니다.
    /// UI_MainCanvasRoot에 포함되며 Inspector 재연결을 요구하지 않습니다.
    /// </summary>
    public sealed class GameplayUiRuntimeBinder :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        [SerializeField] private UIDockController dockController;
        [SerializeField] private BuildPanelController buildPanelController;
        [SerializeField] private GreenFeedPanelController greenFeedController;

        private PlacementController placementController;
        private TileSelectionController tileSelectionController;
        private TooltipController tooltipController;

        public bool IsPlacementBound =>
            placementController != null &&
            dockController != null &&
            buildPanelController != null;

        public bool IsGreenFeedBound
        {
            get
            {
                if (greenFeedController == null ||
                    greenFeedController.TickerView == null)
                {
                    return false;
                }

                GreenFeedHoverRelay[] relays =
                    GetComponentsInChildren<GreenFeedHoverRelay>(true);
                for (int index = 0; index < relays.Length; index++)
                {
                    if (relays[index].Controller != greenFeedController ||
                        (relays[index].Action ==
                             GreenFeedHoverRelay.ClickAction.Locate &&
                         relays[index].TileSelection == null))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public bool IsDockUiBound =>
            dockController != null &&
            dockController.HasExternalUiReferences;

        public bool IsBuildPanelBound =>
            buildPanelController != null &&
            buildPanelController.HasRuntimeReferences;

        private void Awake()
        {
            BindRuntimeReferences();
        }

        private void Start()
        {
            BindRuntimeReferences();
        }

        public void Initialize(CityFlowServices services)
        {
            BindRuntimeReferences();
        }

        public bool BindRuntimeReferences()
        {
            dockController ??= GetComponentInChildren<UIDockController>(true);
            buildPanelController ??=
                GetComponentInChildren<BuildPanelController>(true);
            placementController ??=
                FindAnyObjectByType<PlacementController>(
                    FindObjectsInactive.Include);
            tileSelectionController ??=
                FindAnyObjectByType<TileSelectionController>(
                    FindObjectsInactive.Include);
            greenFeedController ??=
                GetComponentInChildren<GreenFeedPanelController>(true);
            tooltipController ??=
                GetComponentInChildren<TooltipController>(true);

            if (placementController != null)
            {
                dockController?.RebindPlacementController(
                    placementController);
                buildPanelController?.RebindPlacementController(
                    placementController);
            }

            buildPanelController?.RebindTooltipController(
                tooltipController);

            BindDockUiReferences();
            BindGreenFeedReferences();
            return IsPlacementBound &&
                IsDockUiBound &&
                IsBuildPanelBound &&
                IsGreenFeedBound;
        }

        private void BindDockUiReferences()
        {
            if (dockController == null)
            {
                return;
            }

            TopBarActionDockController actionDock =
                GetComponentInChildren<TopBarActionDockController>(true);
            Button floatingButton = actionDock != null
                ? actionDock.transform.Find("Btn_Floating")
                    ?.GetComponent<Button>()
                : null;

            BuildPanelController buildPanel =
                GetComponentInChildren<BuildPanelController>(true);
            ResearchPanelController researchPanel =
                GetComponentInChildren<ResearchPanelController>(true);
            StatsPanelController statsPanel =
                GetComponentInChildren<StatsPanelController>(true);
            SettingsPanelController settingsPanel =
                GetComponentInChildren<SettingsPanelController>(true);
            FloatingPanelController floatingPanel =
                GetComponentInChildren<FloatingPanelController>(true);

            dockController.RebindExternalUi(
                floatingButton,
                buildPanel?.gameObject,
                researchPanel?.gameObject,
                statsPanel?.gameObject,
                settingsPanel?.gameObject,
                floatingPanel?.gameObject);
        }

        private void BindGreenFeedReferences()
        {
            if (greenFeedController == null)
            {
                return;
            }

            GreenFeedPostView ticker = null;
            GreenFeedHoverRelay[] relays =
                GetComponentsInChildren<GreenFeedHoverRelay>(true);
            for (int index = 0; index < relays.Length; index++)
            {
                GreenFeedHoverRelay relay = relays[index];
                relay.RebindRuntimeReferences(
                    greenFeedController,
                    tileSelectionController);

                if (ticker == null &&
                    (relay.Action == GreenFeedHoverRelay.ClickAction.Toggle ||
                     relay.Action == GreenFeedHoverRelay.ClickAction.None))
                {
                    ticker = relay.GetComponent<GreenFeedPostView>();
                }
            }

            if (ticker != null)
            {
                greenFeedController.RebindTicker(ticker);
            }
        }
    }
}
