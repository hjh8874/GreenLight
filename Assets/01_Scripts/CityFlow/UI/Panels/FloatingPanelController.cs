using CityFlow.View;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public sealed class FloatingPanelController : MonoBehaviour
    {
        [Header("Floating Window")]
        [SerializeField] private Toggle tglFloatingMode;
        [SerializeField] private Button btnPresetS;
        [SerializeField] private Button btnPresetM;
        [SerializeField] private Button btnPresetL;

        private bool isBound;
        private FloatingWindowService floatingService;

        private void Start()
        {
            FindAndSubscribeService();
            BindButtons();
            HideWindowModeToggle();
            SyncFloatingUI();
        }

        private void OnEnable()
        {
            SyncFloatingUI();
        }

        private void OnDestroy()
        {
            if (floatingService != null)
            {
                floatingService.OnFloatingStateChanged -= OnFloatingStateChanged;
            }
        }

        private void FindAndSubscribeService()
        {
            if (floatingService != null)
            {
                return;
            }

            floatingService = FindFirstObjectByType<FloatingWindowService>();
            if (floatingService != null)
            {
                floatingService.OnFloatingStateChanged += OnFloatingStateChanged;
            }
        }

        private void BindButtons()
        {
            if (isBound)
            {
                return;
            }

            if (btnPresetS != null) btnPresetS.onClick.AddListener(() => OnPresetClicked(0));
            if (btnPresetM != null) btnPresetM.onClick.AddListener(() => OnPresetClicked(1));
            if (btnPresetL != null) btnPresetL.onClick.AddListener(() => OnPresetClicked(2));
            isBound = true;
        }

        private void HideWindowModeToggle()
        {
            if (tglFloatingMode != null)
            {
                tglFloatingMode.SetIsOnWithoutNotify(true);
                tglFloatingMode.gameObject.SetActive(false);
            }
        }

        private void SyncFloatingUI()
        {
            FindAndSubscribeService();

            if (tglFloatingMode != null)
            {
                tglFloatingMode.SetIsOnWithoutNotify(true);
            }

            UpdatePresetButtonInteractable(floatingService != null);
        }

        private void OnFloatingStateChanged(bool isFloating)
        {
            if (tglFloatingMode != null)
            {
                tglFloatingMode.SetIsOnWithoutNotify(true);
            }

            UpdatePresetButtonInteractable(floatingService != null);
        }

        private void OnPresetClicked(int index)
        {
            FindAndSubscribeService();
            if (floatingService == null)
            {
                return;
            }

            floatingService.SetPreset(index);
            Debug.Log(
                $"[FloatingPanel] Preset selected: " +
                $"{(index == 0 ? "S (480x270)" : index == 1 ? "M (960x540)" : "L (1440x810)")}.");
        }

        private void UpdatePresetButtonInteractable(bool interactable)
        {
            if (btnPresetS != null) btnPresetS.interactable = interactable;
            if (btnPresetM != null) btnPresetM.interactable = interactable;
            if (btnPresetL != null) btnPresetL.interactable = interactable;
        }

        // Unity setup: Assign the S/M/L buttons. The legacy floating toggle is hidden at runtime.
    }
}
