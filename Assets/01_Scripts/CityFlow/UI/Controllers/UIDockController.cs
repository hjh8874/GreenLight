using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public sealed class UIDockController : MonoBehaviour
    {
        public enum MenuType { None, Build, Research, Stats, Settings }

        [Header("Menu Buttons (Dock_Right)")]
        [SerializeField] private Button btnBuild;
        [SerializeField] private Button btnResearch;
        [SerializeField] private Button btnStats;
        [SerializeField] private Button btnSettings;

        [Header("Sub Panels (SubPanels_Right)")]
        [SerializeField] private GameObject panelBuild;
        [SerializeField] private GameObject panelResearch;
        [SerializeField] private GameObject panelStats;
        [SerializeField] private GameObject panelSettings;

        [Header("System Sync")]
        [SerializeField] private PlacementController placementController;

        private MenuType _currentMenu = MenuType.None;
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
            // 버튼 클릭 이벤트 코드로 자동 바인딩
            BindButtons();

            // 시작 시 모든 패널 닫기
            CloseAllPanels();
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
            _isBound = true;
        }

        /// <summary>
        /// 버튼을 누르면 해당 패널을 엽니다. 이미 열린 버튼을 누르면 닫습니다(토글).
        /// </summary>
        public void ToggleMenu(MenuType menu)
        {
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
            UpdatePanelVisibility();
        }

        private void UpdatePanelVisibility()
        {
            // _currentMenu 상태에 따라 4개의 패널 중 딱 하나만 켜고 나머지는 모두 끕니다.
            if (panelBuild != null) panelBuild.SetActive(_currentMenu == MenuType.Build);
            if (panelResearch != null) panelResearch.SetActive(_currentMenu == MenuType.Research);
            if (panelStats != null) panelStats.SetActive(_currentMenu == MenuType.Stats);
            if (panelSettings != null) panelSettings.SetActive(_currentMenu == MenuType.Settings);

            // 건설 패널이 열렸을 때만 고스트 모드를 켜고, 닫히면 고스트 모드도 강제 종료(Sync)합니다.
            if (placementController != null)
            {
                placementController.ToggleBuildMode(_currentMenu == MenuType.Build);
            }
        }
    }
}
