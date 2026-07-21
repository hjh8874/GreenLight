using System.Collections.Generic;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Content
{
    [CreateAssetMenu(
        fileName = "BuildingCatalog",
        menuName = "CityFlow/Content/Building Catalog"
    )]
    public sealed class BuildingCatalogSO : ScriptableObject
    {
        [SerializeField] private List<BuildingDefinitionSO> buildings = new();

        public IReadOnlyList<BuildingDefinitionSO> Buildings => buildings;

        public bool TryGetById(
            string buildingId,
            out BuildingDefinitionSO building
        )
        {
            building = null;

            if (string.IsNullOrWhiteSpace(buildingId))
            {
                return false;
            }

            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingDefinitionSO candidate = buildings[i];
                if (candidate != null && candidate.buildingId == buildingId)
                {
                    building = candidate;
                    return true;
                }
            }

            return false;
        }

        public bool TryGetByTileType(
            TileType tileType,
            out BuildingDefinitionSO building
        )
        {
            building = null;

            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingDefinitionSO candidate = buildings[i];
                if (candidate != null && candidate.tileType == tileType)
                {
                    building = candidate;
                    return true;
                }
            }

            return false;
        }

        public List<BuildingDefinitionSO> GetByCategory(
            BuildingCategory category
        )
        {
            List<BuildingDefinitionSO> result = new();

            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingDefinitionSO candidate = buildings[i];
                if (candidate != null && candidate.category == category)
                {
                    result.Add(candidate);
                }
            }

            return result;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            HashSet<string> ids = new();

            for (int i = buildings.Count - 1; i >= 0; i--)
            {
                BuildingDefinitionSO building = buildings[i];
                if (building == null)
                {
                    buildings.RemoveAt(i);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(building.buildingId))
                {
                    Debug.LogWarning(
                        $"[BuildingCatalog] ID가 비어 있는 건물이 있습니다: {building.name}",
                        this
                    );
                    continue;
                }

                if (!ids.Add(building.buildingId))
                {
                    Debug.LogError(
                        $"[BuildingCatalog] 중복 건물 ID: {building.buildingId}",
                        this
                    );
                }
            }
        }
#endif
    }
}
