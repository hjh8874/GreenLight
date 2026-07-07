using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.DebugTools
{
    // 개발/QA용: 신호 4개짜리 '코리도어'를 놓는다 — 하나의 가로 간선 + 세로 교차 4개.
    // 왼쪽 집 → 오른쪽 회사 통근이 간선을 타고 신호 4개를 순서대로 통과 → 그린웨이브 조율 테스트에 최적.
    // (예전 5×5 격자는 교차로 25개라 신호가 너무 많아 관찰이 어려움 → 코리도어로 단순화.)
    public sealed class DebugCitySeeder : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private int size = 20;

        public void Initialize(CityFlowServices services)
        {
            var p = services.Placement;
            int n = size;
            int y = n / 2;                            // 가로 간선 y (=10)
            int[] cross = { 4, 8, 12, 16 };           // 교차 4개(신호 자리), 4칸 간격 → 신호 사이 travelSlots 4

            // 1. 가로 간선(좌→우 통근로)
            for (int x = 1; x < n - 1; x++) p.Place(new Vector2Int(x, y), TileType.Road);

            // 2. 세로 교차 스텁 — 각 cross x에서 간선 위·아래로 뻗어 (cx,y)를 교차로(도로 이웃 4)로 만듦.
            //    스텁 끝(±2)은 이웃 1, 중간(±1)은 이웃 2 → 신호는 오직 간선 교차점(cx,y)에만 생김.
            foreach (int cx in cross)
            {
                p.Place(new Vector2Int(cx, y - 1), TileType.Road);
                p.Place(new Vector2Int(cx, y - 2), TileType.Road);
                p.Place(new Vector2Int(cx, y + 1), TileType.Road);
                p.Place(new Vector2Int(cx, y + 2), TileType.Road);
            }

            // 3. 좌측 집 · 우측 회사(간선 접점) — 좌→우 통근 흐름이 4개 신호를 관통
            p.Place(new Vector2Int(1, y - 1), TileType.House);
            p.Place(new Vector2Int(1, y + 1), TileType.House);
            p.Place(new Vector2Int(2, y - 1), TileType.House);
            p.Place(new Vector2Int(2, y + 1), TileType.House);
            p.Place(new Vector2Int(n - 2, y - 1), TileType.Office);
            p.Place(new Vector2Int(n - 2, y + 1), TileType.Office);
            p.Place(new Vector2Int(n - 3, y - 1), TileType.Office);
            p.Place(new Vector2Int(n - 3, y + 1), TileType.Office);

            Debug.Log($"[DebugCitySeeder] 코리도어 — 간선 y={y}, 신호 {cross.Length}개(x={string.Join(",", cross)}), 좌 집→우 회사");
        }
    }
}
