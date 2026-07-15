using CityFlow.View;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public class FloatingPanelController : MonoBehaviour
    {
        [Header("Floating Window")]
        [SerializeField] private Toggle tglFloatingMode;
        [SerializeField] private Button btnPresetS;
        [SerializeField] private Button btnPresetM;
        [SerializeField] private Button btnPresetL;

        private bool _isBound;
        private FloatingWindowService _floatingService;

        private void Start()
        {
            _floatingService = FindFirstObjectByType<FloatingWindowService>();
            if (_floatingService != null)
            {
                _floatingService.OnFloatingStateChanged += OnFloatingStateChangedEvent;
            }
            BindButtons();
        }

        private void OnEnable()
        {
            SyncFloatingUI();
        }

        private void OnDestroy()
        {
            if (_floatingService != null)
            {
                _floatingService.OnFloatingStateChanged -= OnFloatingStateChangedEvent;
            }
        }

        private void OnFloatingStateChangedEvent(bool isFloating)
        {
            if (tglFloatingMode != null)
            {
                tglFloatingMode.SetIsOnWithoutNotify(isFloating);
            }
            UpdatePresetButtonInteractable(isFloating);
        }

        private void BindButtons()
        {
            if (_isBound) return;

            if (tglFloatingMode != null)
            {
                tglFloatingMode.onValueChanged.AddListener(OnFloatingToggleChanged);
            }

            if (btnPresetS != null) btnPresetS.onClick.AddListener(() => OnPresetClicked(0));
            if (btnPresetM != null) btnPresetM.onClick.AddListener(() => OnPresetClicked(1));
            if (btnPresetL != null) btnPresetL.onClick.AddListener(() => OnPresetClicked(2));

            _isBound = true;
        }

        private void SyncFloatingUI()
        {
            if (_floatingService == null)
            {
                _floatingService = FindFirstObjectByType<FloatingWindowService>();
            }

            if (_floatingService == null) return;

            if (tglFloatingMode != null)
            {
                tglFloatingMode.SetIsOnWithoutNotify(_floatingService.IsFloating);
            }

            UpdatePresetButtonInteractable(_floatingService.IsFloating);
        }

        private void OnFloatingToggleChanged(bool isOn)
        {
            if (_floatingService == null)
            {
                _floatingService = FindFirstObjectByType<FloatingWindowService>();
            }

            if (_floatingService == null) return;

            if (_floatingService.IsFloating != isOn)
            {
                _floatingService.ToggleFloating();
                Debug.Log($"[FloatingPanel] 플로팅 모드 토글: {(isOn ? "ON (창 모드)" : "OFF (전체 화면)")}");
            }

            UpdatePresetButtonInteractable(isOn);
        }

        private void OnPresetClicked(int index)
        {
            if (_floatingService == null)
            {
                _floatingService = FindFirstObjectByType<FloatingWindowService>();
            }

            if (_floatingService == null) return;

            if (!_floatingService.IsFloating && tglFloatingMode != null)
            {
                tglFloatingMode.isOn = true;
            }

            _floatingService.SetPreset(index);
            Debug.Log($"[FloatingPanel] 플로팅 창 프리셋 변경: {(index == 0 ? "S (480x270)" : index == 1 ? "M (960x540)" : "L (1440x810)")}");
        }

        private void UpdatePresetButtonInteractable(bool floating)
        {
            if (btnPresetS != null) btnPresetS.interactable = floating;
            if (btnPresetM != null) btnPresetM.interactable = floating;
            if (btnPresetL != null) btnPresetL.interactable = floating;
        }
    }
}
