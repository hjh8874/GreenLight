using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.DebugTools
{
    // 개발/QA용: '대각 도로' 데모 — (2,2)→(17,17) 한 칸씩 대각으로만 이어진 길.
    // 8방향 연결 덕에 차가 이 대각 도로를 그대로 따라 집(좌하)→회사(우상)→집 왕복.
    // (SignalMap은 4방향이라 순수 대각 도로엔 신호 없음 — 경로 추종 확인용.)
    public sealed class DebugCitySeeder : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private int size = 20;

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            var p = services.Placement;

            for (int i = 2; i <= 17; i++) p.Place(new Vector2Int(i, i), TileType.Road);   // 대각 도로

            // 집(좌하, 대각 접점) → 회사(우상, 대각 접점)
            p.Place(new Vector2Int(1, 1), TileType.House);
            p.Place(new Vector2Int(3, 1), TileType.House);
            p.Place(new Vector2Int(1, 3), TileType.House);
            p.Place(new Vector2Int(18, 18), TileType.Office);
            p.Place(new Vector2Int(16, 18), TileType.Office);
            p.Place(new Vector2Int(18, 16), TileType.Office);

            Debug.Log("[DebugCitySeeder] 대각 도로 데모 — (2,2)→(17,17) 대각선, 집(좌하)→회사(우상), 차가 대각 경로 왕복");
        }
    }
}
