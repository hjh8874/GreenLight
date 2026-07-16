using System.Collections;
using Kirurobo;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityFlow.View
{
    public sealed class FloatingWindowService : MonoBehaviour
    {
        private enum FloatingState
        {
            Normal,
            Entering,
            Floating,
            Resizing,
            Maximizing,
        }

        private const string FloatingPrefKey = "cityflow.window.floating";
        private const string PresetPrefKey = "cityflow.window.preset";
        private const string TopmostPrefKey = "cityflow.window.topmost";
        private const float BoardFitMargin = 0.5f;

        private static readonly Vector2[] ContentPresets =
        {
            new Vector2(480f, 270f),
            new Vector2(960f, 540f),
            new Vector2(1440f, 810f),
        };

        private float boardWidth;
        private float boardHeight;
        private float titleBarHeight;
        private bool shouldFitBoardToScreen = true;
        private bool isFloating = true;
        private bool isAlwaysOnTop = true;
        private bool isMaximized;
        private bool isDockedToTop;
        private bool notifyFloatingEntry;
        private int presetIndex = 1;
        private int appliedPresetIndex = -1;
        private FloatingState state = FloatingState.Normal;

        private UniWindowController windowController;
        private FloatingWindowPlacementService placementService;
        private FloatingWindowTitleBarController titleBarController;
        private WindowsFloatingWindowHost windowsWindowHost;

        private bool cameraStateSaved;
        private CameraClearFlags savedClearFlags;
        private Color savedBackground;
        private bool savedAllowHdr;
        private bool savedAllowMsaa;
        private Rect savedCameraRect;
        private Camera clearCamera;

        private int lastScreenWidth = -1;
        private int lastScreenHeight = -1;
        private int originalTargetFrameRate;
        private bool originalRunInBackground;
        private bool windowStateCaptured;
        private int originalScreenWidth;
        private int originalScreenHeight;
        private FullScreenMode originalFullScreenMode;
        private Vector2 originalWindowPosition;
        private bool originalWindowPositionCaptured;

        private Vector2 restoreWindowPosition;
        private Vector2 restoreWindowSize;

        private Coroutine transitionCoroutine;
        private Coroutine resizeCoroutine;
        private Coroutine snapCoroutine;
        private Coroutine windowModeCoroutine;

        public bool IsFloating => isFloating;
        public bool IsAlwaysOnTop => isAlwaysOnTop;
        public bool IsMaximized => isMaximized;
        public bool ShouldKeepTitleBarVisible => isDockedToTop && !isMaximized;
        public int PresetIndex => presetIndex;

        public event System.Action<bool> OnFloatingStateChanged;

        public void Init(float width, float height, bool fitBoardToScreen = true)
        {
            boardWidth = width;
            boardHeight = height;
            shouldFitBoardToScreen = fitBoardToScreen;

            originalTargetFrameRate = Application.targetFrameRate;
            originalRunInBackground = Application.runInBackground;
            Application.runInBackground = true;

            titleBarController = FindAnyObjectByType<FloatingWindowTitleBarController>(
                FindObjectsInactive.Include);
            titleBarHeight = titleBarController != null
                ? FloatingWindowTitleBarController.TitleBarHeight
                : 0f;

            isFloating = PlayerPrefs.GetInt(FloatingPrefKey, 1) == 1;
            isAlwaysOnTop = PlayerPrefs.GetInt(TopmostPrefKey, 1) == 1;
            presetIndex = Mathf.Clamp(
                PlayerPrefs.GetInt(PresetPrefKey, 1),
                0,
                ContentPresets.Length - 1);
            SavePrefs();

            if (Application.isEditor)
            {
                if (isFloating)
                {
                    state = FloatingState.Floating;
                    titleBarController?.Initialize(this, null);
                }
            }
            else
            {
                if (isFloating)
                {
                    EnterFloating();
                }
            }

            ApplyPerformanceMode();
            if (shouldFitBoardToScreen)
            {
                FitBoardToScreen();
            }

            if (titleBarController == null)
            {
                Debug.LogWarning(
                    "[FloatingWindowService] Baked title bar was not found. " +
                    "Run Tools > GreenLight > UI > Bake Floating Window Title Bar.");
            }
        }

        private void OnDestroy()
        {
            StopRunningCoroutines();

            if (windowController != null)
            {
                windowController.OnMonitorChanged -= OnMonitorConfigurationChanged;
                windowController.isTransparent = false;
                windowController.isTopmost = false;
            }

            windowsWindowHost?.Dispose();
            windowsWindowHost = null;
            ApplyTransparentCamera(false);

            if (!Application.isEditor && windowStateCaptured)
            {
                RequestOriginalWindowState();
                RestoreOriginalWindowPosition();
            }

            Application.targetFrameRate = originalTargetFrameRate;
            Application.runInBackground = originalRunInBackground;
        }

        private void Update()
        {
            HandleKeyboardShortcuts();
            HandleNativeWindowRequests();
            PollResolutionChange();
        }

        public void SetFloatingMode(bool enable)
        {
            if (isFloating == enable) return;
            isFloating = enable;

            if (Application.isEditor)
            {
                SavePrefs();
                OnFloatingStateChanged?.Invoke(isFloating);
                return;
            }

            if (isFloating)
            {
                if (state == FloatingState.Normal)
                {
                    notifyFloatingEntry = true;
                    EnterFloating();
                }
            }
            else
            {
                ExitFloating();
            }

            ApplyPerformanceMode();
            SavePrefs();
        }

        private void ExitFloating()
        {
            if (state == FloatingState.Normal) return;

            StopRunningCoroutines();

            ApplyTransparentCamera(false);

            if (windowController != null)
            {
                windowController.isTransparent = false;
                windowController.isTopmost = false;
                windowController.isHitTestEnabled = false;
            }

            if (!Application.isEditor && windowStateCaptured)
            {
                windowsWindowHost?.RestoreWindowed();
                RequestOriginalWindowState();
                RestoreOriginalWindowPosition();
            }

            if (titleBarController != null)
            {
                titleBarController.gameObject.SetActive(false);
            }

            state = FloatingState.Normal;
            OnFloatingStateChanged?.Invoke(false);
            
            Debug.Log("[FloatingWindowService] Exited floating mode. Restored to normal window.");
        }

        public void SetPreset(int index)
        {
            presetIndex = Mathf.Clamp(index, 0, ContentPresets.Length - 1);
            isMaximized = false;
            ApplyPerformanceMode();
            SavePrefs();

            if (Application.isEditor || state == FloatingState.Entering)
            {
                return;
            }

            if (!isFloating)
            {
                Vector2 size = ContentPresets[presetIndex];
                Screen.SetResolution((int)size.x, (int)size.y, Screen.fullScreenMode);
                return;
            }

            BeginResizeToPreset();
        }

        public void ToggleAlwaysOnTop()
        {
            isAlwaysOnTop = !isAlwaysOnTop;
            if (windowController != null)
            {
                windowController.isTopmost = isAlwaysOnTop;
            }

            SavePrefs();
            Debug.Log($"[FloatingWindowService] Always on top: {isAlwaysOnTop}.");
        }

        public void MinimizeToTaskbar()
        {
            if (Application.isEditor)
            {
                Debug.Log("[FloatingWindowService] Minimize is available in a Windows player build.");
                return;
            }

            windowsWindowHost?.MinimizeToTaskbar();
        }

        public void ToggleMaximized()
        {
            if (Application.isEditor || state == FloatingState.Entering)
            {
                isMaximized = !isMaximized;
                return;
            }

            BeginMaximizeChange(!isMaximized);
        }

        public void HideToSystemTray()
        {
            if (Application.isEditor)
            {
                Debug.Log("[FloatingWindowService] System tray mode is available in a Windows player build.");
                return;
            }

            windowsWindowHost?.HideToSystemTray();
        }

        public void HandleTitleBarDragEnded()
        {
            if (!isMaximized)
            {
                BeginConstrainWindow("title bar drag completed");
            }
        }

        private void HandleKeyboardShortcuts()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.f2Key.wasPressedThisFrame) SetPreset(0);
            if (keyboard.f3Key.wasPressedThisFrame) SetPreset(1);
            if (keyboard.f4Key.wasPressedThisFrame) SetPreset(2);
        }

        private void HandleNativeWindowRequests()
        {
            if (windowsWindowHost == null)
            {
                return;
            }

            windowsWindowHost.PollRequests(out bool shouldRestore, out bool shouldExit);
            if (shouldRestore)
            {
                windowsWindowHost.RestoreFromSystemTray();
                windowsWindowHost.ApplyBorderless();
                BeginConstrainWindow("system tray restore");
            }

            if (shouldExit)
            {
                Application.Quit();
            }
        }

        private void EnterFloating()
        {
            state = FloatingState.Entering;
            transitionCoroutine = StartCoroutine(EnterFloatingAtBottomRight());
        }

        private IEnumerator EnterFloatingAtBottomRight()
        {
            EnsureWindowController();
            EnsurePlacementService();
            yield return null;
            yield return null;

            EnsureWindowsWindowHost();

            CaptureOriginalWindowState();
            CaptureOriginalWindowPosition();

            int targetPresetIndex = presetIndex;
            WindowWorkArea initialWorkArea = placementService.SelectWorkArea(
                windowController.windowPosition,
                windowController.windowSize,
                windowController.cursorPosition);
            FloatingWindowPlacement placement = placementService.PlaceAtBottomRight(
                GetWindowSizeForPreset(targetPresetIndex),
                initialWorkArea);

            ConfigureFloatingWindow();
            windowsWindowHost?.ApplyBorderless();
            ApplyResolution(placement.Size);
            yield return null;
            yield return null;

            windowsWindowHost?.ApplyBorderless();
            ApplyWindowSize(placement.Size);
            ApplyWindowPosition(placement.Position);
            yield return null;
            ApplyWindowPosition(placement.Position);
            yield return ApplyNativeWorkAreaCorrection(maximize: false);
            UpdateDockedState(placement);

            ApplyTransparentCamera(true);
            ApplyContentViewport();
            if (titleBarController != null)
            {
                titleBarController.gameObject.SetActive(true);
                titleBarController.Initialize(this, windowController);
            }
            SubscribeMonitorChanges();

            state = FloatingState.Floating;
            appliedPresetIndex = targetPresetIndex;
            transitionCoroutine = null;

            if (notifyFloatingEntry)
            {
                notifyFloatingEntry = false;
                OnFloatingStateChanged?.Invoke(true);
            }

            Debug.Log(
                $"[FloatingWindowService] Started borderless on monitor {placement.WorkArea.MonitorIndex} " +
                $"at {placement.Size.x:0}x{placement.Size.y:0} (bottom-right).");

            if (appliedPresetIndex != presetIndex)
            {
                BeginResizeToPreset();
            }
        }

        private void BeginResizeToPreset()
        {
            if (windowModeCoroutine != null)
            {
                StopCoroutine(windowModeCoroutine);
                windowModeCoroutine = null;
            }

            if (resizeCoroutine != null)
            {
                StopCoroutine(resizeCoroutine);
            }

            resizeCoroutine = StartCoroutine(ResizeToPresetRoutine());
        }

        private IEnumerator ResizeToPresetRoutine()
        {
            state = FloatingState.Resizing;
            EnsureWindowController();
            EnsurePlacementService();
            yield return null;

            int targetPresetIndex = presetIndex;
            FloatingWindowPlacement placement = placementService.ResizeWithinWorkArea(
                windowController.windowPosition,
                windowController.windowSize,
                GetWindowSizeForPreset(targetPresetIndex),
                windowController.cursorPosition);

            yield return ApplyWindowPlacement(placement);
            appliedPresetIndex = targetPresetIndex;
            isMaximized = false;
            state = FloatingState.Floating;
            resizeCoroutine = null;

            Debug.Log(
                $"[FloatingWindowService] Resized to {placement.Size.x:0}x{placement.Size.y:0} " +
                $"inside monitor {placement.WorkArea.MonitorIndex} ({placement.Anchor}).");

            if (appliedPresetIndex != presetIndex)
            {
                BeginResizeToPreset();
            }
        }

        private void BeginMaximizeChange(bool maximize)
        {
            if (resizeCoroutine != null)
            {
                StopCoroutine(resizeCoroutine);
                resizeCoroutine = null;
            }

            if (windowModeCoroutine != null)
            {
                StopCoroutine(windowModeCoroutine);
            }

            windowModeCoroutine = StartCoroutine(ChangeMaximizedStateRoutine(maximize));
        }

        private IEnumerator ChangeMaximizedStateRoutine(bool maximize)
        {
            state = FloatingState.Maximizing;
            EnsurePlacementService();

            FloatingWindowPlacement placement;
            if (maximize)
            {
                Vector2 currentPosition = windowController.windowPosition;
                Vector2 currentSize = windowController.windowSize;
                if (!isMaximized)
                {
                    restoreWindowPosition = currentPosition;
                    restoreWindowSize = currentSize;
                }

                WindowWorkArea workArea = placementService.SelectWorkArea(
                    currentPosition,
                    currentSize,
                    windowController.cursorPosition);
                placement = new FloatingWindowPlacement(
                    workArea.Bounds.position,
                    workArea.Bounds.size,
                    workArea,
                    FloatingWindowAnchor.TopLeft);
            }
            else
            {
                Vector2 requestedSize = restoreWindowSize.sqrMagnitude > 1f
                    ? restoreWindowSize
                    : GetWindowSizeForPreset(presetIndex);
                placement = placementService.ConstrainAfterDrag(
                    restoreWindowPosition,
                    requestedSize,
                    windowController.cursorPosition);
            }

            isMaximized = maximize;
            yield return ApplyWindowPlacement(placement);
            state = FloatingState.Floating;
            windowModeCoroutine = null;

            Debug.Log(maximize
                ? $"[FloatingWindowService] Expanded to monitor {placement.WorkArea.MonitorIndex} work area."
                : "[FloatingWindowService] Restored the floating window size.");
        }

        private IEnumerator ApplyWindowPlacement(FloatingWindowPlacement placement)
        {
            ApplyResolution(placement.Size);
            yield return null;
            yield return null;

            windowsWindowHost?.ApplyBorderless();
            ApplyWindowSize(placement.Size);
            ApplyWindowPosition(placement.Position);
            yield return null;
            ApplyWindowPosition(placement.Position);
            yield return ApplyNativeWorkAreaCorrection(isMaximized);
            UpdateDockedState(placement);
            ApplyContentViewport();
        }

        private void OnMonitorConfigurationChanged()
        {
            if (isMaximized)
            {
                BeginMaximizeChange(true);
                return;
            }

            BeginConstrainWindow("monitor configuration changed");
        }

        private void BeginConstrainWindow(string reason)
        {
            if (Application.isEditor || state != FloatingState.Floating)
            {
                return;
            }

            if (snapCoroutine != null)
            {
                StopCoroutine(snapCoroutine);
            }

            snapCoroutine = StartCoroutine(ConstrainWindowRoutine(reason));
        }

        private IEnumerator ConstrainWindowRoutine(string reason)
        {
            yield return null;
            EnsurePlacementService();

            FloatingWindowPlacement placement = placementService.ConstrainAfterDrag(
                windowController.windowPosition,
                windowController.windowSize,
                windowController.cursorPosition);

            if ((placement.Size - windowController.windowSize).sqrMagnitude > 1f)
            {
                ApplyResolution(placement.Size);
                yield return null;
                yield return null;
                windowsWindowHost?.ApplyBorderless();
                ApplyWindowSize(placement.Size);
            }

            ApplyWindowPosition(placement.Position);
            yield return null;
            ApplyWindowPosition(placement.Position);
            yield return ApplyNativeWorkAreaCorrection(maximize: false);
            UpdateDockedState(placement);

            snapCoroutine = null;
            Debug.Log(
                $"[FloatingWindowService] Window constrained after {reason}: " +
                $"monitor {placement.WorkArea.MonitorIndex}, {placement.Anchor}.");
        }

        private void ConfigureFloatingWindow()
        {
            if (windowController == null)
            {
                return;
            }

            windowController.isTransparent = true;
            windowController.isTopmost = isAlwaysOnTop;
            windowController.isHitTestEnabled = true;
            windowController.hitTestType = UniWindowController.HitTestType.Opacity;
        }

        private void EnsureWindowController()
        {
            if (windowController != null)
            {
                return;
            }

            GameObject host = new GameObject("FloatingWindow");
            host.transform.SetParent(transform, false);
            windowController = host.AddComponent<UniWindowController>();
            windowController.forceWindowed = true;
            windowController.autoSwitchCameraBackground = false;
        }

        private void EnsurePlacementService()
        {
            placementService ??= new FloatingWindowPlacementService(
                new WindowsWindowWorkAreaProvider(),
                edgeMargin: 0f);
        }

        private void EnsureWindowsWindowHost()
        {
            windowsWindowHost ??= new WindowsFloatingWindowHost();
            windowsWindowHost.TryAttach();
        }

        private void SubscribeMonitorChanges()
        {
            if (windowController == null)
            {
                return;
            }

            windowController.OnMonitorChanged -= OnMonitorConfigurationChanged;
            windowController.OnMonitorChanged += OnMonitorConfigurationChanged;
        }

        private void ApplyTransparentCamera(bool transparent)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            if (transparent)
            {
                if (!cameraStateSaved)
                {
                    savedClearFlags = camera.clearFlags;
                    savedBackground = camera.backgroundColor;
                    savedAllowHdr = camera.allowHDR;
                    savedAllowMsaa = camera.allowMSAA;
                    savedCameraRect = camera.rect;
                    cameraStateSaved = true;
                }

                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                EnsureClearCamera(camera);
            }
            else if (cameraStateSaved)
            {
                camera.allowHDR = savedAllowHdr;
                camera.allowMSAA = savedAllowMsaa;
                camera.clearFlags = savedClearFlags;
                camera.backgroundColor = savedBackground;
                camera.rect = savedCameraRect;
                cameraStateSaved = false;

                if (clearCamera != null)
                {
                    Destroy(clearCamera.gameObject);
                    clearCamera = null;
                }
            }
        }

        private void EnsureClearCamera(Camera mainCamera)
        {
            if (clearCamera == null)
            {
                GameObject clearObject = new GameObject("FloatingWindowTransparentClearCamera");
                clearObject.transform.SetParent(transform, false);
                clearCamera = clearObject.AddComponent<Camera>();
            }

            clearCamera.clearFlags = CameraClearFlags.SolidColor;
            clearCamera.backgroundColor = Color.clear;
            clearCamera.cullingMask = 0;
            clearCamera.depth = mainCamera.depth - 100f;
            clearCamera.allowHDR = false;
            clearCamera.allowMSAA = false;
            clearCamera.rect = new Rect(0f, 0f, 1f, 1f);
        }

        private void ApplyContentViewport()
        {
            Camera camera = Camera.main;
            if (camera == null || Screen.height <= 0)
            {
                return;
            }

            float contentHeight = Mathf.Max(1f, Screen.height - GetReservedTitleBarHeight());
            camera.rect = new Rect(0f, 0f, 1f, contentHeight / Screen.height);

            if (shouldFitBoardToScreen)
            {
                FitBoardToScreen();
            }
        }

        private void ApplyWindowSize(Vector2 size)
        {
            if (windowController != null)
            {
                windowController.windowSize = size;
            }
        }

        private void ApplyWindowPosition(Vector2 position)
        {
            if (windowController != null)
            {
                windowController.windowPosition = position;
            }
        }

        private static void ApplyResolution(Vector2 size)
        {
            Screen.SetResolution(
                Mathf.RoundToInt(size.x),
                Mathf.RoundToInt(size.y),
                FullScreenMode.Windowed);
        }

        private Vector2 GetWindowSizeForPreset(int index)
        {
            Vector2 contentSize = ContentPresets[Mathf.Clamp(index, 0, ContentPresets.Length - 1)];
            return new Vector2(contentSize.x, contentSize.y + titleBarHeight);
        }

        private IEnumerator ApplyNativeWorkAreaCorrection(bool maximize)
        {
            if (Application.isEditor || windowsWindowHost == null)
            {
                yield break;
            }

            bool corrected = maximize
                ? windowsWindowHost.FitToNearestMonitorWorkArea()
                : windowsWindowHost.ConstrainToNearestMonitorWorkArea();
            if (!corrected)
            {
                Debug.LogWarning("[FloatingWindowService] Native work-area correction failed.");
                yield break;
            }

            yield return null;
            ApplyContentViewport();
        }

        private float GetReservedTitleBarHeight()
        {
            return isMaximized ? 0f : titleBarHeight;
        }

        private void UpdateDockedState(FloatingWindowPlacement placement)
        {
            isDockedToTop = placement.Anchor == FloatingWindowAnchor.Top
                || placement.Anchor == FloatingWindowAnchor.TopLeft
                || placement.Anchor == FloatingWindowAnchor.TopRight;
        }

        private void CaptureOriginalWindowState()
        {
            if (windowStateCaptured)
            {
                return;
            }

            originalScreenWidth = Screen.width;
            originalScreenHeight = Screen.height;
            originalFullScreenMode = Screen.fullScreenMode;
            windowStateCaptured = true;
        }

        private void CaptureOriginalWindowPosition()
        {
            if (windowController == null)
            {
                return;
            }

            originalWindowPosition = windowController.windowPosition;
            originalWindowPositionCaptured = true;
        }

        private void RequestOriginalWindowState()
        {
            if (!windowStateCaptured)
            {
                return;
            }

            Screen.SetResolution(
                originalScreenWidth,
                originalScreenHeight,
                originalFullScreenMode);
        }

        private void RestoreOriginalWindowPosition()
        {
            if (windowController != null && originalWindowPositionCaptured)
            {
                windowController.windowPosition = originalWindowPosition;
            }
        }

        private void ApplyPerformanceMode()
        {
            Application.targetFrameRate = presetIndex == 0 ? 30 : 60;
        }

        private void SavePrefs()
        {
            PlayerPrefs.SetInt(FloatingPrefKey, isFloating ? 1 : 0);
            PlayerPrefs.SetInt(PresetPrefKey, presetIndex);
            PlayerPrefs.SetInt(TopmostPrefKey, isAlwaysOnTop ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void PollResolutionChange()
        {
            if (Screen.width == lastScreenWidth && Screen.height == lastScreenHeight)
            {
                return;
            }

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            ApplyContentViewport();
        }

        private void FitBoardToScreen()
        {
            Camera camera = Camera.main;
            float contentHeight = Screen.height - GetReservedTitleBarHeight();
            if (camera == null
                || boardWidth <= 0f
                || boardHeight <= 0f
                || contentHeight <= 0f)
            {
                return;
            }

            float aspect = Screen.width / contentHeight;
            camera.orthographicSize = Mathf.Max(
                boardHeight * 0.5f,
                boardWidth / (2f * aspect)) + BoardFitMargin;
        }

        private void StopRunningCoroutines()
        {
            if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
            if (resizeCoroutine != null) StopCoroutine(resizeCoroutine);
            if (snapCoroutine != null) StopCoroutine(snapCoroutine);
            if (windowModeCoroutine != null) StopCoroutine(windowModeCoroutine);
        }

        // Unity setup: MainCityView creates this service at runtime.
        // Bake the custom title bar through Tools > GreenLight > UI before making a Windows build.
    }
}
