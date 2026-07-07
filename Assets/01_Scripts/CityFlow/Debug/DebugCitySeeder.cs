using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.DebugTools
{
    // 개발용: 진짜 엔진(useFakeServices=false)에 "판단 가능한" 도시를 놓는다.
    // 도로 격자 + 왼쪽 주거(집)·오른쪽 상업(회사)·가운데 학교 → 좌→우 통근 흐름이 격자를 타고 흐름.
    public sealed class DebugCitySeeder : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private int size = 20;

        private static readonly Vector2Int[] Dirs =
            { new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0) };

        public void Initialize(CityFlowServices services)
        {
            var p = services.Placement;
            var d = services.TileData;
            int n = size;

            // 1. 도로 격자 — 가로·세로 간선(4칸 간격). 교차점이 나중에 신호 자리.
            int[] lines = { 2, 6, 10, 14, 18 };
            foreach (int r in lines) for (int x = 1; x < n - 1; x++) p.Place(new Vector2Int(x, r), TileType.Road);
            foreach (int c in lines) for (int y = 1; y < n - 1; y++) p.Place(new Vector2Int(c, y), TileType.Road);

            // 2. 도로 옆 빈 칸에 건물(듬성듬성). 왼쪽=집·오른쪽=회사·가운데=학교.
            int houses = 0, offices = 0, schools = 0;
            for (int y = 1; y < n - 1; y++)
                for (int x = 1; x < n - 1; x++)
                {
                    var t = new Vector2Int(x, y);
                    if (d.GetTileType(t) != TileType.Empty) continue;   // 도로/기존 건물 위 X
                    if (!AdjacentToRoad(d, t, n)) continue;             // 도로 접점 없으면 통근 불가
                    if ((x + y) % 2 != 0) continue;                     // 절반만 = 빽빽하지 않게

                    if (x <= 8) { p.Place(t, TileType.House); houses++; }
                    else if (x >= 12) { p.Place(t, TileType.Office); offices++; }
                    else { p.Place(t, TileType.School); schools++; }
                }

            Debug.Log($"[DebugCitySeeder] 도시 배치 — 집{houses}·회사{offices}·학교{schools} (좌 주거→우 상업 통근)");
        }

        private static bool AdjacentToRoad(IReadOnlyTileData d, Vector2Int t, int n)
        {
            foreach (var dir in Dirs)
            {
                var v = t + dir;
                if (v.x < 0 || v.x >= n || v.y < 0 || v.y >= n) continue;
                if (d.GetTileType(v) == TileType.Road) return true;
            }
            return false;
        }
    }
}
