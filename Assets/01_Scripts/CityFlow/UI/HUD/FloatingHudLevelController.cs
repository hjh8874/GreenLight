using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using CityFlow.View;

namespace CityFlow.UI
{
    /// <summary>
    /// Maps floating-window presets to the already-baked HUD hierarchy.
    /// Wire chromeRoot to build/menu chrome, minimalOverlay to coin/dot/[+],
    /// and levelDeltas to [0]=M-only and [1]=L-only content in the scene.
    /// </summary>
    public sealed class FloatingHudLevelController : MonoBehaviour
    {
        [SerializeField] private CanvasGroup chromeRoot;
        [SerializeField] private CanvasGroup minimalOverlay;
        [SerializeField] private GameObject[] levelDeltas;

        private FloatingWindowService _floatingWindow;
        private bool _isFloating;
        private bool _isRevealed;
        private int _presetIndex;

        private void Awake()
        {
            if (chromeRoot == null) chromeRoot = GetComponent<CanvasGroup>();
        }

        private void OnEnable()
        {
            _floatingWindow = FindAnyObjectByType<FloatingWindowService>();
            if (_floatingWindow == null)
            {
                ApplyNormalState();
                return;
            }

            _floatingWindow.OnFloatingStateChanged += OnFloatingStateChanged;
            _floatingWindow.OnPresetChanged += OnPresetChanged;
            _isFloating = _floatingWindow.IsFloating;
            _isRevealed = !_isFloating;
            _presetIndex = _floatingWindow.PresetIndex;
            Apply();
        }

        private void OnDisable()
        {
            if (_floatingWindow == null)
            {
                return;
            }

            _floatingWindow.OnFloatingStateChanged -= OnFloatingStateChanged;
            _floatingWindow.OnPresetChanged -= OnPresetChanged;
            _floatingWindow = null;
        }

        private void Update()
        {
            if (!_isFloating || Mouse.current == null ||
                !Mouse.current.leftButton.wasPressedThisFrame)
            {
                return;
            }

            // Visible HUD controls keep their normal click behavior. A click
            // outside UI is the map reveal/hide gesture.
            if (_isRevealed && EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            _isRevealed = !_isRevealed;
            Apply();
        }

        private void OnFloatingStateChanged(bool floating)
        {
            _isFloating = floating;
            _isRevealed = !floating;
            Apply();
        }

        private void OnPresetChanged(int presetIndex)
        {
            _presetIndex = presetIndex;
            Apply();
        }

        private void ApplyNormalState()
        {
            _isFloating = false;
            _isRevealed = true;
            _presetIndex = 2;
            Apply();
        }

        private void Apply()
        {
            bool chromeVisible = !_isFloating || _isRevealed;
            SetCanvasGroup(chromeRoot, chromeVisible);
            SetCanvasGroup(minimalOverlay, true);
            if (levelDeltas == null)
            {
                return;
            }

            for (int i = 0; i < levelDeltas.Length; i++)
            {
                if (levelDeltas[i] != null)
                {
                    CanvasGroup group = GetDeltaGroup(levelDeltas[i]);
                    if (group != null)
                    {
                        bool visible = chromeVisible && _presetIndex > i;
                        group.alpha = visible ? 1f : 0f;
                        group.interactable = visible;
                        group.blocksRaycasts = visible;
                    }
                }
            }
        }

        private static void SetCanvasGroup(CanvasGroup group, bool visible)
        {
            if (group == null) return;
            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }

        private static CanvasGroup GetDeltaGroup(GameObject delta)
        {
            if (delta == null)
            {
                return null;
            }

            return delta.GetComponent<CanvasGroup>() ??
                delta.AddComponent<CanvasGroup>();
        }
    }
}
