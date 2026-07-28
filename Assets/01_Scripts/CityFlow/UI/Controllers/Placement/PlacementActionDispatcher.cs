using System;
using UnityEngine;
using CityFlow.Contracts;
using CityFlow.Bootstrap;

namespace CityFlow.UI.Controllers.Placement
{
    internal class PlacementActionDispatcher
    {
        private readonly CityFlow.Configs.TileDataSO[] _availableTiles;
        private readonly bool _useFakeMode;

        public PlacementActionDispatcher(CityFlow.Configs.TileDataSO[] availableTiles, bool useFakeMode)
        {
            _availableTiles = availableTiles;
            _useFakeMode = useFakeMode;
        }

        public long GetTileCost(
            TileType type,
            string specialBuildingId = null,
            CityFlowServices services = null)
        {
            if (type == TileType.SpecialBuilding &&
                services?.SpecialBuildings != null &&
                services.SpecialBuildings.TryGetBuildOption(
                    specialBuildingId,
                    out SpecialBuildingBuildOption option))
            {
                return option.BuildCost;
            }

            if (_availableTiles == null) return 0;
            foreach (var t in _availableTiles)
            {
                if (t != null && t.Category == type) return t.BuildCost;
            }
            return 0;
        }

        public bool CheckCanPlace(
            Vector2Int coord,
            TileType currentType,
            PlacementDirection direction,
            CityFlowServices services,
            string specialBuildingId = null)
        {
            if (_useFakeMode) return true;

            if (services != null && services.Placement != null && services.TileData != null)
            {
                Vector2Int footprint = TileFootprint.GetRotatedSize(
                    currentType,
                    direction);
                bool isAccessible = services.WorldGrid != null
                    ? services.WorldGrid.IsAreaUnlocked(coord, footprint)
                    : GridUtil.IsInside(coord);
                if (!isAccessible) return false;
                if (currentType == TileType.Empty) return true;

                if (currentType == TileType.SpecialBuilding)
                {
                    return services.SpecialBuildings?.CanPlace(
                        specialBuildingId,
                        coord,
                        direction) == true;
                }

                Vector2Int previousAnchor = ResolveFootprintAnchor(coord, services);
                TileType previousType = services.TileData.GetTileType(previousAnchor);

                if (previousType != TileType.Empty)
                {
                    return false;
                }

                return services.Placement.CanPlace(coord, currentType, direction);
            }
            return false;
        }

        public void PlaceInfrastructure(
            Vector2Int coord,
            TileType currentType,
            PlacementDirection direction,
            CityFlowServices services,
            string specialBuildingId = null)
        {
            if (_useFakeMode)
            {
                Debug.Log($"[UI Fake Mode] 타일 {currentType} 적용 성공! 위치: {coord}");
                return;
            }

            if (services != null && services.Placement != null && services.TileData != null)
            {
                Vector2Int previousAnchor = ResolveFootprintAnchor(coord, services);
                TileType previousType = services.TileData.GetTileType(previousAnchor);

                if (currentType == TileType.Empty)
                {
                    long refundCost = GetDemolitionRefund(
                        previousAnchor,
                        previousType,
                        services);
                    bool removed = previousType == TileType.SpecialBuilding
                        ? services.SpecialBuildings?.TryRemove(previousAnchor) == true
                        : services.Placement.Remove(previousAnchor);
                    if (removed)
                    {
                        if (services.Economy != null && refundCost > 0)
                            services.Economy.AddCoins(refundCost, "Demolish Refund");

                        Debug.Log($"[Real Mode] 코어 엔진에 {coord} 위치 철거 명령 전달 (환불 {refundCost}).");
                    }
                }
                else
                {
                    long buildCost = GetTileCost(
                        currentType,
                        specialBuildingId,
                        services);

                    if (previousType != TileType.Empty)
                    {
                        Debug.LogWarning("[UI] 기존 건물이나 도로가 있는 위치에는 새 건물을 지을 수 없습니다.");
                        return;
                    }

                    if (services.Economy != null && buildCost > 0 && services.Economy.Coins < buildCost)
                    {
                        Debug.LogWarning("[UI] 코인이 부족하여 건설할 수 없습니다!");
                        return;
                    }

                    bool placed = currentType == TileType.SpecialBuilding
                        ? services.SpecialBuildings?.TryPlace(
                            specialBuildingId,
                            coord,
                            direction) == true
                        : services.Placement.Place(
                            coord,
                            currentType,
                            direction);
                    if (placed)
                    {
                        if (services.Economy != null && buildCost > 0)
                            services.Economy.TrySpend(buildCost);

                        Debug.Log($"[Real Mode] 코어 엔진에 {coord} 위치 {currentType} 건설 명령 전달 (비용 {buildCost}).");
                    }
                }
            }
        }

        public bool TryDemolishAt(Vector2Int coord, CityFlowServices services)
        {
            var infraCoord = UnityEngine.Object.FindAnyObjectByType<CityFlow.UI.Controllers.InfrastructurePlacementCoordinator>();
            if (infraCoord != null && infraCoord.TryDemolishInfrastructureAt(coord))
            {
                return true;
            }

            if (_useFakeMode)
            {
                Debug.Log($"[UI Fake Mode] 타일 {coord} 철거 성공!");
                return true;
            }

            if (services == null || services.Placement == null || services.TileData == null)
            {
                return false;
            }

            Vector2Int targetCoord = ResolveFootprintAnchor(coord, services);
            TileType previousType = services.TileData.GetTileType(targetCoord);
            if (previousType == TileType.Empty)
            {
                return false;
            }

            long refundCost = GetDemolitionRefund(
                targetCoord,
                previousType,
                services);
            bool removed = previousType == TileType.SpecialBuilding
                ? services.SpecialBuildings?.TryRemove(targetCoord) == true
                : services.Placement.Remove(targetCoord);
            if (!removed)
            {
                return false;
            }

            if (services.Economy != null && refundCost > 0)
            {
                services.Economy.AddCoins(refundCost, "Demolish Refund");
            }

            Debug.Log($"[Real Mode] 코어 엔진에 {targetCoord} 위치 철거 명령 전달 (환불 {refundCost}).");
            return true;
        }

        public void HandleRoadExpandClicked(CityFlowServices services)
        {
        }

        private Vector2Int ResolveFootprintAnchor(Vector2Int coord, CityFlowServices services)
        {
            if (services != null && services.TileData != null &&
                services.TileData.TryGetFootprintAnchor(coord, out Vector2Int anchor))
            {
                return anchor;
            }
            return coord;
        }

        private long GetDemolitionRefund(
            Vector2Int anchor,
            TileType previousType,
            CityFlowServices services)
        {
            if (previousType == TileType.SpecialBuilding &&
                services?.SpecialBuildings != null &&
                services.SpecialBuildings.TryGetBuilding(
                    anchor,
                    out SpecialBuildingInstance building))
            {
                return GetTileCost(
                    previousType,
                    building.BuildingId,
                    services);
            }

            return GetTileCost(previousType);
        }
    }
}
