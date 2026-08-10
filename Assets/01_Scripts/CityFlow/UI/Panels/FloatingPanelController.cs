using CityFlow.View;
using TMPro;
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

        private void Awake()
        {
            ApplyFloatingToggleReadability();
        }

        private void Start()
        {
            FindAndSubscribeService();
            BindButtons();
            SyncFloatingUI();
        }

        private void OnEnable()
        {
            ApplyFloatingToggleReadability();
            SyncFloatingUI();
        }

        private void ApplyFloatingToggleReadability()
        {
            if (tglFloatingMode == null)
            {
                return;
            }

            TMP_Text tmpLabel =
                tglFloatingMode.GetComponentInChildren<TMP_Text>(true);
            if (tmpLabel != null)
            {
                tmpLabel.color = Color.white;
                tmpLabel.fontSize = 16f;
                tmpLabel.fontStyle = FontStyles.Bold;
            }

            Text legacyLabel =
                tglFloatingMode.GetComponentInChildren<Text>(true);
            if (legacyLabel != null)
            {
                legacyLabel.color = Color.white;
                legacyLabel.fontSize = 16;
                legacyLabel.fontStyle = FontStyle.Bold;
            }

            Image background = tglFloatingMode.targetGraphic as Image;
            if (background != null)
            {
                background.color = new Color(0.12f, 0.16f, 0.2f, 1f);
                Outline outline = background.GetComponent<Outline>();
                if (outline == null)
                {
                    outline = background.gameObject.AddComponent<Outline>();
                }

                outline.effectColor = new Color(0.45f, 1f, 0.86f, 1f);
                outline.effectDistance = new Vector2(1f, -1f);
                outline.useGraphicAlpha = false;
            }

            if (tglFloatingMode.graphic is Image checkmark)
            {
                checkmark.color = new Color(0.45f, 1f, 0.86f, 1f);
            }
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

            floatingService = FindAnyObjectByType<FloatingWindowService>();
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

            if (tglFloatingMode != null)
            {
                tglFloatingMode.onValueChanged.AddListener(OnFloatingModeToggled);
            }

            isBound = true;
        }

        private void OnFloatingModeToggled(bool isOn)
        {
            FindAndSubscribeService();
            if (floatingService != null)
            {
                floatingService.SetFloatingMode(isOn);
            }
        }

        private void SyncFloatingUI()
        {
            FindAndSubscribeService();

            if (tglFloatingMode != null && floatingService != null)
            {
                tglFloatingMode.SetIsOnWithoutNotify(floatingService.IsFloating);
            }

            UpdatePresetButtonInteractable(floatingService != null);
        }

        private void OnFloatingStateChanged(bool isFloating)
        {
            if (tglFloatingMode != null)
            {
                tglFloatingMode.SetIsOnWithoutNotify(isFloating);
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
