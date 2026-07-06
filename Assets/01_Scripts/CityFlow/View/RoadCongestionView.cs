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

        public void Initialize(CityFlowServices services)
        {
            tileData = services.TileData;
            Refresh();
        }

        private void Awake()
        {
            cachedRenderer = GetComponent<Renderer>();
            propertyBlock = new MaterialPropertyBlock();
        }

        private void Update()
        {
            Refresh();
        }

        private void Refresh()
        {
            if (tileData == null || cachedRenderer == null)
            {
                return;
            }

            Color color = tileData.GetCongestion(tile) switch
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
