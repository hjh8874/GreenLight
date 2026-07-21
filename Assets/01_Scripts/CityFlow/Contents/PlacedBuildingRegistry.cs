using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Content
{
    public class PlacedBuildingRegistry : MonoBehaviour
    {
        [Header("현재 배치된 건물 목록")]
        [SerializeField] private List<BuildingDefinitionSO> placedBuildings = new();

        private readonly Dictionary<Vector2Int, BuildingDefinitionSO> buildingsByTile = new();

        public IReadOnlyList<BuildingDefinitionSO> PlacedBuildings => placedBuildings;
        public int Count => placedBuildings.Count;

        public event Action<BuildingDefinitionSO> OnBuildingPlaced;
        public event Action<BuildingDefinitionSO> OnBuildingRemoved;
        public event Action<Vector2Int, BuildingDefinitionSO> BuildingPlacedAt;
        public event Action<Vector2Int, BuildingDefinitionSO> BuildingRemovedAt;

        public void RegisterBuilding(BuildingDefinitionSO building)
        {
            if (!ValidateBuilding(building))
            {
                return;
            }

            placedBuildings.Add(building);
            OnBuildingPlaced?.Invoke(building);

            Debug.Log($"[BuildingRegistry] 건물 등록: {building.buildingName}", this);
        }

        public bool RegisterBuilding(Vector2Int tile, BuildingDefinitionSO building)
        {
            if (!ValidateBuilding(building))
            {
                return false;
            }

            if (buildingsByTile.TryGetValue(tile, out BuildingDefinitionSO existing))
            {
                if (existing == building)
                {
                    return false;
                }

                Debug.LogWarning(
                    $"[BuildingRegistry] 이미 건물이 등록된 타일입니다. " +
                    $"Tile: ({tile.x}, {tile.y}), Existing: {existing.buildingName}",
                    this
                );
                return false;
            }

            buildingsByTile.Add(tile, building);
            placedBuildings.Add(building);

            OnBuildingPlaced?.Invoke(building);
            BuildingPlacedAt?.Invoke(tile, building);

            Debug.Log(
                $"[BuildingRegistry] 건물 등록: {building.buildingName}, " +
                $"Tile: ({tile.x}, {tile.y})",
                this
            );

            return true;
        }

        public void UnregisterBuilding(BuildingDefinitionSO building)
        {
            if (building == null)
            {
                return;
            }

            if (!placedBuildings.Remove(building))
            {
                Debug.LogWarning(
                    $"[BuildingRegistry] 제거할 건물이 목록에 없습니다: {building.buildingName}",
                    this
                );
                return;
            }

            Vector2Int? registeredTile = null;
            foreach (KeyValuePair<Vector2Int, BuildingDefinitionSO> pair in buildingsByTile)
            {
                if (pair.Value == building)
                {
                    registeredTile = pair.Key;
                    break;
                }
            }

            if (registeredTile.HasValue)
            {
                buildingsByTile.Remove(registeredTile.Value);
                BuildingRemovedAt?.Invoke(registeredTile.Value, building);
            }

            OnBuildingRemoved?.Invoke(building);
            Debug.Log($"[BuildingRegistry] 건물 제거: {building.buildingName}", this);
        }

        public bool UnregisterBuilding(Vector2Int tile)
        {
            if (!buildingsByTile.TryGetValue(tile, out BuildingDefinitionSO building))
            {
                return false;
            }

            buildingsByTile.Remove(tile);
            placedBuildings.Remove(building);

            OnBuildingRemoved?.Invoke(building);
            BuildingRemovedAt?.Invoke(tile, building);

            Debug.Log(
                $"[BuildingRegistry] 건물 제거: {building.buildingName}, " +
                $"Tile: ({tile.x}, {tile.y})",
                this
            );

            return true;
        }

        public bool TryGetBuilding(Vector2Int tile, out BuildingDefinitionSO building)
        {
            return buildingsByTile.TryGetValue(tile, out building);
        }

        public bool ContainsTile(Vector2Int tile)
        {
            return buildingsByTile.ContainsKey(tile);
        }

        public int GetTotalBuildingCount()
        {
            return placedBuildings.Count;
        }

        public int GetBuildingCount(BuildingDefinitionSO building)
        {
            if (building == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < placedBuildings.Count; i++)
            {
                if (placedBuildings[i] == building)
                {
                    count++;
                }
            }

            return count;
        }

        public int GetBuildingCountByCategory(BuildingCategory category)
        {
            int count = 0;

            for (int i = 0; i < placedBuildings.Count; i++)
            {
                if (placedBuildings[i] != null && placedBuildings[i].category == category)
                {
                    count++;
                }
            }

            return count;
        }

        public int GetTotalDailyCoinValue()
        {
            int total = 0;

            for (int i = 0; i < placedBuildings.Count; i++)
            {
                if (placedBuildings[i] == null)
                {
                    continue;
                }

                total += placedBuildings[i].dailyCoinValue;
            }

            return total;
        }

        public int GetTotalProsperityValue()
        {
            int total = 0;

            for (int i = 0; i < placedBuildings.Count; i++)
            {
                if (placedBuildings[i] == null)
                {
                    continue;
                }

                total += placedBuildings[i].prosperityValue;
            }

            return total;
        }

        public void ClearRegistry()
        {
            placedBuildings.Clear();
            buildingsByTile.Clear();
        }

        private bool ValidateBuilding(BuildingDefinitionSO building)
        {
            if (building != null)
            {
                return true;
            }

            Debug.LogWarning("[BuildingRegistry] 등록할 건물 데이터가 없습니다.", this);
            return false;
        }
    }
}
