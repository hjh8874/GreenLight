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

        private UniWindowController uniWinController;
        private int pendingSizeReapplyFrames;   // 부착 후 창 크기 재적용 카운터(리뷰 픽스 — 부착 전 설정은 유실됨)
        private bool cameraStateSaved;
        private CameraClearFlags savedClearFlags;
        private Color savedBackground;

        private int lastScreenWidth = -1;
        private int lastScreenHeight = -1;

        public void Init(float width, float height)
        {
            boardW = width;
            boardH = height;

            isFloating = PlayerPrefs.GetInt(FloatingPrefKey, 0) == 1;
            presetIndex = Mathf.Clamp(PlayerPrefs.GetInt(PresetPrefKey, 1), 0, Presets.Length - 1);

            if (!Application.isEditor && isFloating)
            {
                EnterFloating();
                ApplyPresetWindowSize();
            }

            ApplyPerfMode();
            FitBoardToScreen();
        }

        private void OnDestroy()
        {
            Application.targetFrameRate = 60;
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

            // 부착 후 창 크기 재적용(리뷰 픽스): UniWinC 네이티브 부착은 자신의 첫 Update에서 일어나고
            // 부착 시 재적용 목록에 windowSize가 없어, 부착 전 설정한 크기는 유실된다 — 2프레임 뒤 1회 재적용.
            if (pendingSizeReapplyFrames > 0 && !Application.isEditor)
            {
                pendingSizeReapplyFrames--;
                if (pendingSizeReapplyFrames == 0)
                {
                    ApplyPresetWindowSize();
                }
            }

            PollResolutionChange();
        }

        private void ToggleFloating()
        {
            isFloating = !isFloating;

            if (!Application.isEditor)
            {
                if (isFloating)
                {
                    EnterFloating();
                    ApplyPresetWindowSize();
                }
                else
                {
                    ExitFloating();
                }
            }

            ApplyPerfMode();
            SavePrefs();
        }

        private void SetPreset(int index)
        {
            presetIndex = index;

            if (!Application.isEditor)
            {
                ApplyPresetWindowSize();
            }

            ApplyPerfMode();
            SavePrefs();
        }

        // UniWinC 부착(1회) + 투명/최상위/히트테스트 설정 + 카메라 투명 전환. 호출자가 빌드 전용을 보장.
        private void EnterFloating()
        {
            EnsureUniWinController();

            uniWinController.isTransparent = true;
            uniWinController.isTopmost = true;
            uniWinController.isHitTestEnabled = true;
            uniWinController.hitTestType = UniWindowController.HitTestType.Opacity;   // 픽셀 알파 자동 클릭통과

            pendingSizeReapplyFrames = 2;   // 네이티브 창 부착(UniWinC 첫 Update) 후 프리셋 크기 재적용

            ApplyTransparentCamera(true);
        }

        private void ExitFloating()
        {
            if (uniWinController != null)
            {
                uniWinController.isTransparent = false;
                uniWinController.isTopmost = false;
            }

            ApplyTransparentCamera(false);
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
                    cameraStateSaved = true;
                }

                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.clear;   // 투명 블랙(리뷰 픽스) — premultiplied alpha 합성 전제, 패키지 컨벤션과 일치
            }
            else if (cameraStateSaved)
            {
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
