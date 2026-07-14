using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CityFlow.UI
{
    public sealed class UIDockController : MonoBehaviour
    {
        public enum MenuType { None, Build, Research, Stats, Settings, Infra }

        [Header("Menu Buttons (Dock_Right)")]
        [SerializeField] private Button btnBuild;
        [SerializeField] private Button btnResearch;
        [SerializeField] private Button btnStats;
        [SerializeField] private Button btnSettings;
        [SerializeField] private Button btnInfra;

        [Header("Sub Panels (SubPanels_Right)")]
        [SerializeField] private GameObject panelBuild;
        [SerializeField] private GameObject panelResearch;
        [SerializeField] private GameObject panelStats;
        [SerializeField] private GameObject panelSettings;
        [SerializeField] private GameObject panelInfra;

        [Header("System Sync")]
        [SerializeField] private PlacementController placementController;
        [SerializeField] private bool normalizeLayoutOnStart;

        private MenuType _currentMenu = MenuType.None;
        private bool _isBuildPanelCollapsed;
        private bool _isBound;

        public void Configure(
            Button build,
            Button research,
            Button stats,
            Button settings,
            GameObject buildPanel,
            GameObject researchPanel,
            GameObject statsPanel,
            GameObject settingsPanel,
            PlacementController placement)
        {
            btnBuild = build;
            btnResearch = research;
            btnStats = stats;
            btnSettings = settings;
            panelBuild = buildPanel;
            panelResearch = researchPanel;
            panelStats = statsPanel;
            panelSettings = settingsPanel;
            placementController = placement;
            BindButtons();
        }

        private void Start()
        {
            BindButtons();

            if (normalizeLayoutOnStart)
            {
                NormalizeDockLayout();
            }

            CloseAllPanels();
        }

        private void NormalizeDockLayout()
        {
            ConfigureDockButton(btnBuild, new Vector2(-24f, 156f));
            ConfigureDockButton(btnResearch, new Vector2(-24f, 114f));
            ConfigureDockButton(btnStats, new Vector2(-24f, 72f));
            ConfigureDockButton(btnSettings, new Vector2(-24f, 30f));
            ConfigureDockButton(btnInfra, new Vector2(-24f, -12f));
        }

        private static void ConfigureDockButton(Button button, Vector2 anchoredPosition)
        {
            if (button == null)
            {
                return;
            }

            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(120f, 34f);

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label == null)
            {
                return;
            }

            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 16f;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
        }

        private void BindButtons()
        {
            if (_isBound)
            {
                return;
            }

            if (btnBuild != null) btnBuild.onClick.AddListener(() => ToggleMenu(MenuType.Build));
            if (btnResearch != null) btnResearch.onClick.AddListener(() => ToggleMenu(MenuType.Research));
            if (btnStats != null) btnStats.onClick.AddListener(() => ToggleMenu(MenuType.Stats));
            if (btnSettings != null) btnSettings.onClick.AddListener(() => ToggleMenu(MenuType.Settings));
            if (btnInfra != null) btnInfra.onClick.AddListener(() => ToggleMenu(MenuType.Infra));
            _isBound = true;
        }

        /// <summary>
        /// 버튼을 누르면 해당 패널을 엽니다. 이미 열린 버튼을 누르면 닫습니다(토글).
        /// </summary>
        public void ToggleMenu(MenuType menu)
        {
            _isBuildPanelCollapsed = false;

            if (_currentMenu == menu)
            {
                // 이미 열려있는 메뉴의 버튼을 또 누르면 닫기
                _currentMenu = MenuType.None;
            }
            else
            {
                // 새로운 메뉴 열기
                _currentMenu = menu;
            }
            
            UpdatePanelVisibility();
        }

        /// <summary>
        /// 모든 패널을 강제로 닫습니다. (ESC 키나 허공 클릭 시 활용 가능)
        /// </summary>
        public void CloseAllPanels()
        {
            _currentMenu = MenuType.None;
            _isBuildPanelCollapsed = false;
            UpdatePanelVisibility();
        }

        public void CollapseBuildPanelForPlacement()
        {
            if (_currentMenu != MenuType.Build)
            {
                return;
            }

            _isBuildPanelCollapsed = true;
            UpdatePanelVisibility();
        }

        private void UpdatePanelVisibility()
        {
            // _currentMenu 상태에 따라 패널 중 딱 하나만 켜고 나머지는 모두 끕니다.
            if (panelBuild != null) panelBuild.SetActive(_currentMenu == MenuType.Build && !_isBuildPanelCollapsed);
            if (panelResearch != null) panelResearch.SetActive(_currentMenu == MenuType.Research);
            if (panelStats != null) panelStats.SetActive(_currentMenu == MenuType.Stats);
            if (panelSettings != null) panelSettings.SetActive(_currentMenu == MenuType.Settings);
            if (panelInfra != null) panelInfra.SetActive(_currentMenu == MenuType.Infra);

            // 건설 패널이 열렸을 때만 고스트 모드를 켜고, 닫히면 고스트 모드도 강제 종료(Sync)합니다.
            if (placementController != null)
            {
                placementController.ToggleBuildMode(_currentMenu == MenuType.Build);
            }

            if (_currentMenu != MenuType.Infra)
            {
                var infraCoord = UnityEngine.Object.FindFirstObjectByType<CityFlow.UI.Controllers.InfrastructurePlacementCoordinator>();
                if (infraCoord != null)
                {
                    infraCoord.CancelPlacement();
                }
            }
        }
    }
}
