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
        ICityFlowServiceConsumer,
        IEmergencyIncidentSaveSource
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

        [Header("Play Mode Test")]
        [SerializeField]
        private bool testUseRandomTarget = true;
        [SerializeField]
        private Vector2Int testTarget;
        [SerializeField, Min(0)]
        private int testDefinitionIndex;

        private readonly List<Vector2Int> sourceTiles = new();
        private readonly List<Vector2Int> hospitalTiles = new();
        private readonly List<Vector2Int> candidateTiles = new();
        private readonly List<Vector2Int> recentTargets = new();
        private readonly List<EmergencyIncident> incidents = new();
        private readonly HashSet<Vector2Int> occupiedSources = new();
        private readonly HashSet<int> reportedOutcomeIds = new();

        private CityFlowServices services;
        private IReadOnlyTileData tileData;
        private IGameCalendarService calendar;
        private long nextAutomaticDispatchDay =
            long.MaxValue;
        private long automaticDispatchCountDay =
            long.MinValue;
        private int automaticDispatchCount;
        private int nextIncidentId = 1;
        private bool initialized;
        private bool subscribed;
        private bool calendarSubscribed;
        private bool restoredSnapshot;

        public IReadOnlyList<EmergencyIncident> ActiveIncidents =>
            incidents;
        public IReadOnlyList<Vector2Int> HospitalTiles =>
            hospitalTiles;
        public int ActiveIncidentCount => incidents.Count;
        public int AutomaticDispatchCountToday =>
            automaticDispatchCount;
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
            services.RegisterEmergencyIncidentSaveSource(this);

            if (!restoredSnapshot)
            {
                ScheduleNextSpawn();
            }
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
            if (initialized)
            {
                Tick(Time.deltaTime);
            }
        }

        public void Tick(float deltaTime)
        {
            AdvanceIncidents(Mathf.Max(0f, deltaTime));
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
            return calendar != null
                ? TryCreateAutomaticIncident()
                : TryCreateRandomIncidentCore(
                    countAsAutomatic: false);
        }

        public bool TryCreateAutomaticIncident()
        {
            ResetAutomaticDispatchCounterIfNeeded();

            if (calendar != null &&
                automaticDispatchCount >=
                    config.MaximumAutomaticIncidentsPerDay)
            {
                return false;
            }

            return TryCreateRandomIncidentCore(
                countAsAutomatic: true);
        }

        public bool TryCreateTestIncidentNow()
        {
            if (!initialized)
            {
                Debug.LogWarning(
                    "[EmergencyIncidentSystem] Initialize the system before creating a test incident.",
                    this);
                return false;
            }

            if (incidents.Count >=
                config.MaximumActiveIncidents)
            {
                Debug.LogWarning(
                    "[EmergencyIncidentSystem] The active incident limit was reached.",
                    this);
                return false;
            }

            if (hospitalTiles.Count == 0)
            {
                Debug.LogWarning(
                    "[EmergencyIncidentSystem] A hospital is required to test ambulance dispatch.",
                    this);
                return false;
            }

            Vector2Int target = testTarget;
            if (testUseRandomTarget)
            {
                CollectCandidateTiles(excludeRecent: true);

                if (candidateTiles.Count == 0)
                {
                    CollectCandidateTiles(
                        excludeRecent: false);
                }

                if (!TryChooseWeightedTarget(
                        candidateTiles,
                        out target))
                {
                    Debug.LogWarning(
                        "[EmergencyIncidentSystem] No eligible test incident target was found.",
                        this);
                    return false;
                }
            }

            EmergencyIncidentDefinitionSO definition =
                GetTestDefinition();
            bool created =
                TryCreateIncidentAt(target, definition);

            if (!created)
            {
                Debug.LogWarning(
                    $"[EmergencyIncidentSystem] Could not create a test incident at {target}.",
                    this);
                return false;
            }

            Debug.Log(
                $"[EmergencyIncidentSystem] Created test incident at {target}. The automatic daily limit and schedule were not changed.",
                this);
            return true;
        }

        [ContextMenu("Testing/Create Incident Now")]
        private void CreateTestIncidentFromContextMenu()
        {
            TryCreateTestIncidentNow();
        }

        public void RepublishActiveAlerts()
        {
            for (int i = 0; i < incidents.Count; i++)
            {
                PublishAlert(incidents[i]);
            }
        }

        private bool TryCreateRandomIncidentCore(
            bool countAsAutomatic)
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

            if (!TryChooseWeightedTarget(
                    candidateTiles,
                    out Vector2Int target) ||
                !TryChooseDefinition(
                    out EmergencyIncidentDefinitionSO
                        definition) ||
                !TryCreateIncidentAt(
                    target,
                    definition))
            {
                return false;
            }

            if (countAsAutomatic && calendar != null)
            {
                ResetAutomaticDispatchCounterIfNeeded();
                automaticDispatchCount++;
            }

            return true;
        }

        public bool TryCreateIncidentAt(Vector2Int tile)
        {
            return TryChooseDefinition(
                       out EmergencyIncidentDefinitionSO
                           definition) &&
                   TryCreateIncidentAt(tile, definition);
        }

        private bool TryCreateIncidentAt(
            Vector2Int tile,
            EmergencyIncidentDefinitionSO definition)
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
                type,
                definition,
                GetCurrentAbsoluteHour());

            AddIncident(incident);
            TryDispatch(incident);

            if (verboseLogging)
            {
                Debug.Log(
                    $"[EmergencyIncidentSystem] Created incident #{incident.IncidentId} ({incident.DefinitionId}) at {anchor}; deadline hour {incident.DeadlineAbsoluteHour}.",
                    this);
            }

            return true;
        }

        public bool TryFailIncidentRouteUnavailable(
            int incidentId)
        {
            return TryFailIncidentRouteUnavailable(
                incidentId,
                ambulanceLeftHospital: true);
        }

        public bool TryFailIncidentRouteUnavailable(
            int incidentId,
            bool ambulanceLeftHospital)
        {
            return TryFindIncident(
                       incidentId,
                       out _,
                       out int index) &&
                   FailIncidentAt(
                       index,
                       EmergencyIncidentFailureReason
                           .DestinationUnreachable,
                       returnToHospital:
                           ambulanceLeftHospital);
        }

        public bool TryFailIncident(
            int incidentId,
            EmergencyIncidentFailureReason reason)
        {
            return TryFindIncident(
                       incidentId,
                       out _,
                       out int index) &&
                   FailIncidentAt(
                       index,
                       reason,
                       returnToHospital: true);
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
                        or EmergencyIncidentState.AmbulanceReturning
                        or EmergencyIncidentState
                            .AmbulanceReturningAfterFailure)
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
                        ResolveIncidentAt(i);
                        break;

                    case EmergencyIncidentState
                        .AmbulanceReturningAfterFailure:
                        incident.CompleteFailure();
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
                incident.State is not (
                    EmergencyIncidentState.AmbulanceReturning
                    or EmergencyIncidentState
                        .AmbulanceReturningAfterFailure))
            {
                return false;
            }

            if (incident.State ==
                EmergencyIncidentState.AmbulanceReturning)
            {
                ResolveIncidentAt(index);
            }
            else
            {
                incident.CompleteFailure();
                IncidentChanged?.Invoke(incident);
                RemoveIncidentAt(index);
            }

            return true;
        }

        public EmergencyIncidentSaveData CreateSnapshot()
        {
            var savedIncidents =
                new EmergencyIncidentEntrySaveData[
                    incidents.Count];

            for (int i = 0; i < incidents.Count; i++)
            {
                EmergencyIncident incident = incidents[i];
                savedIncidents[i] =
                    new EmergencyIncidentEntrySaveData
                    {
                        IncidentId = incident.IncidentId,
                        DefinitionId =
                            incident.DefinitionId,
                        LocationX = incident.Location.x,
                        LocationY = incident.Location.y,
                        SourceType =
                            (int)incident.SourceType,
                        State = (int)incident.State,
                        HospitalX =
                            incident.AssignedHospital.x,
                        HospitalY =
                            incident.AssignedHospital.y,
                        StateRemainingSeconds =
                            incident.StateRemainingSeconds,
                        CreatedAbsoluteHour =
                            incident.CreatedAbsoluteHour,
                        DeadlineAbsoluteHour =
                            incident.DeadlineAbsoluteHour,
                        FailureReason =
                            (int)incident.FailureReason
                    };
            }

            var savedTargets =
                new EmergencyIncidentTargetSaveData[
                    recentTargets.Count];
            for (int i = 0; i < recentTargets.Count; i++)
            {
                savedTargets[i] =
                    new EmergencyIncidentTargetSaveData
                    {
                        X = recentTargets[i].x,
                        Y = recentTargets[i].y
                    };
            }

            return new EmergencyIncidentSaveData
            {
                NextIncidentId = nextIncidentId,
                NextAutomaticDispatchDay =
                    nextAutomaticDispatchDay,
                AutomaticDispatchCountDay =
                    automaticDispatchCountDay,
                AutomaticDispatchCount =
                    automaticDispatchCount,
                ActiveIncidents = savedIncidents,
                RecentTargets = savedTargets
            };
        }

        public void RestoreSnapshot(
            EmergencyIncidentSaveData snapshot)
        {
            ClearIncidentsForRestore();
            RebuildLocations();
            restoredSnapshot = true;
            nextIncidentId = Mathf.Max(
                1,
                snapshot?.NextIncidentId ?? 1);
            nextAutomaticDispatchDay =
                snapshot?.NextAutomaticDispatchDay ??
                long.MaxValue;
            automaticDispatchCountDay =
                snapshot?.AutomaticDispatchCountDay ??
                long.MinValue;
            automaticDispatchCount = Mathf.Max(
                0,
                snapshot?.AutomaticDispatchCount ?? 0);

            RestoreRecentTargets(snapshot?.RecentTargets);
            EmergencyIncidentEntrySaveData[] entries =
                snapshot?.ActiveIncidents;

            if (entries != null)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    RestoreIncident(entries[i]);
                }
            }

            ResetAutomaticDispatchCounterIfNeeded();

            if (nextAutomaticDispatchDay <= 0L ||
                nextAutomaticDispatchDay == long.MaxValue)
            {
                ScheduleNextSpawn();
            }
        }

        private void RestoreIncident(
            EmergencyIncidentEntrySaveData entry)
        {
            if (entry == null)
            {
                return;
            }

            Vector2Int location =
                new(entry.LocationX, entry.LocationY);
            Vector2Int hospital =
                new(entry.HospitalX, entry.HospitalY);
            TileType currentType =
                tileData.GetTileType(location);

            if (!IsEligibleIncidentSource(currentType) ||
                occupiedSources.Contains(location))
            {
                return;
            }

            EmergencyIncidentState savedState =
                Enum.IsDefined(
                    typeof(EmergencyIncidentState),
                    entry.State)
                    ? (EmergencyIncidentState)entry.State
                    : EmergencyIncidentState
                        .WaitingForHospital;

            if (savedState is
                EmergencyIncidentState.Resolved
                or EmergencyIncidentState.Failed)
            {
                return;
            }

            bool hasHospital =
                hospitalTiles.Contains(hospital);
            EmergencyIncidentState restoredState =
                savedState !=
                    EmergencyIncidentState.WaitingForHospital &&
                hasHospital
                    ? savedState
                    : EmergencyIncidentState
                        .WaitingForHospital;
            Vector2Int restoredHospital =
                restoredState ==
                    EmergencyIncidentState.WaitingForHospital
                    ? new Vector2Int(-1, -1)
                    : hospital;
            EmergencyIncidentFailureReason restoredFailure =
                RestoreFailureReason(
                    entry.FailureReason,
                    restoredState);
            EmergencyIncidentDefinitionSO definition =
                FindDefinition(entry.DefinitionId);
            var incident = EmergencyIncident.Restore(
                entry.IncidentId,
                location,
                currentType,
                definition,
                Math.Max(0L, entry.CreatedAbsoluteHour),
                entry.DeadlineAbsoluteHour,
                restoredState,
                restoredHospital,
                entry.StateRemainingSeconds,
                restoredFailure);

            nextIncidentId = Mathf.Max(
                nextIncidentId,
                incident.IncidentId + 1);

            if (incident.IsResponsePending &&
                GetCurrentAbsoluteHour() >=
                    incident.DeadlineAbsoluteHour)
            {
                incident.Fail(
                    EmergencyIncidentFailureReason
                        .ResponseDeadlineExceeded);
                PublishOutcome(
                    incident,
                    EmergencyIncidentOutcome.Failed,
                    incident.FailureReason);
                return;
            }

            if (restoredState ==
                EmergencyIncidentState
                    .AmbulanceReturningAfterFailure)
            {
                reportedOutcomeIds.Add(
                    incident.IncidentId);
            }

            AddIncident(incident);
        }

        private static EmergencyIncidentFailureReason
            RestoreFailureReason(
                int savedReason,
                EmergencyIncidentState restoredState)
        {
            if (restoredState !=
                EmergencyIncidentState
                    .AmbulanceReturningAfterFailure)
            {
                return EmergencyIncidentFailureReason.None;
            }

            if (Enum.IsDefined(
                    typeof(EmergencyIncidentFailureReason),
                    savedReason) &&
                (EmergencyIncidentFailureReason)savedReason !=
                EmergencyIncidentFailureReason.None)
            {
                return (EmergencyIncidentFailureReason)savedReason;
            }

            return EmergencyIncidentFailureReason
                .DestinationUnreachable;
        }

        private void RestoreRecentTargets(
            EmergencyIncidentTargetSaveData[] targets)
        {
            recentTargets.Clear();

            if (targets == null)
            {
                return;
            }

            int start = Mathf.Max(
                0,
                targets.Length -
                config.RecentTargetHistorySize);
            for (int i = start; i < targets.Length; i++)
            {
                EmergencyIncidentTargetSaveData target =
                    targets[i];
                if (target != null)
                {
                    recentTargets.Add(
                        new Vector2Int(target.X, target.Y));
                }
            }
        }

        private bool FailIncidentAt(
            int index,
            EmergencyIncidentFailureReason reason,
            bool returnToHospital)
        {
            if (index < 0 || index >= incidents.Count)
            {
                return false;
            }

            EmergencyIncident incident = incidents[index];
            if (incident.IsFinished ||
                incident.State ==
                    EmergencyIncidentState
                        .AmbulanceReturningAfterFailure)
            {
                return false;
            }

            bool canReturn =
                returnToHospital &&
                incident.AssignedHospital.x >= 0 &&
                incident.AssignedHospital.y >= 0 &&
                hospitalTiles.Contains(
                    incident.AssignedHospital) &&
                incident.State !=
                    EmergencyIncidentState.WaitingForHospital;

            if (canReturn)
            {
                incident.BeginFailedReturn(
                    reason,
                    useExternalAmbulanceTransport
                        ? config.RouteRetrySeconds
                        : TravelSeconds(incident));
                PublishOutcome(
                    incident,
                    EmergencyIncidentOutcome.Failed,
                    reason);
                IncidentChanged?.Invoke(incident);
                return true;
            }

            incident.Fail(reason);
            PublishOutcome(
                incident,
                EmergencyIncidentOutcome.Failed,
                reason);
            IncidentChanged?.Invoke(incident);
            RemoveIncidentAt(index);
            return true;
        }

        private void ResolveIncidentAt(int index)
        {
            EmergencyIncident incident = incidents[index];
            incident.Resolve();
            PublishOutcome(
                incident,
                EmergencyIncidentOutcome.Resolved,
                EmergencyIncidentFailureReason.None);
            IncidentChanged?.Invoke(incident);
            RemoveIncidentAt(index);
        }

        private void AddIncident(EmergencyIncident incident)
        {
            incidents.Add(incident);
            occupiedSources.Add(incident.Location);
            RememberTarget(incident.Location);
            IncidentCreated?.Invoke(incident);
            PublishAlert(incident);
        }

        private void PublishAlert(EmergencyIncident incident)
        {
            services?.Events?.Publish(
                new EmergencyIncidentAlertEvent(
                    incident.IncidentId,
                    incident.DefinitionId,
                    incident.Title,
                    incident.Description,
                    incident.Location,
                    incident.DeadlineAbsoluteHour));
        }

        private void PublishOutcome(
            EmergencyIncident incident,
            EmergencyIncidentOutcome outcome,
            EmergencyIncidentFailureReason reason)
        {
            if (!reportedOutcomeIds.Add(
                    incident.IncidentId))
            {
                return;
            }

            string message = outcome ==
                             EmergencyIncidentOutcome.Resolved
                ? incident.SuccessMessage
                : incident.GetFailureMessage(reason);
            services?.Events?.Publish(
                new EmergencyIncidentOutcomeEvent(
                    incident.IncidentId,
                    incident.DefinitionId,
                    incident.Title,
                    message,
                    incident.Location,
                    outcome,
                    reason,
                    outcome ==
                        EmergencyIncidentOutcome.Failed
                        ? incident
                            .SuggestedFailureHappinessDelta
                        : 0f));
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

            UnbindCalendar();
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

                if (incident.Location == removed)
                {
                    FailIncidentAt(
                        i,
                        EmergencyIncidentFailureReason
                            .TargetRemoved,
                        returnToHospital: true);
                }
                else if (
                    incident.AssignedHospital == removed)
                {
                    FailIncidentAt(
                        i,
                        EmergencyIncidentFailureReason
                            .HospitalRemoved,
                        returnToHospital: false);
                }
            }
        }

        private void OnRestoreCompleted(
            RestoreCompletedEvent _)
        {
            RebuildLocations();
            ResetAutomaticDispatchCounterIfNeeded();

            if (nextAutomaticDispatchDay ==
                long.MaxValue)
            {
                ScheduleNextSpawn();
            }

            for (int i = 0; i < incidents.Count; i++)
            {
                PublishAlert(incidents[i]);
            }
        }

        private void ClearIncidentsForRestore()
        {
            for (int i = incidents.Count - 1;
                 i >= 0;
                 i--)
            {
                IncidentRemoved?.Invoke(incidents[i]);
            }

            incidents.Clear();
            occupiedSources.Clear();
            reportedOutcomeIds.Clear();
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

            UnbindCalendar();
            calendar = gameCalendar;

            if (calendar != null)
            {
                calendar.HourChanged += OnHourChanged;
                calendar.DayChanged += OnDayChanged;
                calendarSubscribed = true;
                ResetAutomaticDispatchCounterIfNeeded();
            }
        }

        private void UnbindCalendar()
        {
            if (calendarSubscribed && calendar != null)
            {
                calendar.HourChanged -= OnHourChanged;
                calendar.DayChanged -= OnDayChanged;
            }

            calendarSubscribed = false;
        }

        private void OnGameCalendarRegistered(
            IGameCalendarService gameCalendar)
        {
            BindCalendar(gameCalendar);

            if (!restoredSnapshot)
            {
                ScheduleNextSpawn();
            }
        }

        private void OnHourChanged(int _)
        {
            long currentHour = GetCurrentAbsoluteHour();

            for (int i = incidents.Count - 1;
                 i >= 0;
                 i--)
            {
                EmergencyIncident incident = incidents[i];

                if (incident.IsResponsePending &&
                    currentHour >=
                        incident.DeadlineAbsoluteHour)
                {
                    FailIncidentAt(
                        i,
                        EmergencyIncidentFailureReason
                            .ResponseDeadlineExceeded,
                        returnToHospital: true);
                }
            }
        }

        private void OnDayChanged(int _)
        {
            ResetAutomaticDispatchCounterIfNeeded();

            if (!enableAutomaticSpawn ||
                calendar == null ||
                calendar.TotalDays <
                    nextAutomaticDispatchDay)
            {
                return;
            }

            TryCreateAutomaticIncident();
            ScheduleNextSpawn();
        }

        private void ResetAutomaticDispatchCounterIfNeeded()
        {
            if (calendar == null ||
                automaticDispatchCountDay ==
                    calendar.TotalDays)
            {
                return;
            }

            automaticDispatchCountDay =
                calendar.TotalDays;
            automaticDispatchCount = 0;
        }

        private long GetCurrentAbsoluteHour()
        {
            if (calendar == null)
            {
                return 0L;
            }

            return calendar.TotalDays *
                   Math.Max(1, calendar.HoursPerDay) +
                   Math.Max(0, calendar.Hour);
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

        private bool TryChooseDefinition(
            out EmergencyIncidentDefinitionSO definition)
        {
            IReadOnlyList<EmergencyIncidentDefinitionSO>
                definitions = config.IncidentDefinitions;
            float totalWeight = 0f;

            for (int i = 0; i < definitions.Count; i++)
            {
                if (definitions[i] != null)
                {
                    totalWeight +=
                        definitions[i].SelectionWeight;
                }
            }

            if (totalWeight <= 0f)
            {
                definition = null;
                return true;
            }

            float selected =
                UnityEngine.Random.Range(0f, totalWeight);
            for (int i = 0; i < definitions.Count; i++)
            {
                EmergencyIncidentDefinitionSO candidate =
                    definitions[i];
                if (candidate == null)
                {
                    continue;
                }

                selected -= candidate.SelectionWeight;
                if (selected <= 0f)
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = null;
            return true;
        }

        private EmergencyIncidentDefinitionSO
            GetTestDefinition()
        {
            IReadOnlyList<EmergencyIncidentDefinitionSO>
                definitions = config.IncidentDefinitions;

            if (definitions.Count == 0)
            {
                return null;
            }

            int index = Mathf.Clamp(
                testDefinitionIndex,
                0,
                definitions.Count - 1);
            return definitions[index];
        }

        private EmergencyIncidentDefinitionSO FindDefinition(
            string definitionId)
        {
            IReadOnlyList<EmergencyIncidentDefinitionSO>
                definitions = config.IncidentDefinitions;

            for (int i = 0; i < definitions.Count; i++)
            {
                EmergencyIncidentDefinitionSO definition =
                    definitions[i];
                if (definition != null &&
                    string.Equals(
                        definition.IncidentId,
                        definitionId,
                        StringComparison.Ordinal))
                {
                    return definition;
                }
            }

            return null;
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
