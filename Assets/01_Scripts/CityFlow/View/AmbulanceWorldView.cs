using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Content.Transit;
using CityFlow.Contracts;
using CityFlow.ViewKit;
using UnityEngine;

namespace CityFlow.View
{
    /// <summary>
    /// Renders an ambulance on MainCityView's shared right-hand traffic
    /// polyline and registers it with the common vehicle spacing system.
    /// </summary>
    public sealed class AmbulanceWorldView :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        [SerializeField]
        private BusRoute route;
        [SerializeField]
        private EmergencyIncidentConfigSO config;
        [SerializeField]
        private MainCityView cityView;

        private readonly List<Vector2Int> routeRoadTiles = new();
        private readonly List<int> routePathToRoadIndex = new();
        private readonly BusRoutePolylineMotion routeMotion = new();
        private readonly BufferedRouteFollower routeFollower = new();

        private IReadOnlyTileData tileData;
        private CityFlowServices services;
        private IRoadTrafficService roadTraffic;
        private Transform visual;
        private VehicleNightLighting nightLighting;
        private RoutePolyline movementPath;
        private float movementStartDistance;
        private float targetMovementDistance;
        private float movementElapsed;
        private float currentMovementDuration;
        private bool hasTarget;
        private bool subscribed;
        private int cachedRoutePathHash = int.MinValue;
        private int cachedRoutePathCount = -1;
        private Vector2 currentVisualDirection = Vector2.right;
        private Vector2Int currentTrafficTile;
        private bool hasCurrentTrafficTile;
        private Vector3 previousVisualPosition;
        private float currentVisualSpeed;
        private bool visualBlockedByTraffic;
        private float nextVehicleSpacingWarningTime;
        private float lastTrafficTickProgress;
        private bool hasTrafficTickProgress;
        private bool isParkedOffRoad;
        private bool parkingTransitionActive;
        private float parkingTransitionElapsed;
        private float parkingTransitionDuration;
        private Vector3 parkingTransitionStartPosition;
        private Vector3 parkingTransitionTargetPosition;
        private Quaternion parkingTransitionStartRotation;
        private Quaternion parkingTransitionTargetRotation;
        private bool parkingTransitionEndsParked;
        private Action parkingCompleted;
        private RoutePolyline parkingTransitionPath;
        private float parkingTransitionStartDistance;
        private float parkingTransitionTargetDistance;
        private bool parkingRequestPending;
        private Vector3 pendingParkingTargetPosition;
        private Quaternion pendingParkingTargetRotation;
        private bool pendingParkingEndsParked;
        private Action pendingParkingCompleted;
        private float nextParkingWaitLogTime;
        private bool hasHospitalParkingPose;
        private Vector2Int hospitalParkingTile;
        private Vector3 hospitalParkingPosition;
        private bool hasIncidentParkingPose;
        private Vector2Int incidentParkingTile;
        private Vector3 incidentParkingPosition;
        private Vector3 departureParkingPosition;
        private bool useDepartureParkingAnchor;

        public bool HasVisibleAmbulance =>
            visual != null &&
            visual.gameObject.activeInHierarchy;

        public void Initialize(CityFlowServices services)
        {
            this.services = services;
            tileData = services?.TileData;
            roadTraffic = services?.RoadTraffic;
            ResolveReferences();
            EnsureVisual();
            nightLighting = VehicleNightLighting.Attach(
                visual != null ? visual.gameObject : null,
                services,
                Vector3.right);
            Subscribe();
        }

        private void Awake()
        {
            ResolveReferences();
            EnsureVisual();
        }

        private void OnEnable()
        {
            ResolveReferences();
            EnsureVisual();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            ClearPendingParkingRequest();
            cityView?.RemoveVehiclePresentation(this);
            cityView?.UnregisterExternalSelectableVehicle(
                this);
        }

        private void OnDestroy()
        {
            Unsubscribe();
            ClearPendingParkingRequest();
            cityView?.RemoveVehiclePresentation(this);
            cityView?.UnregisterExternalSelectableVehicle(
                this);
            DestroyVisual();
        }

        private void Update()
        {
            nightLighting?.SetMoving(
                parkingTransitionActive ||
                (hasTarget &&
                 routeFollower.HasPath &&
                 !isParkedOffRoad));

            if (parkingTransitionActive)
            {
                cityView?.RemoveVehiclePresentation(this);
                UpdateParkingTransition();
                return;
            }

            bool targetExtended = false;
            TrySynchronizeAuthoritativeTrafficTarget(
                out targetExtended);
            if (ObserveTrafficTickEdge() &&
                !targetExtended)
            {
                routeFollower.MarkAuthorityHeld();
            }

            if (!hasTarget ||
                visual == null ||
                !routeFollower.HasPath)
            {
                TryBeginPendingParkingTransition();
                return;
            }

            RoutePolyline path = routeFollower.Path;
            float candidateDistance =
                routeFollower.CalculateCandidateDistance(
                    Time.deltaTime,
                    GetNominalRoadSpeed());
            float currentDistance = routeFollower.CurrentDistance;
            float limitedDistance = candidateDistance;
            VehiclePresentationLeader leader = default;
            if (cityView != null &&
                route != null &&
                route.TryGetRoadTrafficSnapshot(
                    out RoadTrafficSnapshot trafficSnapshot) &&
                trafficSnapshot.IsVisible)
            {
                limitedDistance =
                    cityView.LimitVehiclePresentationAdvance(
                        this,
                        trafficSnapshot.Kind,
                        trafficSnapshot.Footprint,
                        path,
                        currentDistance,
                        candidateDistance,
                        yieldToCrossFlowCars: true,
                        out leader);
            }

            float requestedAdvance =
                Mathf.Max(0f, candidateDistance - currentDistance);
            float allowedAdvance =
                Mathf.Max(0f, limitedDistance - currentDistance);
            float allowedFraction = requestedAdvance > 0.0001f
                ? Mathf.Clamp01(allowedAdvance / requestedAdvance)
                : 1f;

            routeFollower.CommitCandidate(
                candidateDistance,
                allowedFraction);
            visualBlockedByTraffic = allowedFraction < 0.999f;
            if (visualBlockedByTraffic &&
                Time.unscaledTime >= nextVehicleSpacingWarningTime)
            {
                nextVehicleSpacingWarningTime =
                    Time.unscaledTime + 1.5f;
                Debug.Log(
                    $"[VehicleSpacingGuard] Ambulance view held behind {leader.Kind}. " +
                    $"headway={leader.Headway:F3}, required={leader.RequiredHeadway:F3}.",
                    this);
            }

            Sample committedSample =
                path.SampleAt(
                    routeFollower.CurrentDistance);
            ApplySample(committedSample);
            movementElapsed =
                routeFollower.IsAtTarget
                    ? currentMovementDuration
                    : 0f;

            float deltaTime = Mathf.Max(
                Time.deltaTime,
                0.0001f);
            currentVisualSpeed =
                Vector3.Distance(
                    previousVisualPosition,
                    visual.localPosition) /
                deltaTime;
            previousVisualPosition =
                visual.localPosition;
            PublishExternalTraffic();
            TryBeginPendingParkingTransition();
        }

        private void ResolveReferences()
        {
            route ??= GetComponent<BusRoute>();
            config ??=
                GetComponent<AmbulanceVehicleAgent>()
                    ?.Config;
            cityView ??=
                FindAnyObjectByType<MainCityView>();
        }

        private void Subscribe()
        {
            if (subscribed || route == null)
            {
                return;
            }

            route.TileChanged += HandleTileChanged;
            route.RouteUnavailable +=
                HandleRouteUnavailable;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || route == null)
            {
                return;
            }

            route.TileChanged -= HandleTileChanged;
            route.RouteUnavailable -=
                HandleRouteUnavailable;
            subscribed = false;
        }

        private void EnsureVisual()
        {
            if (visual != null)
            {
                cityView?.RegisterExternalSelectableVehicle(
                    this,
                    visual,
                    Vector3.right);
                return;
            }

            if (config?.VehicleVisualPrefab == null ||
                cityView == null)
            {
                return;
            }

            GameObject instance = Instantiate(
                config.VehicleVisualPrefab,
                cityView.transform);
            instance.name = "AmbulanceVisual";
            VehicleVisualUtility.PrepareLit(instance);
            visual = instance.transform;
            visual.localScale =
                CalculateVisualScale(
                    visual,
                    config,
                    cityView.TileSize);
            nightLighting =
                VehicleNightLighting.Attach(
                    instance,
                    services,
                    Vector3.right);
            instance.SetActive(false);
            previousVisualPosition =
                visual.localPosition;
            cityView.RegisterExternalSelectableVehicle(
                this,
                visual,
                Vector3.right);
        }

        internal static Vector3 CalculateVisualScale(
            Transform visualRoot,
            EmergencyIncidentConfigSO visualConfig,
            float tileSize)
        {
            float fallbackScale =
                visualConfig != null
                    ? visualConfig.VisualScale
                    : 1f;
            Vector3 fallback =
                Vector3.one * fallbackScale;
            if (visualRoot == null ||
                visualConfig == null ||
                !TryGetModelBounds(
                    visualRoot,
                    out Bounds modelBounds))
            {
                return fallback;
            }

            float safeTileSize =
                Mathf.Max(0.0001f, tileSize);
            float modelLength =
                Mathf.Max(
                    0.0001f,
                    modelBounds.size.x);
            float modelWidth =
                Mathf.Max(
                    0.0001f,
                    modelBounds.size.y);

            float widthScale =
                visualConfig.VehicleWidthTiles *
                safeTileSize / modelWidth;
            float lengthScale =
                visualConfig.VehicleLengthTiles *
                safeTileSize / modelLength;

            return new Vector3(
                lengthScale,
                widthScale,
                Mathf.Min(widthScale, lengthScale));
        }

        private static bool TryGetModelBounds(
            Transform visualRoot,
            out Bounds modelBounds)
        {
            modelBounds = default;
            Renderer[] renderers =
                visualRoot.GetComponentsInChildren<
                    Renderer>(true);
            bool hasBounds = false;

            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                Renderer renderer =
                    renderers[rendererIndex];
                Bounds bounds = renderer.localBounds;
                Vector3 min = bounds.min;
                Vector3 max = bounds.max;

                for (int corner = 0;
                     corner < 8;
                     corner++)
                {
                    Vector3 localCorner = new(
                        (corner & 1) == 0
                            ? min.x
                            : max.x,
                        (corner & 2) == 0
                            ? min.y
                            : max.y,
                        (corner & 4) == 0
                            ? min.z
                            : max.z);
                    Vector3 rootCorner =
                        visualRoot.InverseTransformPoint(
                            renderer.transform
                                .TransformPoint(
                                    localCorner));

                    if (!hasBounds)
                    {
                        modelBounds =
                            new Bounds(
                                rootCorner,
                                Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        modelBounds.Encapsulate(
                            rootCorner);
                    }
                }
            }

            return hasBounds &&
                   modelBounds.size.x > 0.0001f &&
                   modelBounds.size.y > 0.0001f;
        }

        public void ShowParkedAtHospital(
            Vector2Int hospital,
            int parkingSlot,
            bool immediate,
            Action onParked = null)
        {
            ShowParkedAtBuilding(
                hospital,
                parkingSlot,
                immediate,
                isHospital: true,
                onParked: onParked);
        }

        public void PrepareIncidentParking(
            Vector2Int incidentTile,
            int parkingSlot)
        {
            if (!TryResolveBuildingParkingPose(
                    incidentTile,
                    parkingSlot,
                    out Vector3 position,
                    out _))
            {
                return;
            }

            hasIncidentParkingPose = true;
            incidentParkingTile = incidentTile;
            incidentParkingPosition = position;
        }

        public void PrepareRoadsideIncidentStop()
        {
            hasIncidentParkingPose = false;
        }

        public void PrepareRoadsideDeparture()
        {
            hasIncidentParkingPose = false;
            useDepartureParkingAnchor = false;
            isParkedOffRoad = false;
        }

        public void ShowParkedAtIncident(
            Vector2Int incidentTile,
            int parkingSlot,
            bool immediate,
            Action onParked = null)
        {
            ShowParkedAtBuilding(
                incidentTile,
                parkingSlot,
                immediate,
                isHospital: false,
                onParked: onParked);
        }

        private void ShowParkedAtBuilding(
            Vector2Int building,
            int parkingSlot,
            bool immediate,
            bool isHospital,
            Action onParked)
        {
            EnsureVisual();

            if (visual == null || cityView == null)
            {
                onParked?.Invoke();
                return;
            }

            if (!TryResolveBuildingParkingPose(
                    building,
                    parkingSlot,
                    out Vector3 targetPosition,
                    out Vector3 targetForward))
            {
                onParked?.Invoke();
                return;
            }

            if (isHospital)
            {
                hasHospitalParkingPose = true;
                hospitalParkingTile = building;
                hospitalParkingPosition = targetPosition;
            }
            else
            {
                hasIncidentParkingPose = true;
                incidentParkingTile = building;
                incidentParkingPosition = targetPosition;
            }

            departureParkingPosition = targetPosition;
            useDepartureParkingAnchor = true;
            Quaternion targetRotation =
                CreateRotation(
                    new Vector2(
                        targetForward.x,
                        targetForward.y));

            visual.gameObject.SetActive(true);

            if (immediate ||
                !visual.gameObject.activeInHierarchy)
            {
                ClearPendingParkingRequest();
                routeFollower.Reset();
                movementPath = null;
                hasTarget = false;
                hasCurrentTrafficTile = false;
                visualBlockedByTraffic = false;
                currentVisualSpeed = 0f;
                hasTrafficTickProgress = false;
                lastTrafficTickProgress = 0f;
                parkingTransitionActive = false;
                parkingCompleted = null;
                visual.localPosition = targetPosition;
                visual.localRotation = targetRotation;
                previousVisualPosition =
                    visual.localPosition;
                isParkedOffRoad = true;
                cityView?.RemoveVehiclePresentation(this);
                onParked?.Invoke();
                return;
            }

            parkingRequestPending = true;
            pendingParkingTargetPosition = targetPosition;
            pendingParkingTargetRotation = targetRotation;
            pendingParkingEndsParked = true;
            pendingParkingCompleted = onParked;
            TryBeginPendingParkingTransition();
        }

        private void TryBeginPendingParkingTransition()
        {
            if (!parkingRequestPending ||
                parkingTransitionActive ||
                visual == null)
            {
                return;
            }

            if (routeFollower.HasPath &&
                !routeFollower.IsAtTarget)
            {
                if (Time.unscaledTime >= nextParkingWaitLogTime)
                {
                    nextParkingWaitLogTime =
                        Time.unscaledTime + 1.5f;
                    Debug.Log(
                        "[VehicleTransitionGate] Ambulance parking waits for " +
                        $"its road presentation. current=" +
                        $"{routeFollower.CurrentDistance:F3}, target=" +
                        $"{routeFollower.TargetDistance:F3}.",
                        this);
                }

                return;
            }

            RoutePolyline arrivalPath = movementPath;
            float arrivalDistance = routeFollower.HasPath
                ? routeFollower.CurrentDistance
                : 0f;
            Vector3 targetPosition =
                pendingParkingTargetPosition;
            Quaternion targetRotation =
                pendingParkingTargetRotation;
            bool endsParked = pendingParkingEndsParked;
            Action onCompleted = pendingParkingCompleted;
            ClearPendingParkingRequest();

            routeFollower.Reset();
            movementPath = null;
            hasTarget = false;
            hasCurrentTrafficTile = false;
            visualBlockedByTraffic = false;
            currentVisualSpeed = 0f;
            hasTrafficTickProgress = false;
            lastTrafficTickProgress = 0f;
            BeginParkingTransition(
                targetPosition,
                targetRotation,
                endsParked,
                onCompleted,
                arrivalPath: arrivalPath,
                arrivalDistance: arrivalDistance);
        }

        private void ClearPendingParkingRequest()
        {
            parkingRequestPending = false;
            pendingParkingTargetPosition = default;
            pendingParkingTargetRotation = default;
            pendingParkingEndsParked = false;
            pendingParkingCompleted = null;
        }

        private void BeginParkingTransition(
            Vector3 targetPosition,
            Quaternion targetRotation,
            bool endsParked,
            Action onCompleted,
            RoutePolyline arrivalPath,
            float arrivalDistance)
        {
            parkingTransitionStartPosition =
                visual.localPosition;
            parkingTransitionTargetPosition =
                targetPosition;
            parkingTransitionStartRotation =
                visual.localRotation;
            parkingTransitionTargetRotation =
                targetRotation;
            parkingTransitionElapsed = 0f;
            parkingTransitionDuration =
                CalculateParkingTransitionDuration(
                    Vector3.Distance(
                        parkingTransitionStartPosition,
                        parkingTransitionTargetPosition),
                    GetNominalRoadSpeed());
            parkingTransitionEndsParked =
                endsParked;
            parkingCompleted = onCompleted;
            parkingTransitionPath = null;
            if (endsParked &&
                TryBakeBuildingParkingPath(
                    targetPosition,
                    arrivalPath,
                    arrivalDistance,
                    out RoutePolyline parkingPath,
                    out float startDistance))
            {
                parkingTransitionPath = parkingPath;
                parkingTransitionStartDistance =
                    startDistance;
                parkingTransitionTargetDistance =
                    parkingPath.Length;
                parkingTransitionDuration =
                    Mathf.Max(
                        0.1f,
                        (parkingTransitionTargetDistance -
                         parkingTransitionStartDistance) /
                        GetNominalRoadSpeed());
            }
            parkingTransitionActive = true;
            isParkedOffRoad = false;
            nightLighting?.SetMoving(true);
        }

        private void UpdateParkingTransition()
        {
            if (visual == null)
            {
                CompleteParkingTransition();
                return;
            }

            parkingTransitionElapsed =
                Mathf.Min(
                    parkingTransitionElapsed +
                    Mathf.Max(0f, Time.deltaTime),
                    parkingTransitionDuration);
            float progress = Mathf.Clamp01(
                parkingTransitionElapsed /
                Mathf.Max(
                    0.01f,
                    parkingTransitionDuration));
            float eased =
                progress * progress *
                (3f - 2f * progress);

            if (parkingTransitionPath != null)
            {
                Sample sample =
                    parkingTransitionPath.SampleAt(
                        Mathf.Lerp(
                            parkingTransitionStartDistance,
                            parkingTransitionTargetDistance,
                            eased));
                visual.localPosition = sample.Pos;
                visual.localRotation =
                    CreateRotation(
                        new Vector2(
                            sample.Dir.x,
                            sample.Dir.y));
            }
            else
            {
                visual.localPosition =
                    Vector3.Lerp(
                        parkingTransitionStartPosition,
                        parkingTransitionTargetPosition,
                        eased);
                visual.localRotation =
                    Quaternion.Slerp(
                        parkingTransitionStartRotation,
                        parkingTransitionTargetRotation,
                        eased);
            }

            if (progress >= 1f - 0.0001f)
            {
                CompleteParkingTransition();
            }
        }

        private void CompleteParkingTransition()
        {
            parkingTransitionActive = false;
            parkingTransitionPath = null;

            if (visual != null)
            {
                visual.localPosition =
                    parkingTransitionTargetPosition;
                visual.localRotation =
                    parkingTransitionTargetRotation;
                previousVisualPosition =
                    visual.localPosition;
            }

            isParkedOffRoad =
                parkingTransitionEndsParked;
            if (isParkedOffRoad)
            {
                cityView?.RemoveVehiclePresentation(this);
            }

            nightLighting?.SetMoving(
                !parkingTransitionEndsParked);
            Action callback = parkingCompleted;
            parkingCompleted = null;
            callback?.Invoke();
        }

        private bool TryBakeBuildingParkingPath(
            Vector3 parkingPosition,
            RoutePolyline arrivalPath,
            float arrivalDistance,
            out RoutePolyline parkingPath,
            out float startDistance)
        {
            parkingPath = null;
            startDistance = 0f;
            if (tileData == null ||
                cityView == null)
            {
                return false;
            }

            if (arrivalPath != null &&
                arrivalPath.TileCount > 0)
            {
                Sample endSample =
                    arrivalPath.SampleAt(
                        arrivalPath.Length);
                float parkingMatchTolerance =
                    Mathf.Max(
                        0.01f,
                        cityView.TileSize * 0.08f);
                if (Vector3.Distance(
                        endSample.Pos,
                        parkingPosition) <=
                    parkingMatchTolerance)
                {
                    parkingPath = arrivalPath;
                    startDistance = Mathf.Clamp(
                        arrivalDistance,
                        0f,
                        parkingPath.Length);
                    return parkingPath.Length >
                           startDistance + 0.0001f;
                }
            }

            if (route == null)
            {
                return false;
            }

            IReadOnlyList<Vector2Int> sourcePath =
                route.CurrentRoadPath;
            if (sourcePath == null ||
                sourcePath.Count == 0)
            {
                return false;
            }

            var roadTiles =
                new List<Vector2Int>(
                    sourcePath.Count);
            for (int i = 0; i < sourcePath.Count; i++)
            {
                Vector2Int tile = sourcePath[i];
                if (tileData.GetTileType(tile) ==
                    TileType.Road)
                {
                    roadTiles.Add(tile);
                }
            }

            if (roadTiles.Count == 0 ||
                !IsContinuousRoadTileSequence(roadTiles))
            {
                return false;
            }

            parkingPath = cityView.BakeTrafficRoute(
                roadTiles,
                GetVisualSurfaceDepth(),
                null,
                parkingPosition,
                clampAnchorSpurOvershoot: true);
            if (parkingPath == null)
            {
                return false;
            }

            startDistance =
                parkingPath.DistanceAtTile(
                    parkingPath.TileCount - 1);
            return parkingPath.Length >
                   startDistance + 0.0001f;
        }

        public static float CalculateParkingTransitionDuration(
            float remainingDistance,
            float nominalSpeed)
        {
            return Mathf.Max(
                0.1f,
                Mathf.Max(0f, remainingDistance) /
                Mathf.Max(0.01f, nominalSpeed));
        }

        private bool TryResolveBuildingParkingPose(
            Vector2Int building,
            int parkingSlot,
            out Vector3 position,
            out Vector3 forward)
        {
            position = default;
            forward = default;
            if (cityView == null)
            {
                return false;
            }

            if (cityView.TryGetBuildingParkingPose(
                    building,
                    parkingSlot,
                    out position,
                    out forward))
            {
                position.z = GetVisualSurfaceDepth();
                return true;
            }

            float depth = GetVisualSurfaceDepth();
            Vector3 buildingCenter =
                cityView.GridToLocal(building, depth);
            if (route != null &&
                route.TryGetAccessRoadForStop(
                    building,
                    out Vector2Int accessRoad))
            {
                Vector3 roadCenter =
                    cityView.GridToLocal(
                        accessRoad,
                        depth);
                forward =
                    roadCenter - buildingCenter;
                forward.z = 0f;
                if (forward.sqrMagnitude > 0.0001f)
                {
                    forward.Normalize();
                    Vector3 side =
                        new(
                            forward.y,
                            -forward.x,
                            0f);
                    Vector2 slotOffset =
                        PolylineMath.ParkingSlotOffset(
                            parkingSlot,
                            1,
                            0.32f);
                    position =
                        buildingCenter +
                        forward *
                        (cityView.TileSize *
                         slotOffset.x) +
                        side *
                        (cityView.TileSize *
                         slotOffset.y);
                    return true;
                }
            }

            position = buildingCenter;
            forward = Vector3.up;
            return true;
        }

        private void DestroyVisual()
        {
            if (visual == null)
            {
                return;
            }

            cityView?.UnregisterExternalSelectableVehicle(
                this);
            cityView?.RemoveVehiclePresentation(this);
            GameObject visualObject = visual.gameObject;
            visual = null;

            if (Application.isPlaying)
            {
                Destroy(visualObject);
            }
            else
            {
                DestroyImmediate(visualObject);
            }
        }

        private void HandleTileChanged(Vector2Int tile)
        {
            EnsureVisual();

            if (visual == null ||
                cityView == null ||
                !IsRoad(tile))
            {
                return;
            }

            if (route != null &&
                route.UsesRoadTraffic &&
                TrySynchronizeAuthoritativeTrafficTarget(
                    out _))
            {
                return;
            }

            GetRouteAnchors(
                out Vector3? startAnchor,
                out Vector3? endAnchor);
            if (!routeMotion.TryRefresh(
                    route,
                    tileData,
                    cityView,
                    GetVisualSurfaceDepth(),
                    startAnchor,
                    endAnchor,
                    out int roadIndex))
            {
                return;
            }

            movementPath = routeMotion.Polyline;
            float nextDistance =
                movementPath.DistanceAtTile(roadIndex);
            Sample nextSample =
                movementPath.SampleAt(nextDistance);

            if (isParkedOffRoad)
            {
                routeFollower.SetTarget(
                    movementPath,
                    0f,
                    nextDistance,
                    snapToTarget: false);
                currentMovementDuration =
                    GetMovementDuration();
                visual.gameObject.SetActive(true);
                hasTarget = true;
                isParkedOffRoad = false;
                visualBlockedByTraffic = false;
                currentVisualSpeed = 0f;
                ApplySample(
                    movementPath.SampleAt(0f));
                previousVisualPosition =
                    visual.localPosition;
                return;
            }

            float startDistance =
                movementPath.DistanceAtTile(
                    Mathf.Max(0, roadIndex - 1));
            routeFollower.SetTarget(
                movementPath,
                startDistance,
                nextDistance,
                snapToTarget: !hasTarget);

            movementStartDistance = startDistance;
            targetMovementDistance = nextDistance;
            currentMovementDuration =
                GetMovementDuration();
            movementElapsed = 0f;
            hasTarget = true;
            visualBlockedByTraffic = false;
            visual.gameObject.SetActive(true);
            previousVisualPosition =
                visual.localPosition;
            PublishExternalTraffic();
        }

        private bool TrySynchronizeAuthoritativeTrafficTarget(
            out bool targetExtended)
        {
            targetExtended = false;
            if (route == null ||
                !route.UsesRoadTraffic ||
                cityView == null ||
                visual == null ||
                !route.TryGetRoadTrafficSnapshot(
                    out RoadTrafficSnapshot snapshot) ||
                !snapshot.IsVisible ||
                snapshot.State ==
                    RoadTrafficAgentState.RouteUnavailable ||
                !TryRefreshRouteMotion(
                    out _))
            {
                return false;
            }

            movementPath = routeMotion.Polyline;
            if (movementPath == null ||
                movementPath.TileCount == 0)
            {
                return false;
            }

            int roadIndex = Mathf.Clamp(
                snapshot.RouteTileIndex,
                0,
                movementPath.TileCount - 1);
            Vector2Int roadTile =
                movementPath.TileAt(roadIndex);
            float headInset =
                cityView.IsSharedIntersectionTile(roadTile)
                    ? cityView.IntersectionQueueInsetTiles *
                      cityView.TileSize
                    : 0f;
            float targetDistance =
                movementPath.ReprojectDistance(
                    roadIndex,
                    snapshot.QueueOffsetTiles *
                    cityView.TileSize,
                    headInset,
                    snapshot.IntersectionProgress01,
                    snapshot.LinkProgress01,
                    snapshot.RoundaboutProgress01,
                    cityView.RoundaboutTransitionSpanTiles);
            float startDistance =
                movementPath.DistanceAtTile(
                    Mathf.Max(0, roadIndex - 1));
            Sample targetSample =
                movementPath.SampleAt(targetDistance);

            if (isParkedOffRoad)
            {
                routeFollower.SetAuthorizedTarget(
                    movementPath,
                    0f,
                    targetDistance,
                    snapToTarget: false);
                currentMovementDuration =
                    GetMovementDuration();
                visual.gameObject.SetActive(true);
                hasTarget = true;
                isParkedOffRoad = false;
                visualBlockedByTraffic = false;
                currentVisualSpeed = 0f;
                ApplySample(
                    movementPath.SampleAt(0f));
                previousVisualPosition =
                    visual.localPosition;
                targetExtended = true;
                return true;
            }

            targetExtended =
                routeFollower.SetAuthorizedTarget(
                    movementPath,
                    startDistance,
                    targetDistance,
                    snapToTarget: !hasTarget);

            movementStartDistance = startDistance;
            targetMovementDistance = targetDistance;
            currentMovementDuration =
                GetMovementDuration();
            movementElapsed = 0f;
            hasTarget = true;
            visual.gameObject.SetActive(true);

            if (!targetExtended)
            {
                return true;
            }

            previousVisualPosition =
                visual.localPosition;
            PublishExternalTraffic();
            return true;
        }

        private bool TryRefreshRouteMotion(
            out int roadIndex)
        {
            GetRouteAnchors(
                out Vector3? startAnchor,
                out Vector3? endAnchor);
            return routeMotion.TryRefresh(
                route,
                tileData,
                cityView,
                GetVisualSurfaceDepth(),
                startAnchor,
                endAnchor,
                out roadIndex);
        }

        private void GetRouteAnchors(
            out Vector3? startAnchor,
            out Vector3? endAnchor)
        {
            startAnchor = null;
            endAnchor = null;
            if (route?.CurrentRoadPath == null ||
                route.CurrentRoadPath.Count == 0)
            {
                return;
            }

            IReadOnlyList<Vector2Int> sourcePath =
                route.CurrentRoadPath;
            if (useDepartureParkingAnchor)
            {
                startAnchor = departureParkingPosition;
            }

            Vector2Int destination =
                sourcePath[sourcePath.Count - 1];
            if (hasHospitalParkingPose &&
                destination == hospitalParkingTile)
            {
                endAnchor = hospitalParkingPosition;
            }
            else if (hasIncidentParkingPose &&
                     destination == incidentParkingTile)
            {
                endAnchor = incidentParkingPosition;
            }
        }

        private bool ObserveTrafficTickEdge()
        {
            if (cityView == null ||
                route == null ||
                !route.UsesRoadTraffic ||
                roadTraffic == null)
            {
                hasTrafficTickProgress = false;
                return false;
            }

            float progress =
                roadTraffic.StepProgress01;
            bool tickEdge =
                hasTrafficTickProgress &&
                progress <
                lastTrafficTickProgress - 0.0001f;
            lastTrafficTickProgress = progress;
            hasTrafficTickProgress = true;
            return tickEdge;
        }

        private bool TryRefreshTrafficPath(
            out int currentRoadIndex)
        {
            currentRoadIndex = -1;
            if (route == null || cityView == null)
            {
                return false;
            }

            IReadOnlyList<Vector2Int> sourcePath =
                route.CurrentRoadPath;
            if (sourcePath == null ||
                sourcePath.Count == 0)
            {
                return false;
            }

            int pathHash = ComputePathHash(sourcePath);
            if (movementPath == null ||
                cachedRoutePathCount != sourcePath.Count ||
                cachedRoutePathHash != pathHash)
            {
                routeRoadTiles.Clear();
                routePathToRoadIndex.Clear();

                for (int i = 0;
                     i < sourcePath.Count;
                     i++)
                {
                    Vector2Int pathTile = sourcePath[i];
                    if (IsRoad(pathTile))
                    {
                        routePathToRoadIndex.Add(
                            routeRoadTiles.Count);
                        routeRoadTiles.Add(pathTile);
                    }
                    else
                    {
                        routePathToRoadIndex.Add(-1);
                    }
                }

                if (!IsContinuousRoadTileSequence(
                        routeRoadTiles))
                {
                    return false;
                }

                RoutePolyline refreshedPath =
                    cityView.BakeTrafficRoute(
                        routeRoadTiles,
                        GetVisualSurfaceDepth());
                if (refreshedPath == null)
                {
                    return false;
                }

                movementPath = refreshedPath;
                cachedRoutePathHash = pathHash;
                cachedRoutePathCount =
                    sourcePath.Count;
            }

            int routePathIndex =
                route.CurrentRoadPathIndex;
            if (movementPath == null ||
                routePathIndex < 0 ||
                routePathIndex >=
                routePathToRoadIndex.Count)
            {
                return false;
            }

            currentRoadIndex =
                routePathToRoadIndex[routePathIndex];
            return currentRoadIndex >= 0 &&
                   currentRoadIndex <
                   movementPath.TileCount;
        }

        private bool TryGetNextTrafficSample(
            out Sample sample)
        {
            sample = default;
            if (!TryRefreshRouteMotion(
                    out int currentRoadIndex))
            {
                return false;
            }

            RoutePolyline path =
                routeMotion.Polyline;
            int nextRoadIndex =
                currentRoadIndex + 1;
            if (path == null ||
                nextRoadIndex < 0 ||
                nextRoadIndex >= path.TileCount)
            {
                return false;
            }

            sample = path.SampleAt(
                path.DistanceAtTile(
                    nextRoadIndex));
            return true;
        }

        private bool CanEnterTile(
            Vector2Int currentTile,
            Vector2Int nextTile)
        {
            return !parkingTransitionActive;
        }

        private float GetMovementDuration()
        {
            return Mathf.Max(
                0.01f,
                route != null
                    ? route.SecondsPerTile
                    : config?.TravelSecondsPerTile ??
                      0.45f);
        }

        private float GetNominalRoadSpeed()
        {
            return Mathf.Max(
                0.01f,
                cityView.TileSize /
                GetMovementDuration());
        }

        private float GetVisualSurfaceDepth()
        {
            return ResolveVisualSurfaceDepth(
                cityView,
                config);
        }

        internal static float ResolveVisualSurfaceDepth(
            MainCityView targetCityView,
            EmergencyIncidentConfigSO visualConfig)
        {
            return targetCityView != null
                ? targetCityView.VehicleGroundZ
                : visualConfig?.VisualDepth ?? -0.38f;
        }

        private void CompleteCurrentMovement()
        {
            if (!hasTarget ||
                visual == null ||
                movementPath == null)
            {
                return;
            }

            ApplySample(
                movementPath.SampleAt(
                    targetMovementDistance));
            movementElapsed =
                Mathf.Max(
                    0.01f,
                    currentMovementDuration);
            visualBlockedByTraffic = false;
        }

        private void ApplySample(Sample sample)
        {
            if (visual == null || movementPath == null)
            {
                return;
            }

            Vector3 direction3 =
                sample.Dir.sqrMagnitude >
                0.0001f
                    ? sample.Dir.normalized
                    : Vector3.right;
            Vector2 direction =
                new(
                    direction3.x,
                    direction3.y);

            visual.localPosition = sample.Pos;
            visual.localRotation =
                CreateRotation(direction);
            currentVisualDirection = direction;

            int trafficTileIndex =
                sample.TileIndex;
            if (sample.SegT >= 0.5f)
            {
                trafficTileIndex++;
            }

            trafficTileIndex = Mathf.Clamp(
                trafficTileIndex,
                0,
                movementPath.TileCount - 1);
            currentTrafficTile =
                movementPath.TileAt(
                    trafficTileIndex);
            hasCurrentTrafficTile = !sample.IsSpur;
        }

        private void EvaluateMovementPose(
            float progress,
            out Vector3 position,
            out Quaternion rotation,
            out Vector2 direction,
            out Vector2Int trafficTile)
        {
            float distance = Mathf.Lerp(
                movementStartDistance,
                targetMovementDistance,
                Mathf.Clamp01(progress));
            Sample sample =
                movementPath.SampleAt(distance);
            Vector3 direction3 =
                sample.Dir.sqrMagnitude >
                0.0001f
                    ? sample.Dir.normalized
                    : Vector3.right;

            position = sample.Pos;
            direction =
                new Vector2(
                    direction3.x,
                    direction3.y);
            rotation = CreateRotation(direction);

            int trafficTileIndex =
                sample.TileIndex;
            if (sample.SegT >= 0.5f)
            {
                trafficTileIndex++;
            }

            trafficTileIndex = Mathf.Clamp(
                trafficTileIndex,
                0,
                movementPath.TileCount - 1);
            trafficTile =
                movementPath.TileAt(
                    trafficTileIndex);
        }

        private void PublishExternalTraffic()
        {
            if (cityView == null ||
                visual == null ||
                !visual.gameObject.activeInHierarchy ||
                isParkedOffRoad ||
                parkingTransitionActive ||
                route == null ||
                !route.TryGetRoadTrafficSnapshot(
                    out RoadTrafficSnapshot snapshot) ||
                !snapshot.IsVisible ||
                snapshot.State ==
                    RoadTrafficAgentState.RouteUnavailable)
            {
                cityView?.RemoveVehiclePresentation(this);
                return;
            }

            cityView.PublishVehiclePresentation(
                this,
                snapshot.Kind,
                snapshot.Footprint,
                visual.localPosition,
                new Vector3(
                    currentVisualDirection.x,
                    currentVisualDirection.y,
                    0f),
                currentVisualSpeed);
        }

        private bool IsRoad(Vector2Int tile)
        {
            return tileData == null ||
                   tileData.GetTileType(tile) ==
                   TileType.Road;
        }

        private bool HasContinuousTrafficPath(
            IReadOnlyList<Vector2Int> sourcePath)
        {
            if (sourcePath == null)
            {
                return false;
            }

            bool foundRoad = false;
            Vector2Int previousRoad = default;

            for (int i = 0; i < sourcePath.Count; i++)
            {
                Vector2Int tile = sourcePath[i];
                if (!IsRoad(tile))
                {
                    continue;
                }

                if (foundRoad &&
                    ManhattanDistance(
                        previousRoad,
                        tile) != 1)
                {
                    return false;
                }

                previousRoad = tile;
                foundRoad = true;
            }

            return foundRoad;
        }

        internal static bool
            IsContinuousRoadTileSequence(
                IReadOnlyList<Vector2Int> roadTiles)
        {
            if (roadTiles == null ||
                roadTiles.Count == 0)
            {
                return false;
            }

            for (int i = 1;
                 i < roadTiles.Count;
                 i++)
            {
                if (ManhattanDistance(
                        roadTiles[i - 1],
                        roadTiles[i]) != 1)
                {
                    return false;
                }
            }

            return true;
        }

        private static int ManhattanDistance(
            Vector2Int first,
            Vector2Int second)
        {
            Vector2Int delta = second - first;
            return Mathf.Abs(delta.x) +
                   Mathf.Abs(delta.y);
        }

        private static int ComputePathHash(
            IReadOnlyList<Vector2Int> path)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < path.Count; i++)
                {
                    hash =
                        hash * 31 +
                        path[i].GetHashCode();
                }

                return hash;
            }
        }

        internal static Quaternion CreateRotation(
            Vector2 direction)
        {
            float angle =
                Mathf.Atan2(
                    direction.y,
                    direction.x) *
                Mathf.Rad2Deg;
            return Quaternion.Euler(
                0f,
                0f,
                angle);
        }

        private void HandleRouteUnavailable()
        {
            visualBlockedByTraffic = false;
            currentVisualSpeed = 0f;
            movementElapsed =
                Mathf.Max(
                    movementElapsed,
                    currentMovementDuration);

            if (visual != null &&
                hasCurrentTrafficTile)
            {
                visual.gameObject.SetActive(true);
                previousVisualPosition =
                    visual.localPosition;
            }

            PublishExternalTraffic();
        }
    }
}
