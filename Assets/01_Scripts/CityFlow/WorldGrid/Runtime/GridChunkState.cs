using CityFlow.Contracts;

namespace CityFlow.WorldGrid
{
    public sealed class GridChunkState
    {
        public GridChunkState(GridChunkId id)
        {
            Id = id;
        }

        public GridChunkId Id { get; }
        public bool IsUnlocked { get; private set; }

        internal bool TryUnlock()
        {
            if (IsUnlocked)
            {
                return false;
            }

            IsUnlocked = true;
            return true;
        }

        internal void Reset(bool isUnlocked)
        {
            IsUnlocked = isUnlocked;
        }
    }
}
