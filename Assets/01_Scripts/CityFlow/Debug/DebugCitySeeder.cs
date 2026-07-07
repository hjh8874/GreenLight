using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.DebugTools
{
    // 개발/QA용: MM 방식 — 실제로 '집↔회사'를 잇는 관통 도로들이 교차. 막다른 스텁 없음.
    // 가로 2 × 세로 2 도로가 교차하는 4곳이 자연스럽게 신호가 됨. 차는 실제 도로를 따라
    // 직각으로 꺾어 출발지→목적지→출발지 왕복. (엔진 그리드는 4방향 연결 = 진짜 대각선은 계단식 근사.)
    public sealed class DebugCitySeeder : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private int size = 20;

        public void Initialize(CityFlowServices services)
        {
            var p = services.Placement;
            int n = size;
            int[] rows = { 6, 13 };    // 가로 관통 도로 y
            int[] cols = { 6, 13 };    // 세로 관통 도로 x  → 교차점 4곳 = 신호

            // 1. 관통 도로(끝에서 끝까지 = 스텁 없음). 교차점이 이웃 4 → 신호.
            foreach (int ry in rows) for (int x = 1; x < n - 1; x++) p.Place(new Vector2Int(x, ry), TileType.Road);
            foreach (int cx in cols) for (int y = 1; y < n - 1; y++) p.Place(new Vector2Int(cx, y), TileType.Road);

            // 2. 집(좌측, 가로 도로 접점) → 회사(상단, 세로 도로 접점) : 통근이 격자를 가로질러 꺾임
            p.Place(new Vector2Int(1, rows[0] - 1), TileType.House);
            p.Place(new Vector2Int(1, rows[0] + 1), TileType.House);
            p.Place(new Vector2Int(1, rows[1] - 1), TileType.House);
            p.Place(new Vector2Int(1, rows[1] + 1), TileType.House);

            p.Place(new Vector2Int(cols[1] - 1, n - 1 - 1), TileType.Office);
            p.Place(new Vector2Int(cols[1] + 1, n - 1 - 1), TileType.Office);
            p.Place(new Vector2Int(cols[0] - 1, n - 1 - 1), TileType.Office);
            p.Place(new Vector2Int(cols[0] + 1, n - 1 - 1), TileType.Office);

            Debug.Log($"[DebugCitySeeder] MM 격자 — 가로 y={rows[0]},{rows[1]} × 세로 x={cols[0]},{cols[1]}, 교차 신호 4개(스텁 없음), 좌 집→상 회사");
        }
    }
}
