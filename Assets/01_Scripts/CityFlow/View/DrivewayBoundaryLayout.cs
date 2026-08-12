using UnityEngine;

namespace CityFlow.View
{
    internal readonly struct DrivewayBoundarySegment
    {
        public DrivewayBoundarySegment(
            string name,
            Vector2 center,
            Vector2 size)
        {
            Name = name;
            Center = center;
            Size = size;
        }

        public string Name { get; }
        public Vector2 Center { get; }
        public Vector2 Size { get; }
    }

    internal static class DrivewayBoundaryLayout
    {
        internal const float LineWidthTiles = 0.015f;
        internal const float SurfaceOffsetTiles = 0.001f;

        internal static DrivewayBoundarySegment[] CreatePerimeter(
            float tileSize,
            float lotWidth,
            float lotLength,
            Vector2 lotCenter)
        {
            float safeTileSize = Mathf.Max(0.0001f, tileSize);
            float safeWidth = Mathf.Max(0.0001f, lotWidth);
            float safeLength = Mathf.Max(0.0001f, lotLength);
            float lineWidth = Mathf.Min(
                safeTileSize * LineWidthTiles,
                Mathf.Min(safeWidth, safeLength) * 0.25f);
            float halfWidth = safeWidth * 0.5f;
            float halfLength = safeLength * 0.5f;
            float horizontalLength = Mathf.Max(
                0.0001f,
                safeWidth - lineWidth * 2f);

            return new[]
            {
                new DrivewayBoundarySegment(
                    "DrivewayPerimeter_Left",
                    new Vector2(
                        lotCenter.x - halfWidth + lineWidth * 0.5f,
                        lotCenter.y),
                    new Vector2(lineWidth, safeLength)),
                new DrivewayBoundarySegment(
                    "DrivewayPerimeter_Right",
                    new Vector2(
                        lotCenter.x + halfWidth - lineWidth * 0.5f,
                        lotCenter.y),
                    new Vector2(lineWidth, safeLength)),
                new DrivewayBoundarySegment(
                    "DrivewayPerimeter_Rear",
                    new Vector2(
                        lotCenter.x,
                        lotCenter.y + halfLength - lineWidth * 0.5f),
                    new Vector2(horizontalLength, lineWidth)),
                new DrivewayBoundarySegment(
                    "DrivewayPerimeter_Front",
                    new Vector2(
                        lotCenter.x,
                        lotCenter.y - halfLength + lineWidth * 0.5f),
                    new Vector2(horizontalLength, lineWidth))
            };
        }
    }
}
