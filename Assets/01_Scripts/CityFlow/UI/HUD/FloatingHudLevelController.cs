using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using CityFlow.View;

namespace CityFlow.UI
{
    /// <summary>
    /// Maps floating-window presets to the already-baked HUD hierarchy.
    /// Wire minimalOverlay to HUD_TopBar (coin/dot/[+]), mLevelObjects to
    /// [AnalysisCard_BottomLeft, SubPanels_Right], and lLevelObjects to
    /// [Build_Panel, Dock_Right] (scene names are illustrative only).
    /// </summary>
    public sealed class FloatingHudLevelController : MonoBehaviour
    {
        [SerializeField] private CanvasGroup minimalOverlay;
        [SerializeField] private GameObject[] mLevelObjects;
        [SerializeField] private GameObject[] lLevelObjects;

        private FloatingWindowService _floatingWindow;
        private bool _isFloating;
        private bool _isRevealed;
        private int _presetIndex;

        public void Configure(
            CanvasGroup minimal,
            GameObject[] mLevel,
            GameObject[] lLevel)
        {
            minimalOverlay = minimal;
            mLevelObjects = mLevel;
            lLevelObjects = lLevel;
        }

        private void Start()
        {
            FindAndSubscribeService();
            if (_floatingWindow == null)
            {
                ApplyNormalState();
                return;
            }
        }

        private void FindAndSubscribeService()
        {
            if (_floatingWindow != null)
            {
                return;
            }

            _floatingWindow = FindAnyObjectByType<FloatingWindowService>();
            if (_floatingWindow != null)
            {
                _floatingWindow.OnFloatingStateChanged += OnFloatingStateChanged;
                _floatingWindow.OnPresetChanged += OnPresetChanged;
                _isFloating = _floatingWindow.IsFloating;
                _isRevealed = !_isFloating;
                _presetIndex = _floatingWindow.PresetIndex;
                Apply();
            }
        }

        private void OnDestroy()
        {
            if (_floatingWindow == null)
            {
                return;
            }

            _floatingWindow.OnFloatingStateChanged -= OnFloatingStateChanged;
            _floatingWindow.OnPresetChanged -= OnPresetChanged;
        }

        private void Update()
        {
            if (_floatingWindow == null)
            {
                FindAndSubscribeService();
            }

            if (!_isFloating || Mouse.current == null ||
                !Mouse.current.leftButton.wasPressedThisFrame)
            {
                return;
            }

            // HUD controls keep their normal click behavior in both hidden and
            // revealed states; only a click outside UI reveals/toggles the map.
            bool pointerOverUi = EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject();
            if (!ShouldToggleOnClick(pointerOverUi))
            {
                return;
            }

            _isRevealed = !_isRevealed;
            Apply();
        }

        public static bool ShouldToggleOnClick(bool pointerOverUi)
        {
            return !pointerOverUi;
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
            SetCanvasGroup(minimalOverlay, true);
            bool mVisible = !_isFloating || (_isRevealed && _presetIndex >= 1);
            bool lVisible = !_isFloating || (_isRevealed && _presetIndex >= 2);
            ApplyObjects(mLevelObjects, mVisible);
            ApplyObjects(lLevelObjects, lVisible);
        }

        private static void ApplyObjects(GameObject[] objects, bool visible)
        {
            if (objects == null) return;
            for (int i = 0; i < objects.Length; i++)
            {
                CanvasGroup group = GetDeltaGroup(objects[i]);
                SetCanvasGroup(group, visible);
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

            CanvasGroup group = delta.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = delta.AddComponent<CanvasGroup>();
            }

            return group;
        }
    }
}
