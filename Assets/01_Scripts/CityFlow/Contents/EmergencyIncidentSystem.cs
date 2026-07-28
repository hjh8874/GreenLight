using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Content
{
    public sealed class EmergencyIncidentSystem :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        [Header("Configuration")]
        [SerializeField]
        private EmergencyIncidentConfigSO config;

        [Header("Grid")]
        [SerializeField, Min(1)]
        private int gridWidth = GridUtil.DefaultWidth;
        [SerializeField, Min(1)]
        private int gridHeight = GridUtil.DefaultHeight;

        [Header("Startup")]
        [SerializeField]
        private bool enableAutomaticSpawn = true;
        [SerializeField]
        private bool verboseLogging;

        private readonly List<Vector2Int> sourceTiles = new();
        private readonly List<Vector2Int> hospitalTiles = new();
        private readonly List<EmergencyIncident> incidents = new();
        private readonly HashSet<Vector2Int> occupiedSources = new();

        private CityFlowServices services;
        private IReadOnlyTileData tileData;
        private float spawnRemainingSeconds;
        private int nextIncidentId = 1;
        private bool initialized;
        private bool subscribed;

        public IReadOnlyList<EmergencyIncident> ActiveIncidents =>
            incidents;
        public int ActiveIncidentCount => incidents.Count;
        public bool IsInitialized => initialized;

        public event Action<EmergencyIncident> IncidentCreated;
        public event Action<EmergencyIncident> IncidentChanged;
        public event Action<EmergencyIncident> IncidentRemoved;

        public void Initialize(CityFlowServices cityServices)
        {
            if (!isActiveAndEnabled || initialized)
            {
                return;
            }

            if (cityServices?.TileData == null ||
                config == null)
            {
                Debug.LogError(
                    "[EmergencyIncidentSystem] Services, TileData, and config are required.",
                    this);
                return;
            }

            services = cityServices;
            tileData = services.TileData;
            ApplyWorldGridBounds(services.WorldGrid);
            initialized = true;
            RebuildLocations();
            ScheduleNextSpawn();
            Subscribe();
        }

        private void ApplyWorldGridBounds(IWorldGridAccess worldGrid)
        {
            if (worldGrid == null)
            {
                return;
            }

            gridWidth = Mathf.Max(1, worldGrid.WorldWidth);
            gridHeight = Mathf.Max(1, worldGrid.WorldHeight);
        }

        private void Update()
        {
            if (!initialized)
            {
                return;
            }

            Tick(Time.deltaTime);
        }

        public void Tick(float deltaTime)
        {
            float safeDelta = Mathf.Max(0f, deltaTime);
            AdvanceIncidents(safeDelta);

            if (!enableAutomaticSpawn)
            {
                return;
            }

            spawnRemainingSeconds -= safeDelta;

            if (spawnRemainingSeconds > 0f)
            {
                return;
            }

            TryCreateRandomIncident();
            ScheduleNextSpawn();
        }

        private void OnEnable()
        {
            if (initialized)
            {
                Subscribe();
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        public bool TryCreateRandomIncident()
        {
            if (incidents.Count >=
                    config.MaximumActiveIncidents ||
                sourceTiles.Count == 0)
            {
                return false;
            }

            int start = UnityEngine.Random.Range(
                0,
                sourceTiles.Count);

            for (int offset = 0;
                 offset < sourceTiles.Count;
                 offset++)
            {
                Vector2Int tile =
                    sourceTiles[
                        (start + offset) %
                        sourceTiles.Count];

                if (occupiedSources.Contains(tile))
                {
                    continue;
                }

                TileType type =
                    tileData.GetTileType(tile);
                float weight = type switch
                {
                    TileType.House =>
                        config.HouseWeight,
                    TileType.Office =>
                        config.OfficeWeight,
                    _ => 0f
                };

                if (UnityEngine.Random.value <= weight)
                {
                    return TryCreateIncidentAt(tile);
                }
            }

            return false;
        }

        public bool TryCreateIncidentAt(Vector2Int tile)
        {
            if (!initialized ||
                incidents.Count >=
                    config.MaximumActiveIncidents)
            {
                return false;
            }

            Vector2Int anchor = ResolveAnchor(tile);
            TileType type = tileData.GetTileType(anchor);

            if (type is not TileType.House
                    and not TileType.Office ||
                occupiedSources.Contains(anchor))
            {
                return false;
            }

            var incident = new EmergencyIncident(
                nextIncidentId++,
                anchor,
                type);

            incidents.Add(incident);
            occupiedSources.Add(anchor);
            IncidentCreated?.Invoke(incident);
            TryDispatch(incident);

            if (verboseLogging)
            {
                Debug.Log(
                    $"[EmergencyIncidentSystem] Created incident #{incident.IncidentId} at {anchor}.",
                    this);
            }

            return true;
        }

        public void RebuildLocations()
        {
            sourceTiles.Clear();
            hospitalTiles.Clear();

            if (tileData == null)
            {
                return;
            }

            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    Vector2Int tile = new(x, y);
                    TileType type =
                        tileData.GetTileType(tile);

                    if (TileFootprint.IsBuilding(type) &&
                        !tileData.IsFootprintAnchor(tile))
                    {
                        continue;
                    }

                    if (type is TileType.House
                        or TileType.Office)
                    {
                        sourceTiles.Add(tile);
                    }
                    else if (type == TileType.Hospital)
                    {
                        hospitalTiles.Add(tile);
                    }
                }
            }
        }

        private void AdvanceIncidents(float deltaTime)
        {
            for (int i = incidents.Count - 1;
                 i >= 0;
                 i--)
            {
                EmergencyIncident incident = incidents[i];

                if (incident.State ==
                    EmergencyIncidentState.WaitingForHospital)
                {
                    TryDispatch(incident);
                    continue;
                }

                if (!incident.Advance(deltaTime))
                {
                    continue;
                }

                switch (incident.State)
                {
                    case EmergencyIncidentState.AmbulanceOutbound:
                        incident.BeginTreatment(
                            config.TreatmentSeconds);
                        IncidentChanged?.Invoke(incident);
                        break;

                    case EmergencyIncidentState.Treating:
                        incident.BeginReturn(
                            TravelSeconds(incident));
                        IncidentChanged?.Invoke(incident);
                        break;

                    case EmergencyIncidentState.AmbulanceReturning:
                        incident.Resolve();
                        IncidentChanged?.Invoke(incident);
                        RemoveIncidentAt(i);
                        break;
                }
            }
        }

        private bool TryDispatch(EmergencyIncident incident)
        {
            if (!TryFindAvailableHospital(
                    incident.Location,
                    out Vector2Int hospital))
            {
                return false;
            }

            incident.Dispatch(
                hospital,
                TravelSeconds(
                    incident.Location,
                    hospital));
            IncidentChanged?.Invoke(incident);
            return true;
        }

        private bool TryFindAvailableHospital(
            Vector2Int source,
            out Vector2Int result)
        {
            result = default;
            int bestDistance = int.MaxValue;
            bool found = false;

            for (int i = 0; i < hospitalTiles.Count; i++)
            {
                Vector2Int hospital = hospitalTiles[i];

                if (CountAssigned(hospital) >=
                    config.AmbulancesPerHospital)
                {
                    continue;
                }

                int distance = Manhattan(
                    source,
                    hospital);

                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                result = hospital;
                found = true;
            }

            return found;
        }

        private int CountAssigned(Vector2Int hospital)
        {
            int count = 0;

            for (int i = 0; i < incidents.Count; i++)
            {
                EmergencyIncident incident = incidents[i];

                if (!incident.IsFinished &&
                    incident.State !=
                        EmergencyIncidentState.WaitingForHospital &&
                    incident.AssignedHospital == hospital)
                {
                    count++;
                }
            }

            return count;
        }

        private float TravelSeconds(
            EmergencyIncident incident)
        {
            return TravelSeconds(
                incident.Location,
                incident.AssignedHospital);
        }

        private float TravelSeconds(
            Vector2Int source,
            Vector2Int hospital)
        {
            return Mathf.Max(
                config.TravelSecondsPerTile,
                Manhattan(source, hospital) *
                config.TravelSecondsPerTile);
        }

        private void Subscribe()
        {
            if (subscribed)
            {
                return;
            }

            services.Events.Placed += OnPlaced;

            if (services.Save != null)
            {
                services.Save.RestoreCompleted +=
                    OnRestoreCompleted;
            }

            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }

            if (services?.Events != null)
            {
                services.Events.Placed -= OnPlaced;
            }

            if (services?.Save != null)
            {
                services.Save.RestoreCompleted -=
                    OnRestoreCompleted;
            }

            subscribed = false;
        }

        private void OnPlaced(PlacedEvent placed)
        {
            if (placed.Type is not TileType.House
                    and not TileType.Office
                    and not TileType.Hospital)
            {
                return;
            }

            RebuildLocations();

            if (!placed.IsRemove)
            {
                return;
            }

            Vector2Int removed =
                ResolveAnchor(placed.Tile);

            for (int i = incidents.Count - 1;
                 i >= 0;
                 i--)
            {
                EmergencyIncident incident = incidents[i];

                if (incident.Location != removed &&
                    incident.AssignedHospital != removed)
                {
                    continue;
                }

                incident.Fail();
                IncidentChanged?.Invoke(incident);
                RemoveIncidentAt(i);
            }
        }

        private void OnRestoreCompleted(
            RestoreCompletedEvent _)
        {
            ClearTransientIncidents();
            RebuildLocations();
            ScheduleNextSpawn();
        }

        private void ClearTransientIncidents()
        {
            for (int i = incidents.Count - 1;
                 i >= 0;
                 i--)
            {
                incidents[i].Fail();
                IncidentChanged?.Invoke(incidents[i]);
                IncidentRemoved?.Invoke(incidents[i]);
            }

            incidents.Clear();
            occupiedSources.Clear();
        }

        private void RemoveIncidentAt(int index)
        {
            EmergencyIncident incident = incidents[index];
            incidents.RemoveAt(index);
            occupiedSources.Remove(incident.Location);
            IncidentRemoved?.Invoke(incident);
        }

        private Vector2Int ResolveAnchor(Vector2Int tile)
        {
            return tileData.TryGetFootprintAnchor(
                tile,
                out Vector2Int anchor)
                    ? anchor
                    : tile;
        }

        private void ScheduleNextSpawn()
        {
            spawnRemainingSeconds =
                UnityEngine.Random.Range(
                    config.MinimumSpawnInterval,
                    config.MaximumSpawnInterval);
        }

        private static int Manhattan(
            Vector2Int left,
            Vector2Int right)
        {
            return Mathf.Abs(left.x - right.x) +
                   Mathf.Abs(left.y - right.y);
        }
    }
}
