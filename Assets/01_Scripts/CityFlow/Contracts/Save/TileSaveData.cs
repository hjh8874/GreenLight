using System;
using CityFlow.Contracts;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class TileSaveData
    {
        public int X;
        public int Y;
        public TileType Type;
        public PlacementDirection Direction;
    }
}
