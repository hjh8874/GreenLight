using Kirurobo;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.View
{
    public sealed class FloatingWindowTitleBarController : MonoBehaviour
    {
        public const float TitleBarHeight = 36f;

        private const float RevealDistance = 12f;
        private const float MaximizedRevealEdgeDistance = 2f;
        private const float HideDelaySeconds = 0.2f;

        [Header("Title Bar")]
        [SerializeField] private CanvasGroup titleBarGroup;
        [SerializeField] private FloatingWindowDragHandle moveHandle;
        [SerializeField] private Canvas contentCanvas;
        [SerializeField] private RectTransform contentRoot;

        [Header("Buttons")]
        [SerializeField] private Button topmostButton;
        [SerializeField] private Button minimizeButton;
        [SerializeField] private Button maximizeButton;
        [SerializeField] private Button closeButton;

        [Header("State Visuals")]
        [SerializeField] private Image topmostButtonImage;
        [SerializeField] private TMP_Text topmostLabel;
        [SerializeField] private TMP_Text maximizeLabel;

        private static readonly Color TopmostActiveColor = new Color32(43, 216, 169, 255);
        private static readonly Color TopmostInactiveColor = new Color32(48, 55, 62, 255);

        private FloatingWindowService floatingWindowService;
        private UniWindowController windowController;
        private bool isBound;
        private bool wasDragging;
        private bool isVisible;
        private bool lastTopmostState;
        private bool lastMaximizedState;
        private float hideAt;

        public void Initialize(
            FloatingWindowService service,
            UniWindowController controller)
        {
            floatingWindowService = service;
            windowController = controller;

            SynchronizeCanvasScaling();
            BindButtons();
            if (moveHandle != null)
            {
                moveHandle.Initialize(controller);
                moveHandle.enabled = controller != null;
            }

            ApplyContentInset();
            SetVisible(Application.isEditor);
            RefreshStateVisuals(force: true);
        }

        private void Update()
        {
            SynchronizeCanvasScaling();
            ApplyContentInset();

            if (floatingWindowService == null || windowController == null)
            {
                return;
            }

            bool isDragging = moveHandle != null && moveHandle.IsDragging;
            bool isMaximized = floatingWindowService.IsMaximized;
            if (isMaximized && !lastMaximizedState)
            {
                SetVisible(false);
                hideAt = 0f;
            }

            bool shouldReveal = isMaximized
                ? isDragging
                    || IsCursorAtMaximizedRevealEdge()
                    || isVisible && IsCursorInsideVisibleTitleBar()
                : Application.isEditor
                    || floatingWindowService.ShouldKeepTitleBarVisible
                    || isDragging
                    || IsCursorInTitleBarZone();

            if (shouldReveal)
            {
                hideAt = Time.unscaledTime + HideDelaySeconds;
                SetVisible(true);
            }
            else if (Time.unscaledTime >= hideAt)
            {
                SetVisible(false);
            }

            if (wasDragging && !isDragging)
            {
                floatingWindowService.HandleTitleBarDragEnded();
            }

            if (moveHandle != null)
            {
                moveHandle.enabled = !floatingWindowService.IsMaximized;
            }

            wasDragging = isDragging;
            RefreshStateVisuals(force: false);
        }

        public void ApplyContentInset()
        {
            if (contentCanvas == null || contentRoot == null)
            {
                return;
            }

            float reservedHeight = floatingWindowService != null
                && floatingWindowService.IsFloating
                && !floatingWindowService.IsMaximized
                    ? TitleBarHeight
                    : 0f;
            ApplyTopInset(contentRoot, contentCanvas, reservedHeight);
        }

        internal static void ApplyTopInset(
            RectTransform target,
            Canvas canvas,
            float pixelHeight)
        {
            if (target == null || canvas == null)
            {
                return;
            }

            float inset = Mathf.Max(0f, pixelHeight);
            target.anchorMin = Vector2.zero;
            target.anchorMax = Vector2.one;
            target.pivot = new Vector2(0.5f, 0.5f);
            target.anchoredPosition = new Vector2(0f, -inset * 0.5f);
            target.sizeDelta = new Vector2(0f, -inset);
        }

        private void SynchronizeCanvasScaling()
        {
            if (contentCanvas == null)
            {
                return;
            }

            CanvasScaler source = contentCanvas.GetComponent<CanvasScaler>();
            CanvasScaler target = GetComponent<CanvasScaler>();
            if (source == null || target == null)
            {
                return;
            }

            target.uiScaleMode = source.uiScaleMode;
            target.referencePixelsPerUnit = source.referencePixelsPerUnit;
            target.scaleFactor = source.scaleFactor;
            target.referenceResolution = source.referenceResolution;
            target.screenMatchMode = source.screenMatchMode;
            target.matchWidthOrHeight = source.matchWidthOrHeight;
            target.physicalUnit = source.physicalUnit;
            target.fallbackScreenDPI = source.fallbackScreenDPI;
            target.defaultSpriteDPI = source.defaultSpriteDPI;
            target.dynamicPixelsPerUnit = source.dynamicPixelsPerUnit;
        }

        private void OnDestroy()
        {
            UnbindButtons();
        }

        private bool IsCursorInTitleBarZone()
        {
            if (!TryGetLocalCursor(out Vector2 localCursor, out Vector2 windowSize))
            {
                return false;
            }

            return localCursor.y >= windowSize.y - TitleBarHeight - RevealDistance;
        }

        private bool IsCursorAtMaximizedRevealEdge()
        {
            if (!TryGetLocalCursor(out Vector2 localCursor, out Vector2 windowSize))
            {
                return false;
            }

            return localCursor.y >= windowSize.y - MaximizedRevealEdgeDistance;
        }

        private bool IsCursorInsideVisibleTitleBar()
        {
            if (!TryGetLocalCursor(out Vector2 localCursor, out Vector2 windowSize))
            {
                return false;
            }

            return localCursor.y >= windowSize.y - TitleBarHeight;
        }

        private bool TryGetLocalCursor(
            out Vector2 localCursor,
            out Vector2 windowSize)
        {
            Vector2 windowPosition = windowController.windowPosition;
            windowSize = windowController.windowSize;
            localCursor = windowController.cursorPosition - windowPosition;

            if (localCursor.x < 0f
                || localCursor.x > windowSize.x
                || localCursor.y < 0f
                || localCursor.y > windowSize.y)
            {
                return false;
            }

            return true;
        }

        private void BindButtons()
        {
            if (isBound)
            {
                return;
            }

            if (topmostButton != null) topmostButton.onClick.AddListener(OnTopmostClicked);
            if (minimizeButton != null) minimizeButton.onClick.AddListener(OnMinimizeClicked);
            if (maximizeButton != null) maximizeButton.onClick.AddListener(OnMaximizeClicked);
            if (closeButton != null) closeButton.onClick.AddListener(OnCloseClicked);
            isBound = true;
        }

        private void UnbindButtons()
        {
            if (!isBound)
            {
                return;
            }

            if (topmostButton != null) topmostButton.onClick.RemoveListener(OnTopmostClicked);
            if (minimizeButton != null) minimizeButton.onClick.RemoveListener(OnMinimizeClicked);
            if (maximizeButton != null) maximizeButton.onClick.RemoveListener(OnMaximizeClicked);
            if (closeButton != null) closeButton.onClick.RemoveListener(OnCloseClicked);
            isBound = false;
        }

        private void OnTopmostClicked()
        {
            floatingWindowService?.ToggleAlwaysOnTop();
            RefreshStateVisuals(force: true);
        }

        private void OnMinimizeClicked()
        {
            floatingWindowService?.MinimizeToTaskbar();
        }

        private void OnMaximizeClicked()
        {
            floatingWindowService?.ToggleMaximized();
            RefreshStateVisuals(force: true);
        }

        private void OnCloseClicked()
        {
            floatingWindowService?.HideToSystemTray();
        }

        private void RefreshStateVisuals(bool force)
        {
            if (floatingWindowService == null)
            {
                return;
            }

            bool topmost = floatingWindowService.IsAlwaysOnTop;
            bool maximized = floatingWindowService.IsMaximized;
            if (!force
                && topmost == lastTopmostState
                && maximized == lastMaximizedState)
            {
                return;
            }

            lastTopmostState = topmost;
            lastMaximizedState = maximized;

            if (topmostButtonImage != null)
            {
                topmostButtonImage.color = topmost
                    ? TopmostActiveColor
                    : TopmostInactiveColor;
            }

            if (topmostLabel != null)
            {
                topmostLabel.text = "TOP";
                topmostLabel.color = topmost ? Color.black : Color.white;
            }

            if (maximizeLabel != null)
            {
                maximizeLabel.text = maximized ? "][" : "[]";
            }
        }

        private void SetVisible(bool visible)
        {
            if (titleBarGroup == null)
            {
                return;
            }

            titleBarGroup.alpha = visible ? 1f : 0f;
            titleBarGroup.interactable = visible;
            titleBarGroup.blocksRaycasts = visible;
            isVisible = visible;
        }

        // Unity setup: Create this component through Tools > GreenLight > UI > Bake Floating Window Title Bar.
    }
}
