using System;
using UnityEngine;

namespace CityFlow.Sim
{
    internal readonly struct RouteSearchBounds
    {
        public RouteSearchBounds(int minX, int minY, int width, int height)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            MinX = minX;
            MinY = minY;
            Width = width;
            Height = height;
        }

        public int MinX { get; }
        public int MinY { get; }
        public int Width { get; }
        public int Height { get; }
        public int MaxXExclusive => MinX + Width;
        public int MaxYExclusive => MinY + Height;
        public int TileCount => Width * Height;

        public bool Contains(Vector2Int tile) =>
            tile.x >= MinX && tile.x < MaxXExclusive &&
            tile.y >= MinY && tile.y < MaxYExclusive;

        public int ToLocalIndex(Vector2Int tile) =>
            (tile.y - MinY) * Width + tile.x - MinX;

        public Vector2Int FromLocalIndex(int index) =>
            new Vector2Int(
                MinX + index % Width,
                MinY + index / Width);
    }
}
