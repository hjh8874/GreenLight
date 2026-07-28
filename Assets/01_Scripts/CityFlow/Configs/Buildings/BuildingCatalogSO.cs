using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Content
{
    [CreateAssetMenu(
        fileName = "BuildingCatalog",
        menuName = "CityFlow/Content/Building Catalog")]
    public sealed class BuildingCatalogSO : ScriptableObject
    {
        [SerializeField]
        private List<BuildingDefinitionSO> buildings = new();

        private readonly Dictionary<string, BuildingDefinitionSO> byId =
            new(StringComparer.Ordinal);
        private bool indexDirty = true;

        public IReadOnlyList<BuildingDefinitionSO> Buildings => buildings;
        public int Count => buildings?.Count ?? 0;

        public bool TryGet(string buildingId, out BuildingDefinitionSO definition)
        {
            EnsureIndex();
            definition = null;
            if (string.IsNullOrWhiteSpace(buildingId))
            {
                return false;
            }

            return byId.TryGetValue(buildingId.Trim(), out definition);
        }

        public bool Contains(string buildingId) =>
            TryGet(buildingId, out _);

        private void OnEnable()
        {
            indexDirty = true;
        }

        private void OnValidate()
        {
            indexDirty = true;
            EnsureIndex(logWarnings: true);
        }

        private void EnsureIndex(bool logWarnings = false)
        {
            if (!indexDirty)
            {
                return;
            }

            byId.Clear();
            indexDirty = false;

            if (buildings == null)
            {
                return;
            }

            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingDefinitionSO definition = buildings[i];
                string id = definition?.buildingId?.Trim();
                if (definition == null || string.IsNullOrEmpty(id))
                {
                    if (logWarnings)
                    {
                        Debug.LogWarning(
                            $"[BuildingCatalogSO] Entry {i} has no building ID.",
                            this);
                    }
                    continue;
                }

                if (!byId.TryAdd(id, definition) && logWarnings)
                {
                    Debug.LogWarning(
                        $"[BuildingCatalogSO] Duplicate building ID: {id}",
                        this);
                }
            }
        }
    }
}
