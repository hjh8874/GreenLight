using System;
using CityFlow.Contracts;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class SpecialBuildingSaveData
    {
        public SpecialBuildingInstanceSaveData[] Buildings =
            Array.Empty<SpecialBuildingInstanceSaveData>();
    }

    [Serializable]
    public sealed class SpecialBuildingInstanceSaveData
    {
        public string BuildingId;
        public int X;
        public int Y;
        public PlacementDirection Direction;
    }
}
