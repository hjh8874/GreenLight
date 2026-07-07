using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.DebugTools
{
    // Scene뷰 Gizmos로 시뮬 상태를 눈으로 보는 개발용 오버레이(환).
    //  - 도로: 혼잡 레벨 색(Free 초록 / Slow 노랑 / Jam 빨강), 진하기 = 밀도
    //  - 건물: 종류별 플랫 색(집/회사/학교)
    //  - Burst: 발생 타일에 잠깐 커졌다 줄어드는 와이어 구
    // 김건의 Game뷰 렌더러와 영역이 다름(이건 디버그/시연 도구). Bootstrap이 주입.
    public sealed class SimDebugOverlay : MonoBehaviour, ICityFlowServiceConsumer
    {
        // IReadOnlyTileData엔 그리드 크기가 없어서 여기서 들고 있음(Bootstrap과 같은 기본값).
        [SerializeField] private int width = GridUtil.DefaultWidth;
        [SerializeField] private int height = GridUtil.DefaultHeight;
        [SerializeField] private float burstSeconds = 0.8f;
        [SerializeField] private float burstMaxRadius = 0.9f;

        private CityFlowServices services;

        // 진행 중인 Burst들(디버그 도구라 alloc 신경 안 씀). hideAt = 사라질 시각.
        private readonly List<(Vector2Int tile, int reward, float hideAt)> activeBursts = new();

        public void Initialize(CityFlowServices services)
        {
            this.services = services;
            services.Events.FlowBurst += OnFlowBurst;
        }

        private void OnDestroy()
        {
            if (services != null)
            {
                services.Events.FlowBurst -= OnFlowBurst;
            }
        }

        private void OnFlowBurst(FlowBurstEvent e)
        {
            activeBursts.Add((e.Tile, e.Reward, Time.time + burstSeconds));
        }

        private void Update()
        {
            // 수명 지난 Burst 제거(뒤에서부터 지워야 인덱스 안 꼬임).
            for (int i = activeBursts.Count - 1; i >= 0; i--)
            {
                if (Time.time >= activeBursts[i].hideAt)
                {
                    activeBursts.RemoveAt(i);
                }
            }
        }

        // Gizmos는 Scene뷰에서 플레이 중 실시간 렌더. services 없으면(에디트 모드) 조용히 스킵.
        private void OnDrawGizmos()
        {
            if (services == null)
            {
                return;
            }

            IReadOnlyTileData data = services.TileData;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var tile = new Vector2Int(x, y);
                    TileType type = data.GetTileType(tile);
                    if (type == TileType.Empty)
                    {
                        continue;
                    }

                    Gizmos.color = ColorFor(type, data, tile);
                    Gizmos.DrawCube(GridUtil.GridToWorld(tile), Vector3.one * 0.9f);
                }
            }

            DrawBursts();
        }

        private static Color ColorFor(TileType type, IReadOnlyTileData data, Vector2Int tile)
        {
            switch (type)
            {
                case TileType.Road:
                    // 혼잡 레벨로 색, 밀도로 진하기 — 한산한 도로는 옅게, 꽉 찬 도로는 선명하게.
                    Color baseColor = data.GetCongestion(tile) switch
                    {
                        CongestionLevel.Jam => Color.red,
                        CongestionLevel.Slow => Color.yellow,
                        _ => Color.green,
                    };
                    float density = data.GetDensity01(tile);
                    return Color.Lerp(new Color(0.2f, 0.2f, 0.2f), baseColor, Mathf.Max(0.25f, density));
                case TileType.House:
                    return new Color(0.3f, 0.6f, 1f);   // 파랑
                case TileType.Office:
                    return new Color(1f, 0.6f, 0.1f);   // 주황
                case TileType.School:
                    return new Color(0.7f, 0.4f, 1f);   // 보라
                default:
                    return Color.gray;
            }
        }

        private void DrawBursts()
        {
            Gizmos.color = Color.white;
            for (int i = 0; i < activeBursts.Count; i++)
            {
                var b = activeBursts[i];
                // 남은 수명 0~1 → 반지름 크게 시작해 줄어듦(FlowBurstView와 같은 감각).
                float life01 = Mathf.Clamp01((b.hideAt - Time.time) / burstSeconds);
                float radius = Mathf.Lerp(0.1f, burstMaxRadius, life01);
                Gizmos.DrawWireSphere(GridUtil.GridToWorld(b.tile), radius);
            }
        }
    }
}
