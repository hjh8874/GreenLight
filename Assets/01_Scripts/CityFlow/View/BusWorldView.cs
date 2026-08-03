using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Content.Transit;
using CityFlow.Contracts;
using CityFlow.ViewKit;
using UnityEngine;

namespace CityFlow.View
{
    [RequireComponent(typeof(BusRoute))]
    public class BusWorldView :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        [Header("Configuration")]
        [SerializeField] private BusDefinitionSO definition;
        [SerializeField] private BusRoute busRoute;
        [SerializeField] private CityBusService cityBusService;
        [SerializeField] private MainCityView cityView;
        [SerializeField] private GameObject busVisualPrefab;
        [SerializeField] private Material busMaterial;

        [Header("Presentation")]
        [SerializeField, Min(0.01f)]
        private float visualScale = 0.085f;
        [SerializeField]
        private float visualDepth = -0.38f;
        [SerializeField, Min(0.01f)]
        private float movementDuration = 0.22f;

        [Header("School Parking")]
        [SerializeField, Min(0)]
        private int schoolParkingSlot = 1;
        [SerializeField, Min(0.1f)]
        private float parkingApproachDistance = 0.7f;

        private readonly List<Vector2Int> bakedRoadTiles = new();
        private IReadOnlyTileData tileData;
        private IRoadTrafficService roadTraffic;
        private IIntersectionFacilityService intersectionFacilities;
        private Transform visual;
        private RoutePolyline roadPolyline;
        private int roadPathHash;
        private bool hasRoadPathHash;
        private bool subscribed;
        private bool hasRoadPose;
        private int observedRoadSegmentVersion = -1;
        private float currentDistance;
        private float segmentStartDistance;
        private float targetDistance;
        private float segmentElapsed;
        private float segmentDuration;
        private bool hasStopPresentationTarget;
        private float stopPresentationDistance;
        private int stopPresentationSegmentVersion = -1;
        private bool hasObservedTrafficSnapshot;
        private bool observedSnapshotVisible;
        private RoadTrafficAgentState observedSnapshotState;
        private bool snapshotMissingLogged;

        private bool offRoadTransitionActive;
        private bool transitionEndsOnRoad;
        private bool isParkedOffRoad;
        private Vector3 transitionStart;
        private Vector3 transitionControl;
        private Vector3 transitionTarget;
        private Vector2 parkingForward;
        private float transitionElapsed;
        private float transitionDuration;

        public bool HasVisibleBus =>
            visual != null &&
            visual.gameObject.activeInHierarchy;
        public bool IsAgentVisible { get; private set; } = true;

        public void ConfigureCityBusAgent(
            CityFlowServices services,
            BusDefinitionSO busDefinition,
            BusRoute route,
            CityBusService owner,
            BusWorldView presentationTemplate = null)
        {
            Unsubscribe();
            definition = busDefinition;
            busRoute = route;
            cityBusService = owner;
            IsAgentVisible = false;

            if (presentationTemplate != null &&
                presentationTemplate != this)
            {
                cityView = presentationTemplate.cityView;
                busVisualPrefab =
                    presentationTemplate.busVisualPrefab;
                busMaterial = presentationTemplate.busMaterial;
                visualScale = presentationTemplate.visualScale;
                visualDepth = presentationTemplate.visualDepth;
                movementDuration =
                    presentationTemplate.movementDuration;
                schoolParkingSlot =
                    presentationTemplate.schoolParkingSlot;
                parkingApproachDistance =
                    presentationTemplate.parkingApproachDistance;
            }

            Initialize(services);
        }

        public void Initialize(CityFlowServices services)
        {
            tileData = services?.TileData;
            roadTraffic = services?.RoadTraffic;
            intersectionFacilities =
                services?.Placement as IIntersectionFacilityService;
            ResolveReferences();
            EnsureVisual();
            UpdateStopPresentationGate();
            Subscribe();
            HandleTileChanged(busRoute != null
                ? busRoute.CurrentTile
                : default);
            LogCityBusPresentationState("configured");
        }

        public void SetAgentVisible(bool visible)
        {
            IsAgentVisible = visible;
            UpdateStopPresentationGate();
            if (!visible)
            {
                HideCityBusVisual();
                return;
            }

            RefreshVisiblePresentation();
            LogCityBusPresentationState("agent visibility enabled");
        }

        public void DetachCityBusAgent()
        {
            DisableStopPresentationGate();
            Unsubscribe();
            IsAgentVisible = false;
            HideCityBusVisual();
            cityBusService = null;
            busRoute = null;
        }

        protected virtual void Awake()
        {
            ResolveReferences();
            EnsureVisual();
        }

        protected virtual void OnEnable()
        {
            ResolveReferences();
            EnsureVisual();
            UpdateStopPresentationGate();
            Subscribe();
        }

        protected virtual void OnDisable()
        {
            DisableStopPresentationGate();
            Unsubscribe();
        }

        protected virtual void OnDestroy()
        {
            DisableStopPresentationGate();
            Unsubscribe();

            if (visual == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(visual.gameObject);
            }
            else
            {
                DestroyImmediate(visual.gameObject);
            }

            visual = null;
        }

        private void Update()
        {
            ResolveReferences();
            EnsureVisual();
            UpdateStopPresentationGate();

            if (!IsAgentVisible ||
                (cityBusService != null &&
                 !cityBusService.IsVehicleVisible))
            {
                HideCityBusVisual();
                return;
            }

            if (offRoadTransitionActive)
            {
                UpdateOffRoadTransition();
                return;
            }

            UpdateRoadPose();
        }

        private void ResolveReferences()
        {
            busRoute ??= GetComponent<BusRoute>();
            cityBusService ??= GetComponent<CityBusService>();
            cityView ??= FindAnyObjectByType<MainCityView>();

            if (definition == null)
            {
                definition = cityBusService?.Definition;
            }

            if (definition == null)
            {
                SchoolBusService schoolBusService =
                    GetComponent<SchoolBusService>();
                definition = schoolBusService?.Definition;
            }
        }

        private void Subscribe()
        {
            if (subscribed || busRoute == null)
            {
                return;
            }

            busRoute.TileChanged += HandleTileChanged;
            busRoute.RouteUnavailable += HandleRouteUnavailable;
            busRoute.StopPresentationRequested +=
                HandleStopPresentationRequested;
            if (cityBusService != null)
            {
                cityBusService.VehicleVisibilityChanged +=
                    HandleVehicleVisibilityChanged;
            }
            subscribed = true;

            if (cityBusService != null)
            {
                HandleVehicleVisibilityChanged(
                    cityBusService.IsVehicleVisible);
            }
        }

        private void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }

            if (busRoute != null)
            {
                busRoute.TileChanged -= HandleTileChanged;
                busRoute.RouteUnavailable -= HandleRouteUnavailable;
                busRoute.StopPresentationRequested -=
                    HandleStopPresentationRequested;
            }
            if (cityBusService != null)
            {
                cityBusService.VehicleVisibilityChanged -=
                    HandleVehicleVisibilityChanged;
            }
            subscribed = false;
        }

        private void EnsureVisual()
        {
            if (visual != null || cityView == null)
            {
                return;
            }

            GameObject prefab = busVisualPrefab != null
                ? busVisualPrefab
                : definition?.VehicleVisualPrefab;
            if (prefab == null)
            {
                return;
            }

            GameObject instance = Instantiate(
                prefab,
                cityView.transform);
            string busName = definition?.BusType == BusType.SchoolBus
                ? "SchoolBusVisual"
                : "CityBusVisual";
            instance.name = busName;
            visual = instance.transform;
            visual.localScale = Vector3.one * visualScale;
            ApplyFeatureMaterial(instance);
            instance.SetActive(false);

            CityBusVehicleAgent agent =
                GetComponent<CityBusVehicleAgent>();
            if (agent != null)
            {
                Debug.Log(
                    $"[BusWorldView] Route {agent.RouteId} " +
                    $"{agent.Direction} visual instantiated. " +
                    $"visual={instance.name}, " +
                    $"visualEntity={instance.GetEntityId()}, " +
                    $"cityView={cityView.name}.",
                    this);
            }
        }

        private void ApplyFeatureMaterial(GameObject instance)
        {
            if (busMaterial == null)
            {
                return;
            }

            Renderer[] renderers =
                instance.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer targetRenderer in renderers)
            {
                Material[] materials = targetRenderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    materials[index] = busMaterial;
                }

                targetRenderer.sharedMaterials = materials;
            }
        }

        private void HandleTileChanged(Vector2Int tile)
        {
            EnsureVisual();
            if (visual == null || cityView == null)
            {
                return;
            }

            if (TryGetSchoolParkingPose(
                    tile,
                    out Vector3 parkingPosition,
                    out Vector2 forward))
            {
                parkingForward = forward;
                if (!visual.gameObject.activeSelf || !hasRoadPose)
                {
                    ApplyPose(parkingPosition, forward);
                    visual.gameObject.SetActive(true);
                    isParkedOffRoad = true;
                    offRoadTransitionActive = false;
                    return;
                }

                Vector3 control = parkingPosition -
                    ToVector3(forward) * GetParkingApproachDistance();
                BeginOffRoadTransition(
                    parkingPosition,
                    control,
                    endsOnRoad: false);
                return;
            }

            if (!IsRoad(tile) || !isParkedOffRoad)
            {
                return;
            }

            if (offRoadTransitionActive && transitionEndsOnRoad)
            {
                return;
            }

            if (!TryBakeRoadPath(out _) || roadPolyline == null)
            {
                return;
            }

            Sample firstRoadPose = roadPolyline.SampleAt(0f);
            Vector3 departureControl = visual.localPosition -
                ToVector3(parkingForward) * GetParkingApproachDistance();
            BeginOffRoadTransition(
                firstRoadPose.Pos,
                departureControl,
                endsOnRoad: true);
        }

        private void UpdateRoadPose()
        {
            if (visual == null || busRoute == null || isParkedOffRoad)
            {
                return;
            }

            if (!busRoute.TryGetRoadTrafficSnapshot(
                    out RoadTrafficSnapshot snapshot))
            {
                LogMissingTrafficSnapshot();
                return;
            }

            LogTrafficSnapshotTransition(snapshot);
            if (!snapshot.IsVisible ||
                snapshot.State == RoadTrafficAgentState.RouteUnavailable ||
                !TryBakeRoadPath(out bool pathChanged) ||
                roadPolyline == null)
            {
                return;
            }

            bool segmentChanged =
                observedRoadSegmentVersion !=
                busRoute.RoadSegmentVersion;
            observedRoadSegmentVersion =
                busRoute.RoadSegmentVersion;

            int tileIndex = Mathf.Clamp(
                snapshot.RouteTileIndex,
                0,
                roadPolyline.TileCount - 1);
            Vector2Int tile = roadPolyline.TileAt(tileIndex);
            float headInset = cityView.IsSharedIntersectionTile(tile)
                ? cityView.IntersectionQueueInsetTiles * cityView.TileSize
                : 0f;
            float nextTarget = roadPolyline.ReprojectDistance(
                tileIndex,
                snapshot.QueueOffsetTiles * cityView.TileSize,
                headInset,
                snapshot.IntersectionProgress01,
                snapshot.LinkProgress01,
                snapshot.RoundaboutProgress01,
                cityView.RoundaboutTransitionSpanTiles);
            nextTarget = ResolveStopPresentationTarget(nextTarget);

            bool stopPresentationPending =
                busRoute.IsStopPresentationPending;
            if (!hasRoadPose ||
                (!stopPresentationPending &&
                 (pathChanged || segmentChanged)))
            {
                currentDistance = nextTarget;
                targetDistance = nextTarget;
                segmentStartDistance = nextTarget;
                segmentElapsed = 0f;
                segmentDuration = 0f;
                hasRoadPose = true;
                bool visualWasInactive =
                    !visual.gameObject.activeSelf;
                visual.gameObject.SetActive(true);
                ApplyRoadSample(snapshot);
                if (visualWasInactive)
                {
                    LogCityBusPresentationState(
                        "road visual activated");
                }

                TryConfirmStopPresentationReached(
                    snapshot,
                    nextTarget);
                return;
            }

            if (nextTarget > targetDistance + 0.0001f)
            {
                segmentStartDistance = currentDistance;
                targetDistance = nextTarget;
                segmentElapsed = 0f;
                float stepInterval = roadTraffic?.StepIntervalSeconds ?? 0.1f;
                float stepProgress = roadTraffic?.StepProgress01 ?? 0f;
                segmentDuration = Mathf.Max(
                    0.02f,
                    stepInterval * (1f - stepProgress));
            }

            if (currentDistance < targetDistance - 0.0001f)
            {
                segmentElapsed = Mathf.Min(
                    segmentElapsed + Time.deltaTime,
                    segmentDuration);
                float progress = segmentDuration > 0f
                    ? Mathf.Clamp01(segmentElapsed / segmentDuration)
                    : 1f;
                currentDistance = Mathf.Lerp(
                    segmentStartDistance,
                    targetDistance,
                    progress);
            }

            ApplyRoadSample(snapshot);
            TryConfirmStopPresentationReached(
                snapshot,
                nextTarget);
        }

        private float ResolveStopPresentationTarget(
            float snapshotTarget)
        {
            if (busRoute == null ||
                !busRoute.IsStopPresentationPending)
            {
                ResetStopPresentationTarget();
                return snapshotTarget;
            }

            if (!hasStopPresentationTarget ||
                stopPresentationSegmentVersion !=
                busRoute.RoadSegmentVersion)
            {
                stopPresentationDistance = hasRoadPose
                    ? Mathf.Max(currentDistance, snapshotTarget)
                    : snapshotTarget;
                stopPresentationSegmentVersion =
                    busRoute.RoadSegmentVersion;
                hasStopPresentationTarget = true;

                if (hasRoadPose)
                {
                    segmentStartDistance = currentDistance;
                    targetDistance = stopPresentationDistance;
                    segmentElapsed = 0f;
                    float stepInterval =
                        roadTraffic?.StepIntervalSeconds ?? 0.1f;
                    float stepProgress =
                        roadTraffic?.StepProgress01 ?? 0f;
                    segmentDuration = Mathf.Max(
                        0.02f,
                        stepInterval * (1f - stepProgress));
                }
            }

            return stopPresentationDistance;
        }

        private void TryConfirmStopPresentationReached(
            RoadTrafficSnapshot snapshot,
            float stopDistance)
        {
            if (busRoute == null ||
                !busRoute.IsStopPresentationPending ||
                cityView == null)
            {
                return;
            }

            float tolerance = Mathf.Max(
                0.005f,
                cityView.TileSize * 0.02f);
            if (Mathf.Abs(currentDistance - stopDistance) >
                tolerance)
            {
                return;
            }

            currentDistance = stopDistance;
            targetDistance = stopDistance;
            segmentStartDistance = stopDistance;
            segmentElapsed = 0f;
            segmentDuration = 0f;
            ApplyRoadSample(snapshot);
            if (busRoute.ConfirmStopPresentationReached())
            {
                ResetStopPresentationTarget();
            }
        }

        private bool TryBakeRoadPath(out bool changed)
        {
            changed = false;
            IReadOnlyList<Vector2Int> source = busRoute?.CurrentRoadPath;
            if (source == null || source.Count == 0 || cityView == null)
            {
                return roadPolyline != null;
            }

            int hash = 17;
            int roadCount = 0;
            bool foundRoad = false;
            for (int index = 0; index < source.Count; index++)
            {
                if (!IsRoad(source[index]))
                {
                    if (foundRoad)
                    {
                        break;
                    }

                    continue;
                }

                foundRoad = true;
                roadCount++;
                unchecked
                {
                    hash = hash * 31 + source[index].GetHashCode();
                }
            }

            unchecked
            {
                hash = hash * 31 + cityView.TileSize.GetHashCode();
                hash = hash * 31 + cityView.LaneOffset.GetHashCode();
                hash = hash * 31 + visualDepth.GetHashCode();
                hash = hash * 31 +
                       cityView.CornerTurnRadiusFraction.GetHashCode();
                hash = hash * 31 +
                       cityView.RoundaboutOrbitRadiusTiles.GetHashCode();
                hash = hash * 31 +
                       cityView.RoundaboutEntryExitRadians.GetHashCode();
                hash = hash * 31 +
                       cityView.RoundaboutTransitionSpanTiles.GetHashCode();
                IReadOnlyList<Vector2Int> roundabouts =
                    intersectionFacilities?.RoundaboutTiles;
                if (roundabouts != null)
                {
                    for (int index = 0;
                         index < roundabouts.Count;
                         index++)
                    {
                        hash = hash * 31 +
                               roundabouts[index].GetHashCode();
                    }
                }
            }

            if (roadCount == 0)
            {
                return roadPolyline != null;
            }

            if (hasRoadPathHash && roadPathHash == hash)
            {
                return true;
            }

            bakedRoadTiles.Clear();
            foundRoad = false;
            for (int index = 0; index < source.Count; index++)
            {
                Vector2Int candidate = source[index];
                if (!IsRoad(candidate))
                {
                    if (foundRoad)
                    {
                        break;
                    }

                    continue;
                }

                foundRoad = true;
                bakedRoadTiles.Add(candidate);
            }

            roadPolyline = RoutePolyline.Bake(new BakeInput
            {
                Tiles = bakedRoadTiles,
                GridOrigin = cityView.GridOrigin,
                TileSize = cityView.TileSize,
                LaneOffset = cityView.LaneOffset,
                CornerRadiusFraction = cityView.CornerTurnRadiusFraction,
                OrbitRadius = cityView.RoundaboutOrbitRadiusTiles,
                EntryExitOffsetRad = cityView.RoundaboutEntryExitRadians,
                TransitionLength = cityView.RoundaboutTransitionSpanTiles,
                Z = visualDepth,
                IsRoundabout = cityView.IsRoundaboutRoadTile,
                SamplesPerSegment = 8
            });
            roadPathHash = hash;
            hasRoadPathHash = true;
            changed = true;
            return true;
        }

        private void ApplyRoadSample(RoadTrafficSnapshot snapshot)
        {
            Sample sample = roadPolyline.SampleAt(currentDistance);
            Vector3 position = sample.Pos;
            if (snapshot.LinkProgress01 > 0f)
            {
                position.z -= 0.35f *
                    Mathf.Sin(snapshot.LinkProgress01 * Mathf.PI);
            }

            ApplyPose(
                position,
                new Vector2(sample.Dir.x, sample.Dir.y));
        }

        private void BeginOffRoadTransition(
            Vector3 target,
            Vector3 control,
            bool endsOnRoad)
        {
            transitionStart = visual.localPosition;
            transitionControl = control;
            transitionTarget = target;
            transitionElapsed = 0f;
            transitionDuration = CalculateTransitionDuration(
                transitionStart,
                transitionControl,
                transitionTarget);
            transitionEndsOnRoad = endsOnRoad;
            offRoadTransitionActive = true;
            visual.gameObject.SetActive(true);
        }

        private void UpdateOffRoadTransition()
        {
            transitionElapsed = Mathf.Min(
                transitionElapsed + Time.deltaTime,
                transitionDuration);
            float progress = transitionDuration > 0f
                ? Mathf.Clamp01(transitionElapsed / transitionDuration)
                : 1f;
            Vector3 position = EvaluateQuadraticPoint(
                transitionStart,
                transitionControl,
                transitionTarget,
                progress);
            Vector3 tangent = EvaluateQuadraticTangent(
                transitionStart,
                transitionControl,
                transitionTarget,
                progress);
            Vector2 direction = new(tangent.x, tangent.y);
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = parkingForward.sqrMagnitude > 0.5f
                    ? parkingForward
                    : Vector2.right;
            }

            ApplyPose(position, direction.normalized);
            if (progress < 1f)
            {
                return;
            }

            offRoadTransitionActive = false;
            isParkedOffRoad = !transitionEndsOnRoad;
            if (transitionEndsOnRoad)
            {
                hasRoadPose = false;
                return;
            }

            busRoute?.ConfirmStopPresentationReached();
        }

        private bool TryGetSchoolParkingPose(
            Vector2Int tile,
            out Vector3 localPosition,
            out Vector2 forward)
        {
            localPosition = default;
            forward = default;
            if (definition?.BusType != BusType.SchoolBus ||
                tileData == null ||
                tileData.GetTileType(tile) != TileType.School)
            {
                return false;
            }

            Vector2Int schoolAnchor = tile;
            tileData.TryGetFootprintAnchor(tile, out schoolAnchor);
            if (!cityView.TryGetBuildingParkingPose(
                    schoolAnchor,
                    schoolParkingSlot,
                    out localPosition,
                    out Vector3 localForward))
            {
                return false;
            }

            localPosition.z = visualDepth;
            forward = new Vector2(
                localForward.x,
                localForward.y).normalized;
            return forward.sqrMagnitude > 0.5f;
        }

        private float CalculateTransitionDuration(
            Vector3 start,
            Vector3 control,
            Vector3 end)
        {
            float distance = Vector3.Distance(start, control) +
                             Vector3.Distance(control, end);
            float secondsPerTile = busRoute != null
                ? busRoute.SecondsPerTile
                : movementDuration;
            return Mathf.Max(
                0.01f,
                distance /
                Mathf.Max(0.01f, cityView.TileSize) *
                Mathf.Max(0.01f, secondsPerTile));
        }

        private float GetParkingApproachDistance() =>
            parkingApproachDistance * cityView.TileSize;

        private bool IsRoad(Vector2Int tile) =>
            tileData == null ||
            tileData.GetTileType(tile) == TileType.Road;

        private void ApplyPose(Vector3 position, Vector2 direction)
        {
            visual.localPosition = position;
            if (direction.sqrMagnitude > 0.0001f)
            {
                visual.localRotation = CreateRotation(direction);
            }
        }

        private static Quaternion CreateRotation(Vector2 direction)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) *
                          Mathf.Rad2Deg;
            return Quaternion.Euler(0f, 0f, angle + 90f) *
                   Quaternion.Euler(90f, 0f, 0f);
        }

        private static Vector3 EvaluateQuadraticPoint(
            Vector3 start,
            Vector3 control,
            Vector3 end,
            float progress)
        {
            float t = Mathf.Clamp01(progress);
            float inverse = 1f - t;
            return inverse * inverse * start +
                   2f * inverse * t * control +
                   t * t * end;
        }

        private static Vector3 EvaluateQuadraticTangent(
            Vector3 start,
            Vector3 control,
            Vector3 end,
            float progress)
        {
            float t = Mathf.Clamp01(progress);
            return 2f * (1f - t) * (control - start) +
                   2f * t * (end - control);
        }

        private static Vector3 ToVector3(Vector2 value) =>
            new(value.x, value.y, 0f);

        private void HandleRouteUnavailable()
        {
            offRoadTransitionActive = false;
            segmentElapsed = 0f;
            segmentDuration = 0f;
            ResetStopPresentationTarget();
        }

        private void HandleStopPresentationRequested(
            Vector2Int _,
            int __)
        {
            ResetStopPresentationTarget();
        }

        private void HandleVehicleVisibilityChanged(bool visible)
        {
            UpdateStopPresentationGate();

            if (!visible)
            {
                HideCityBusVisual();
                return;
            }

            RefreshVisiblePresentation();
            LogCityBusPresentationState(
                "service visibility enabled");
        }

        private void RefreshVisiblePresentation()
        {
            if (!IsAgentVisible)
            {
                return;
            }

            ResolveReferences();
            EnsureVisual();
            UpdateStopPresentationGate();
            if (offRoadTransitionActive)
            {
                UpdateOffRoadTransition();
                return;
            }

            UpdateRoadPose();
        }

        private void LogMissingTrafficSnapshot()
        {
            if (snapshotMissingLogged)
            {
                return;
            }

            snapshotMissingLogged = true;
            CityBusVehicleAgent agent =
                GetComponent<CityBusVehicleAgent>();
            if (agent == null)
            {
                return;
            }

            Debug.LogWarning(
                $"[BusWorldView] Route {agent.RouteId} " +
                $"{agent.Direction} has no road traffic snapshot. " +
                $"agentEntity={agent.GetEntityId()}.",
                this);
        }

        private void LogTrafficSnapshotTransition(
            RoadTrafficSnapshot snapshot)
        {
            snapshotMissingLogged = false;
            if (hasObservedTrafficSnapshot &&
                observedSnapshotVisible == snapshot.IsVisible &&
                observedSnapshotState == snapshot.State)
            {
                return;
            }

            hasObservedTrafficSnapshot = true;
            observedSnapshotVisible = snapshot.IsVisible;
            observedSnapshotState = snapshot.State;

            CityBusVehicleAgent agent =
                GetComponent<CityBusVehicleAgent>();
            if (agent == null)
            {
                return;
            }

            Debug.Log(
                $"[BusWorldView] Route {agent.RouteId} " +
                $"{agent.Direction} traffic changed. " +
                $"state={snapshot.State}, " +
                $"snapshotVisible={snapshot.IsVisible}, " +
                $"tile={snapshot.CurrentTile}, " +
                $"routeIndex={snapshot.RouteTileIndex}, " +
                $"visualExists={visual != null}, " +
                $"visualActive=" +
                $"{(visual != null && visual.gameObject.activeSelf)}.",
                this);
        }

        private void LogCityBusPresentationState(string reason)
        {
            CityBusVehicleAgent agent =
                GetComponent<CityBusVehicleAgent>();
            if (agent == null)
            {
                return;
            }

            string trafficState = "snapshot=missing";
            if (busRoute != null &&
                busRoute.TryGetRoadTrafficSnapshot(
                    out RoadTrafficSnapshot snapshot))
            {
                trafficState =
                    $"state={snapshot.State}, " +
                    $"snapshotVisible={snapshot.IsVisible}, " +
                    $"tile={snapshot.CurrentTile}, " +
                    $"routeIndex={snapshot.RouteTileIndex}";
            }

            Debug.Log(
                $"[BusWorldView] Route {agent.RouteId} " +
                $"{agent.Direction} {reason}. " +
                $"agentEntity={agent.GetEntityId()}, " +
                $"agentVisible={IsAgentVisible}, " +
                $"visualExists={visual != null}, " +
                $"visualActiveSelf=" +
                $"{(visual != null && visual.gameObject.activeSelf)}, " +
                $"visualActiveInHierarchy={HasVisibleBus}, " +
                trafficState,
                this);
        }

        private void UpdateStopPresentationGate()
        {
            if (busRoute == null)
            {
                return;
            }

            bool shouldRequireConfirmation =
                isActiveAndEnabled &&
                IsAgentVisible &&
                visual != null &&
                cityView != null &&
                roadTraffic != null &&
                (cityBusService == null ||
                 cityBusService.IsVehicleVisible) &&
                busRoute.UsesRoadTraffic;
            busRoute.RequireStopPresentationConfirmation =
                shouldRequireConfirmation;
        }

        private void DisableStopPresentationGate()
        {
            ResetStopPresentationTarget();

            if (busRoute != null)
            {
                busRoute.RequireStopPresentationConfirmation =
                    false;
            }
        }

        private void HideCityBusVisual()
        {
            if (visual == null)
            {
                return;
            }

            offRoadTransitionActive = false;
            isParkedOffRoad = false;
            hasRoadPose = false;
            observedRoadSegmentVersion = -1;
            segmentElapsed = 0f;
            segmentDuration = 0f;
            hasObservedTrafficSnapshot = false;
            snapshotMissingLogged = false;
            ResetStopPresentationTarget();
            visual.gameObject.SetActive(false);
        }

        private void ResetStopPresentationTarget()
        {
            hasStopPresentationTarget = false;
            stopPresentationDistance = 0f;
            stopPresentationSegmentVersion = -1;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            visualScale = Mathf.Max(0.01f, visualScale);
            movementDuration = Mathf.Max(0.01f, movementDuration);
            schoolParkingSlot = Mathf.Max(0, schoolParkingSlot);
            parkingApproachDistance = Mathf.Max(
                0.1f,
                parkingApproachDistance);
        }
#endif

        // Unity integration: place either bus content Prefab; no extra scene wiring is required.
    }
}
