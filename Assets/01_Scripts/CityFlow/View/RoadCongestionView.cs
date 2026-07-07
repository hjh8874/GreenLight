using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.View
{
    [RequireComponent(typeof(Renderer))]
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

        public void Initialize(CityFlowServices services)
        {
            this.services = services;
            tileData = services.TileData;
            services.Events.CongestionChanged += OnCongestionChanged;
            Refresh();
        }

        private void Awake()
        {
            cachedRenderer = GetComponent<Renderer>();
            propertyBlock = new MaterialPropertyBlock();
            Refresh();
        }

        private void OnDestroy()
        {
            if (services != null)
            {
                services.Events.CongestionChanged -= OnCongestionChanged;
            }
        }

        private void OnCongestionChanged(CongestionEvent e)
        {
            if (e.Tile != tile)
            {
                return;
            }

            ApplyColor(e.Level);
        }

        private void Refresh()
        {
            if (tileData == null || cachedRenderer == null)
            {
                return;
            }

            ApplyColor(tileData.GetCongestion(tile));
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
