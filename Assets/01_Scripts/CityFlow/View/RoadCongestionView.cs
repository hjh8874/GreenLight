using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.View
{
    public sealed class RoadCongestionView : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private Vector2Int tile;
        [SerializeField] private Color freeColor = new Color(0.25f, 0.8f, 0.35f);
        [SerializeField] private Color slowColor = new Color(1f, 0.75f, 0.2f);
        [SerializeField] private Color jamColor = new Color(0.95f, 0.25f, 0.2f);

        private IReadOnlyTileData tileData;
        private Renderer cachedRenderer;
        private MaterialPropertyBlock propertyBlock;
        private CityFlowServices services;
        private bool subscribed;
        private bool isOverlayEnabled = false;

        public void Configure(
            Vector2Int tile,
            Renderer targetRenderer,
            Color freeColor,
            Color slowColor,
            Color jamColor)
        {
            this.tile = tile;
            cachedRenderer = targetRenderer;
            this.freeColor = freeColor;
            this.slowColor = slowColor;
            this.jamColor = jamColor;
            propertyBlock ??= new MaterialPropertyBlock();
            Refresh();
        }

        public void Initialize(CityFlowServices services)
        {
            if (subscribed && this.services == services)
            {
                Refresh();
                return;
            }

            if (subscribed && this.services != null)
            {
                this.services.Events.CongestionChanged -= OnCongestionChanged;
                this.services.Events.CongestionViewToggled -= OnCongestionViewToggled;
            }

            this.services = services;
            tileData = services.TileData;
            services.Events.CongestionChanged += OnCongestionChanged;
            services.Events.CongestionViewToggled += OnCongestionViewToggled;
            
            isOverlayEnabled = services.Events.IsCongestionViewEnabled;
            
            subscribed = true;
            Refresh();
        }

        private void Awake()
        {
            cachedRenderer ??= GetComponentInChildren<Renderer>();
            propertyBlock ??= new MaterialPropertyBlock();
            Refresh();
        }

        private void OnDestroy()
        {
            if (subscribed && services != null)
            {
                services.Events.CongestionChanged -= OnCongestionChanged;
                services.Events.CongestionViewToggled -= OnCongestionViewToggled;
            }
            subscribed = false;
        }

        private void OnCongestionChanged(CongestionEvent e)
        {
            if (e.Tile != tile || !isOverlayEnabled)
            {
                return;
            }

            ApplyColor(e.Level);
        }

        private void OnCongestionViewToggled(bool isEnabled)
        {
            isOverlayEnabled = isEnabled;
            if (isEnabled)
            {
                Refresh();
            }
            else
            {
                ClearColor();
            }
        }

        private void Refresh()
        {
            if (tileData == null || cachedRenderer == null)
            {
                return;
            }

            if (isOverlayEnabled)
            {
                ApplyColor(tileData.GetCongestion(tile));
            }
            else
            {
                ClearColor();
            }
        }

        private void ClearColor()
        {
            ApplyColor(CongestionLevel.Free);
        }

        private void ApplyColor(CongestionLevel level)
        {
            if (cachedRenderer == null)
            {
                return;
            }

            Color color = level switch
            {
                CongestionLevel.Jam => jamColor,
                CongestionLevel.Slow => slowColor,
                _ => freeColor
            };

            cachedRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor("_BaseColor", color);
            propertyBlock.SetColor("_Color", color);
            cachedRenderer.SetPropertyBlock(propertyBlock);
        }
    }
}
