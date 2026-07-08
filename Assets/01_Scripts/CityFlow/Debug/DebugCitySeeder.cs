using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.DebugTools
{
    // 개발/QA용: 작은 테스트 도시 — 신호 1개(중앙 직교 십자) + 대각선 접근로 + 직교 팔.
    // 차가 대각선·가로·세로 여러 방향으로 그 하나의 신호(7,7)를 통과 → 8방향 + 신호 + 튜너 동시 테스트.
    public sealed class DebugCitySeeder : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private int size = 14;   // 작은 도시

        public void Initialize(CityFlowServices services)
        {
            var p = services.Placement;
            void Road(int x, int y) => p.Place(new Vector2Int(x, y), TileType.Road);

            // 1. 중앙 직교 십자 = 유일한 신호 (7,7). 가로팔·세로팔.
            for (int x = 3; x <= 11; x++) Road(x, 7);   // 가로팔
            for (int y = 3; y <= 11; y++) Road(7, y);   // 세로팔

            // 2. 대각선 접근로 (신호 안 만듦 — 4방향 이웃 없음, 8방향으로만 연결)
            Road(2, 6); Road(1, 5);      // 좌하 대각 (가로팔 왼끝 3,7에서 뻗음)
            Road(12, 8); Road(13, 9);    // 우상 대각 (가로팔 오른끝 11,7에서 뻗음)

            // 3. 집(좌·하) → 회사(우·상). 접근 방향 섞음(대각/직교).
            p.Place(new Vector2Int(0, 5), TileType.House);   // 좌하 대각 접근
            p.Place(new Vector2Int(1, 4), TileType.House);   // 좌하 대각 접근
            p.Place(new Vector2Int(7, 2), TileType.House);   // 세로팔 아래(직교 접근)

            p.Place(new Vector2Int(13, 10), TileType.Office); // 우상 대각 접근
            p.Place(new Vector2Int(12, 11), TileType.Office); // 우상 대각 접근
            p.Place(new Vector2Int(7, 12), TileType.Office);  // 세로팔 위(직교 접근)

            Debug.Log("[DebugCitySeeder] 테스트 도시 — 신호 1개(7,7 십자) + 대각/직교 접근로, 대각·가로·세로 통과 테스트");
        }
    }
}
