using System;
using UnityEngine;
using CityFlow.Contracts;
using CityFlow.Bootstrap;

namespace CityFlow.UI.Controllers.Placement
{
    internal class PlacementActionDispatcher
    {
        // 건설 패널 3종 분리 전 임시 기본값(2026-07-30 환 결정) —
        // 패널이 유형 선택을 넘기면 대체한다.
        private const string DefaultCompanyTypeId = "office";

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

        public bool IsTileTypeUnlocked(
            TileType type,
            CityFlowServices services)
        {
            if (_useFakeMode ||
                type == TileType.Empty ||
                type == TileType.Road ||
                type == TileType.SpecialBuilding)
            {
                return true;
            }

            CityFlow.Configs.TileDataSO tileData =
                FindTileData(type);
            string requiredResearchId =
                tileData?.RequiredResearchId?.Trim() ?? string.Empty;
            return requiredResearchId.Length == 0 ||
                   services?.Research?.IsUnlocked(
                       requiredResearchId) == true;
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
                if (!IsTileTypeUnlocked(currentType, services))
                {
                    return false;
                }

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

        public bool PlaceInfrastructure(
            Vector2Int coord,
            TileType currentType,
            PlacementDirection direction,
            CityFlowServices services,
            string specialBuildingId = null,
            string companyTypeId = null)
        {
            if (_useFakeMode)
            {
                Debug.Log($"[UI Fake Mode] 타일 {currentType} 적용 성공! 위치: {coord}");
                return true;
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
                    bool ownsSpecialBuilding =
                        previousType == TileType.SpecialBuilding ||
                        services.SpecialBuildings?.TryGetBuilding(
                            previousAnchor,
                            out _) == true;
                    bool removed = ownsSpecialBuilding
                        ? services.SpecialBuildings?.TryRemove(previousAnchor) == true
                        : services.Placement.Remove(previousAnchor);
                    if (removed)
                    {
                        if (services.Economy != null && refundCost > 0)
                            services.Economy.AddCoins(refundCost, "Demolish Refund");

                        Debug.Log($"[Real Mode] 코어 엔진에 {coord} 위치 철거 명령 전달 (환불 {refundCost}).");
                        return true;
                    }
                    return false;
                }
                else
                {
                    if (!IsTileTypeUnlocked(currentType, services))
                    {
                        Debug.LogWarning(
                            $"[UI] {currentType} 건물은 연구 완료 후 건설할 수 있습니다.");
                        return false;
                    }

                    long buildCost = GetTileCost(
                        currentType,
                        specialBuildingId,
                        services);

                    if (previousType != TileType.Empty)
                    {
                        Debug.LogWarning("[UI] 기존 건물이나 도로가 있는 위치에는 새 건물을 지을 수 없습니다.");
                        return false;
                    }

                    if (services.Economy != null && buildCost > 0 && services.Economy.Coins < buildCost)
                    {
                        Debug.LogWarning("[UI] 코인이 부족하여 건설할 수 없습니다!");
                        return false;
                    }

                    bool placed = currentType == TileType.SpecialBuilding
                        ? services.SpecialBuildings?.TryPlace(
                            specialBuildingId,
                            coord,
                            direction) == true
                        : services.Placement.Place(
                            coord,
                            currentType,
                            direction,
                            currentType == TileType.Office
                                ? (string.IsNullOrWhiteSpace(companyTypeId)
                                    ? DefaultCompanyTypeId
                                    : companyTypeId.Trim())
                                : null);
                    if (placed)
                    {
                        if (services.Economy != null && buildCost > 0)
                            services.Economy.TrySpend(buildCost);

                        Debug.Log($"[Real Mode] 코어 엔진에 {coord} 위치 {currentType} 건설 명령 전달 (비용 {buildCost}).");
                        return true;
                    }
                    return false;
                }
            }
            return false;
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
            bool ownsSpecialBuilding =
                previousType == TileType.SpecialBuilding ||
                services.SpecialBuildings?.TryGetBuilding(
                    targetCoord,
                    out _) == true;
            bool removed = ownsSpecialBuilding
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


        private Vector2Int ResolveFootprintAnchor(Vector2Int coord, CityFlowServices services)
        {
            if (services != null && services.TileData != null &&
                services.TileData.TryGetFootprintAnchor(coord, out Vector2Int anchor))
            {
                return anchor;
            }
            return coord;
        }

        private CityFlow.Configs.TileDataSO FindTileData(
            TileType type)
        {
            if (_availableTiles == null)
            {
                return null;
            }

            for (int index = 0; index < _availableTiles.Length; index++)
            {
                CityFlow.Configs.TileDataSO tileData =
                    _availableTiles[index];
                if (tileData != null && tileData.Category == type)
                {
                    return tileData;
                }
            }

            return null;
        }

        private long GetDemolitionRefund(
            Vector2Int anchor,
            TileType previousType,
            CityFlowServices services)
        {
            TileType refundType = previousType;
            if (previousType == TileType.UnderConstruction &&
                services?.TileData != null &&
                services.TileData.TryGetConstructionTargetType(
                    anchor,
                    out TileType targetType))
            {
                refundType = targetType;
            }

            if (refundType == TileType.SpecialBuilding &&
                services?.SpecialBuildings != null &&
                services.SpecialBuildings.TryGetBuilding(
                    anchor,
                    out SpecialBuildingInstance building))
            {
                return GetTileCost(
                    refundType,
                    building.BuildingId,
                    services);
            }

            return GetTileCost(refundType);
        }
    }
}
