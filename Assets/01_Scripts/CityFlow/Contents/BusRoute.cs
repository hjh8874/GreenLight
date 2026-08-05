using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Content.Transit
{
    public enum BusRouteState
    {
        Idle = 0,
        Moving = 1,
        WaitingAtStop = 2,
        Completed = 3,
        RouteUnavailable = 4,
        WaitingForRoadEntry = 5,
        EnteringRoad = 6,
        LeavingRoad = 7
    }

    /// <summary>
    /// 정류장 사이 도로 경로를 계산하고 버스의 가상 위치를 갱신합니다.
    ///
    /// 일반 버스는 LoopRoute를 활성화하여
    /// 마지막 정류장 다음에 첫 정류장으로 돌아갑니다.
    ///
    /// 실제 버스 프리팹은 CurrentTile과
    /// CurrentRoadPathIndex를 읽어 View에서 표시할 수 있습니다.
    /// </summary>
    public sealed class BusRoute :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        private static readonly Vector2Int[] Directions =
        {
            Vector2Int.up,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.left
        };
        private static readonly Vector2Int InvalidAccessRoad =
            new(int.MinValue, int.MinValue);

        [Header("그리드 크기")]
        [Min(1)]
        [SerializeField]
        private int gridWidth = GridUtil.DefaultWidth;

        [Min(1)]
        [SerializeField]
        private int gridHeight = GridUtil.DefaultHeight;

        [Header("운행 설정")]
        [Min(0.01f)]
        [Tooltip("도로 타일 한 칸을 이동하는 데 필요한 시간입니다.")]
        [SerializeField]
        private float secondsPerTile = 0.2f;

        [Min(0f)]
        [Tooltip("각 정류장에서 기다리는 시간입니다.")]
        [SerializeField]
        private float stopWaitSeconds = 2f;

        [Tooltip("마지막 정류장 도착 후 첫 정류장으로 돌아갑니다.")]
        [SerializeField]
        private bool loopRoute = true;

        [Tooltip("컴포넌트 초기화 후 자동 운행합니다.")]
        [SerializeField]
        private bool autoStart;

        [Tooltip("정류장 출발 시 방금 진입한 도로로 되돌아가는 즉시 유턴을 방지합니다.")]
        [SerializeField]
        private bool avoidImmediateUTurn;

        private readonly List<Vector2Int> stops = new();
        private readonly List<Vector2Int> stopAccessRoads = new();
        private readonly List<Vector2Int> currentRoadPath = new();
        private readonly List<Vector2Int> roadTrafficPath = new();
        private readonly List<Vector2Int> candidateRoadPath = new();
        private readonly List<Vector2Int> validationRoadPath = new();
        private readonly List<Vector2Int> validationLoopRoadPath = new();
        private readonly List<Vector2Int> validationStartRoads = new();
        private readonly List<Vector2Int> validationRemainingStops = new();
        private readonly List<Vector2Int> validationOrderedStops = new();
        private readonly List<Vector2Int> validationBestOrderedStops = new();
        private readonly List<Vector2Int> validationSelectedRoadPath = new();

        private readonly Queue<Vector2Int> searchQueue = new();
        private readonly Dictionary<Vector2Int, Vector2Int> cameFrom = new();
        private readonly HashSet<Vector2Int> visited = new();

        private CityFlowServices services;
        private IReadOnlyTileData tileData;
        private IWorldGridAccess worldGridAccess;
        private IRoadTrafficService roadTraffic;
        private RoadTrafficAgentRegistration roadTrafficRegistration;
        private RoadTrafficAgentId roadTrafficAgentId;

        private int currentStopIndex;
        private int currentRoadPathIndex;

        private float moveTimer;
        private float waitTimer;
        private bool requireStopPresentationConfirmation;
        private bool stopPresentationPending;

        private bool isInitialized;
        private bool routeRequested;
        private Vector2Int departureStop;
        private bool hasDepartureStop;
        private Vector2Int forbiddenDepartureTile;
        private bool hasForbiddenDepartureTile;
        private Vector2Int currentStopAccessRoad;
        private bool hasCurrentStopAccessRoad;
        private bool useRoadsideStopApproach;
        private bool roadsideStopsUsePairedPlatforms;
        private bool useOppositePairedPlatformDirection;
        private bool currentSegmentUsesRoadsideStop;
        private bool roadTrafficConfigured;
        private bool roadTrafficRecoverySubscribed;
        private bool holdRoadTrafficAtDestination;
        private bool roadTrafficArrivalHandled;
        private int roadTrafficPathOffset;
        private int pendingOffRoadStopIndex = -1;
        private int roadsideStopSetbackTiles;
        private Vector2Int preferredInitialAccessRoad;
        private bool hasPreferredInitialAccessRoad;

        public IReadOnlyList<Vector2Int> Stops => stops;
        public IReadOnlyList<Vector2Int> CurrentRoadPath => currentRoadPath;

        public BusRouteState State { get; private set; } =
            BusRouteState.Idle;

        public Vector2Int CurrentTile { get; private set; }
        public Vector2Int CurrentStop => GetCurrentStop();
        public Vector2Int NextStop => GetNextStop();

        public int CurrentStopIndex => currentStopIndex;
        public int CurrentRoadPathIndex => currentRoadPathIndex;
        public int RoadSegmentVersion { get; private set; }
        public bool UsesRoadTraffic =>
            roadTraffic != null && roadTrafficAgentId.IsValid;

        public bool SynchronizeOffRoadTransitions { get; private set; }

        public bool LoopRoute
        {
            get => loopRoute;
            set => loopRoute = value;
        }

        public float SecondsPerTile
        {
            get => secondsPerTile;
            set => secondsPerTile = Mathf.Max(0.01f, value);
        }

        public float StopWaitSeconds
        {
            get => stopWaitSeconds;
            set => stopWaitSeconds = Mathf.Max(0f, value);
        }

        public bool RequireStopPresentationConfirmation
        {
            get => requireStopPresentationConfirmation;
            set
            {
                requireStopPresentationConfirmation = value;
                if (!value && stopPresentationPending)
                {
                    BeginStopWait();
                }
            }
        }

        public bool IsStopPresentationPending =>
            stopPresentationPending;

        public bool UseRoadsideStopApproach
        {
            get => useRoadsideStopApproach;
            set => useRoadsideStopApproach = value;
        }

        public int RoadsideStopSetbackTiles
        {
            get => roadsideStopSetbackTiles;
            set => roadsideStopSetbackTiles =
                Mathf.Max(0, value);
        }

        public bool RoadsideStopsUsePairedPlatforms
        {
            get => roadsideStopsUsePairedPlatforms;
            set => roadsideStopsUsePairedPlatforms = value;
        }

        public bool UseOppositePairedPlatformDirection
        {
            get => useOppositePairedPlatformDirection;
            set => useOppositePairedPlatformDirection = value;
        }

        public bool AllowUnscheduledStopArrival
        {
            get;
            set;
        } = true;

        public bool AvoidImmediateUTurn
        {
            get => avoidImmediateUTurn;
            set => avoidImmediateUTurn = value;
        }

        public Func<Vector2Int, bool> RoadsideStopFilter
        {
            get;
            set;
        }

        /// <summary>
        /// 외부 교통 표시가 다음 타일 진입 가능 여부를 판단할 때 사용합니다.
        /// 등록되지 않으면 기존과 동일하게 즉시 진입합니다.
        /// </summary>
        public Func<Vector2Int, Vector2Int, bool> CanEnterTile
        {
            get;
            set;
        }

        public event Action<Vector2Int> TileChanged;
        public event Action<Vector2Int, int> StopArrived;
        public event Action<Vector2Int, int> StopPresentationRequested;
        public event Action<RoadTrafficSnapshot> RoadEntryReserved;
        public event Action<Vector2Int> OffRoadExitRequested;
        public event Action RouteCompleted;
        public event Action RouteUnavailable;

        public bool TryGetRoadTrafficSnapshot(
            out RoadTrafficSnapshot snapshot)
        {
            if (roadTraffic == null || !roadTrafficAgentId.IsValid)
            {
                snapshot = default;
                return false;
            }

            return roadTraffic.TryGetSnapshot(
                roadTrafficAgentId,
                out snapshot);
        }

        public void ConfigureRoadTrafficAgent(
            RoadTrafficAgentKind kind,
            VehicleFootprint footprint,
            bool holdAtDestination = false)
        {
            if (roadTrafficAgentId.IsValid)
            {
                ReleaseRoadTrafficAgent();
            }

            roadTrafficRegistration =
                new RoadTrafficAgentRegistration(kind, footprint);
            roadTrafficConfigured = true;
            holdRoadTrafficAtDestination = holdAtDestination;
            TryRegisterRoadTrafficAgent();
        }

        public void ConfigureOffRoadTransitionSynchronization(
            bool enabled)
        {
            SynchronizeOffRoadTransitions = enabled;
        }

        public void ConfigureRoadTrafficAgent(
            RoadTrafficAgentKind kind,
            float lengthTiles,
            float widthTiles,
            bool holdAtDestination = false)
        {
            ConfigureRoadTrafficAgent(
                kind,
                new VehicleFootprint(
                    VehicleSizeClass.Standard,
                    lengthTiles,
                    widthTiles,
                    0f),
                holdAtDestination);
        }

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (isInitialized)
            {
                return;
            }

            if (services == null || services.TileData == null)
            {
                Debug.LogError(
                    "[BusRoute] CityFlowServices 또는 TileData가 없습니다.",
                    this
                );
                return;
            }

            this.services = services;
            tileData = services.TileData;
            roadTraffic = services.RoadTraffic;
            SubscribeRoadTrafficRecovery();
            worldGridAccess = services.WorldGrid;
            if (worldGridAccess != null)
            {
                gridWidth = Mathf.Max(1, worldGridAccess.WorldWidth);
                gridHeight = Mathf.Max(1, worldGridAccess.WorldHeight);
            }
            isInitialized = true;
            TryRegisterRoadTrafficAgent();

            if (autoStart && stops.Count >= 2)
            {
                StartRoute();
            }
        }

        private void Start()
        {
            if (isInitialized)
            {
                return;
            }

            CityBootstrap bootstrap =
                FindAnyObjectByType<CityBootstrap>();

            if (bootstrap?.Services == null)
            {
                Debug.LogWarning(
                    "[BusRoute] CityBootstrap 또는 Services를 찾지 못했습니다.",
                    this
                );
                return;
            }

            Initialize(bootstrap.Services);
        }

        private void Update()
        {
            if (!isInitialized || !routeRequested)
            {
                return;
            }

            switch (State)
            {
                case BusRouteState.WaitingForRoadEntry:
                    UpdateRoadEntryReservation();
                    break;

                case BusRouteState.Moving:
                    UpdateMoving(Time.deltaTime);
                    break;

                case BusRouteState.WaitingAtStop:
                    UpdateWaiting(Time.deltaTime);
                    break;
            }
        }

        public bool ConfigureRoute(
            IReadOnlyList<Vector2Int> newStops,
            bool shouldLoop
        )
        {
            StopRoute();

            stops.Clear();
            stopAccessRoads.Clear();

            if (newStops == null)
            {
                return false;
            }

            for (int i = 0; i < newStops.Count; i++)
            {
                Vector2Int stop = newStops[i];

                if (stops.Count > 0 &&
                    stops[stops.Count - 1] == stop)
                {
                    continue;
                }

                stops.Add(stop);
            }

            loopRoute = shouldLoop;

            if (stops.Count == 0)
            {
                return false;
            }

            CurrentTile = stops[0];
            currentStopIndex = 0;
            currentRoadPathIndex = 0;

            State = BusRouteState.Idle;
            TileChanged?.Invoke(CurrentTile);

            return stops.Count >= 2 ||
                   (stops.Count == 1 && loopRoute);
        }

        public bool TryGetAccessRoadForStop(
            Vector2Int stop,
            out Vector2Int accessRoad)
        {
            if (!isInitialized)
            {
                accessRoad = default;
                return false;
            }

            return TryFindAccessRoad(stop, out accessRoad);
        }

        public bool TryFindReachableRoadsideStop(
            Vector2Int startRoad,
            Vector2Int destinationStop,
            bool preventImmediateReverse,
            Vector2Int forbiddenFirstStep,
            out Vector2Int arrivalRoad,
            out Vector2Int arrivalPreviousRoad)
        {
            arrivalRoad = default;
            arrivalPreviousRoad = default;
            if (!isInitialized || !IsRoad(startRoad))
            {
                return false;
            }

            bool found =
                TryFindRoadsidePath(
                    startRoad,
                    destinationStop,
                    validationRoadPath,
                    preventImmediateReverse,
                    forbiddenFirstStep,
                    out arrivalRoad) &&
                validationRoadPath.Count > 1;
            if (found)
            {
                arrivalPreviousRoad =
                    validationRoadPath[
                        validationRoadPath.Count - 2];
            }

            return found;
        }

        public bool CanReachStopFromRoad(
            Vector2Int startRoad,
            Vector2Int destinationStop,
            bool preventImmediateReverse,
            Vector2Int forbiddenFirstStep)
        {
            if (!isInitialized ||
                !IsRoad(startRoad) ||
                !TryFindAccessRoad(
                    destinationStop,
                    out Vector2Int destinationRoad))
            {
                return false;
            }

            return startRoad == destinationRoad
                ? TryFindRoadCycle(
                    startRoad,
                    validationRoadPath,
                    preventImmediateReverse,
                    forbiddenFirstStep)
                : TryFindRoadPath(
                    startRoad,
                    destinationRoad,
                    validationRoadPath,
                    preventImmediateReverse,
                    forbiddenFirstStep);
        }

        public bool CanTraverseLoop(
            IReadOnlyList<Vector2Int> candidateStops) =>
            TryBuildLoopRoadRoute(
                candidateStops,
                false,
                out _);

        public bool TryBuildLoopRoadRoute(
            IReadOnlyList<Vector2Int> candidateStops,
            out RoadRoutePlan loopRoutePlan) =>
            TryBuildLoopRoadRoute(
                candidateStops,
                false,
                out loopRoutePlan);

        public bool TryBuildLoopRoadRoute(
            IReadOnlyList<Vector2Int> candidateStops,
            bool reverseAccessPreference,
            out RoadRoutePlan loopRoutePlan)
        {
            return TryBuildLoopRoadRoute(
                candidateStops,
                reverseAccessPreference,
                out loopRoutePlan,
                out _);
        }

        public bool TryBuildLoopRoadRoute(
            IReadOnlyList<Vector2Int> candidateStops,
            bool reverseAccessPreference,
            out RoadRoutePlan loopRoutePlan,
            out RoadRoutePlan firstSegmentPlan)
        {
            loopRoutePlan = default;
            firstSegmentPlan = default;
            if (!isInitialized ||
                candidateStops == null ||
                candidateStops.Count < 2)
            {
                return false;
            }

            CollectAccessRoads(
                candidateStops[0],
                validationStartRoads);
            for (int offset = 0;
                 offset < validationStartRoads.Count;
                 offset++)
            {
                int index = reverseAccessPreference
                    ? validationStartRoads.Count - 1 - offset
                    : offset;
                Vector2Int startRoad =
                    validationStartRoads[index];

                if (TryBuildLoopFromStartRoad(
                        candidateStops,
                        startRoad,
                        out loopRoutePlan,
                        out firstSegmentPlan))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryOrderRoadsideLoopStops(
            IReadOnlyList<Vector2Int> candidateStops,
            out IReadOnlyList<Vector2Int> orderedStops)
        {
            orderedStops = Array.Empty<Vector2Int>();
            if (!isInitialized ||
                !useRoadsideStopApproach ||
                !roadsideStopsUsePairedPlatforms ||
                candidateStops == null ||
                candidateStops.Count < 2)
            {
                return false;
            }

            validationBestOrderedStops.Clear();

            for (int startIndex = 0;
                 startIndex < candidateStops.Count;
                 startIndex++)
            {
                if (!TryOrderRoadsideLoopFromStop(
                        candidateStops,
                        startIndex))
                {
                    continue;
                }

                if (validationOrderedStops.Count >
                    validationBestOrderedStops.Count)
                {
                    CopyPath(
                        validationOrderedStops,
                        validationBestOrderedStops);
                }

                if (validationBestOrderedStops.Count ==
                    candidateStops.Count)
                {
                    break;
                }
            }

            if (validationBestOrderedStops.Count < 2)
            {
                return false;
            }

            orderedStops = Array.AsReadOnly(
                validationBestOrderedStops.ToArray());
            return true;
        }

        private bool TryOrderRoadsideLoopFromStop(
            IReadOnlyList<Vector2Int> candidateStops,
            int startIndex)
        {
            validationOrderedStops.Clear();
            validationRemainingStops.Clear();
            validationSelectedRoadPath.Clear();

            Vector2Int startStop = candidateStops[startIndex];
            if (!TryGetPairedStopApproach(
                    startStop,
                    out Vector2Int currentRoad,
                    out _,
                    out Vector2Int arrivalDirection))
            {
                return false;
            }

            Vector2Int forbiddenFirstStep =
                currentRoad - arrivalDirection;
            if (!IsRoad(forbiddenFirstStep))
            {
                return false;
            }

            validationOrderedStops.Add(startStop);
            for (int index = 0;
                 index < candidateStops.Count;
                 index++)
            {
                if (index != startIndex)
                {
                    validationRemainingStops.Add(
                        candidateStops[index]);
                }
            }

            while (validationRemainingStops.Count > 0)
            {
                int selectedIndex = -1;
                int selectedPathLength = int.MaxValue;
                Vector2Int selectedEndRoad = default;
                validationSelectedRoadPath.Clear();

                for (int index = 0;
                     index < validationRemainingStops.Count;
                     index++)
                {
                    Vector2Int candidate =
                        validationRemainingStops[index];
                    if (!TryFindRoadsidePath(
                            currentRoad,
                            candidate,
                            validationRoadPath,
                            true,
                            forbiddenFirstStep,
                            out Vector2Int candidateEndRoad) ||
                        validationRoadPath.Count < 2)
                    {
                        continue;
                    }

                    bool shorter =
                        validationRoadPath.Count <
                        selectedPathLength;
                    bool sameLengthEarlierCoordinate =
                        validationRoadPath.Count ==
                        selectedPathLength &&
                        (selectedIndex < 0 ||
                         CompareStopCoordinates(
                             candidate,
                             validationRemainingStops[
                                 selectedIndex]) < 0);
                    if (!shorter &&
                        !sameLengthEarlierCoordinate)
                    {
                        continue;
                    }

                    selectedIndex = index;
                    selectedPathLength =
                        validationRoadPath.Count;
                    selectedEndRoad = candidateEndRoad;
                    CopyPath(
                        validationRoadPath,
                        validationSelectedRoadPath);
                }

                if (selectedIndex < 0 ||
                    validationSelectedRoadPath.Count < 2)
                {
                    break;
                }

                validationOrderedStops.Add(
                    validationRemainingStops[selectedIndex]);
                validationRemainingStops.RemoveAt(selectedIndex);
                forbiddenFirstStep =
                    validationSelectedRoadPath[
                        validationSelectedRoadPath.Count - 2];
                currentRoad = selectedEndRoad;
            }

            return validationOrderedStops.Count >= 2 &&
                   TryFindRoadsidePath(
                       currentRoad,
                       startStop,
                       validationRoadPath,
                       true,
                       forbiddenFirstStep,
                       out _) &&
                   validationRoadPath.Count >= 2;
        }

        private static void CopyPath(
            IReadOnlyList<Vector2Int> source,
            List<Vector2Int> destination)
        {
            destination.Clear();
            for (int index = 0; index < source.Count; index++)
            {
                destination.Add(source[index]);
            }
        }

        private static int CompareStopCoordinates(
            Vector2Int left,
            Vector2Int right)
        {
            int yCompare = left.y.CompareTo(right.y);
            return yCompare != 0
                ? yCompare
                : left.x.CompareTo(right.x);
        }

        public void SetInitialAccessRoad(Vector2Int accessRoad)
        {
            preferredInitialAccessRoad = accessRoad;
            hasPreferredInitialAccessRoad = IsRoad(accessRoad);
        }

        private bool TryBuildLoopFromStartRoad(
            IReadOnlyList<Vector2Int> candidateStops,
            Vector2Int startRoad,
            out RoadRoutePlan loopRoutePlan,
            out RoadRoutePlan firstSegmentPlan)
        {
            loopRoutePlan = default;
            firstSegmentPlan = default;
            Vector2Int currentRoad = startRoad;

            bool preventImmediateReverse = false;
            Vector2Int forbiddenFirstStep = default;
            int segmentCount = candidateStops.Count * 2;
            validationLoopRoadPath.Clear();

            for (int segment = 0;
                 segment < segmentCount;
                 segment++)
            {
                int destinationIndex =
                    (segment + 1) % candidateStops.Count;
                Vector2Int destinationStop =
                    candidateStops[destinationIndex];

                bool foundPath;
                Vector2Int endRoad;
                if (ShouldUseRoadsideStop(destinationStop))
                {
                    foundPath = TryFindRoadsidePath(
                        currentRoad,
                        destinationStop,
                        validationRoadPath,
                        preventImmediateReverse,
                        forbiddenFirstStep,
                        out endRoad);
                }
                else if (TryFindAccessRoad(
                             destinationStop,
                             out endRoad))
                {
                    foundPath = TryBuildRoadPath(
                        currentRoad,
                        endRoad,
                        validationRoadPath,
                        preventImmediateReverse,
                        forbiddenFirstStep);
                }
                else
                {
                    foundPath = false;
                }

                if (!foundPath || validationRoadPath.Count < 2)
                {
                    return false;
                }

                if (segment < candidateStops.Count)
                {
                    AppendSegment(
                        validationLoopRoadPath,
                        validationRoadPath);
                }

                if (segment == 0)
                {
                    firstSegmentPlan = new RoadRoutePlan(
                        validationRoadPath);
                }

                forbiddenFirstStep =
                    validationRoadPath[
                        validationRoadPath.Count - 2];
                preventImmediateReverse = avoidImmediateUTurn;
                currentRoad = endRoad;
            }

            loopRoutePlan = new RoadRoutePlan(
                validationLoopRoadPath);
            return loopRoutePlan.TileCount > 1 &&
                firstSegmentPlan.TileCount > 1;
        }

        private void CollectAccessRoads(
            Vector2Int stopTile,
            List<Vector2Int> result)
        {
            result.Clear();
            if (IsRoad(stopTile))
            {
                result.Add(stopTile);
                return;
            }

            if (roadsideStopsUsePairedPlatforms &&
                BusStopInfrastructurePolicy.TryGetPlatformPair(
                    stopTile,
                    IsRoad,
                    out Vector2Int pairedAccessRoad,
                    out _))
            {
                result.Add(pairedAccessRoad);
                return;
            }

            TileType stopType = tileData.GetTileType(stopTile);
            Vector2Int footprint = TileFootprint.IsBuilding(stopType)
                ? tileData.GetFootprintSize(stopType)
                : Vector2Int.one;

            for (int y = 0; y < footprint.y; y++)
            {
                for (int x = 0; x < footprint.x; x++)
                {
                    Vector2Int footprintTile =
                        stopTile + new Vector2Int(x, y);
                    for (int directionIndex = 0;
                         directionIndex < Directions.Length;
                         directionIndex++)
                    {
                        Vector2Int candidate =
                            footprintTile +
                            Directions[directionIndex];
                        if (IsRoad(candidate) &&
                            !result.Contains(candidate))
                        {
                            result.Add(candidate);
                        }
                    }
                }
            }
        }

        private static void AppendSegment(
            List<Vector2Int> destination,
            IReadOnlyList<Vector2Int> segment)
        {
            int startIndex = destination.Count > 0 &&
                segment.Count > 0 &&
                destination[destination.Count - 1] == segment[0]
                    ? 1
                    : 0;

            for (int index = startIndex;
                 index < segment.Count;
                 index++)
            {
                destination.Add(segment[index]);
            }
        }

        public bool ReconfigureLoopAtCurrentStop(
            IReadOnlyList<Vector2Int> newStops)
        {
            Vector2Int currentStop = CurrentStop;
            Vector2Int preservedForbiddenTile =
                forbiddenDepartureTile;
            bool preservedForbidden =
                hasForbiddenDepartureTile;
            Vector2Int preservedAccessRoad =
                currentStopAccessRoad;
            bool preservedAccess = hasCurrentStopAccessRoad;

            PrepareHeldRouteReconfiguration();
            bool configured = ReplaceStops(newStops);

            if (configured)
            {
                currentStopIndex = stops.IndexOf(currentStop);
                configured = currentStopIndex >= 0;
            }

            if (configured && avoidImmediateUTurn)
            {
                forbiddenDepartureTile =
                    preservedForbiddenTile;
                hasForbiddenDepartureTile =
                    preservedForbidden;
            }

            if (configured && useRoadsideStopApproach &&
                preservedAccess)
            {
                currentStopAccessRoad = preservedAccessRoad;
                hasCurrentStopAccessRoad = true;
                CurrentTile = preservedAccessRoad;
            }
            else if (configured)
            {
                CurrentTile = currentStop;
            }

            if (configured)
            {
                TileChanged?.Invoke(CurrentTile);
            }

            return configured;
        }

        public bool ReconfigureLoopFromCurrentPosition(
            Vector2Int currentPosition,
            IReadOnlyList<Vector2Int> newStops)
        {
            Vector2Int preservedForbiddenTile =
                forbiddenDepartureTile;
            bool preservedForbidden =
                hasForbiddenDepartureTile;

            PrepareHeldRouteReconfiguration();
            if (!ReplaceStops(newStops))
            {
                return false;
            }

            departureStop = currentPosition;
            hasDepartureStop = true;
            currentStopIndex = -1;
            currentRoadPathIndex = 0;
            CurrentTile = currentPosition;
            State = BusRouteState.Idle;

            if (avoidImmediateUTurn)
            {
                forbiddenDepartureTile =
                    preservedForbiddenTile;
                hasForbiddenDepartureTile =
                    preservedForbidden;
            }

            TileChanged?.Invoke(CurrentTile);
            return true;
        }

        public bool StartRoute()
        {
            if (!isInitialized)
            {
                Debug.LogWarning(
                    "[BusRoute] 초기화되지 않아 운행을 시작할 수 없습니다.",
                    this
                );
                return false;
            }

            if (stops.Count == 0 ||
                (stops.Count == 1 && !loopRoute))
            {
                Debug.LogWarning(
                    "[BusRoute] 순환 노선에는 정류장이 최소 1개 필요합니다.",
                    this
                );
                return false;
            }

            routeRequested = true;

            if (!hasDepartureStop)
            {
                currentStopIndex = 0;
                CurrentTile = stops[0];
            }

            TileChanged?.Invoke(CurrentTile);

            return BuildPathToNextStop();
        }

        public void StopRoute()
        {
            routeRequested = false;

            moveTimer = 0f;
            waitTimer = 0f;
            stopPresentationPending = false;

            currentRoadPath.Clear();
            roadTrafficPath.Clear();
            currentRoadPathIndex = 0;
            hasDepartureStop = false;
            hasForbiddenDepartureTile = false;
            hasCurrentStopAccessRoad = false;
            currentSegmentUsesRoadsideStop = false;
            hasPreferredInitialAccessRoad = false;
            pendingOffRoadStopIndex = -1;

            ReleaseRoadTrafficAgent();

            State = BusRouteState.Idle;
        }

        public bool RebuildCurrentSegment()
        {
            if (!routeRequested || stops.Count == 0)
            {
                return false;
            }

            if (useRoadsideStopApproach &&
                State == BusRouteState.Moving &&
                currentRoadPathIndex > 0)
            {
                Vector2Int previousRoad =
                    currentRoadPath[currentRoadPathIndex - 1];

                if (IsRoad(previousRoad))
                {
                    forbiddenDepartureTile = previousRoad;
                    hasForbiddenDepartureTile = true;
                }
            }

            return BuildPathToNextStop();
        }

        private void UpdateMoving(float deltaTime)
        {
            if (roadTrafficAgentId.IsValid)
            {
                UpdateRoadTrafficMovement();
                return;
            }

            moveTimer += Mathf.Max(0f, deltaTime);

            float safeSecondsPerTile =
                Mathf.Max(0.01f, secondsPerTile);

            while (moveTimer >= safeSecondsPerTile &&
                   State == BusRouteState.Moving)
            {
                moveTimer -= safeSecondsPerTile;
                MoveOneTile();
            }
        }

        private void UpdateRoadTrafficMovement()
        {
            if (roadTraffic == null ||
                !roadTraffic.TryGetSnapshot(
                    roadTrafficAgentId,
                    out RoadTrafficSnapshot snapshot))
            {
                SetRouteUnavailable();
                return;
            }

            if (snapshot.State ==
                RoadTrafficAgentState.RouteUnavailable)
            {
                SetRouteUnavailable();
                return;
            }

            int pathIndex = Mathf.Clamp(
                roadTrafficPathOffset + snapshot.RouteTileIndex,
                0,
                Mathf.Max(0, currentRoadPath.Count - 1));
            if (currentRoadPath.Count > 0 &&
                (CurrentTile != snapshot.CurrentTile ||
                 currentRoadPathIndex != pathIndex))
            {
                currentRoadPathIndex = pathIndex;
                CurrentTile = snapshot.CurrentTile;
                TileChanged?.Invoke(CurrentTile);
            }

            if (snapshot.State == RoadTrafficAgentState.Arrived &&
                !roadTrafficArrivalHandled)
            {
                roadTrafficArrivalHandled = true;
                ArriveAtNextStop();
            }
            else if (snapshot.State ==
                         RoadTrafficAgentState.HoldingAtDestination &&
                     !roadTrafficArrivalHandled)
            {
                roadTrafficArrivalHandled = true;
                if (SynchronizeOffRoadTransitions &&
                    !currentSegmentUsesRoadsideStop)
                {
                    BeginOffRoadExit();
                }
                else
                {
                    ArriveAtNextStop();
                }
            }
        }

        private void UpdateRoadEntryReservation()
        {
            if (roadTraffic == null ||
                !roadTraffic.TryGetSnapshot(
                    roadTrafficAgentId,
                    out RoadTrafficSnapshot snapshot))
            {
                SetRouteUnavailable();
                return;
            }

            if (snapshot.State ==
                RoadTrafficAgentState.RouteUnavailable)
            {
                SetRouteUnavailable();
                return;
            }

            if (!snapshot.IsVisible ||
                snapshot.State != RoadTrafficAgentState.Paused)
            {
                return;
            }

            currentRoadPathIndex = Mathf.Clamp(
                roadTrafficPathOffset + snapshot.RouteTileIndex,
                0,
                Mathf.Max(0, currentRoadPath.Count - 1));
            CurrentTile = snapshot.CurrentTile;
            State = BusRouteState.EnteringRoad;

            Action<RoadTrafficSnapshot> handler =
                RoadEntryReserved;
            if (handler == null)
            {
                CompleteRoadEntryTransition();
                return;
            }

            handler.Invoke(snapshot);
        }

        public bool CompleteRoadEntryTransition()
        {
            if (State != BusRouteState.EnteringRoad)
            {
                return false;
            }

            if (roadTraffic == null ||
                !roadTrafficAgentId.IsValid ||
                !roadTraffic.TrySetAgentPaused(
                    roadTrafficAgentId,
                    false))
            {
                SetRouteUnavailable();
                return false;
            }

            State = BusRouteState.Moving;
            TileChanged?.Invoke(CurrentTile);
            return true;
        }

        private void BeginOffRoadExit()
        {
            int nextStopIndex = GetNextStopIndex();
            if (nextStopIndex < 0)
            {
                CompleteRoute();
                return;
            }

            pendingOffRoadStopIndex = nextStopIndex;
            State = BusRouteState.LeavingRoad;

            Action<Vector2Int> handler = OffRoadExitRequested;
            if (handler == null)
            {
                CompleteOffRoadExitTransition();
                return;
            }

            handler.Invoke(stops[nextStopIndex]);
        }

        public bool CompleteOffRoadExitTransition()
        {
            if (State != BusRouteState.LeavingRoad ||
                pendingOffRoadStopIndex < 0)
            {
                return false;
            }

            int stopIndex = pendingOffRoadStopIndex;
            pendingOffRoadStopIndex = -1;
            ReleaseRoadTrafficAgent();
            ArriveAtStop(stopIndex);
            return true;
        }

        private void MoveOneTile()
        {
            if (currentRoadPath.Count == 0)
            {
                SetRouteUnavailable();
                return;
            }

            if (currentRoadPathIndex >=
                currentRoadPath.Count - 1)
            {
                ArriveAtNextStop();
                return;
            }

            Vector2Int nextTile =
                currentRoadPath[currentRoadPathIndex + 1];

            if (currentSegmentUsesRoadsideStop &&
                !IsRoad(nextTile))
            {
                RebuildCurrentSegment();
                return;
            }

            if (CanEnterTile != null &&
                !CanEnterTile(CurrentTile, nextTile))
            {
                return;
            }

            currentRoadPathIndex++;
            CurrentTile =
                currentRoadPath[currentRoadPathIndex];

            TileChanged?.Invoke(CurrentTile);

            if (IsRoad(CurrentTile) &&
                TryFindStopAtAccessRoad(
                    CurrentTile,
                    out int encounteredStopIndex))
            {
                ArriveAtStop(encounteredStopIndex);
                return;
            }

            if (currentRoadPathIndex >=
                currentRoadPath.Count - 1)
            {
                ArriveAtNextStop();
            }
        }

        private void ArriveAtNextStop()
        {
            int nextIndex = GetNextStopIndex();

            if (nextIndex < 0)
            {
                CompleteRoute();
                return;
            }

            ArriveAtStop(nextIndex);
        }

        private void ArriveAtStop(int stopIndex)
        {
            RememberArrivalApproach();

            if (currentSegmentUsesRoadsideStop &&
                IsRoad(CurrentTile))
            {
                currentStopAccessRoad = CurrentTile;
                hasCurrentStopAccessRoad = true;
            }
            else
            {
                hasCurrentStopAccessRoad = false;
            }

            hasDepartureStop = false;
            currentStopIndex = stopIndex;
            if (!currentSegmentUsesRoadsideStop)
            {
                CurrentTile = stops[currentStopIndex];
            }

            TileChanged?.Invoke(CurrentTile);

            State = BusRouteState.WaitingAtStop;
            if (requireStopPresentationConfirmation)
            {
                stopPresentationPending = true;
                waitTimer = 0f;
                StopPresentationRequested?.Invoke(
                    stops[currentStopIndex],
                    currentStopIndex);
                return;
            }

            BeginStopWait();
        }

        private void RememberArrivalApproach()
        {
            hasForbiddenDepartureTile = false;

            if (!avoidImmediateUTurn ||
                currentRoadPath.Count < 2)
            {
                return;
            }

            int arrivalRoadIndex =
                Mathf.Min(
                    currentRoadPathIndex,
                    currentRoadPath.Count - 1);

            if (!IsRoad(currentRoadPath[arrivalRoadIndex]))
            {
                arrivalRoadIndex--;
            }

            int previousRoadIndex = arrivalRoadIndex - 1;

            if (previousRoadIndex < 0 ||
                !IsRoad(currentRoadPath[arrivalRoadIndex]) ||
                !IsRoad(currentRoadPath[previousRoadIndex]))
            {
                return;
            }

            Vector2Int arrivalRoad =
                currentRoadPath[arrivalRoadIndex];
            Vector2Int previousRoad =
                currentRoadPath[previousRoadIndex];
            Vector2Int delta = arrivalRoad - previousRoad;

            if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1)
            {
                return;
            }

            forbiddenDepartureTile = previousRoad;
            hasForbiddenDepartureTile = true;
        }

        private bool TryFindStopAtAccessRoad(
            Vector2Int roadTile,
            out int stopIndex)
        {
            if (currentSegmentUsesRoadsideStop)
            {
                return TryFindRoadsideStopAtRoad(
                    roadTile,
                    out stopIndex);
            }

            if (stopAccessRoads.Count != stops.Count)
            {
                RefreshStopAccessRoads();
            }

            stopIndex = BusStopRoutePolicy.FindStopIndexAtRoad(
                stopAccessRoads,
                currentStopIndex,
                GetNextStopIndex(),
                roadTile);

            int scheduledStopIndex = GetNextStopIndex();
            if (!AllowUnscheduledStopArrival &&
                stopIndex != scheduledStopIndex)
            {
                stopIndex = -1;
                return false;
            }

            if (stopIndex < 0)
            {
                return false;
            }

            /*
             * A loop may contain the same physical stop twice
             * (for example School -> Houses -> School).
             * While leaving the first occurrence, its shared access road
             * must not be mistaken for the final occurrence.
             */
            if (stopIndex != scheduledStopIndex &&
                currentStopIndex >= 0 &&
                currentStopIndex < stops.Count &&
                stops[stopIndex] == stops[currentStopIndex])
            {
                stopIndex = -1;
                return false;
            }

            return true;
        }

        private bool TryFindRoadsideStopAtRoad(
            Vector2Int roadTile,
            out int stopIndex)
        {
            stopIndex = -1;

            if (currentRoadPathIndex <= 0)
            {
                return false;
            }

            Vector2Int previousRoad =
                currentRoadPath[currentRoadPathIndex - 1];
            if (!IsRoad(previousRoad))
            {
                return false;
            }

            Vector2Int travelDirection =
                roadTile - previousRoad;
            if (!IsCardinalDirection(travelDirection))
            {
                return false;
            }

            int scheduledStopIndex = GetNextStopIndex();
            if (IsEligibleRoadsideStop(
                    scheduledStopIndex,
                    roadTile,
                    travelDirection))
            {
                stopIndex = scheduledStopIndex;
                return true;
            }

            if (!AllowUnscheduledStopArrival)
            {
                return false;
            }

            for (int i = 0; i < stops.Count; i++)
            {
                if (i == scheduledStopIndex)
                {
                    continue;
                }

                if (currentStopIndex >= 0 &&
                    currentStopIndex < stops.Count &&
                    stops[i] == stops[currentStopIndex])
                {
                    continue;
                }

                if (IsEligibleRoadsideStop(
                        i,
                        roadTile,
                        travelDirection))
                {
                    stopIndex = i;
                    return true;
                }
            }

            return false;
        }

        private bool IsEligibleRoadsideStop(
            int stopIndex,
            Vector2Int roadTile,
            Vector2Int travelDirection)
        {
            if (stopIndex < 0 ||
                stopIndex >= stops.Count ||
                stopIndex == currentStopIndex)
            {
                return false;
            }

            Vector2Int stop = stops[stopIndex];
            TileType stopType = tileData.GetTileType(stop);
            Vector2Int footprint =
                TileFootprint.IsBuilding(stopType)
                    ? tileData.GetFootprintSize(stopType)
                    : Vector2Int.one;

            if (roadsideStopsUsePairedPlatforms)
            {
                if (!TryGetPairedStopApproach(
                        stop,
                        out Vector2Int pairedAccessRoad,
                        out Vector2Int physicalPlatform,
                        out _) ||
                    roadTile != pairedAccessRoad)
                {
                    return false;
                }

                Vector2Int pairedRightSide = new(
                    travelDirection.y,
                    -travelDirection.x);
                return roadTile + pairedRightSide == physicalPlatform;
            }

            Vector2Int rightSide = new(
                travelDirection.y,
                -travelDirection.x);
            Vector2Int roadsideTile =
                roadTile + rightSide;

            for (int y = 0; y < footprint.y; y++)
            {
                for (int x = 0; x < footprint.x; x++)
                {
                    if (stop + new Vector2Int(x, y) ==
                        roadsideTile)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryGetPairedStopApproach(
            Vector2Int logicalStop,
            out Vector2Int accessRoad,
            out Vector2Int physicalPlatform,
            out Vector2Int arrivalDirection)
        {
            accessRoad = default;
            physicalPlatform = default;
            arrivalDirection = default;

            if (!BusStopInfrastructurePolicy.TryGetPlatformPair(
                    logicalStop,
                    IsRoad,
                    out accessRoad,
                    out Vector2Int oppositePlatform))
            {
                return false;
            }

            physicalPlatform = useOppositePairedPlatformDirection
                ? oppositePlatform
                : logicalStop;
            Vector2Int platformSide = physicalPlatform - accessRoad;
            if (!IsCardinalDirection(platformSide))
            {
                return false;
            }

            arrivalDirection = new(
                -platformSide.y,
                platformSide.x);
            return true;
        }

        private static bool IsRoadAdjacentToFootprint(
            Vector2Int roadTile,
            Vector2Int footprintOrigin,
            Vector2Int footprintSize)
        {
            for (int y = 0; y < footprintSize.y; y++)
            {
                for (int x = 0; x < footprintSize.x; x++)
                {
                    Vector2Int footprintTile =
                        footprintOrigin + new Vector2Int(x, y);
                    Vector2Int delta = roadTile - footprintTile;
                    if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) == 1)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void RefreshStopAccessRoads()
        {
            stopAccessRoads.Clear();

            for (int i = 0; i < stops.Count; i++)
            {
                stopAccessRoads.Add(
                    TryFindAccessRoad(
                        stops[i],
                        out Vector2Int accessRoad)
                        ? accessRoad
                        : InvalidAccessRoad);
            }
        }

        private void UpdateWaiting(float deltaTime)
        {
            if (stopPresentationPending)
            {
                return;
            }

            waitTimer -= Mathf.Max(0f, deltaTime);

            if (waitTimer <= 0f)
            {
                ContinueAfterWait();
            }
        }

        public bool ConfirmStopPresentationReached()
        {
            if (!stopPresentationPending)
            {
                return false;
            }

            BeginStopWait();
            return true;
        }

        private void BeginStopWait()
        {
            stopPresentationPending = false;
            StopArrived?.Invoke(
                stops[currentStopIndex],
                currentStopIndex);

            waitTimer = stopWaitSeconds;
            State = BusRouteState.WaitingAtStop;

            if (waitTimer <= 0f)
            {
                ContinueAfterWait();
            }
        }

        private void ContinueAfterWait()
        {
            int nextIndex = GetNextStopIndex();

            if (nextIndex < 0)
            {
                CompleteRoute();
                return;
            }

            BuildPathToNextStop();
        }

        private bool BuildPathToNextStop()
        {
            int nextStopIndex = GetNextStopIndex();

            if (nextStopIndex < 0)
            {
                CompleteRoute();
                return false;
            }

            Vector2Int startStop = GetCurrentStop();
            Vector2Int destinationStop = stops[nextStopIndex];
            bool requiresRoadEntryTransition =
                SynchronizeOffRoadTransitions &&
                roadTrafficConfigured &&
                roadTraffic != null &&
                !IsRoad(CurrentTile);
            currentSegmentUsesRoadsideStop =
                ShouldUseRoadsideStop(destinationStop);

            Vector2Int startRoad;
            if (useRoadsideStopApproach &&
                State == BusRouteState.Moving &&
                IsRoad(CurrentTile))
            {
                startRoad = CurrentTile;
            }
            else if (useRoadsideStopApproach &&
                     hasCurrentStopAccessRoad &&
                     IsRoad(currentStopAccessRoad))
            {
                startRoad = currentStopAccessRoad;
            }
            else if (useRoadsideStopApproach &&
                     hasPreferredInitialAccessRoad &&
                     IsRoad(preferredInitialAccessRoad))
            {
                startRoad = preferredInitialAccessRoad;
                hasPreferredInitialAccessRoad = false;
            }
            else if (!TryFindAccessRoad(
                         startStop,
                         out startRoad))
            {
                Debug.LogWarning(
                    $"[BusRoute] 정류장 근처 도로를 찾지 못했습니다. " +
                    $"Start: {startStop}",
                    this
                );

                SetRouteUnavailable();
                return false;
            }

            if (!currentSegmentUsesRoadsideStop)
            {
                RefreshStopAccessRoads();
            }

            bool preventImmediateReverse =
                avoidImmediateUTurn &&
                hasForbiddenDepartureTile;
            Vector2Int forbiddenFirstStep =
                forbiddenDepartureTile;
            hasForbiddenDepartureTile = false;

            bool foundPath;
            Vector2Int endRoad = default;

            if (currentSegmentUsesRoadsideStop)
            {
                foundPath = TryFindRoadsidePath(
                    startRoad,
                    destinationStop,
                    currentRoadPath,
                    preventImmediateReverse,
                    forbiddenFirstStep,
                    out endRoad);
            }
            else if (!TryFindAccessRoad(
                         destinationStop,
                         out endRoad))
            {
                foundPath = false;
            }
            else
            {
                foundPath = startRoad == endRoad
                    ? TryFindRoadCycle(
                        startRoad,
                        currentRoadPath,
                        preventImmediateReverse,
                        forbiddenFirstStep)
                    : TryFindRoadPath(
                        startRoad,
                        endRoad,
                        currentRoadPath,
                        preventImmediateReverse,
                        forbiddenFirstStep);
            }

            if (foundPath &&
                !currentSegmentUsesRoadsideStop &&
                startRoad != endRoad &&
                services?.RoadRoutePlanning != null)
            {
                foundPath = services.RoadRoutePlanning.TryPlanRoadRoute(
                    startRoad,
                    endRoad,
                    out RoadRoutePlan plannedRoute);
                if (foundPath)
                {
                    currentRoadPath.Clear();
                    for (int index = 0;
                         index < plannedRoute.TileCount;
                         index++)
                    {
                        currentRoadPath.Add(plannedRoute.Tiles[index]);
                    }
                }
            }

            if (!foundPath)
            {
                Debug.LogWarning(
                    $"[BusRoute] 연결된 합법 경로를 찾지 못했습니다. " +
                    $"DestinationStop: {destinationStop}, " +
                    $"StartRoad: {startRoad}, EndRoad: {endRoad}",
                    this
                );

                SetRouteUnavailable();
                return false;
            }

            /*
             * 정류장 타일 자체는 도로가 아닐 수 있으므로
             * 정류장 → 접근 도로 → 도로 경로 → 정류장 순서로 구성합니다.
             */
            if (!useRoadsideStopApproach)
            {
                if (currentRoadPath.Count == 0 ||
                    currentRoadPath[0] != startStop)
                {
                    currentRoadPath.Insert(0, startStop);
                }

                if (currentRoadPath[currentRoadPath.Count - 1] !=
                    destinationStop)
                {
                    currentRoadPath.Add(destinationStop);
                }
            }
            else if (!currentSegmentUsesRoadsideStop &&
                     currentRoadPath[currentRoadPath.Count - 1] !=
                     destinationStop)
            {
                currentRoadPath.Add(destinationStop);
            }

            currentRoadPathIndex = 0;
            CurrentTile = currentRoadPath[0];

            moveTimer = 0f;
            State = requiresRoadEntryTransition
                ? BusRouteState.WaitingForRoadEntry
                : BusRouteState.Moving;

            if (roadTrafficConfigured &&
                !TryStartRoadTrafficSegment(
                    requiresRoadEntryTransition))
            {
                SetRouteUnavailable();
                return false;
            }

            if (!requiresRoadEntryTransition)
            {
                TileChanged?.Invoke(CurrentTile);
            }
            return true;
        }

        private bool TryStartRoadTrafficSegment(
            bool pauseOnEntry = false)
        {
            if (roadTraffic == null)
            {
                return true;
            }

            if (!TryRegisterRoadTrafficAgent())
            {
                return false;
            }

            roadTrafficPath.Clear();
            roadTrafficPathOffset = -1;
            for (int index = 0; index < currentRoadPath.Count; index++)
            {
                if (!IsRoad(currentRoadPath[index]))
                {
                    if (roadTrafficPath.Count > 0)
                    {
                        break;
                    }

                    continue;
                }

                if (roadTrafficPathOffset < 0)
                {
                    roadTrafficPathOffset = index;
                }

                roadTrafficPath.Add(currentRoadPath[index]);
            }

            if (roadTrafficPath.Count == 0)
            {
                return false;
            }

            roadTrafficArrivalHandled = false;
            var route = new RoadRoutePlan(roadTrafficPath);
            RoadSegmentVersion++;
            return roadTraffic.TryAssignRoute(
                       new RoadTrafficRouteRequest(
                           roadTrafficAgentId,
                           route,
                           false,
                            holdRoadTrafficAtDestination
                                ? RoadTrafficArrivalPolicy
                                    .HoldAtDestination
                                : RoadTrafficArrivalPolicy
                                    .ReleaseAtDestination,
                            pauseOnEntry))
                && roadTraffic.TryStartAgent(roadTrafficAgentId);
        }

        private void PrepareHeldRouteReconfiguration()
        {
            routeRequested = false;
            moveTimer = 0f;
            waitTimer = 0f;
            stopPresentationPending = false;
            currentRoadPath.Clear();
            roadTrafficPath.Clear();
            stopAccessRoads.Clear();
            currentRoadPathIndex = 0;
            hasDepartureStop = false;
            hasCurrentStopAccessRoad = false;
            currentSegmentUsesRoadsideStop = false;
            pendingOffRoadStopIndex = -1;
            State = BusRouteState.Idle;
        }

        private bool ReplaceStops(
            IReadOnlyList<Vector2Int> newStops)
        {
            stops.Clear();
            loopRoute = true;

            if (newStops == null)
            {
                return false;
            }

            for (int index = 0; index < newStops.Count; index++)
            {
                Vector2Int stop = newStops[index];
                if (stops.Count == 0 ||
                    stops[stops.Count - 1] != stop)
                {
                    stops.Add(stop);
                }
            }

            return stops.Count > 0;
        }

        private bool TryRegisterRoadTrafficAgent()
        {
            if (roadTrafficAgentId.IsValid)
            {
                return true;
            }

            return roadTrafficConfigured &&
                roadTraffic != null &&
                roadTraffic.TryRegisterAgent(
                    roadTrafficRegistration,
                    out roadTrafficAgentId);
        }

        private void ReleaseRoadTrafficAgent()
        {
            if (roadTraffic != null && roadTrafficAgentId.IsValid)
            {
                roadTraffic.TryRemoveAgent(roadTrafficAgentId);
            }

            roadTrafficAgentId = RoadTrafficAgentId.Invalid;
            roadTrafficPath.Clear();
            roadTrafficPathOffset = 0;
            roadTrafficArrivalHandled = false;
        }

        private bool TryFindRoadsidePath(
            Vector2Int startRoad,
            Vector2Int destinationStop,
            List<Vector2Int> result,
            bool preventImmediateReverse,
            Vector2Int forbiddenFirstStep,
            out Vector2Int selectedAccessRoad)
        {
            result.Clear();
            selectedAccessRoad = default;

            if (roadsideStopsUsePairedPlatforms &&
                TryGetPairedStopApproach(
                    destinationStop,
                    out Vector2Int pairedAccessRoad,
                    out _,
                    out Vector2Int pairedArrivalDirection))
            {
                if (!TryBuildRoadsideApproachPath(
                        startRoad,
                        pairedAccessRoad,
                        pairedArrivalDirection,
                        candidateRoadPath,
                        preventImmediateReverse,
                        forbiddenFirstStep))
                {
                    return false;
                }

                CopyRoadsidePathWithSetback(
                    candidateRoadPath,
                    result,
                    out selectedAccessRoad);
                return result.Count > 0;
            }

            bool found = false;
            TileType stopType =
                tileData.GetTileType(destinationStop);
            Vector2Int footprint =
                TileFootprint.IsBuilding(stopType)
                    ? tileData.GetFootprintSize(stopType)
                    : Vector2Int.one;

            for (int y = 0; y < footprint.y; y++)
            {
                for (int x = 0; x < footprint.x; x++)
                {
                    Vector2Int footprintTile =
                        destinationStop +
                        new Vector2Int(x, y);

                    for (int i = 0;
                         i < Directions.Length;
                         i++)
                    {
                        Vector2Int accessRoad =
                            footprintTile + Directions[i];
                        if (!IsRoad(accessRoad))
                        {
                            continue;
                        }

                        Vector2Int stopSide =
                            footprintTile - accessRoad;
                        Vector2Int arrivalDirection = new(
                            -stopSide.y,
                            stopSide.x);

                        if (!TryBuildRoadsideApproachPath(
                                startRoad,
                                accessRoad,
                                arrivalDirection,
                                candidateRoadPath,
                                preventImmediateReverse,
                                forbiddenFirstStep))
                        {
                            continue;
                        }

                        int candidateCount =
                            GetRoadsidePathCount(
                                candidateRoadPath);
                        if (candidateCount <= 0)
                        {
                            continue;
                        }

                        if (!found ||
                            candidateCount < result.Count)
                        {
                            CopyRoadsidePathWithSetback(
                                candidateRoadPath,
                                result,
                                out selectedAccessRoad);
                            found = true;
                        }
                    }
                }
            }

            if (found)
            {
                return true;
            }

            return TryFindFallbackRoadsidePath(
                startRoad,
                destinationStop,
                result,
                preventImmediateReverse,
                forbiddenFirstStep,
                out selectedAccessRoad);
        }

        private bool TryFindFallbackRoadsidePath(
            Vector2Int startRoad,
            Vector2Int destinationStop,
            List<Vector2Int> result,
            bool preventImmediateReverse,
            Vector2Int forbiddenFirstStep,
            out Vector2Int selectedAccessRoad)
        {
            bool found = false;
            selectedAccessRoad = default;
            TileType stopType =
                tileData.GetTileType(destinationStop);
            Vector2Int footprint =
                TileFootprint.IsBuilding(stopType)
                    ? tileData.GetFootprintSize(stopType)
                    : Vector2Int.one;

            for (int y = 0; y < footprint.y; y++)
            {
                for (int x = 0; x < footprint.x; x++)
                {
                    Vector2Int footprintTile =
                        destinationStop +
                        new Vector2Int(x, y);

                    for (int i = 0;
                         i < Directions.Length;
                         i++)
                    {
                        Vector2Int accessRoad =
                            footprintTile + Directions[i];
                        if (!IsRoad(accessRoad))
                        {
                            continue;
                        }

                        bool pathFound;
                        if (startRoad == accessRoad)
                        {
                            candidateRoadPath.Clear();
                            candidateRoadPath.Add(startRoad);
                            pathFound = true;
                        }
                        else
                        {
                            pathFound = TryBuildRoadPath(
                                startRoad,
                                accessRoad,
                                candidateRoadPath,
                                preventImmediateReverse,
                                forbiddenFirstStep);
                        }

                        if (!pathFound)
                        {
                            continue;
                        }

                        int candidateCount =
                            GetRoadsidePathCount(
                                candidateRoadPath);
                        if (candidateCount <= 0)
                        {
                            continue;
                        }

                        if (!found ||
                            candidateCount < result.Count)
                        {
                            CopyRoadsidePathWithSetback(
                                candidateRoadPath,
                                result,
                                out selectedAccessRoad);
                            found = true;
                        }
                    }
                }
            }

            return found;
        }

        private void CopyRoadsidePathWithSetback(
            IReadOnlyList<Vector2Int> source,
            List<Vector2Int> destination,
            out Vector2Int selectedAccessRoad)
        {
            destination.Clear();
            int count = GetRoadsidePathCount(source);

            for (int index = 0; index < count; index++)
            {
                destination.Add(source[index]);
            }

            selectedAccessRoad = destination.Count > 0
                ? destination[destination.Count - 1]
                : default;
        }

        private int GetRoadsidePathCount(
            IReadOnlyList<Vector2Int> source)
        {
            if (source == null || source.Count == 0)
            {
                return 0;
            }

            int setback = Mathf.Min(
                roadsideStopSetbackTiles,
                source.Count - 1);
            int count = source.Count - setback;

            if (!roadTrafficConfigured ||
                !holdRoadTrafficAtDestination ||
                roadTraffic == null)
            {
                return count;
            }

            while (count > 0 &&
                   !roadTraffic.IsSafeHoldTile(
                       source[count - 1]))
            {
                count--;
            }

            return count;
        }

        private bool TryBuildRoadsideApproachPath(
            Vector2Int startRoad,
            Vector2Int accessRoad,
            Vector2Int arrivalDirection,
            List<Vector2Int> result,
            bool preventImmediateReverse,
            Vector2Int forbiddenFirstStep)
        {
            if (services?.RoadRoutePlanning == null ||
                startRoad == accessRoad)
            {
                return TryFindRoadPathToApproach(
                    startRoad,
                    accessRoad,
                    arrivalDirection,
                    result,
                    preventImmediateReverse,
                    forbiddenFirstStep);
            }

            bool planned = services.RoadRoutePlanning.TryPlanRoadRoute(
                startRoad,
                accessRoad,
                out RoadRoutePlan route);
            bool satisfiesApproach = planned &&
                route.TileCount >= 2 &&
                route.Tiles[route.TileCount - 2] ==
                    accessRoad - arrivalDirection &&
                (!preventImmediateReverse ||
                 route.Tiles[1] != forbiddenFirstStep);

            if (!satisfiesApproach)
            {
                return TryFindRoadPathToApproach(
                    startRoad,
                    accessRoad,
                    arrivalDirection,
                    result,
                    preventImmediateReverse,
                    forbiddenFirstStep);
            }

            result.Clear();
            for (int index = 0; index < route.TileCount; index++)
            {
                result.Add(route.Tiles[index]);
            }

            return true;
        }

        private bool TryBuildRoadPath(
            Vector2Int startRoad,
            Vector2Int destinationRoad,
            List<Vector2Int> result,
            bool preventImmediateReverse,
            Vector2Int forbiddenFirstStep)
        {
            if (startRoad == destinationRoad)
            {
                return TryFindRoadCycle(
                    startRoad,
                    result,
                    preventImmediateReverse,
                    forbiddenFirstStep);
            }

            if (services?.RoadRoutePlanning == null)
            {
                return TryFindRoadPath(
                    startRoad,
                    destinationRoad,
                    result,
                    preventImmediateReverse,
                    forbiddenFirstStep);
            }

            if (!services.RoadRoutePlanning.TryPlanRoadRoute(
                    startRoad,
                    destinationRoad,
                    out RoadRoutePlan route) ||
                route.TileCount < 2 ||
                (preventImmediateReverse &&
                 route.Tiles[1] == forbiddenFirstStep))
            {
                return false;
            }

            result.Clear();
            for (int index = 0; index < route.TileCount; index++)
            {
                result.Add(route.Tiles[index]);
            }

            return true;
        }

        private bool ShouldUseRoadsideStop(
            Vector2Int stop)
        {
            if (!useRoadsideStopApproach)
            {
                return false;
            }

            return RoadsideStopFilter == null ||
                   RoadsideStopFilter(stop);
        }

        private bool TryFindRoadPathToApproach(
            Vector2Int startRoad,
            Vector2Int accessRoad,
            Vector2Int arrivalDirection,
            List<Vector2Int> result,
            bool preventImmediateReverse,
            Vector2Int forbiddenFirstStep)
        {
            if (!IsCardinalDirection(arrivalDirection))
            {
                return false;
            }

            Vector2Int predecessor =
                accessRoad - arrivalDirection;
            if (!IsRoad(predecessor))
            {
                return false;
            }

            if (startRoad == accessRoad)
            {
                return TryFindRoadCycleForApproach(
                    startRoad,
                    arrivalDirection,
                    result,
                    preventImmediateReverse,
                    forbiddenFirstStep);
            }

            if (predecessor == startRoad)
            {
                if (preventImmediateReverse &&
                    accessRoad == forbiddenFirstStep)
                {
                    return false;
                }

                result.Clear();
                result.Add(startRoad);
                result.Add(accessRoad);
                return true;
            }

            if (!TryFindRoadPath(
                    startRoad,
                    predecessor,
                    result,
                    preventImmediateReverse,
                    forbiddenFirstStep,
                    accessRoad,
                    true))
            {
                return false;
            }

            result.Add(accessRoad);
            return true;
        }

        private bool TryFindRoadCycleForApproach(
            Vector2Int start,
            Vector2Int arrivalDirection,
            List<Vector2Int> result,
            bool preventImmediateReverse,
            Vector2Int forbiddenFirstStep)
        {
            Vector2Int predecessor =
                start - arrivalDirection;
            if (!IsRoad(predecessor))
            {
                return false;
            }

            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int first = start + Directions[i];
                if (!IsRoad(first) ||
                    first == predecessor ||
                    (preventImmediateReverse &&
                     first == forbiddenFirstStep))
                {
                    continue;
                }

                searchQueue.Clear();
                cameFrom.Clear();
                visited.Clear();
                searchQueue.Enqueue(first);
                visited.Add(start);
                visited.Add(first);

                bool found = first == predecessor;
                while (searchQueue.Count > 0 && !found)
                {
                    Vector2Int current =
                        searchQueue.Dequeue();

                    for (int directionIndex = 0;
                         directionIndex < Directions.Length;
                         directionIndex++)
                    {
                        Vector2Int next =
                            current + Directions[directionIndex];

                        if (!IsRoad(next) ||
                            !visited.Add(next))
                        {
                            continue;
                        }

                        cameFrom[next] = current;
                        if (next == predecessor)
                        {
                            found = true;
                            break;
                        }

                        searchQueue.Enqueue(next);
                    }
                }

                if (!found)
                {
                    continue;
                }

                result.Clear();
                Vector2Int pathTile = predecessor;
                result.Add(pathTile);

                while (pathTile != first)
                {
                    if (!cameFrom.TryGetValue(
                            pathTile,
                            out Vector2Int previous))
                    {
                        result.Clear();
                        break;
                    }

                    pathTile = previous;
                    result.Add(pathTile);
                }

                if (result.Count == 0)
                {
                    continue;
                }

                result.Reverse();
                result.Insert(0, start);
                result.Add(start);
                return true;
            }

            return false;
        }

        private bool TryFindAccessRoad(
            Vector2Int stopTile,
            out Vector2Int roadTile
        )
        {
            if (IsRoad(stopTile))
            {
                roadTile = stopTile;
                return true;
            }

            TileType stopType = tileData.GetTileType(stopTile);
            Vector2Int footprint = TileFootprint.IsBuilding(stopType)
                ? tileData.GetFootprintSize(stopType)
                : Vector2Int.one;

            for (int y = 0; y < footprint.y; y++)
            {
                for (int x = 0; x < footprint.x; x++)
                {
                    Vector2Int footprintTile =
                        stopTile + new Vector2Int(x, y);

                    for (int i = 0; i < Directions.Length; i++)
                    {
                        Vector2Int candidate =
                            footprintTile + Directions[i];

                        if (IsRoad(candidate))
                        {
                            roadTile = candidate;
                            return true;
                        }
                    }
                }
            }

            roadTile = default;
            return false;
        }

        private bool TryFindRoadPath(
            Vector2Int start,
            Vector2Int destination,
            List<Vector2Int> result,
            bool preventImmediateReverse,
            Vector2Int forbiddenFirstStep,
            Vector2Int blockedTile = default,
            bool hasBlockedTile = false
        )
        {
            result.Clear();
            searchQueue.Clear();
            cameFrom.Clear();
            visited.Clear();

            if (!IsRoad(start) || !IsRoad(destination))
            {
                return false;
            }

            searchQueue.Enqueue(start);
            visited.Add(start);

            bool found = start == destination;

            while (searchQueue.Count > 0 && !found)
            {
                Vector2Int current = searchQueue.Dequeue();

                for (int i = 0; i < Directions.Length; i++)
                {
                    Vector2Int next =
                        current + Directions[i];

                    if (preventImmediateReverse &&
                        current == start &&
                        next == forbiddenFirstStep)
                    {
                        continue;
                    }

                    if (hasBlockedTile && next == blockedTile)
                    {
                        continue;
                    }

                    if (!IsRoad(next) ||
                        !visited.Add(next))
                    {
                        continue;
                    }

                    cameFrom[next] = current;

                    if (next == destination)
                    {
                        found = true;
                        break;
                    }

                    searchQueue.Enqueue(next);
                }
            }

            if (!found)
            {
                return false;
            }

            Vector2Int pathTile = destination;
            result.Add(pathTile);

            while (pathTile != start)
            {
                if (!cameFrom.TryGetValue(
                        pathTile,
                        out Vector2Int previous
                    ))
                {
                    result.Clear();
                    return false;
                }

                pathTile = previous;
                result.Add(pathTile);
            }

            result.Reverse();
            return true;
        }

        private bool TryFindRoadCycle(
            Vector2Int start,
            List<Vector2Int> result,
            bool preventImmediateReverse,
            Vector2Int forbiddenFirstStep)
        {
            result.Clear();

            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int first = start + Directions[i];

                if (!IsRoad(first) ||
                    (preventImmediateReverse &&
                     first == forbiddenFirstStep))
                {
                    continue;
                }

                searchQueue.Clear();
                cameFrom.Clear();
                visited.Clear();
                searchQueue.Enqueue(first);
                visited.Add(start);
                visited.Add(first);

                Vector2Int cycleEnd = default;
                bool found = false;

                while (searchQueue.Count > 0 && !found)
                {
                    Vector2Int current =
                        searchQueue.Dequeue();

                    for (int directionIndex = 0;
                         directionIndex < Directions.Length;
                         directionIndex++)
                    {
                        Vector2Int next =
                            current + Directions[directionIndex];

                        if (next == start &&
                            current != first)
                        {
                            cycleEnd = current;
                            found = true;
                            break;
                        }

                        if (!IsRoad(next) ||
                            !visited.Add(next))
                        {
                            continue;
                        }

                        cameFrom[next] = current;
                        searchQueue.Enqueue(next);
                    }
                }

                if (!found)
                {
                    continue;
                }

                result.Add(cycleEnd);
                Vector2Int pathTile = cycleEnd;

                while (pathTile != first)
                {
                    if (!cameFrom.TryGetValue(
                            pathTile,
                            out Vector2Int previous))
                    {
                        result.Clear();
                        break;
                    }

                    pathTile = previous;
                    result.Add(pathTile);
                }

                if (result.Count == 0)
                {
                    continue;
                }

                result.Reverse();
                result.Insert(0, start);
                result.Add(start);
                return true;
            }

            return false;
        }

        private bool IsRoad(Vector2Int tile)
        {
            if (!IsInsideGrid(tile) || tileData == null)
            {
                return false;
            }

            return tileData.GetTileType(tile) ==
                   TileType.Road;
        }

        private bool IsInsideGrid(Vector2Int tile)
        {
            if (worldGridAccess != null)
            {
                return worldGridAccess.IsInsideWorld(tile);
            }

            return
                tile.x >= 0 &&
                tile.y >= 0 &&
                tile.x < gridWidth &&
                tile.y < gridHeight;
        }

        private static bool IsCardinalDirection(
            Vector2Int direction)
        {
            return Mathf.Abs(direction.x) +
                   Mathf.Abs(direction.y) == 1;
        }

        private int GetNextStopIndex()
        {
            if (stops.Count == 0)
            {
                return -1;
            }

            if (hasDepartureStop)
            {
                return 0;
            }

            if (stops.Count == 1)
            {
                return loopRoute ? 0 : -1;
            }

            int nextIndex = currentStopIndex + 1;

            if (nextIndex < stops.Count)
            {
                return nextIndex;
            }

            return loopRoute
                ? 0
                : -1;
        }

        private Vector2Int GetCurrentStop()
        {
            if (hasDepartureStop)
            {
                return departureStop;
            }

            if (currentStopIndex < 0 ||
                currentStopIndex >= stops.Count)
            {
                return default;
            }

            return stops[currentStopIndex];
        }

        private Vector2Int GetNextStop()
        {
            int nextIndex = GetNextStopIndex();

            return nextIndex >= 0
                ? stops[nextIndex]
                : default;
        }

        private void CompleteRoute()
        {
            routeRequested = false;
            stopPresentationPending = false;
            currentSegmentUsesRoadsideStop = false;
            pendingOffRoadStopIndex = -1;
            ReleaseRoadTrafficAgent();
            State = BusRouteState.Completed;

            RouteCompleted?.Invoke();
        }

        private void SetRouteUnavailable()
        {
            routeRequested = false;
            stopPresentationPending = false;
            currentSegmentUsesRoadsideStop = false;
            pendingOffRoadStopIndex = -1;
            ReleaseRoadTrafficAgent();
            State = BusRouteState.RouteUnavailable;

            RouteUnavailable?.Invoke();
        }

        private void SubscribeRoadTrafficRecovery()
        {
            if (roadTrafficRecoverySubscribed || roadTraffic == null)
            {
                return;
            }

            roadTraffic.RecoveryRequested +=
                HandleRoadTrafficRecoveryRequested;
            roadTrafficRecoverySubscribed = true;
        }

        private void UnsubscribeRoadTrafficRecovery()
        {
            if (!roadTrafficRecoverySubscribed || roadTraffic == null)
            {
                return;
            }

            roadTraffic.RecoveryRequested -=
                HandleRoadTrafficRecoveryRequested;
            roadTrafficRecoverySubscribed = false;
        }

        private void HandleRoadTrafficRecoveryRequested(
            RoadTrafficRecoveryRequest request)
        {
            if (request.AgentId != roadTrafficAgentId ||
                !routeRequested ||
                State != BusRouteState.Moving)
            {
                return;
            }

            CurrentTile = request.CurrentTile;
            int currentIndex = currentRoadPath.IndexOf(
                request.CurrentTile);
            if (currentIndex >= 0)
            {
                currentRoadPathIndex = currentIndex;
            }

            bool rebuilt = RebuildCurrentSegment();
            Debug.LogWarning(
                $"[RoadTrafficRecovery] {request.Kind} route " +
                $"replan {(rebuilt ? "succeeded" : "failed")} at " +
                $"{request.CurrentTile} after {request.BlockedTicks} ticks.",
                this);
        }

        private void OnDestroy()
        {
            UnsubscribeRoadTrafficRecovery();
            ReleaseRoadTrafficAgent();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            gridWidth = Mathf.Max(1, gridWidth);
            gridHeight = Mathf.Max(1, gridHeight);

            secondsPerTile =
                Mathf.Max(0.01f, secondsPerTile);

            stopWaitSeconds =
                Mathf.Max(0f, stopWaitSeconds);
        }
#endif
    }
}
