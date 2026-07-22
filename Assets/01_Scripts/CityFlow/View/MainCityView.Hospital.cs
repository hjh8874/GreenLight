using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.View
{
    public sealed partial class MainCityView
    {
        [Header("Hospital Visual")]
        [SerializeField] private Color hospitalColor = new Color(0.35f, 0.78f, 0.72f);
        [SerializeField] private Vector3 hospitalScaleRatio = new Vector3(0.72f, 0.62f, 0.75f);

        /// <summary>
        /// 기존 MainCityView의 타일 생성 파이프라인을 유지하면서 Hospital 타일에
        /// 전용 크기와 색상을 적용합니다. 병원 전용 프리팹이 연결되기 전에도
        /// fallback primitive가 투명해지지 않고 정상적으로 표시됩니다.
        /// </summary>
        private void LateUpdate()
        {
            if (tileData == null)
            {
                return;
            }

            foreach (var pair in tileVisuals)
            {
                TileVisual visual = pair.Value;
                if (visual == null ||
                    visual.Type != TileType.Hospital ||
                    visual.Object == null ||
                    visual.Renderer == null)
                {
                    continue;
                }

                visual.Object.transform.localScale = new Vector3(
                    tileSize * hospitalScaleRatio.x,
                    tileSize * hospitalScaleRatio.y,
                    tileSize * hospitalScaleRatio.z);

                ApplyRendererColor(
                    visual.Renderer,
                    hospitalColor,
                    visual.Block);
            }
        }
    }
}
