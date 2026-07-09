using UnityEngine;
using UnityEngine.Tilemaps;
using CityFlow.Contracts;

namespace CityFlow.Authoring
{
    // Bake 결과: 배치 성공 수 + 스킵 수(격자 밖이거나 이미 찬 칸).
    public readonly struct BakeResult
    {
        public readonly int Placed;
        public readonly int Skipped;
        public BakeResult(int placed, int skipped) { Placed = placed; Skipped = skipped; }
    }

    // Tilemap을 스캔해 CityTile이 칠해진 칸을 IPlacementService.Place로 엔진에 굽는다(순수 로직).
    // 셀 좌표(x,y) = 엔진 그리드(x,y). 빈 칸·비-CityTile은 무시, 격자 밖·중복은 Place 실패로 스킵 집계.
    public static class TilemapBake
    {
        public static BakeResult Bake(Tilemap tilemap, IPlacementService placement)
        {
            if (tilemap == null || placement == null) return new BakeResult(0, 0);

            int placed = 0, skipped = 0;
            foreach (var cell in tilemap.cellBounds.allPositionsWithin)
            {
                var tile = tilemap.GetTile(cell) as CityTile;   // 빈 칸·비-CityTile은 null → 무시
                if (tile == null) continue;
                if (placement.Place(new Vector2Int(cell.x, cell.y), tile.type)) placed++;
                else skipped++;   // 격자 밖이거나 이미 찬 칸 — 조용히 스킵하되 개수는 알린다
            }
            return new BakeResult(placed, skipped);
        }
    }
}
