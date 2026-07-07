using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.DebugTools
{
    // 개발/QA용: 'ㄱ자(L)' 코리도어 — 집(좌하) →가로 간선→ 코너 →세로 간선→ 회사(우상).
    // 통근 경로가 꺾여 흐르고(MM 방식: 차는 실제 도로를 따라 출발→목적, 목적→출발 왕복),
    // 신호 4개(가로 축 2 + 세로 축 2)를 순서대로 통과. 코너는 이웃 2 = 신호 없음(꺾임만).
    // 그린웨이브 판단은 같은 다리(가로끼리/세로끼리)에서 정확 — 코너 넘는 쌍은 회전 근사(2차).
    public sealed class DebugCitySeeder : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private int size = 20;

        public void Initialize(CityFlowServices services)
        {
            var p = services.Placement;

            const int hy = 4;               // 가로 간선 y
            const int vx = 15;              // 세로 간선 x (코너 = (vx,hy))
            const int hx0 = 2, hxEnd = 15;  // 가로 간선 x 범위
            const int vy0 = 4, vyEnd = 17;  // 세로 간선 y 범위

            // 1. 가로 다리(집 쪽) + 세로 다리(회사 쪽) — (vx,hy)에서 직각으로 꺾임
            for (int x = hx0; x <= hxEnd; x++) p.Place(new Vector2Int(x, hy), TileType.Road);
            for (int y = vy0; y <= vyEnd; y++) p.Place(new Vector2Int(vx, y), TileType.Road);

            // 2. 신호 만들 교차 스텁 — 가로 다리엔 세로 스텁, 세로 다리엔 가로 스텁.
            //    스텁 끝은 이웃 1, 중간 2 → 신호는 간선 교차점에만.
            foreach (int cx in new[] { 6, 10 })          // 가로 축 신호 2개
                foreach (int dy in new[] { -2, -1, 1, 2 })
                    p.Place(new Vector2Int(cx, hy + dy), TileType.Road);
            foreach (int cy in new[] { 8, 12 })          // 세로 축 신호 2개
                foreach (int dx in new[] { -2, -1, 1, 2 })
                    p.Place(new Vector2Int(vx + dx, cy), TileType.Road);

            // 3. 집(가로 다리 좌측 시작) · 회사(세로 다리 상단) — 통근이 ㄱ자를 관통
            p.Place(new Vector2Int(hx0, hy - 1), TileType.House);
            p.Place(new Vector2Int(hx0, hy + 1), TileType.House);
            p.Place(new Vector2Int(hx0 + 1, hy - 1), TileType.House);
            p.Place(new Vector2Int(vx - 1, vyEnd), TileType.Office);
            p.Place(new Vector2Int(vx + 1, vyEnd), TileType.Office);
            p.Place(new Vector2Int(vx - 1, vyEnd - 1), TileType.Office);

            Debug.Log($"[DebugCitySeeder] ㄱ자 코리도어 — 코너({vx},{hy}), 신호 4개[가로 x=6,10 / 세로 y=8,12], 집(좌하)→회사(우상)");
        }
    }
}
