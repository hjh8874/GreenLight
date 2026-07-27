using System;

namespace CityFlow.Contracts
{
    [Serializable]
    public readonly struct GridChunkId : IEquatable<GridChunkId>
    {
        public GridChunkId(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }

        public bool Equals(GridChunkId other) =>
            X == other.X && Y == other.Y;

        public override bool Equals(object obj) =>
            obj is GridChunkId other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y);

        public override string ToString() => $"({X}, {Y})";

        public static bool operator ==(GridChunkId left, GridChunkId right) =>
            left.Equals(right);

        public static bool operator !=(GridChunkId left, GridChunkId right) =>
            !left.Equals(right);
    }
}
