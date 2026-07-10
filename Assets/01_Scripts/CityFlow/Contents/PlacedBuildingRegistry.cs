using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Content
{
    public class PlacedBuildingRegistry : MonoBehaviour
    {
        [Header("현재 배치된 건물 목록")]
        [SerializeField] private List<BuildingDefinitionSO> placedBuildings = new();

        public IReadOnlyList<BuildingDefinitionSO> PlacedBuildings => placedBuildings;

        public event Action<BuildingDefinitionSO> OnBuildingPlaced;
        public event Action<BuildingDefinitionSO> OnBuildingRemoved;

        public void RegisterBuilding(BuildingDefinitionSO building)
        {
            if (building == null)
            {
                Debug.LogWarning("[BuildingRegistry] 등록할 건물 데이터가 없습니다.");
                return;
            }

            placedBuildings.Add(building);
            OnBuildingPlaced?.Invoke(building);

            Debug.Log($"[BuildingRegistry] 건물 등록: {building.buildingName}");
        }

        public void UnregisterBuilding(BuildingDefinitionSO building)
        {
            if (building == null)
                return;

            if (!placedBuildings.Remove(building))
            {
                Debug.LogWarning($"[BuildingRegistry] 제거할 건물이 목록에 없습니다: {building.buildingName}");
                return;
            }

            OnBuildingRemoved?.Invoke(building);

            Debug.Log($"[BuildingRegistry] 건물 제거: {building.buildingName}");
        }

        public int GetTotalBuildingCount()
        {
            return placedBuildings.Count;
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
                    continue;

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
                    continue;

                total += placedBuildings[i].prosperityValue;
            }

            return total;
        }
    }
}
