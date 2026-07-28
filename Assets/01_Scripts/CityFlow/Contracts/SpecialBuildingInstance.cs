using UnityEngine;

namespace CityFlow.Contracts
{
    public readonly struct SpecialBuildingInstance
    {
        public SpecialBuildingInstance(
            string buildingId,
            Vector2Int anchor,
            PlacementDirection direction)
        {
            BuildingId = buildingId ?? string.Empty;
            Anchor = anchor;
            Direction = direction;
        }

        public string BuildingId { get; }
        public Vector2Int Anchor { get; }
        public PlacementDirection Direction { get; }
    }

    public readonly struct SpecialBuildingChangedEvent
    {
        public SpecialBuildingChangedEvent(
            SpecialBuildingInstance building,
            bool isRemove)
        {
            Building = building;
            IsRemove = isRemove;
        }

        public SpecialBuildingInstance Building { get; }
        public bool IsRemove { get; }
    }
}
