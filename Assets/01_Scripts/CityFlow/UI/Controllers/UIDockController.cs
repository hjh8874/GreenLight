using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CityFlow.UI.Controllers;

namespace CityFlow.UI
{
    public sealed class UIDockController : MonoBehaviour
    {
        private const float SubPanelDockGap = 50f;

        public enum MenuType { None, Build, Research, Stats, Settings, Floating }

        [Header("Menu Buttons (Dock_Right)")]
        [SerializeField] private Button btnBuild;
        [SerializeField] private Button btnResearch;
        [SerializeField] private Button btnStats;
        [SerializeField] private Button btnSettings;
        [SerializeField] private Button btnFloatingMode;

        [Header("Sub Panels (SubPanels_Right)")]
        [SerializeField] private GameObject panelBuild;
        [SerializeField] private GameObject panelResearch;
        [SerializeField] private GameObject panelStats;
        [SerializeField] private GameObject panelSettings;
        [SerializeField] private GameObject panelFloating;

        [Header("System Sync")]
        [SerializeField] private PlacementController placementController;
        [SerializeField] private bool normalizeLayoutOnStart;

        private MenuType _currentMenu = MenuType.None;
        private bool _isBound;

        public MenuType CurrentMenu => _currentMenu;
        public bool IsAnyMenuOpen => _currentMenu != MenuType.None;

        public event Action<MenuType> MenuChanged;

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

        /// <summary>
        /// 씬에 배치된 연구 패널 프리팹이 도크 연결을 스스로 복구할 때 사용합니다.
        /// </summary>
        public void RebindResearchPanel(GameObject researchPanel)
        {
            panelResearch = researchPanel;
        }

        private void Awake()
        {
            var scalers = FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include);
            foreach (var scaler in scalers)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
            }
        }

        private void Start()
        {
            BindButtons();
            LocalizeDockLabels();
            EnsureEscapeController();

            if (normalizeLayoutOnStart)
            {
                NormalizeDockLayout();
            }

            CloseAllPanels();
        }

        /// <summary>
        /// 도크 버튼의 텍스트를 한글로 설정합니다.
        /// 씬에 저장된 문자열에 의존하지 않고 스크립트에서 일괄 지정하여
        /// 모든 통합 씬에서 동일한 한글 문구가 표시되도록 합니다.
        /// </summary>
        private void LocalizeDockLabels()
        {
            SetButtonLabel(btnBuild, "건설");
            SetButtonLabel(btnResearch, "연구");
            SetButtonLabel(btnStats, "통계");
            SetButtonLabel(btnSettings, "설정");
            SetButtonLabel(btnFloatingMode, "플로팅");
            MatchButtonStyle(btnSettings, btnResearch);
        }

        private static void MatchButtonStyle(Button target, Button source)
        {
            if (target == null || source == null)
            {
                return;
            }

            Image targetImage = target.targetGraphic as Image;
            Image sourceImage = source.targetGraphic as Image;
            if (targetImage != null && sourceImage != null)
            {
                targetImage.sprite = sourceImage.sprite;
                targetImage.type = sourceImage.type;
                targetImage.color = sourceImage.color;
                targetImage.pixelsPerUnitMultiplier =
                    sourceImage.pixelsPerUnitMultiplier;
            }

            target.colors = source.colors;
            TMP_Text label = target.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.color = Color.white;
            }
        }

        private static void SetButtonLabel(Button button, string text)
        {
            if (button == null) return;

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = text;
            }
        }

        private void EnsureEscapeController()
        {
            EscapeUIController escapeController = UnityEngine.Object.FindAnyObjectByType<EscapeUIController>(FindObjectsInactive.Include);
            if (escapeController == null)
            {
                escapeController = gameObject.AddComponent<EscapeUIController>();
            }

            ConfirmPopupController confirmPopup = UnityEngine.Object.FindAnyObjectByType<ConfirmPopupController>(FindObjectsInactive.Include);
            AnalysisCardController analysisCard = UnityEngine.Object.FindAnyObjectByType<AnalysisCardController>(FindObjectsInactive.Include);
            escapeController.Configure(confirmPopup, analysisCard, this);
        }

        private void NormalizeDockLayout()
        {
            ConfigureDockButton(btnBuild, new Vector2(-24f, 156f));
            ConfigureDockButton(btnResearch, new Vector2(-24f, 114f));
            ConfigureDockButton(btnStats, new Vector2(-24f, 72f));
            ConfigureDockButton(btnSettings, new Vector2(-24f, 30f));
            ConfigureDockButton(btnFloatingMode, new Vector2(-24f, -12f));
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

            label.alignment = TextAlignmentOptions.Midline;
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
            
            if (btnFloatingMode != null) btnFloatingMode.onClick.AddListener(() => ToggleMenu(MenuType.Floating));
            
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
            MenuChanged?.Invoke(_currentMenu);
        }

        /// <summary>
        /// 모든 패널을 강제로 닫습니다. (ESC 키나 허공 클릭 시 활용 가능)
        /// </summary>
        public void CloseAllPanels()
        {
            _currentMenu = MenuType.None;
            UpdatePanelVisibility();
            MenuChanged?.Invoke(_currentMenu);
        }

        public void SetDriveViewActive(bool isActive)
        {
            if (isActive)
            {
                CloseAllPanels();
            }

            if (isActive && gameObject.activeSelf)
            {
                Debug.LogWarning(
                    "[DockVisibility] Dock_Right deactivated by " +
                    "UIDockController because drive view became active.",
                    this);
            }

            gameObject.SetActive(!isActive);
        }

        private void UpdatePanelVisibility()
        {
            // _currentMenu 상태에 따라 패널 중 딱 하나만 켜고 나머지는 모두 끕니다.
            if (panelBuild != null) panelBuild.SetActive(_currentMenu == MenuType.Build);
            if (panelResearch != null) panelResearch.SetActive(_currentMenu == MenuType.Research);
            if (panelStats != null) panelStats.SetActive(_currentMenu == MenuType.Stats);
            if (panelSettings != null) panelSettings.SetActive(_currentMenu == MenuType.Settings);
            if (panelFloating != null) panelFloating.SetActive(_currentMenu == MenuType.Floating);
            AlignActiveSubPanelToDock();
            BuildModeCursorFeedback.SetBuilding(
                this,
                _currentMenu == MenuType.Build);

            var infraCoord = UnityEngine.Object.FindFirstObjectByType<CityFlow.UI.Controllers.InfrastructurePlacementCoordinator>();
            if (infraCoord != null)
            {
                infraCoord.CancelPlacement();
            }

            // 메뉴 전환 시 선택을 비우고, 실제 배치 모드는 건설 피스를 누를 때 시작합니다.
            if (placementController != null)
            {
                placementController.ToggleBuildMode(false);
            }
        }

        private void AlignActiveSubPanelToDock()
        {
            GameObject activePanel = _currentMenu switch
            {
                MenuType.Research => panelResearch,
                MenuType.Stats => panelStats,
                MenuType.Settings => panelSettings,
                MenuType.Floating => panelFloating,
                _ => null
            };

            if (activePanel == null ||
                !activePanel.TryGetComponent(out RectTransform panelRect) ||
                !TryGetComponent(out RectTransform dockRect))
            {
                return;
            }

            Canvas.ForceUpdateCanvases();

            Vector3[] dockCorners = new Vector3[4];
            Vector3[] panelCorners = new Vector3[4];
            RectTransform alignmentRect = panelRect;
            if (_currentMenu == MenuType.Stats &&
                activePanel.TryGetComponent(out StatsPanelController statsPanel))
            {
                alignmentRect = statsPanel.PanelAlignmentRect;
            }

            dockRect.GetWorldCorners(dockCorners);
            alignmentRect.GetWorldCorners(panelCorners);

            Vector3 dockLeftCenter = (dockCorners[0] + dockCorners[1]) * 0.5f;
            Vector3 panelRightCenter = (panelCorners[2] + panelCorners[3]) * 0.5f;
            Vector3 gap = dockRect.TransformVector(
                Vector3.left * SubPanelDockGap);

            panelRect.position += dockLeftCenter + gap - panelRightCenter;
        }

        private void OnDisable()
        {
            BuildModeCursorFeedback.SetBuilding(this, false);
        }
    }
}
