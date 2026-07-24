using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Content
{
    public sealed class EmergencyIncidentSystem : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("설정")]
        [SerializeField] private EmergencyIncidentConfigSO config;

        [Header("그리드")]
        [SerializeField, Min(1)] private int gridWidth = GridUtil.DefaultWidth;
        [SerializeField, Min(1)] private int gridHeight = GridUtil.DefaultHeight;

        [Header("디버그")]
        [SerializeField] private bool enableAutomaticSpawn = true;
        [SerializeField] private bool verboseLogging = true;

        private readonly List<Vector2Int> emergencySourceTiles = new();
        private readonly List<EmergencyIncident> activeIncidents = new();
        private readonly HashSet<Vector2Int> occupiedIncidentTiles = new();

        private CityFlowServices services;
        private IReadOnlyTileData tileData;
        private float nextSpawnTime;
        private int nextIncidentId = 1;
        private bool isRestoreSubscribed;

        public IReadOnlyList<EmergencyIncident> ActiveIncidents => activeIncidents;
        public int ActiveIncidentCount => activeIncidents.Count;

        public event Action<EmergencyIncident> IncidentCreated;
        public event Action<EmergencyIncident> IncidentChanged;
        public event Action<EmergencyIncident> IncidentRemoved;

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled) return;

            if (services == null)
            {
                Debug.LogError("[EmergencyIncidentSystem] CityFlowServices가 없습니다.", this);
                return;
            }

            if (config == null)
            {
                Debug.LogError("[EmergencyIncidentSystem] EmergencyIncidentConfigSO가 연결되지 않았습니다.", this);
                return;
            }

            this.services = services;
            tileData = services.TileData;

            if (tileData == null)
            {
                Debug.LogError("[EmergencyIncidentSystem] IReadOnlyTileData를 찾을 수 없습니다.", this);
                return;
            }

            RebuildEmergencySources();
            ScheduleNextSpawn();
            services.Events.Placed += OnPlaced;
            SubscribeRestore();
        }

        private void Update()
        {
            if (!enableAutomaticSpawn || services == null || tileData == null || config == null) return;
            if (Time.time < nextSpawnTime) return;

            TryCreateRandomIncident();
            ScheduleNextSpawn();
        }

        private void OnDestroy()
        {
            if (services == null) return;
            services.Events.Placed -= OnPlaced;
            UnsubscribeRestore();
        }

        private void OnPlaced(PlacedEvent placed)
        {
            if (placed.Type != TileType.House && placed.Type != TileType.Office) return;
            RebuildEmergencySources();

            if (placed.IsRemove)
            {
                FailIncidentAtRemovedTile(placed.Tile);
            }
        }

        private void SubscribeRestore()
        {
            if (isRestoreSubscribed || services?.Save == null) return;
            services.Save.RestoreCompleted += OnRestoreCompleted;
            isRestoreSubscribed = true;
        }

        private void UnsubscribeRestore()
        {
            if (!isRestoreSubscribed || services?.Save == null) return;
            services.Save.RestoreCompleted -= OnRestoreCompleted;
            isRestoreSubscribed = false;
        }

        private void OnRestoreCompleted(RestoreCompletedEvent _)
        {
            ClearAllIncidents();
            RebuildEmergencySources();
            ScheduleNextSpawn();
        }

        public void RebuildEmergencySources()
        {
            emergencySourceTiles.Clear();
            if (tileData == null) return;

            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    Vector2Int tile = new Vector2Int(x, y);
                    TileType type = tileData.GetTileType(tile);

                    if (type != TileType.House && type != TileType.Office) continue;
                    if (!tileData.IsFootprintAnchor(tile)) continue;

                    emergencySourceTiles.Add(tile);
                }
            }
        }

        public bool TryCreateRandomIncident()
        {
            if (config == null ||
                activeIncidents.Count >= config.MaximumActiveIncidents ||
                emergencySourceTiles.Count == 0)
            {
                return false;
            }

            int startIndex = UnityEngine.Random.Range(0, emergencySourceTiles.Count);

            for (int offset = 0; offset < emergencySourceTiles.Count; offset++)
            {
                int index = (startIndex + offset) % emergencySourceTiles.Count;
                Vector2Int tile = emergencySourceTiles[index];

                if (occupiedIncidentTiles.Contains(tile)) continue;

                TileType sourceType = tileData.GetTileType(tile);
                float chance = GetSpawnChance(sourceType);

                if (UnityEngine.Random.value > chance) continue;
                return TryCreateIncidentAt(tile);
            }

            return false;
        }

        public bool TryCreateIncidentAt(Vector2Int tile)
        {
            if (config == null || tileData == null || activeIncidents.Count >= config.MaximumActiveIncidents)
            {
                return false;
            }

            Vector2Int sourceAnchor = ResolveAnchor(tile);
            if (occupiedIncidentTiles.Contains(sourceAnchor)) return false;

            TileType sourceType = tileData.GetTileType(sourceAnchor);
            if (sourceType != TileType.House && sourceType != TileType.Office) return false;

            EmergencyIncident incident = new EmergencyIncident(
                nextIncidentId++, sourceAnchor, sourceType, Time.time);

            activeIncidents.Add(incident);
            occupiedIncidentTiles.Add(sourceAnchor);
            IncidentCreated?.Invoke(incident);

            if (verboseLogging)
            {
                Debug.Log($"[EmergencyIncidentSystem] 응급상황 발생 #{incident.IncidentId} 위치={incident.Location}, 건물={incident.SourceType}", this);
            }

            return true;
        }

        public bool TryGetIncident(int incidentId, out EmergencyIncident incident)
        {
            for (int i = 0; i < activeIncidents.Count; i++)
            {
                if (activeIncidents[i].IncidentId != incidentId) continue;
                incident = activeIncidents[i];
                return true;
            }

            incident = null;
            return false;
        }

        public void NotifyIncidentChanged(EmergencyIncident incident)
        {
            if (incident == null || !activeIncidents.Contains(incident)) return;
            IncidentChanged?.Invoke(incident);
        }

        public bool ResolveIncident(int incidentId)
        {
            if (!TryGetIncident(incidentId, out EmergencyIncident incident)) return false;
            incident.Resolve();
            IncidentChanged?.Invoke(incident);
            RemoveIncident(incident);
            return true;
        }

        public bool FailIncident(int incidentId)
        {
            if (!TryGetIncident(incidentId, out EmergencyIncident incident)) return false;
            incident.Fail();
            IncidentChanged?.Invoke(incident);
            RemoveIncident(incident);
            return true;
        }

        public void ClearAllIncidents()
        {
            for (int i = activeIncidents.Count - 1; i >= 0; i--)
            {
                EmergencyIncident incident = activeIncidents[i];
                incident.Fail();
                IncidentChanged?.Invoke(incident);
                IncidentRemoved?.Invoke(incident);
            }

            activeIncidents.Clear();
            occupiedIncidentTiles.Clear();
        }

        private void RemoveIncident(EmergencyIncident incident)
        {
            if (incident == null) return;

            activeIncidents.Remove(incident);
            occupiedIncidentTiles.Remove(incident.Location);
            IncidentRemoved?.Invoke(incident);

            if (verboseLogging)
            {
                Debug.Log($"[EmergencyIncidentSystem] 응급상황 종료 #{incident.IncidentId}, 상태={incident.State}", this);
            }
        }

        private void FailIncidentAtRemovedTile(Vector2Int removedTile)
        {
            Vector2Int removedAnchor = ResolveAnchor(removedTile);

            for (int i = activeIncidents.Count - 1; i >= 0; i--)
            {
                EmergencyIncident incident = activeIncidents[i];
                if (incident.Location != removedAnchor) continue;

                incident.Fail();
                IncidentChanged?.Invoke(incident);
                RemoveIncident(incident);
            }
        }

        private Vector2Int ResolveAnchor(Vector2Int tile)
        {
            if (tileData != null && tileData.TryGetFootprintAnchor(tile, out Vector2Int anchor))
            {
                return anchor;
            }

            return tile;
        }

        private float GetSpawnChance(TileType type)
        {
            return type switch
            {
                TileType.House => config.HouseSpawnChance,
                TileType.Office => config.OfficeSpawnChance,
                _ => 0f
            };
        }

        private void ScheduleNextSpawn()
        {
            if (config == null)
            {
                nextSpawnTime = Time.time + 10f;
                return;
            }

            nextSpawnTime = Time.time + UnityEngine.Random.Range(
                config.MinimumSpawnInterval,
                config.MaximumSpawnInterval);
        }
    }
}
