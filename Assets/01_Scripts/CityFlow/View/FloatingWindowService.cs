using System.Collections;
using Kirurobo;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityFlow.View
{
    // 데스크톱 플로팅 창 글루(스펙 2026-07-12): UniWindowController(MIT, com.kirurobo.uniwinc) 위 얇은 서비스.
    // 씬 배선 0 — MainCityView.Initialize가 런타임 AddComponent. 기본 = 일반 창, F1로 옵트인(스펙 §핵심결정).
    // 에디터 = no-op — 창/카메라 API 전부 스킵, 상태·프리셋·저부하 로직만 살아있음(스펙 §핵심결정).
    public sealed class FloatingWindowService : MonoBehaviour
    {
        private enum FloatingState
        {
            Normal,
            Entering,
            Floating,
            Exiting,
        }

        private const string FloatingPrefKey = "cityflow.window.floating";
        private const string PresetPrefKey = "cityflow.window.preset";
        private const float Margin = 0.5f;

        // S / M / L — 방치형 유저는 "구석에 작게↔볼 때 크게" 두 모드만 씀(자유 리사이즈는 YAGNI, 스펙 §핵심결정).
        private static readonly Vector2[] Presets =
        {
            new Vector2(480f, 270f),
            new Vector2(960f, 540f),
            new Vector2(1440f, 810f),
        };

        private float boardW;
        private float boardH;
        private bool isFloating;
        private int presetIndex = 1;   // 기본 M
        private FloatingState state = FloatingState.Normal;

        private UniWindowController uniWinController;
        private bool cameraStateSaved;
        private CameraClearFlags savedClearFlags;
        private Color savedBackground;
        private bool savedAllowHdr;
        private bool savedAllowMsaa;

        private int lastScreenWidth = -1;
        private int lastScreenHeight = -1;

        private int originalTargetFrameRate;   // 리뷰 픽스 — Destroy 시 무조건 60이 아니라 진입 시점 값으로 복원
        private bool originalRunInBackground;
        private bool windowStateCaptured;
        private int originalScreenWidth;
        private int originalScreenHeight;
        private FullScreenMode originalFullScreenMode;
        private Vector2 originalWindowPosition;
        private bool originalWindowPositionCaptured;
        private Vector2 resizeAnchorPosition;
        private bool resizeAnchorCaptured;
        private Coroutine transitionCoroutine;
        private Coroutine resizeCoroutine;

        public bool IsFloating => isFloating;
        public int PresetIndex => presetIndex;

        public void Init(float width, float height)
        {
            boardW = width;
            boardH = height;

            originalTargetFrameRate = Application.targetFrameRate;
            originalRunInBackground = Application.runInBackground;
            Application.runInBackground = true;

            isFloating = PlayerPrefs.GetInt(FloatingPrefKey, 0) == 1;
            presetIndex = Mathf.Clamp(PlayerPrefs.GetInt(PresetPrefKey, 1), 0, Presets.Length - 1);

            if (!Application.isEditor && isFloating)
            {
                EnterFloating();
            }

            ApplyPerfMode();
            FitBoardToScreen();
        }

        private void OnDestroy()
        {
            if (transitionCoroutine != null)
            {
                StopCoroutine(transitionCoroutine);
            }

            if (resizeCoroutine != null)
            {
                StopCoroutine(resizeCoroutine);
            }

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
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null)
            {
                if (keyboard.f1Key.wasPressedThisFrame)
                {
                    ToggleFloating();
                }

                if (keyboard.f2Key.wasPressedThisFrame)
                {
                    SetPreset(0);
                }

                if (keyboard.f3Key.wasPressedThisFrame)
                {
                    SetPreset(1);
                }

                if (keyboard.f4Key.wasPressedThisFrame)
                {
                    SetPreset(2);
                }
            }

            PollResolutionChange();
        }

        private void ToggleFloating()
        {
            if (Application.isEditor)
            {
                isFloating = !isFloating;
            }
            else if (state == FloatingState.Normal)
            {
                isFloating = true;
                EnterFloating();
            }
            else if (state == FloatingState.Floating)
            {
                isFloating = false;
                ExitFloating();
            }
            else
            {
                Debug.Log("[FloatingWindowService] Floating toggle ignored during a window transition.");
                return;
            }

            ApplyPerfMode();
            SavePrefs();
        }

        private void SetPreset(int index)
        {
            presetIndex = Mathf.Clamp(index, 0, Presets.Length - 1);

            if (!Application.isEditor)
            {
                if (state != FloatingState.Entering && state != FloatingState.Exiting)
                {
                    ResizeToPresetKeepingPosition();
                }
            }

            ApplyPerfMode();
            SavePrefs();
        }

        private void EnterFloating()
        {
            state = FloatingState.Entering;
            transitionCoroutine = StartCoroutine(EnterFloatingAtCurrentPosition());
        }

        private void ExitFloating()
        {
            state = FloatingState.Exiting;
            transitionCoroutine = StartCoroutine(ExitFloatingAtOriginalPosition());
        }

        private IEnumerator EnterFloatingAtCurrentPosition()
        {
            if (resizeCoroutine != null)
            {
                yield return resizeCoroutine;
            }

            EnsureUniWinController();

            // UniWinC attaches the native window handle from its first Update.
            yield return null;
            yield return null;

            CaptureOriginalWindowState();
            CaptureOriginalWindowPosition();
            ApplyPresetResolution();

            yield return null;
            yield return null;

            ConfigureFloatingWindow();
            ApplyPresetWindowSize();
            RestoreOriginalWindowPosition();

            yield return null;
            RestoreOriginalWindowPosition();
            ApplyTransparentCamera(true);

            state = FloatingState.Floating;
            transitionCoroutine = null;

            Vector2 size = Presets[presetIndex];
            Debug.Log($"[FloatingWindowService] Entered floating mode at {size.x:0}x{size.y:0} without changing window position.");
        }

        private IEnumerator ExitFloatingAtOriginalPosition()
        {
            if (resizeCoroutine != null)
            {
                yield return resizeCoroutine;
            }

            if (uniWinController != null)
            {
                uniWinController.isTransparent = false;
                uniWinController.isTopmost = false;
            }

            ApplyTransparentCamera(false);
            RequestOriginalWindowState();

            yield return null;
            yield return null;
            RestoreOriginalWindowPosition();

            yield return null;
            RestoreOriginalWindowPosition();
            ClearOriginalWindowState();

            state = FloatingState.Normal;
            transitionCoroutine = null;
            Debug.Log("[FloatingWindowService] Exited floating mode and restored the original window state.");
        }

        private void ConfigureFloatingWindow()
        {
            if (uniWinController == null)
            {
                return;
            }

            uniWinController.isTransparent = true;
            uniWinController.isTopmost = true;
            uniWinController.isHitTestEnabled = true;
            uniWinController.hitTestType = UniWindowController.HitTestType.Opacity;
        }

        private void EnsureUniWinController()
        {
            if (uniWinController != null)
            {
                return;
            }

            // 전용 자식 GO에 부착(리뷰 픽스): UniWinC 싱글턴 Awake는 중복 감지 시 그 GameObject 전체를
            // Destroy한다 — MainCityView와 같은 GO에 붙이면 뷰가 통째로 죽는 지뢰.
            GameObject host = new GameObject("FloatingWindow");
            host.transform.SetParent(transform, false);
            uniWinController = host.AddComponent<UniWindowController>();
            uniWinController.forceWindowed = true;
            uniWinController.autoSwitchCameraBackground = false;   // 카메라 전환은 이 서비스가 직접 소유(중복 방지)
        }

        // 투명창의 전제(스펙 §핵심결정). DriveViewCamera(PiP)는 건드리지 않음 — Camera.main만.
        private void ApplyTransparentCamera(bool transparent)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            if (transparent)
            {
                if (!cameraStateSaved)
                {
                    savedClearFlags = cam.clearFlags;
                    savedBackground = cam.backgroundColor;
                    savedAllowHdr = cam.allowHDR;
                    savedAllowMsaa = cam.allowMSAA;
                    cameraStateSaved = true;
                }

                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.clear;   // 투명 블랙(리뷰 픽스) — premultiplied alpha 합성 전제, 패키지 컨벤션과 일치
            }
            else if (cameraStateSaved)
            {
                cam.allowHDR = savedAllowHdr;
                cam.allowMSAA = savedAllowMsaa;
                cam.clearFlags = savedClearFlags;
                cam.backgroundColor = savedBackground;
                cameraStateSaved = false;
            }
        }

        private void ApplyPresetWindowSize()
        {
            if (uniWinController == null)
            {
                return;
            }

            uniWinController.windowSize = Presets[presetIndex];
        }

        private void ApplyPresetResolution()
        {
            Vector2 size = Presets[presetIndex];
            Screen.SetResolution(
                Mathf.RoundToInt(size.x),
                Mathf.RoundToInt(size.y),
                FullScreenMode.Windowed);
        }

        private void ResizeToPresetKeepingPosition()
        {
            if (resizeCoroutine != null)
            {
                StopCoroutine(resizeCoroutine);
            }

            resizeCoroutine = StartCoroutine(ResizeToPresetKeepingPositionRoutine());
        }

        private IEnumerator ResizeToPresetKeepingPositionRoutine()
        {
            EnsureUniWinController();

            yield return null;
            yield return null;

            if (!resizeAnchorCaptured)
            {
                resizeAnchorPosition = uniWinController.windowPosition;
                resizeAnchorCaptured = true;
            }

            ApplyPresetResolution();

            yield return null;
            yield return null;
            ApplyPresetWindowSize();
            uniWinController.windowPosition = resizeAnchorPosition;

            yield return null;
            uniWinController.windowPosition = resizeAnchorPosition;

            resizeAnchorCaptured = false;
            resizeCoroutine = null;

            Vector2 size = Presets[presetIndex];
            Debug.Log($"[FloatingWindowService] Resized window to {size.x:0}x{size.y:0} at the current position.");
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
            if (uniWinController == null)
            {
                return;
            }

            originalWindowPosition = uniWinController.windowPosition;
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
            if (uniWinController == null || !originalWindowPositionCaptured)
            {
                return;
            }

            uniWinController.windowPosition = originalWindowPosition;
        }

        private void ClearOriginalWindowState()
        {
            windowStateCaptured = false;
            originalWindowPositionCaptured = false;
        }

        // 저부하(스펙 §핵심결정): 플로팅+S면 30, 그 외 60. 창 API가 아니라 에디터에서도 적용·검증 가능.
        private void ApplyPerfMode()
        {
            Application.targetFrameRate = (isFloating && presetIndex == 0) ? 30 : 60;
        }

        private void SavePrefs()
        {
            PlayerPrefs.SetInt(FloatingPrefKey, isFloating ? 1 : 0);
            PlayerPrefs.SetInt(PresetPrefKey, presetIndex);
        }

        private void PollResolutionChange()
        {
            if (Screen.width == lastScreenWidth && Screen.height == lastScreenHeight)
            {
                return;
            }

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            FitBoardToScreen();
        }

        // 띠 비율 창에서 도시가 안 잘리게(스펙 §핵심결정): orthoSize = max(보드 반높이, 반너비/화면비) + margin.
        private void FitBoardToScreen()
        {
            Camera cam = Camera.main;
            if (cam == null || boardW <= 0f || boardH <= 0f || Screen.height <= 0)
            {
                return;
            }

            float aspect = (float)Screen.width / Screen.height;
            cam.orthographicSize = Mathf.Max(boardH * 0.5f, boardW / (2f * aspect)) + Margin;
        }
    }
}
