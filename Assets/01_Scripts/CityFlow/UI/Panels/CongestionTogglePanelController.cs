using CityFlow.Bootstrap;
using CityFlow.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public class CongestionTogglePanelController : MonoBehaviour, ICityFlowServiceConsumer
    {
        private const float PanelWidth = 156f;
        private const float PanelHeight = 52f;
        private const float HarvestButtonWidth = 160f;
        private const float HorizontalGap = 8f;
        private const float LabelFontSize = 18f;
        private const float LabelLeftInset = 35f;
        private static readonly Color CheckboxFrameColor =
            new Color(0.22f, 0.82f, 0.66f, 1f);

        [Header("UI References")]
        [SerializeField] private Toggle tglCongestionView;

        private CityFlowServices _services;
        private bool _subscribed;

        public void Configure(Toggle congestionToggle)
        {
            tglCongestionView = congestionToggle;
            BindToggle();
        }

        public void Initialize(CityFlowServices services)
        {
            _services = services;
            if (tglCongestionView != null && _services?.Events != null && !_subscribed)
            {
                tglCongestionView.SetIsOnWithoutNotify(_services.Events.IsCongestionViewEnabled);

                // Subscribe to external changes
                _services.Events.CongestionViewToggled += OnExternalToggleChanged;
                _subscribed = true;
            }
        }

        private void Awake()
        {
            ConfigureTopBarPresentation();
            BindToggle();
        }

        private void Start()
        {
            ConfigureTopBarPresentation();
            BindToggle();
        }

        private void ConfigureTopBarPresentation()
        {
            RectTransform rect = transform as RectTransform;
            RectTransform topBar = FindTopBar();
            if (rect == null || topBar == null)
            {
                return;
            }

            if (rect.parent != topBar)
            {
                rect.SetParent(topBar, false);
            }

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(
                -(HarvestButtonWidth * 0.5f + HorizontalGap),
                0f);
            rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            Image background = GetComponent<Image>();
            if (background != null)
            {
                Color color = background.color;
                color.a = 0.9f;
                background.color = color;
            }

            Image checkboxFrame = transform.Find("CheckboxFrame")
                ?.GetComponent<Image>();
            if (checkboxFrame != null)
            {
                checkboxFrame.enabled = true;
                checkboxFrame.color = CheckboxFrameColor;
            }

            TMP_Text label = transform.Find("Label")?.GetComponent<TMP_Text>();
            if (label != null)
            {
                label.enableAutoSizing = false;
                label.fontSize = LabelFontSize;
                label.fontWeight = FontWeight.SemiBold;
                label.characterSpacing = 2f;

                RectTransform labelRect = label.rectTransform;
                labelRect.offsetMin = new Vector2(LabelLeftInset, 2f);
                labelRect.offsetMax = new Vector2(-1f, -2f);
            }
        }

        private RectTransform FindTopBar()
        {
            if (transform.parent is RectTransform directParent &&
                directParent.name == "HUD_TopBar")
            {
                return directParent;
            }

            Transform current = transform.parent;
            while (current != null)
            {
                if (current.Find("HUD_TopBar") is RectTransform topBar)
                {
                    return topBar;
                }

                current = current.parent;
            }

            return null;
        }

        private void BindToggle()
        {
            if (tglCongestionView != null)
            {
                tglCongestionView.onValueChanged.RemoveListener(OnCongestionToggleChanged);
                tglCongestionView.onValueChanged.AddListener(OnCongestionToggleChanged);
            }
        }

        private void OnCongestionToggleChanged(bool isOn)
        {
            if (_services != null && _services.Events != null)
            {
                _services.Events.PublishCongestionViewToggled(isOn);
                Debug.Log($"[Settings] 혼잡도 뷰 토글: {isOn}");
            }
        }

        private void OnExternalToggleChanged(bool isOn)
        {
            if (tglCongestionView != null && tglCongestionView.isOn != isOn)
            {
                tglCongestionView.SetIsOnWithoutNotify(isOn);
            }
        }

        private void OnDestroy()
        {
            if (tglCongestionView != null)
            {
                tglCongestionView.onValueChanged.RemoveListener(OnCongestionToggleChanged);
            }
            if (_services != null && _services.Events != null)
            {
                _services.Events.CongestionViewToggled -= OnExternalToggleChanged;
            }
            _subscribed = false;
        }
    }
}
