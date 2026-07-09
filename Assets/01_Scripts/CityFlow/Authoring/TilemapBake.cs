using UnityEngine;
using UnityEngine.Tilemaps;
using CityFlow.Contracts;

namespace CityFlow.Authoring
{
    // Tilemap을 스캔해 CityTile이 칠해진 칸을 IPlacementService.Place로 엔진에 굽는다(순수 로직).
    // 셀 좌표(x,y) = 엔진 그리드(x,y). 범위 밖·비-CityTile·빈 칸은 Place가 걸러 스킵.
    public static class TilemapBake
    {
        public static int Bake(Tilemap tilemap, IPlacementService placement)
        {
            if (tilemap == null || placement == null) return 0;

            int placed = 0;
            foreach (var cell in tilemap.cellBounds.allPositionsWithin)
            {
                var tile = tilemap.GetTile(cell) as CityTile;   // 빈 칸·비-CityTile은 null → 스킵
                if (tile == null) continue;
                if (placement.Place(new Vector2Int(cell.x, cell.y), tile.type)) placed++;   // 범위 밖은 false→스킵
            }
            return placed;
        }
    }
}
