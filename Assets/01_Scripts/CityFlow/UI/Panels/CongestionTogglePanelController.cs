using CityFlow.Bootstrap;
using CityFlow.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public class CongestionTogglePanelController : MonoBehaviour, ICityFlowServiceConsumer
    {
        private const float OutlineWidth = 0.22f;

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
            BindToggle();
        }

        private void ConfigureTopBarPresentation()
        {
            if (transform.parent == null ||
                transform.parent.name != "FloatingWindowContentRoot")
            {
                return;
            }

            RectTransform rect = transform as RectTransform;
            RectTransform topBar =
                transform.parent.Find("HUD_TopBar") as RectTransform;
            float height = topBar != null && topBar.rect.height > 0f
                ? topBar.rect.height
                : 60f;
            rect.anchorMin = new Vector2(0.36f, 1f);
            rect.anchorMax = new Vector2(0.46f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, height);

            Image background = GetComponent<Image>();
            if (background != null)
            {
                Color color = background.color;
                color.a = 0.52f;
                background.color = color;
            }

            TMP_Text[] labels = GetComponentsInChildren<TMP_Text>(true);
            for (int index = 0; index < labels.Length; index++)
            {
                ApplyReadableText(labels[index]);
            }
        }

        private static void ApplyReadableText(TMP_Text text)
        {
            text.color = Color.white;
            text.fontWeight = FontWeight.SemiBold;
            if (text.font == null || text.fontSharedMaterial == null)
            {
                return;
            }

            Material material = text.fontMaterial;
            if (material == null)
            {
                return;
            }

            material.EnableKeyword("OUTLINE_ON");
            material.SetColor("_OutlineColor", Color.black);
            material.SetFloat("_OutlineWidth", OutlineWidth);
            text.UpdateMeshPadding();
            text.SetMaterialDirty();
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
