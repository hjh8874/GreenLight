using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.DebugTools
{
    // 개발/QA용: 확장 테스트 도시 — 신호 1개(중앙 십자 7,7) + 집 5가구 → 회사 3곳.
    // 대각·직교 접근 섞어서 여러 경로가 그 하나의 신호를 통과.
    public sealed class DebugCitySeeder : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private int size = 16;

        public void Initialize(CityFlowServices services)
        {
            var p = services.Placement;
            void Road(int x, int y) => p.Place(new Vector2Int(x, y), TileType.Road);
            void House(int x, int y) => p.Place(new Vector2Int(x, y), TileType.House);
            void Office(int x, int y) => p.Place(new Vector2Int(x, y), TileType.Office);

            // 1. 중앙 십자 = 유일한 신호 (7,7)
            for (int x = 2; x <= 12; x++) Road(x, 7);   // 가로팔
            for (int y = 2; y <= 12; y++) Road(7, y);   // 세로팔

            // 2. 대각 접근로 (신호 안 만듦 — 8방향으로만 연결)
            Road(1, 6); Road(0, 5);      // 좌하 대각 (가로팔 왼끝 2,7에서)
            Road(13, 8); Road(14, 9);    // 우상 대각 (가로팔 오른끝 12,7에서)

            // 3. 집 5가구 (좌·하, 대각+직교 접근 섞음)
            House(0, 4); House(0, 6);    // 좌하 대각 접근
            House(2, 8);                 // 가로팔 왼쪽 직교 접근
            House(6, 2); House(8, 2);    // 세로팔 아래 직교 접근

            // 4. 회사 3곳 (우·상) — 도로 옆(대각/직교 접근)
            Office(14, 8);   // 우상 대각(13,8·14,9) 접점
            Office(12, 6);   // 가로팔 오른쪽 직교 접점
            Office(7, 13);   // 세로팔 위 직교 접점

            Debug.Log("[DebugCitySeeder] 확장 도시 — 신호 1개(7,7), 집 5 → 회사 3, 대각·직교 혼합");
        }
    }
}
