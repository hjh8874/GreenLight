using System;
using System.Collections.Generic;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.TilemapTest
{
    public sealed class TilemapTestVehicleTripSystem : MonoBehaviour
    {
        private static readonly Vector2Int[] Directions =
        {
            Vector2Int.up,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.left
        };

        [SerializeField] private TilemapTestField field;
        [SerializeField] private Transform vehiclePrefab;
        [SerializeField] private float spawnInterval = 0.35f;
        [SerializeField] private float vehicleSpeed = 3f;
        [SerializeField] private float laneOffset = 0.18f;
        [SerializeField] private int maxActiveVehicles = 80;
        [SerializeField] private float vehicleHeight = 0.18f;

        private readonly Dictionary<Vector2Int, TileType> tileTypes = new Dictionary<Vector2Int, TileType>();
        private readonly List<Vector2Int> houses = new List<Vector2Int>();
        private readonly List<Vector2Int> sinks = new List<Vector2Int>();
        private readonly List<TilemapTestVehicleAgent> activeVehicles = new List<TilemapTestVehicleAgent>();
        private readonly Queue<Vector2Int> bfsQueue = new Queue<Vector2Int>();
        private readonly Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();

        private float nextSpawnTime;
        private int nextHouseIndex;

        public TilemapTestField Field => field;
        public Transform VehiclePrefab => vehiclePrefab;

        public void Configure(TilemapTestField testField, Transform prefab)
        {
            field = testField;
            vehiclePrefab = prefab;
            RebuildCache();
        }

        public void RebuildCache()
        {
            tileTypes.Clear();
            houses.Clear();
            sinks.Clear();

            TilemapTestField targetField = field != null ? field : GetComponent<TilemapTestField>();
            if (targetField == null)
            {
                return;
            }

            field = targetField;
            TilemapTestTile[] tiles = field.GetComponentsInChildren<TilemapTestTile>(true);

            foreach (TilemapTestTile tile in tiles)
            {
                tileTypes[tile.GridPosition] = tile.TileType;

                if (tile.TileType == TileType.House)
                {
                    houses.Add(tile.GridPosition);
                }
                else if (tile.TileType == TileType.Office || tile.TileType == TileType.School)
                {
                    sinks.Add(tile.GridPosition);
                }
            }

            Debug.Log($"TilemapTestVehicleTripSystem cached {houses.Count} houses and {sinks.Count} destinations.");
        }

        private void Awake()
        {
            if (field == null)
            {
                field = GetComponent<TilemapTestField>();
            }

            RebuildCache();
        }

        private void Update()
        {
            CleanupInactiveVehicles();

            if (Time.time < nextSpawnTime)
            {
                return;
            }

            nextSpawnTime = Time.time + spawnInterval;

            if (activeVehicles.Count >= maxActiveVehicles)
            {
                return;
            }

            TrySpawnTripVehicle();
        }

        private void TrySpawnTripVehicle()
        {
            if (field == null || vehiclePrefab == null || houses.Count == 0 || sinks.Count == 0)
            {
                return;
            }

            Vector2Int house = houses[nextHouseIndex % houses.Count];
            nextHouseIndex++;

            Vector2Int sink = FindNearestSink(house);

            if (!TryGetAccessRoad(house, out Vector2Int from) ||
                !TryGetAccessRoad(sink, out Vector2Int to) ||
                !TryFindRoadPath(from, to, out List<Vector2Int> path))
            {
                return;
            }

            List<Vector3> worldPath = BuildWorldPath(path);
            Transform vehicle = Instantiate(vehiclePrefab, worldPath[0], Quaternion.identity, transform);
            TilemapTestVehicleAgent agent = vehicle.GetComponent<TilemapTestVehicleAgent>();

            if (agent == null)
            {
                agent = vehicle.gameObject.AddComponent<TilemapTestVehicleAgent>();
            }

            agent.Configure(worldPath, vehicleSpeed);
            activeVehicles.Add(agent);
        }

        private Vector2Int FindNearestSink(Vector2Int house)
        {
            Vector2Int nearest = sinks[0];
            int nearestDistance = int.MaxValue;

            foreach (Vector2Int sink in sinks)
            {
                int distance = Math.Abs(house.x - sink.x) + Math.Abs(house.y - sink.y);
                if (distance < nearestDistance)
                {
                    nearest = sink;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        private bool TryGetAccessRoad(Vector2Int building, out Vector2Int road)
        {
            foreach (Vector2Int direction in Directions)
            {
                Vector2Int candidate = building + direction;
                if (IsRoad(candidate))
                {
                    road = candidate;
                    return true;
                }
            }

            road = default;
            return false;
        }

        private bool TryFindRoadPath(Vector2Int from, Vector2Int to, out List<Vector2Int> path)
        {
            path = null;

            if (!IsRoad(from) || !IsRoad(to))
            {
                return false;
            }

            bfsQueue.Clear();
            cameFrom.Clear();
            bfsQueue.Enqueue(from);
            cameFrom[from] = from;

            while (bfsQueue.Count > 0)
            {
                Vector2Int current = bfsQueue.Dequeue();

                if (current == to)
                {
                    path = BuildPath(from, to);
                    return true;
                }

                foreach (Vector2Int direction in Directions)
                {
                    Vector2Int next = current + direction;
                    if (!IsRoad(next) || cameFrom.ContainsKey(next))
                    {
                        continue;
                    }

                    cameFrom[next] = current;
                    bfsQueue.Enqueue(next);
                }
            }

            return false;
        }

        private List<Vector2Int> BuildPath(Vector2Int from, Vector2Int to)
        {
            List<Vector2Int> path = new List<Vector2Int>();
            Vector2Int current = to;

            while (current != from)
            {
                path.Add(current);
                current = cameFrom[current];
            }

            path.Add(from);
            path.Reverse();
            return path;
        }

        private List<Vector3> BuildWorldPath(IReadOnlyList<Vector2Int> gridPath)
        {
            List<Vector3> worldPath = new List<Vector3>(gridPath.Count);

            for (int i = 0; i < gridPath.Count; i++)
            {
                Vector3 point = field.GridToWorld(gridPath[i]);
                point.y += vehicleHeight;

                if (gridPath.Count > 1)
                {
                    Vector2Int previous = i > 0 ? gridPath[i - 1] : gridPath[i];
                    Vector2Int next = i < gridPath.Count - 1 ? gridPath[i + 1] : gridPath[i];
                    Vector2Int direction = next != gridPath[i] ? next - gridPath[i] : gridPath[i] - previous;
                    point += GetLaneOffset(direction);
                }

                worldPath.Add(point);
            }

            return worldPath;
        }

        private Vector3 GetLaneOffset(Vector2Int direction)
        {
            // 진행 방향 기준 오른쪽으로 오프셋(우측통행). 방향 부호를 반영하므로
            // 왕복 차량(동↔서, 남↔북)이 반대 차선을 타 정면으로 겹치지 않는다.
            Vector3 travel = new Vector3(direction.x, 0f, direction.y);
            return Vector3.Cross(Vector3.up, travel).normalized * laneOffset;
        }

        private bool IsRoad(Vector2Int tile)
        {
            return tileTypes.TryGetValue(tile, out TileType type) && type == TileType.Road;
        }

        private void CleanupInactiveVehicles()
        {
            for (int i = activeVehicles.Count - 1; i >= 0; i--)
            {
                if (activeVehicles[i] == null || !activeVehicles[i].IsRunning)
                {
                    activeVehicles.RemoveAt(i);
                }
            }
        }
    }
}

/*
Unity implementation:
1. Add this to the TilemapTestField GameObject or use the editor button to add it.
2. Assign a simple vehicle prefab; the system keeps vehicle data separate from the prefab.
3. It reads baked House, Office, School, and Road tiles to spawn visual trips along road paths.
*/
