using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.ViewKit
{
    public enum HighwayMarkerKind
    {
        Isolated,
        Endpoint,
        Interior
    }

    public static class HighwayVisualMath
    {
        public static bool IsVertical(Vector2Int tile, ISet<Vector2Int> highways)
        {
            return highways != null
                && (highways.Contains(tile + Vector2Int.up) || highways.Contains(tile + Vector2Int.down));
        }

        public static HighwayMarkerKind Kind(Vector2Int tile, ISet<Vector2Int> highways)
        {
            int neighbors = 0;
            if (highways != null)
            {
                if (highways.Contains(tile + Vector2Int.left)) neighbors++;
                if (highways.Contains(tile + Vector2Int.right)) neighbors++;
                if (highways.Contains(tile + Vector2Int.up)) neighbors++;
                if (highways.Contains(tile + Vector2Int.down)) neighbors++;
            }

            if (neighbors == 0) return HighwayMarkerKind.Isolated;
            return neighbors == 1 ? HighwayMarkerKind.Endpoint : HighwayMarkerKind.Interior;
        }
    }
}
