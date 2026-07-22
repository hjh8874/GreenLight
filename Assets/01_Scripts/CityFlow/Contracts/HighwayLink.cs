using System;
using UnityEngine;

namespace CityFlow.Contracts
{
    [Serializable]
    public struct HighwayLink
    {
        public Vector2Int A;
        public Vector2Int B;

        public HighwayLink(Vector2Int a, Vector2Int b)
        {
            A = a;
            B = b;
        }

        public bool Contains(Vector2Int tile) => A == tile || B == tile;
        public Vector2Int Other(Vector2Int tile) => tile == A ? B : A;
        public int Distance => Mathf.Abs(A.x - B.x) + Mathf.Abs(A.y - B.y);
    }
}
