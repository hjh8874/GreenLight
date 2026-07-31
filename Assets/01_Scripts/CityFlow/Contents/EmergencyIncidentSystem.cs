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
        private bool useExternalAmbulanceTransport;
        [SerializeField]
        private bool verboseLogging;

        private readonly List<Vector2Int> sourceTiles = new();
        private readonly List<Vector2Int> hospitalTiles = new();
        private readonly List<Vector2Int> candidateTiles = new();
        private readonly List<Vector2Int> recentTargets = new();
        private readonly List<EmergencyIncident> incidents = new();
        private readonly HashSet<Vector2Int> occupiedSources = new();

        private CityFlowServices services;
        private IReadOnlyTileData tileData;
        private IGameCalendarService calendar;
        private long nextAutomaticDispatchDay =
            long.MaxValue;
        private int nextIncidentId = 1;
        private bool initialized;
        private bool subscribed;
        private bool calendarSubscribed;

        public IReadOnlyList<EmergencyIncident> ActiveIncidents =>
            incidents;
        public IReadOnlyList<Vector2Int> HospitalTiles =>
            hospitalTiles;
        public int ActiveIncidentCount => incidents.Count;
        public bool IsInitialized => initialized;
        public EmergencyIncidentConfigSO Config => config;
        public bool UsesExternalAmbulanceTransport =>
            useExternalAmbulanceTransport;

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
            Subscribe();
            BindCalendar(services.GameCalendar);
            ScheduleNextSpawn();
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
            if (!initialized ||
                incidents.Count >=
                    config.MaximumActiveIncidents ||
                sourceTiles.Count == 0 ||
                hospitalTiles.Count == 0)
            {
                return false;
            }

            CollectCandidateTiles(excludeRecent: true);

            if (candidateTiles.Count == 0)
            {
                CollectCandidateTiles(excludeRecent: false);
            }

            return TryChooseWeightedTarget(
                       candidateTiles,
                       out Vector2Int target) &&
                   TryCreateIncidentAt(target);
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

            if (!IsEligibleIncidentSource(type) ||
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
            RememberTarget(anchor);
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

                    if (IsEligibleIncidentSource(type))
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

                if (useExternalAmbulanceTransport &&
                    incident.State is
                        EmergencyIncidentState.AmbulanceOutbound
                        or EmergencyIncidentState.AmbulanceReturning)
                {
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
                            useExternalAmbulanceTransport
                                ? config.RouteRetrySeconds
                                : TravelSeconds(incident));
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

        public bool TryMarkAmbulanceArrived(int incidentId)
        {
            if (!useExternalAmbulanceTransport ||
                !TryFindIncident(
                    incidentId,
                    out EmergencyIncident incident,
                    out _) ||
                incident.State !=
                    EmergencyIncidentState.AmbulanceOutbound)
            {
                return false;
            }

            incident.BeginTreatment(
                config.TreatmentSeconds);
            IncidentChanged?.Invoke(incident);
            return true;
        }

        public bool TryMarkAmbulanceReturned(int incidentId)
        {
            if (!useExternalAmbulanceTransport ||
                !TryFindIncident(
                    incidentId,
                    out EmergencyIncident incident,
                    out int index) ||
                incident.State !=
                    EmergencyIncidentState.AmbulanceReturning)
            {
                return false;
            }

            incident.Resolve();
            IncidentChanged?.Invoke(incident);
            RemoveIncidentAt(index);
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

            services.GameCalendarRegistered +=
                OnGameCalendarRegistered;
            subscribed = true;
            BindCalendar(
                services.GameCalendar ?? calendar);
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

            if (services != null)
            {
                services.GameCalendarRegistered -=
                    OnGameCalendarRegistered;
            }

            if (calendarSubscribed && calendar != null)
            {
                calendar.DayChanged -= OnDayChanged;
            }

            calendarSubscribed = false;
            subscribed = false;
        }

        private void OnPlaced(PlacedEvent placed)
        {
            if (!IsEligibleIncidentSource(placed.Type) &&
                placed.Type != TileType.Hospital)
            {
                return;
            }

            bool hadHospitals = hospitalTiles.Count > 0;
            bool hadSources = sourceTiles.Count > 0;
            RebuildLocations();

            if (hospitalTiles.Count == 0 ||
                sourceTiles.Count == 0)
            {
                nextAutomaticDispatchDay =
                    long.MaxValue;
            }
            else if ((!hadHospitals || !hadSources) &&
                     !placed.IsRemove)
            {
                ScheduleNextSpawn();
            }

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
            recentTargets.Clear();
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
            if (!enableAutomaticSpawn ||
                calendar == null ||
                hospitalTiles.Count == 0 ||
                sourceTiles.Count == 0)
            {
                nextAutomaticDispatchDay =
                    long.MaxValue;
                return;
            }

            int intervalDays =
                UnityEngine.Random.Range(
                    config.MinimumDispatchIntervalDays,
                    config.MaximumDispatchIntervalDays + 1);
            nextAutomaticDispatchDay =
                calendar.TotalDays + intervalDays;
        }

        private void BindCalendar(
            IGameCalendarService gameCalendar)
        {
            if (ReferenceEquals(calendar, gameCalendar) &&
                calendarSubscribed)
            {
                return;
            }

            if (calendarSubscribed && calendar != null)
            {
                calendar.DayChanged -= OnDayChanged;
            }

            calendar = gameCalendar;
            calendarSubscribed = false;

            if (calendar != null)
            {
                calendar.DayChanged += OnDayChanged;
                calendarSubscribed = true;
            }
        }

        private void OnGameCalendarRegistered(
            IGameCalendarService gameCalendar)
        {
            BindCalendar(gameCalendar);
            ScheduleNextSpawn();
        }

        private void OnDayChanged(int _)
        {
            if (!enableAutomaticSpawn ||
                calendar == null ||
                calendar.TotalDays <
                    nextAutomaticDispatchDay ||
                incidents.Count > 0)
            {
                return;
            }

            if (TryCreateRandomIncident())
            {
                ScheduleNextSpawn();
            }
        }

        private void CollectCandidateTiles(
            bool excludeRecent)
        {
            candidateTiles.Clear();

            for (int i = 0; i < sourceTiles.Count; i++)
            {
                Vector2Int tile = sourceTiles[i];

                if (occupiedSources.Contains(tile) ||
                    (excludeRecent &&
                     recentTargets.Contains(tile)) ||
                    GetSourceWeight(
                        tileData.GetTileType(tile)) <= 0f)
                {
                    continue;
                }

                candidateTiles.Add(tile);
            }
        }

        private bool TryChooseWeightedTarget(
            IReadOnlyList<Vector2Int> candidates,
            out Vector2Int result)
        {
            result = default;
            float totalWeight = 0f;

            for (int i = 0; i < candidates.Count; i++)
            {
                totalWeight += GetSourceWeight(
                    tileData.GetTileType(candidates[i]));
            }

            if (totalWeight <= 0f)
            {
                return false;
            }

            float selected =
                UnityEngine.Random.Range(0f, totalWeight);

            for (int i = 0; i < candidates.Count; i++)
            {
                result = candidates[i];
                selected -= GetSourceWeight(
                    tileData.GetTileType(result));

                if (selected <= 0f)
                {
                    return true;
                }
            }

            return candidates.Count > 0;
        }

        private void RememberTarget(Vector2Int target)
        {
            int historySize =
                config.RecentTargetHistorySize;

            if (historySize <= 0)
            {
                recentTargets.Clear();
                return;
            }

            recentTargets.Remove(target);
            recentTargets.Add(target);

            while (recentTargets.Count > historySize)
            {
                recentTargets.RemoveAt(0);
            }
        }

        private bool TryFindIncident(
            int incidentId,
            out EmergencyIncident incident,
            out int index)
        {
            for (index = 0; index < incidents.Count; index++)
            {
                incident = incidents[index];

                if (incident.IncidentId == incidentId)
                {
                    return true;
                }
            }

            incident = null;
            index = -1;
            return false;
        }

        private float GetSourceWeight(TileType type)
        {
            return type switch
            {
                TileType.House => config.HouseWeight,
                TileType.Office => config.OfficeWeight,
                TileType.School => config.SchoolWeight,
                TileType.SpecialBuilding =>
                    config.SpecialBuildingWeight,
                _ => 0f
            };
        }

        private static bool IsEligibleIncidentSource(
            TileType type)
        {
            return type is TileType.House
                or TileType.Office
                or TileType.School
                or TileType.SpecialBuilding;
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
