using System;
using System.Collections.Generic;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Sim
{
    internal sealed class SchoolZoneMap
    {
        internal const int SchoolZoneNumerator = 30;
        internal const int SchoolZoneRadius = 2;

        private readonly int width;
        private readonly int height;
        private readonly bool[] schoolZone;

        internal SchoolZoneMap(int width, int height)
        {
            this.width = Mathf.Max(0, width);
            this.height = Mathf.Max(0, height);
            schoolZone = new bool[this.width * this.height];
        }

        internal void Rebuild(
            IEnumerable<Vector2Int> schools,
            CityGrid roadGrid = null)
        {
            Array.Clear(schoolZone, 0, schoolZone.Length);
            if (schools == null)
            {
                return;
            }

            foreach (Vector2Int school in schools)
            {
                for (int dy = -SchoolZoneRadius; dy <= SchoolZoneRadius; dy++)
                {
                    for (int dx = -SchoolZoneRadius; dx <= SchoolZoneRadius; dx++)
                    {
                        if (Mathf.Abs(dx) + Mathf.Abs(dy) > SchoolZoneRadius)
                        {
                            continue;
                        }

                        Vector2Int tile = school + new Vector2Int(dx, dy);
                        if (!TryGetIndex(tile, out int index) ||
                            (roadGrid != null &&
                             roadGrid.GetTile(tile) != TileType.Road))
                        {
                            continue;
                        }

                        schoolZone[index] = true;
                    }
                }
            }
        }

        internal bool IsSchoolZone(Vector2Int tile)
        {
            return TryGetIndex(tile, out int index) && schoolZone[index];
        }

        internal int GetEffectiveNumerator(
            int vehicleNumerator,
            Vector2Int tile,
            float gameHour,
            in SimConfig config)
        {
            int safeNumerator = Mathf.Max(0, vehicleNumerator);
            if (!IsSchoolZone(tile) || !IsSchoolWindow(gameHour, config))
            {
                return safeNumerator;
            }

            return Mathf.Min(safeNumerator, SchoolZoneNumerator);
        }

        private static bool IsSchoolWindow(float gameHour, in SimConfig config)
        {
            CommuteWindow window = CommuteWindow.SchoolFromConfig(config);
            bool morning = CommuteWindow.InWindow(
                gameHour,
                window.StartHour,
                window.StartHour + window.StartWindow);
            bool returnWindow = CommuteWindow.InWindow(
                gameHour,
                window.EndHour,
                window.EndHour + window.EndWindow);
            return morning || returnWindow;
        }

        private bool TryGetIndex(Vector2Int tile, out int index)
        {
            if (tile.x < 0 || tile.x >= width ||
                tile.y < 0 || tile.y >= height)
            {
                index = -1;
                return false;
            }

            index = tile.y * width + tile.x;
            return true;
        }
    }
}
