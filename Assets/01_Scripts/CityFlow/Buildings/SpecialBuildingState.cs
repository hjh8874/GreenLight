using System;
using System.Collections.Generic;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Buildings
{
    public sealed class SpecialBuildingState
    {
        private readonly Dictionary<Vector2Int, SpecialBuildingInstance>
            buildingsByAnchor = new();

        public int Count => buildingsByAnchor.Count;

        public bool TryAdd(
            string buildingId,
            Vector2Int anchor,
            PlacementDirection direction)
        {
            string normalizedId = buildingId?.Trim();
            if (string.IsNullOrEmpty(normalizedId) ||
                buildingsByAnchor.ContainsKey(anchor))
            {
                return false;
            }

            buildingsByAnchor.Add(
                anchor,
                new SpecialBuildingInstance(
                    normalizedId,
                    anchor,
                    direction));
            return true;
        }

        public bool TryGet(
            Vector2Int anchor,
            out SpecialBuildingInstance building) =>
            buildingsByAnchor.TryGetValue(anchor, out building);

        public bool TryRemove(
            Vector2Int anchor,
            out SpecialBuildingInstance building)
        {
            if (!buildingsByAnchor.TryGetValue(anchor, out building))
            {
                return false;
            }

            buildingsByAnchor.Remove(anchor);
            return true;
        }

        public void Clear()
        {
            buildingsByAnchor.Clear();
        }

        public SpecialBuildingSaveData CreateSnapshot()
        {
            SpecialBuildingInstance[] entries = CreateInstanceSnapshot();
            var saved = new SpecialBuildingInstanceSaveData[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                SpecialBuildingInstance building = entries[i];
                saved[i] = new SpecialBuildingInstanceSaveData
                {
                    BuildingId = building.BuildingId,
                    X = building.Anchor.x,
                    Y = building.Anchor.y,
                    Direction = building.Direction
                };
            }

            return new SpecialBuildingSaveData
            {
                Buildings = saved
            };
        }

        public SpecialBuildingInstance[] CreateInstanceSnapshot()
        {
            var entries = new List<SpecialBuildingInstance>(
                buildingsByAnchor.Values);
            entries.Sort(CompareByCoordinate);
            return entries.ToArray();
        }

        private static int CompareByCoordinate(
            SpecialBuildingInstance left,
            SpecialBuildingInstance right)
        {
            int y = left.Anchor.y.CompareTo(right.Anchor.y);
            return y != 0 ? y : left.Anchor.x.CompareTo(right.Anchor.x);
        }
    }
}
