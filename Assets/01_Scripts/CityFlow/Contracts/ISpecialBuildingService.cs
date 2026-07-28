using System;
using UnityEngine;

namespace CityFlow.Contracts
{
    public interface ISpecialBuildingService : IHappinessEffectSource
    {
        int BuildingCount { get; }

        event Action<SpecialBuildingChangedEvent> BuildingChanged;
        event Action BuildingsRestored;
        event Action BuildOptionsChanged;

        bool CanPlace(
            string buildingId,
            Vector2Int anchor,
            PlacementDirection direction = PlacementDirection.North);

        bool TryPlace(
            string buildingId,
            Vector2Int anchor,
            PlacementDirection direction = PlacementDirection.North);

        bool TryRemove(Vector2Int tile);

        bool TryGetBuilding(
            Vector2Int tile,
            out SpecialBuildingInstance building);

        bool IsBuildingUnlocked(string buildingId);

        bool TryGetBuildOption(
            string buildingId,
            out SpecialBuildingBuildOption option);

        SpecialBuildingInstance[] CreateBuildingSnapshot();

        SpecialBuildingBuildOption[] CreateBuildOptionSnapshot();
    }
}
