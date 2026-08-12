using System;
using System.Collections.Generic;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Sim
{
    public struct CarSnapshot
    {
        public Vector2Int Home;
        public Vector2Int Work;
        public CarState State;
        public int RouteIndex;
        public int TileIndex;
        public int QueueSlot;
        public float QueueOffsetTiles;
        public int HomeSlot;
        public int WorkSlot;
        public bool IsVisible;
        public VehicleTripPurpose Purpose;
        public bool AwaitingNextWave { get; internal set; }
        public float IntersectionProgress01 { get; internal set; }
        public float LinkProgress01;
        public float RoundaboutProgress01 { get; internal set; }
        // 뷰 mirror(SyncCarSimMirrors)는 CommuteCar 원본이 아닌 스냅샷 복사본을 읽는다 —
        // 심·뷰가 SpeedFactor 하나를 공유하려면 차급이 스냅샷에 실려야 한다(분모 60 고정).
        public int SpeedFactorNumerator;
        // 이번 틱 credit 부족으로 자발 대기 중 — 뷰는 이걸 "심이 잡은 정지"와 구분해
        // 천장을 끄지 않고 감속 순항으로 흘린다(M1-3).
        public bool WaitingForSpeedCredit { get; internal set; }
        // 무정차로 연속 통과한 교차로 수(0..FreeFlowStreakCap). 판정·리셋은 Sim 단독.
        public int FreeFlowStreak { get; internal set; }
        // 이번 통근 중 도달한 최대 연결 단계(0..FreeFlowStreakCap). 보상은 이 값을 읽는다.
        public int FreeFlowStreakMax { get; internal set; }
    }

    internal sealed class CarSim : ICarRouteProvider
    {
        internal const int FreeFlowStreakCap = 3;
        // 연결 배수는 ArrivalEvent.Coins에 반영된다. 따라서 DistanceRewardService의
        // 거리 보너스도 이 금액을 기준으로 계산되어 연결 배수와 복리로 적용된다.
        private static readonly float[] FreeFlowStreakBonus =
            { 0f, 0f, 1f, 3f };
        private const float JumpThresholdHours = 1f;
        private const float StaleSpecialJourneyHours = 24f;
        private const int VehicleRetryDelayTicks = 10;
        private const int RouteRetryDelayTicks = 30;
        private const string PetrolStationBuildingId = "petrol_station";

        private enum SpecialTripStartFailure
        {
            None = 0,
            NoEligibleOrigin = 1,
            NoRoute = 2,
            VehicleCapacity = 3,
            ParkingCapacity = 4
        }

        private readonly SimConfig _cfg;
        private readonly CommuteScheduler _scheduler = new();
        private readonly CommuteTripSource _commuteTripSource = new();
        private readonly TripScheduler _tripScheduler;
        private readonly int _specialTransientCapacity;
        private readonly int _runtimeVehicleCapacity;
        private readonly List<Vector2Int> _sources = new(96);
        private readonly List<Vector2Int> _sinks = new(96);
        private readonly List<VehicleTripPurpose> _routinePurposes = new(96);
        private readonly List<List<Vector2Int>> _outboundRoutes = new(96);
        private readonly List<List<Vector2Int>> _returnRoutes = new(96);
        private readonly List<List<Vector2Int>> _viewOutboundRoutes = new(96);
        private readonly List<List<Vector2Int>> _viewReturnRoutes = new(96);
        private readonly List<int> _plannerRouteIndices = new(96);
        private readonly bool[] _enqueued;
        private readonly int[] _tileIndices;
        private readonly int[] _resumeRouteIndices;
        private readonly Vector2Int[] _resumeIncomingDirections;
        private readonly bool[] _hasResumeIncomingDirection;
        private readonly int[] _queueSlots;
        private readonly float[] _queueOffsets;
        private readonly float[] _intersectionProgress;
        private readonly float[] _linkProgress;
        private readonly float[] _roundaboutProgress;
        private readonly List<Vector2Int>[] _rescueRoutes;
        private readonly int[] _rescueViewRouteIndices;
        private readonly byte[] _rescueStages;
        // 속도 크레딧 적립은 틱당 1회 — _servicePerTick > 1이어도 라운드당 재질의에
        // 이중 적립되지 않도록 Step 진입 시 Clear한다.
        private readonly bool[] _creditAccrued;
        // 이번 틱 credit 거부 마킹(스냅샷 WaitingForSpeedCredit 원천). Step 진입 시 Clear.
        private readonly bool[] _creditWaiting;
        private readonly int[] _freeFlowStreak;
        private readonly int[] _freeFlowStreakMax;
        private readonly int[] _freeFlowCountedIntersection;
        private readonly bool[] _freeFlowTripActive;
        private readonly FreeFlowStreakLedger _freeFlowStreakLedger;
        private readonly int[] _offNetworkBlockedTicks;
        private readonly List<Vector2Int> _originAccessRoads = new(8);
        private readonly List<Vector2Int> _destinationAccessRoads = new(8);
        private readonly Stack<int> _freeSpecialViewRouteIndices = new();
        private readonly HashSet<
            (Vector2Int Destination, int Slot)> _reservedVisitorParking =
            new();
        private RoadQueueNetwork _net;
        private RoutePlanner _planner;
        private CityGrid _grid;
        private DemandMap _demands;
        private RoadNetwork _roadNetwork;
        private float _lastHour;
        private bool _hasLastHour;
        private long _lastReservationSweepAbsoluteHour;
        private bool _hasReservationSweepHour;
        private bool _needsSnap;
        private bool _populationInitialized;

        private readonly struct PreviousAssignment
        {
            public readonly CommuteCar Car;
            public readonly List<Vector2Int> Outbound;
            public readonly List<Vector2Int> Inbound;
            public readonly int ViewRouteIndex;
            // 진행 중 워치독 rescue 상태 — preserve 리빌드가 이걸 안 넘기면 rescue 경로
            // 위의 ResumeTile을 일반 경로에서 못 찾아 route[0] 순간이동이 재발한다.
            public readonly List<Vector2Int> RescueRoute;
            public readonly int RescueViewIndex;
            public readonly byte RescueStage;

            public PreviousAssignment(
                CommuteCar car,
                List<Vector2Int> outbound,
                List<Vector2Int> inbound,
                int viewRouteIndex,
                List<Vector2Int> rescueRoute = null,
                int rescueViewIndex = -1,
                byte rescueStage = 0)
            {
                Car = car;
                Outbound = outbound;
                Inbound = inbound;
                ViewRouteIndex = viewRouteIndex;
                RescueRoute = rescueRoute;
                RescueViewIndex = rescueViewIndex;
                RescueStage = rescueStage;
            }
        }

        public int CarCount => _scheduler.Cars.Count;
        public int SimulatedVehicleCount => _scheduler.ActiveCount;
        public int PendingTripCount => _tripScheduler.PendingCount;
        public int ActiveTripCount =>
            _commuteTripSource.ActiveCount + _tripScheduler.ActiveCount;
        internal int ReservedVisitorParkingCount =>
            _reservedVisitorParking.Count;
        internal IReadOnlyList<List<Vector2Int>> ActiveRoutes => _viewOutboundRoutes;
        internal IReadOnlyList<List<Vector2Int>> ActiveReturnRoutes => _viewReturnRoutes;
        public bool AllParkedHome
        {
            get
            {
                if (_tripScheduler.ActiveCount > 0)
                {
                    return false;
                }

                for (int i = 0; i < _scheduler.Cars.Count; i++)
                {
                    CommuteCar car = _scheduler.Cars[i];
                    if (car.IsTransient || car.SpecialTripReserved)
                    {
                        continue;
                    }

                    if (car.State != CarState.ParkedHome)
                    {
                        return false;
                    }
                }
                return true;
            }
        }
        internal bool LastStepJumped { get; private set; }
        internal int RescueRerouteCount { get; private set; }
        internal int RescueRestartCount { get; private set; }
        internal int LastRescueCarId { get; private set; } = -1;
        internal Vector2Int LastRescueTile { get; private set; }
        internal bool HasCompletedRetirements
        {
            get
            {
                for (int i = 0; i < _scheduler.Cars.Count; i++)
                {
                    CommuteCar car = _scheduler.Cars[i];
                    if (car.IsTransient || car.SpecialTripReserved)
                    {
                        continue;
                    }

                    if (car.RetireReason == RetireReason.HomeLost
                        && (car.State == CarState.ParkedHome || car.State == CarState.ParkedWork))
                    {
                        return true;
                    }
                    if (car.RetireReason == RetireReason.WorkLost
                        && car.State == CarState.ParkedHome)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public CarSim(
            in SimConfig cfg,
            FreeFlowStreakLedger freeFlowStreakLedger = null)
        {
            _cfg = cfg;
            _freeFlowStreakLedger = freeFlowStreakLedger;
            int maxCars = Math.Max(1, cfg.MaxSimCars);
            int requestedSpecialVehicleLimit =
                cfg.MaxConcurrentSpecialTrips > 0
                    ? cfg.MaxConcurrentSpecialTrips
                    : 8;
            _specialTransientCapacity = Math.Min(
                requestedSpecialVehicleLimit,
                maxCars);
            _runtimeVehicleCapacity =
                maxCars + _specialTransientCapacity;
            _tripScheduler = new TripScheduler(
                cfg.MaxPendingVehicleTrips > 0
                    ? cfg.MaxPendingVehicleTrips
                    : 256,
                _specialTransientCapacity);
            _enqueued = new bool[_runtimeVehicleCapacity];
            _tileIndices = new int[_runtimeVehicleCapacity];
            _resumeRouteIndices = new int[_runtimeVehicleCapacity];
            _resumeIncomingDirections =
                new Vector2Int[_runtimeVehicleCapacity];
            _hasResumeIncomingDirection =
                new bool[_runtimeVehicleCapacity];
            _queueSlots = new int[_runtimeVehicleCapacity];
            _queueOffsets = new float[_runtimeVehicleCapacity];
            _intersectionProgress = new float[_runtimeVehicleCapacity];
            _linkProgress = new float[_runtimeVehicleCapacity];
            _roundaboutProgress = new float[_runtimeVehicleCapacity];
            _rescueRoutes =
                new List<Vector2Int>[_runtimeVehicleCapacity];
            _rescueViewRouteIndices =
                new int[_runtimeVehicleCapacity];
            _rescueStages = new byte[_runtimeVehicleCapacity];
            _offNetworkBlockedTicks =
                new int[_runtimeVehicleCapacity];
            _creditAccrued = new bool[_runtimeVehicleCapacity];
            _creditWaiting = new bool[_runtimeVehicleCapacity];
            _freeFlowStreak = new int[_runtimeVehicleCapacity];
            _freeFlowStreakMax = new int[_runtimeVehicleCapacity];
            _freeFlowCountedIntersection = new int[_runtimeVehicleCapacity];
            _freeFlowTripActive = new bool[_runtimeVehicleCapacity];
            Array.Fill(_queueSlots, -1);
            Array.Clear(_queueOffsets, 0, _queueOffsets.Length);
            Array.Fill(_intersectionProgress, -1f);
            Array.Clear(_linkProgress, 0, _linkProgress.Length);
            Array.Fill(_roundaboutProgress, -1f);
            Array.Fill(_rescueViewRouteIndices, -1);
            Array.Fill(_freeFlowCountedIntersection, -1);
        }

        internal void ClearPopulation()
        {
            ReleaseAllSpecialJourneys();
            _scheduler.Clear();
            _commuteTripSource.Clear();
            _tripScheduler.Clear();
            Array.Clear(_enqueued, 0, _enqueued.Length);
            Array.Clear(_tileIndices, 0, _tileIndices.Length);
            Array.Clear(
                _resumeRouteIndices,
                0,
                _resumeRouteIndices.Length);
            Array.Clear(
                _hasResumeIncomingDirection,
                0,
                _hasResumeIncomingDirection.Length);
            Array.Fill(_queueSlots, -1);
            Array.Clear(_queueOffsets, 0, _queueOffsets.Length);
            Array.Fill(_intersectionProgress, -1f);
            Array.Clear(_linkProgress, 0, _linkProgress.Length);
            Array.Fill(_roundaboutProgress, -1f);
            Array.Clear(_rescueRoutes, 0, _rescueRoutes.Length);
            Array.Fill(_rescueViewRouteIndices, -1);
            Array.Clear(_rescueStages, 0, _rescueStages.Length);
            Array.Clear(_offNetworkBlockedTicks, 0, _offNetworkBlockedTicks.Length);
            Array.Clear(_freeFlowStreak, 0, _freeFlowStreak.Length);
            Array.Clear(_freeFlowStreakMax, 0, _freeFlowStreakMax.Length);
            Array.Fill(_freeFlowCountedIntersection, -1);
            Array.Clear(_freeFlowTripActive, 0, _freeFlowTripActive.Length);
            _hasLastHour = false;
            _needsSnap = false;
            _populationInitialized = false;
            _freeSpecialViewRouteIndices.Clear();
            _reservedVisitorParking.Clear();
        }

        internal bool TryScheduleSpecialBuildingVisit(
            SpecialBuildingVisitTripRequest request)
        {
            return _tripScheduler.TryEnqueue(request);
        }

        public void Rebuild(
            DemandMap demands,
            RoutePlanner planner,
            RoadQueueNetwork net,
            bool preserveExistingAssignments = false,
            CityGrid grid = null,
            RoadNetwork roadNetwork = null,
            SimEventBuffer events = null)
        {
            if (demands == null) throw new ArgumentNullException(nameof(demands));
            if (planner == null) throw new ArgumentNullException(nameof(planner));
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _planner = planner;
            _grid = grid ?? _grid;
            _demands = demands;
            _roadNetwork = roadNetwork ?? _roadNetwork;
            var oldWorksByHome = new Dictionary<Vector2Int, List<Vector2Int>>();
            for (int i = 0; i < _scheduler.Cars.Count; i++)
            {
                CommuteCar car = _scheduler.Cars[i];
                if (car.IsTransient || car.State == CarState.Inactive) continue;
                if (!oldWorksByHome.TryGetValue(car.Home, out List<Vector2Int> works))
                    oldWorksByHome[car.Home] = works = new List<Vector2Int>();
                works.Add(car.Work);
            }
            var previousAssignments = new List<PreviousAssignment>(CarCount);
            var resumeIncomingByCar =
                new Dictionary<CommuteCar, Vector2Int>(CarCount);
            var resumeRouteIndexByCar =
                new Dictionary<CommuteCar, int>(CarCount);
            var resumeRouteRestartedAtCurrent =
                new HashSet<CommuteCar>();
            Array.Clear(
                _hasResumeIncomingDirection,
                0,
                _hasResumeIncomingDirection.Length);
            // 클리어 전에 생존 차의 현재 월드 타일을 차 객체에 실어둔다. 이게 없으면
            // 재큐잉이 route[0](집)에서 일어나 건설할 때마다 주행 차가 순간이동한다
            // (라이브 계측 2026-07-20: 도로 1개 배치에 4대·최대 5.90타일).
            for (int i = 0; i < CarCount; i++)
            {
                CommuteCar survivor = _scheduler.Cars[i];
                bool onRoad = (survivor.State == CarState.Outbound || survivor.State == CarState.Inbound)
                    && _enqueued[i]
                    && TryRoute(i, out List<Vector2Int> oldRoute)
                    && oldRoute.Count > 0;
                if (!onRoad) { survivor.HasResume = false; continue; }
                TryRoute(i, out List<Vector2Int> currentRoute);
                int routeIndex = Mathf.Clamp(
                    _tileIndices[i],
                    0,
                    currentRoute.Count - 1);
                Vector2Int resumeTile = currentRoute[routeIndex];
                bool capturedIncoming = false;
                Vector2Int incomingDirection = default;
                if (_net.TryLocateCar(
                        i,
                        out Vector2Int liveTile,
                        out Dir liveDirection,
                        out _))
                {
                    int liveRouteIndex = FindRouteIndexAtOrAfter(
                        currentRoute,
                        liveTile,
                        routeIndex);
                    if (liveRouteIndex >= 0)
                    {
                        routeIndex = liveRouteIndex;
                        resumeTile = liveTile;
                        incomingDirection = DirectionVector(liveDirection);
                        capturedIncoming = true;
                    }
                }
                if (!capturedIncoming && routeIndex > 0 &&
                    TryRouteDirection(
                        currentRoute[routeIndex] -
                        currentRoute[routeIndex - 1],
                        out Dir routeDirection))
                {
                    incomingDirection = DirectionVector(routeDirection);
                    capturedIncoming = true;
                }

                survivor.ResumeTile = resumeTile;
                survivor.HasResume = true;
                resumeRouteIndexByCar[survivor] = routeIndex;
                if (capturedIncoming)
                {
                    resumeIncomingByCar[survivor] = incomingDirection;
                    _resumeIncomingDirections[i] = incomingDirection;
                    _hasResumeIncomingDirection[i] = true;
                }
            }
            // 구 짝/경로 참조는 반드시 기존 _cars 인덱스 순서 그대로 담는다. 주행 차를
            // 먼저 담으면 preserve 리빌드에서 주차·주행 혼재 시 인덱스가 재배열되어,
            // 인덱스 기반 View 미러가 전체 새로고침된다(이 PR이 잡으려는 증상 그 자체).
            for (int i = 0; i < CarCount; i++)
            {
                CommuteCar car = _scheduler.Cars[i];
                if (car.IsTransient)
                {
                    continue;
                }

                if (car.RouteIndex < 0
                    || car.RouteIndex >= _outboundRoutes.Count
                    || car.RouteIndex >= _returnRoutes.Count
                    || car.RouteIndex >= _plannerRouteIndices.Count)
                {
                    continue;
                }
                previousAssignments.Add(new PreviousAssignment(
                    car,
                    _outboundRoutes[car.RouteIndex],
                    _returnRoutes[car.RouteIndex],
                    _plannerRouteIndices[car.RouteIndex],
                    _rescueRoutes[i],
                    _rescueViewRouteIndices[i],
                    _rescueStages[i]));
            }
            // preserve 리빌드에서 새 인덱스로 넘길 rescue 상태 (append 순서 = 새 car 인덱스).
            var rescueCarry = new List<(int index, PreviousAssignment src)>();
            List<List<Vector2Int>> previousViewOutbound = preserveExistingAssignments
                ? new List<List<Vector2Int>>(_viewOutboundRoutes)
                : null;
            List<List<Vector2Int>> previousViewReturn = preserveExistingAssignments
                ? new List<List<Vector2Int>>(_viewReturnRoutes)
                : null;
            _sources.Clear();
            _sinks.Clear();
            _routinePurposes.Clear();
            _outboundRoutes.Clear();
            _returnRoutes.Clear();
            _viewOutboundRoutes.Clear();
            _viewReturnRoutes.Clear();
            _plannerRouteIndices.Clear();
            if (preserveExistingAssignments)
            {
                _viewOutboundRoutes.AddRange(previousViewOutbound);
                _viewReturnRoutes.AddRange(previousViewReturn);
            }
            else
            {
                for (int i = 0; i < planner.CarRoutes.Count; i++)
                {
                    _viewOutboundRoutes.Add(planner.CarRoutes[i]);
                    _viewReturnRoutes.Add(planner.ReturnRoutes[i]);
                }
            }

            IReadOnlyList<Demand> pairs = demands.Demands;
            int count = Math.Min(pairs.Count, planner.CarRoutes.Count);
            var consumedPairs = new bool[count];
            if (preserveExistingAssignments)
            {
                // 건물 변경은 기존 차 순서와 구 경로 참조를 먼저 싣는다. 새 짝만 뒤에
                // 붙여 인덱스 기반 View 미러가 다른 논리 차로 재바인딩되지 않게 한다.
                for (int p = 0; p < previousAssignments.Count; p++)
                {
                    PreviousAssignment previous = previousAssignments[p];
                    CommuteCar car = previous.Car;
                    RetireReason reason = RetireReasonFor(demands, car);
                    car.RetireReason = reason;
                    if (reason != RetireReason.None)
                    {
                        if (RetirementCompleted(car, reason)
                            || !HasUsableRoutes(previous))
                        {
                            continue;
                        }
                        // 포기 귀가로 전환되는 차는 rescue 목적지(회사)가 무효 — rescue를
                        // 버리고 Prepare의 제자리 재계획 경로를 쓴다. 그 외 은퇴 차는 유지.
                        bool flipped = reason == RetireReason.WorkLost
                            && car.State == CarState.Outbound;
                        Vector2Int resumeBeforePrepare = car.ResumeTile;
                        List<Vector2Int> preparedInbound =
                            PrepareWorkLostReturn(
                                car,
                                reason,
                                previous.Inbound);
                        UpdateFallbackResumeState(
                            car,
                            resumeBeforePrepare,
                            preparedInbound,
                            resumeRouteIndexByCar,
                            resumeIncomingByCar);
                        if (car.HasResume &&
                            !ReferenceEquals(
                                preparedInbound,
                                previous.Inbound) &&
                            preparedInbound.Count > 0 &&
                            preparedInbound[0] == car.ResumeTile)
                        {
                            resumeRouteRestartedAtCurrent.Add(car);
                        }
                        previous = new PreviousAssignment(
                            car,
                            previous.Outbound,
                            preparedInbound,
                            previous.ViewRouteIndex,
                            flipped ? null : previous.RescueRoute,
                            flipped ? -1 : previous.RescueViewIndex,
                            flipped ? (byte)0 : previous.RescueStage);
                        AppendPreviousAssignment(previous, preserveViewIndex: true);
                        if (previous.RescueRoute != null)
                            rescueCarry.Add((_sources.Count - 1, previous));
                        continue;
                    }

                    int pairIndex = FindPair(
                        pairs,
                        planner,
                        consumedPairs,
                        car.Home,
                        car.Work);
                    if (pairIndex < 0) continue;
                    consumedPairs[pairIndex] = true;
                    AppendPreviousAssignment(previous, preserveViewIndex: true);
                    if (previous.RescueRoute != null)
                        rescueCarry.Add((_sources.Count - 1, previous));
                }
            }
            else
            {
                // 은퇴 carry-over를 planner 신규 배정보다 먼저 싣는다 — 스케줄러의
                // MaxSimCars 확정 루프는 꼬리를 자르므로, 뒤에 실으면 상한 포화 시
                // 주행 중 은퇴 차가 신규 배정에 밀려 즉시 소멸한다("트립 완주 후
                // 은퇴" 계약 위반).
                for (int i = 0; i < previousAssignments.Count; i++)
                {
                    PreviousAssignment previous = previousAssignments[i];
                    CommuteCar car = previous.Car;
                    RetireReason reason = RetireReasonFor(demands, car);
                    // None도 대입해야 한다 — 철거 후 같은 자리 재건축 시 stale WorkLost가
                    // 남으면 조기 퇴근·헛 리빌드가 생긴다(preserve 경로와 대칭).
                    car.RetireReason = reason;
                    if (reason == RetireReason.None) continue;
                    if (RetirementCompleted(car, reason)
                        || !HasUsableRoutes(previous))
                    {
                        continue;
                    }

                    List<Vector2Int> outbound = previous.Outbound;
                    Vector2Int resumeBeforePrepare = car.ResumeTile;
                    List<Vector2Int> inbound =
                        PrepareWorkLostReturn(car, reason, previous.Inbound);
                    UpdateFallbackResumeState(
                        car,
                        resumeBeforePrepare,
                        inbound,
                        resumeRouteIndexByCar,
                        resumeIncomingByCar);
                    if (car.HasResume &&
                        !ReferenceEquals(inbound, previous.Inbound) &&
                        inbound.Count > 0 &&
                        inbound[0] == car.ResumeTile)
                    {
                        resumeRouteRestartedAtCurrent.Add(car);
                    }
                    // preserve가 아닌 리빌드 = 라우팅 입력(도로·일방통행·턴 표지판·config)이
                    // 바뀐 리빌드다. 은퇴 carry-over의 잔여 구간도 최신 규칙으로 재계획해야
                    // 구 경로가 새 규칙을 위반하거나 사라진 도로를 밟지 않는다.
                    // (WorkLost+Outbound는 위 Prepare가 이미 최신 스냅샷으로 재계획함.
                    //  preserve 리빌드는 라우팅 불변이라 구 경로 = 재계획 결과 — 재계획 불요.)
                    if (car.State == CarState.Outbound)
                    {
                        List<Vector2Int> replanned = ReplanLeg(
                            outbound,
                            car,
                            fromResume: true);
                        if (replanned != null)
                        {
                            outbound = replanned;
                            resumeRouteRestartedAtCurrent.Add(car);
                        }
                    }
                    else if (car.State == CarState.Inbound
                        && ReferenceEquals(inbound, previous.Inbound))
                    {
                        List<Vector2Int> replanned = ReplanLeg(
                            inbound,
                            car,
                            fromResume: true);
                        if (replanned != null)
                        {
                            inbound = replanned;
                            resumeRouteRestartedAtCurrent.Add(car);
                        }
                    }
                    else if (car.State == CarState.ParkedWork)
                        inbound = ReplanLeg(inbound, car, fromResume: false) ?? inbound;
                    previous = new PreviousAssignment(
                        car,
                        outbound,
                        inbound,
                        previous.ViewRouteIndex);
                    AppendPreviousAssignment(previous, preserveViewIndex: false);
                }

                for (int i = 0; i < count; i++)
                {
                    if (!HasUsableRoutes(planner, i)) continue;
                    AppendPlannerAssignment(pairs[i], planner, i);
                    consumedPairs[i] = true;
                }
            }

            if (preserveExistingAssignments)
            {
                // 기존 짝이 모두 같은 인덱스에 자리 잡은 뒤에만 신규 배정을 추가한다.
                for (int i = 0; i < count; i++)
                {
                    if (consumedPairs[i] || !HasUsableRoutes(planner, i)) continue;
                    AppendPlannerAssignment(pairs[i], planner, i, appendViewRoute: true);
                }
            }

            _scheduler.Rebuild(
                _sources,
                _sinks,
                demands.WorkCapacityAt,
                demands.CommuteWindowAt,   // 시각 인자 4개 대체 — 창의 출처를 목적지 하나로 모은다
                Math.Max(1, _cfg.CarsPerHouse),
                Math.Min(_enqueued.Length, Math.Max(1, _cfg.MaxSimCars)),
                deferNewAssignments: _populationInitialized,
                purposes: _routinePurposes,
                transientStorageCapacity: _specialTransientCapacity);
            Array.Clear(
                _hasResumeIncomingDirection,
                0,
                _hasResumeIncomingDirection.Length);
            Array.Clear(
                _resumeRouteIndices,
                0,
                _resumeRouteIndices.Length);
            for (int carIndex = 0;
                 carIndex < _scheduler.Cars.Count;
                 carIndex++)
            {
                CommuteCar rebuiltCar = _scheduler.Cars[carIndex];
                if (resumeRouteIndexByCar.TryGetValue(
                        rebuiltCar,
                        out int resumeRouteIndex))
                {
                    _resumeRouteIndices[carIndex] =
                        resumeRouteRestartedAtCurrent.Contains(rebuiltCar)
                            ? 0
                            : resumeRouteIndex;
                }

                if (!resumeIncomingByCar.TryGetValue(
                        rebuiltCar,
                        out Vector2Int incomingDirection))
                {
                    continue;
                }

                _resumeIncomingDirections[carIndex] = incomingDirection;
                _hasResumeIncomingDirection[carIndex] = true;
            }
            if (events != null)
            {
                var newWorksByHome = new Dictionary<Vector2Int, List<Vector2Int>>();
                for (int i = 0; i < _scheduler.Cars.Count; i++)
                {
                    CommuteCar car = _scheduler.Cars[i];
                    if (car.IsTransient || car.State == CarState.Inactive) continue;
                    if (!newWorksByHome.TryGetValue(car.Home, out List<Vector2Int> works))
                        newWorksByHome[car.Home] = works = new List<Vector2Int>();
                    works.Add(car.Work);
                }
                foreach (KeyValuePair<Vector2Int, List<Vector2Int>> pair in newWorksByHome)
                {
                    if (!oldWorksByHome.TryGetValue(pair.Key, out List<Vector2Int> oldWorks)) continue;
                    var unmatchedOld = new List<Vector2Int>(oldWorks);
                    for (int i = 0; i < pair.Value.Count; i++)
                    {
                        Vector2Int newWork = pair.Value[i];
                        int same = unmatchedOld.IndexOf(newWork);
                        if (same >= 0) { unmatchedOld.RemoveAt(same); continue; }
                        if (unmatchedOld.Count == 0) continue;
                        Vector2Int oldWork = unmatchedOld[0];
                        unmatchedOld.RemoveAt(0);
                        events.QueueJobChanged(new JobChangedEvent(pair.Key, oldWork, newWork));
                    }
                }
            }
            _populationInitialized = true;
            ApplyCommuterVehicleClasses();
            _commuteTripSource.Prune(_scheduler.Cars);
            _tripScheduler.AllowImmediateRetry();
            Array.Clear(_enqueued, 0, _enqueued.Length);
            if (!preserveExistingAssignments)
                Array.Clear(_tileIndices, 0, _tileIndices.Length);
            Array.Fill(_queueSlots, -1);
            Array.Clear(_queueOffsets, 0, _queueOffsets.Length);
            Array.Fill(_intersectionProgress, -1f);
            Array.Clear(_linkProgress, 0, _linkProgress.Length);
            Array.Fill(_roundaboutProgress, -1f);
            Array.Clear(_rescueRoutes, 0, _rescueRoutes.Length);
            Array.Fill(_rescueViewRouteIndices, -1);
            Array.Clear(_rescueStages, 0, _rescueStages.Length);
            Array.Clear(_offNetworkBlockedTicks, 0, _offNetworkBlockedTicks.Length);
            if (!preserveExistingAssignments)
            {
                Array.Clear(_freeFlowStreak, 0, _freeFlowStreak.Length);
                Array.Clear(_freeFlowStreakMax, 0, _freeFlowStreakMax.Length);
                Array.Fill(_freeFlowCountedIntersection, -1);
                Array.Clear(_freeFlowTripActive, 0, _freeFlowTripActive.Length);
            }
            // preserve 리빌드: 진행 중 rescue 상태를 새 car 인덱스로 재적용한다. 뷰 리스트는
            // 통째로 복사됐으므로 rescue 뷰 인덱스도 그대로 유효하다. (non-preserve는
            // 라우팅이 바뀐 리빌드라 rescue 경로 자체가 무효 — 기존대로 폐기.)
            for (int i = 0; i < rescueCarry.Count; i++)
            {
                (int index, PreviousAssignment src) = rescueCarry[i];
                if (index < 0 || index >= _rescueRoutes.Length) continue;
                _rescueRoutes[index] = src.RescueRoute;
                _rescueViewRouteIndices[index] = src.RescueViewIndex;
                _rescueStages[index] = src.RescueStage;
            }
            RebuildActiveSpecialRoutes(preserveExistingAssignments);
            _needsSnap = true;
        }

        private static int FindPair(
            IReadOnlyList<Demand> pairs,
            RoutePlanner planner,
            bool[] consumed,
            Vector2Int home,
            Vector2Int work)
        {
            int count = Math.Min(pairs.Count, planner.CarRoutes.Count);
            for (int i = 0; i < count; i++)
            {
                if (consumed[i]
                    || pairs[i].Source != home
                    || pairs[i].Sink != work
                    || !HasUsableRoutes(planner, i))
                {
                    continue;
                }
                return i;
            }
            return -1;
        }

        private static bool HasUsableRoutes(RoutePlanner planner, int index)
        {
            List<Vector2Int> outbound = planner.CarRoutes[index];
            List<Vector2Int> inbound = planner.ReturnRoutes[index];
            return outbound != null && inbound != null
                && outbound.Count > 0 && inbound.Count > 0;
        }

        private static bool HasUsableRoutes(PreviousAssignment previous) =>
            previous.Outbound != null && previous.Inbound != null
            && previous.Outbound.Count > 0 && previous.Inbound.Count > 0;

        private static RetireReason RetireReasonFor(
            DemandMap demands,
            CommuteCar car) =>
            !demands.ContainsSource(car.Home)
                ? RetireReason.HomeLost
                : !demands.ContainsSink(car.Work)
                    ? RetireReason.WorkLost
                    : RetireReason.None;

        private static bool RetirementCompleted(
            CommuteCar car,
            RetireReason reason) =>
            reason == RetireReason.HomeLost
                ? car.State == CarState.ParkedHome || car.State == CarState.ParkedWork
                : reason == RetireReason.WorkLost && car.State == CarState.ParkedHome;

        // 은퇴 carry-over 구간을 최신 Plan 규칙으로 다시 계산한다. 주행 중(fromResume)이면
        // 현재 타일에서, 주차 대기면 구간 원점에서 같은 종점으로. 실패(고립·도로 소실)는
        // null — 호출부가 구 경로 유지로 폴백하고 워치독이 수렴을 책임진다.
        private List<Vector2Int> ReplanLeg(
            List<Vector2Int> route,
            CommuteCar car,
            bool fromResume)
        {
            Vector2Int from = fromResume && car.HasResume
                ? car.ResumeTile
                : route[0];
            List<Vector2Int> replanned = fromResume && car.HasResume
                ? ReplanWithResumeHeading(
                    from,
                    route[route.Count - 1],
                    car)
                : _planner.ReplanFrom(
                    from,
                    route[route.Count - 1]);
            return replanned != null && replanned.Count > 0 ? replanned : null;
        }

        // 반환값 = 이 차가 실제로 탈 인바운드 경로. 주행 중 전환이면 현재 타일에서
        // 구 귀가 종점까지 재계획한 경로(제자리 포기 귀가 — 순간이동 0), 아니면 구 경로.
        private List<Vector2Int> PrepareWorkLostReturn(
            CommuteCar car,
            RetireReason reason,
            List<Vector2Int> inboundRoute)
        {
            if (reason != RetireReason.WorkLost || car.State != CarState.Outbound)
                return inboundRoute;
            // 회사 철거 시 출근 보상을 만들지 않고, 즉시 "포기 귀가"로 전환한다.
            Vector2Int returnOrigin = car.HasResume
                ? car.ResumeTile
                : car.Home;
            _commuteTripSource.ReplaceWithReturnTrip(car, returnOrigin);
            car.State = CarState.Inbound;
            car.Distance = 0f;
            if (!car.HasResume) return inboundRoute;
            // ResumeTile은 아웃바운드 경로에서 캡처됐다. 일방통행 등으로 왕복 경로가
            // 갈라져 있으면 인바운드 경로에서 못 찾아 start=0(철거된 회사 쪽 타일)으로
            // 폴백한다 — 이 파일 상단 주석이 막으려는 순간이동 그 자체. 현재 타일에서
            // 구 귀가 종점으로 재계획해 제자리에서 이어 달리게 한다(워치독 L2와 같은 기계).
            List<Vector2Int> replanned = ReplanWithResumeHeading(
                car.ResumeTile,
                inboundRoute[inboundRoute.Count - 1],
                car);
            if (replanned != null && replanned.Count > 0)
                return replanned;   // replanned[0] == ResumeTile — 재큐잉이 제자리에서 찾는다
            // 재계획 실패(경로 고립 등) 예외 경로에서만 최근접 타일 스냅으로 폴백한다.
            // 이 스냅은 거리 상한이 없지만, 여기 도달하는 순간 이미 현재 위치에서 집으로
            // 가는 길 자체가 없는 상태라 어차피 워치독 수렴 대상이다.
            int best = 0;
            int bestDist = int.MaxValue;
            for (int p = 0; p < inboundRoute.Count; p++)
            {
                int dist = Mathf.Abs(inboundRoute[p].x - car.ResumeTile.x)
                    + Mathf.Abs(inboundRoute[p].y - car.ResumeTile.y);
                if (dist >= bestDist) continue;
                bestDist = dist;
                best = p;
                if (dist == 0) break;
            }
            car.ResumeTile = inboundRoute[best];
            int carIndex = FindCarIndex(car);
            if (carIndex >= 0)
            {
                _hasResumeIncomingDirection[carIndex] = false;
            }
            return inboundRoute;
        }

        private List<Vector2Int> ReplanWithResumeHeading(
            Vector2Int from,
            Vector2Int to,
            CommuteCar car)
        {
            Vector2Int? incomingDirection =
                ResumeIncomingDirection(FindCarIndex(car));
            if (!incomingDirection.HasValue)
            {
                return _planner.ReplanFrom(from, to);
            }

            List<Vector2Int> headingPreservingRoute =
                _planner.PlanVehicleTrip(
                    from,
                    to,
                    requiredFirstDirection: null,
                    requiredArrivalDirection: null,
                    initialIncomingDirection: incomingDirection);
            // 방향 제약 경로를 찾지 못해도 최신 도로 규칙을 무시한 기존 경로를
            // 유지하지 않고, 현재 위치 기준 일반 재계획으로 안전하게 폴백한다.
            return headingPreservingRoute ??
                _planner.ReplanFrom(from, to);
        }

        private static void UpdateFallbackResumeState(
            CommuteCar car,
            Vector2Int previousResumeTile,
            IReadOnlyList<Vector2Int> route,
            IDictionary<CommuteCar, int> resumeRouteIndexByCar,
            IDictionary<CommuteCar, Vector2Int> resumeIncomingByCar)
        {
            if (!car.HasResume || car.ResumeTile == previousResumeTile)
            {
                return;
            }

            int routeIndex = FindRouteIndexAtOrAfter(
                route,
                car.ResumeTile,
                cursor: 0);
            if (routeIndex >= 0)
            {
                resumeRouteIndexByCar[car] = routeIndex;
            }
            resumeIncomingByCar.Remove(car);
        }

        private void AppendPreviousAssignment(
            PreviousAssignment previous,
            bool preserveViewIndex)
        {
            int viewRouteIndex = previous.ViewRouteIndex;
            if (!preserveViewIndex
                || viewRouteIndex < 0
                || viewRouteIndex >= _viewOutboundRoutes.Count
                || viewRouteIndex >= _viewReturnRoutes.Count)
            {
                viewRouteIndex = _viewOutboundRoutes.Count;
                _viewOutboundRoutes.Add(previous.Outbound);
                _viewReturnRoutes.Add(previous.Inbound);
            }
            else
            {
                // 인덱스 보존 시에도 내용은 이 차의 실제 주행 경로로 덮는다 — 포기 귀가
                // 재계획처럼 carry-over 경로가 교체된 경우 뷰 폴리라인이 따라오게.
                _viewOutboundRoutes[viewRouteIndex] = previous.Outbound;
                _viewReturnRoutes[viewRouteIndex] = previous.Inbound;
            }
            _sources.Add(previous.Car.Home);
            _sinks.Add(previous.Car.Work);
            _routinePurposes.Add(previous.Car.RoutinePurpose);
            _outboundRoutes.Add(previous.Outbound);
            _returnRoutes.Add(previous.Inbound);
            _plannerRouteIndices.Add(viewRouteIndex);
        }

        private void AppendPlannerAssignment(
            Demand pair,
            RoutePlanner planner,
            int plannerIndex,
            bool appendViewRoute = false)
        {
            int viewRouteIndex;
            if (!appendViewRoute)
            {
                viewRouteIndex = plannerIndex;
            }
            else
            {
                viewRouteIndex = _viewOutboundRoutes.Count;
                _viewOutboundRoutes.Add(planner.CarRoutes[plannerIndex]);
                _viewReturnRoutes.Add(planner.ReturnRoutes[plannerIndex]);
            }
            _sources.Add(pair.Source);
            _sinks.Add(pair.Sink);
            _routinePurposes.Add(pair.SinkType == TileType.School
                ? VehicleTripPurpose.School
                : VehicleTripPurpose.Commute);
            _outboundRoutes.Add(planner.CarRoutes[plannerIndex]);
            _returnRoutes.Add(planner.ReturnRoutes[plannerIndex]);
            _plannerRouteIndices.Add(viewRouteIndex);
        }

        public StepResult Step(float gameHour, RoadQueueNetwork net, SimEventBuffer events)
            => Step(gameHour, net, events, null, 0);

        internal StepResult Step(
            float gameHour,
            RoadQueueNetwork net,
            SimEventBuffer events,
            ISignalGate signalGate,
            int tick)
            => Step(0L, gameHour, net, events, signalGate, tick);

        internal StepResult Step(
            long gameDay,
            float gameHour,
            RoadQueueNetwork net,
            SimEventBuffer events,
            ISignalGate signalGate,
            int tick,
            RoadTrafficCoordinator roadTraffic = null)
        {
            if (net == null) throw new ArgumentNullException(nameof(net));
            if (events == null) throw new ArgumentNullException(nameof(events));
            _net = net;
            Array.Clear(_creditAccrued, 0, _creditAccrued.Length);
            Array.Clear(_creditWaiting, 0, _creditWaiting.Length);

            bool jumped = _hasLastHour
                && Mathf.Repeat(gameHour - _lastHour, 24f) > JumpThresholdHours;
            LastStepJumped = jumped;
            if (_needsSnap || jumped)
            {
                net.RemoveAllCars();
                roadTraffic?.ResetNetworkOccupancy();
                if (jumped)
                {
                    CancelAllSpecialJourneys();
                    _commuteTripSource.CancelActiveTrips();
                    // 로드·배속 점프: 이동 연출을 복원하지 않는다 — 전 차 주차로 조대 수렴.
                    _scheduler.SnapToHour(gameHour);
                    for (int i = 0; i < CarCount; i++)
                    {
                        _scheduler.Cars[i].HasResume = false;
                    }
                    Array.Clear(
                        _hasResumeIncomingDirection,
                        0,
                        _hasResumeIncomingDirection.Length);
                    Array.Clear(_rescueRoutes, 0, _rescueRoutes.Length);
                    Array.Fill(_rescueViewRouteIndices, -1);
                    Array.Clear(_rescueStages, 0, _rescueStages.Length);
                    Array.Clear(_offNetworkBlockedTicks, 0, _offNetworkBlockedTicks.Length);
                    Array.Clear(_freeFlowStreak, 0, _freeFlowStreak.Length);
                    Array.Clear(_freeFlowStreakMax, 0, _freeFlowStreakMax.Length);
                    Array.Fill(_freeFlowCountedIntersection, -1);
                    Array.Clear(_freeFlowTripActive, 0, _freeFlowTripActive.Length);
                }
                else
                {
                    // 건설(토폴로지 리빌드): 신규 차만 수렴시키고 생존 차의 상태·진행도는 지킨다.
                    // 전체 SnapToHour를 쓰면 주행 중이던 차가 전부 주차로 되돌아가 순간이동한다.
                    _scheduler.SnapNewToHour(gameHour);
                }
                Array.Clear(_enqueued, 0, _enqueued.Length);
                Array.Fill(_queueSlots, -1);
                Array.Clear(_queueOffsets, 0, _queueOffsets.Length);
                Array.Fill(_intersectionProgress, -1f);
                Array.Clear(_linkProgress, 0, _linkProgress.Length);
                Array.Fill(_roundaboutProgress, -1f);
                _needsSnap = false;
            }
            _lastHour = gameHour;
            _hasLastHour = true;

            float currentAbsoluteHour = gameDay * 24f + gameHour;
            long currentAbsoluteHourBoundary = gameDay * 24L +
                (long)Mathf.Floor(gameHour);
            if (!_hasReservationSweepHour ||
                currentAbsoluteHourBoundary != _lastReservationSweepAbsoluteHour)
            {
                ReleaseOrphanedSpecialReservations();
                _lastReservationSweepAbsoluteHour = currentAbsoluteHourBoundary;
                _hasReservationSweepHour = true;
            }
            CancelStaleSpecialJourneys(currentAbsoluteHour);

            TryResumeDwellingSpecialTrips(currentAbsoluteHour, net);
            TryStartPendingSpecialTrip(gameDay, gameHour, tick);
            _scheduler.UpdateDepartures(gameHour);
            _commuteTripSource.SyncDepartures(
                _scheduler.Cars,
                _cfg.CoinPerTrip);
            roadTraffic?.PrepareStep(this);
            TryEnqueueDepartures(net);
            StepResult result = net.Step(
                roadTraffic ?? (ICarRouteProvider)this,
                signalGate,
                tick);
            UpdateFreeFlowStreaks(net);
            int externalArrivals = roadTraffic?.ProcessArrivals() ?? 0;
            result.Arrivals = Math.Max(
                0,
                result.Arrivals - externalArrivals);
            for (int i = 0; i < net.ArrivalCount; i++)
            {
                ArrivalRecord arrival = net.GetArrival(i);
                if (arrival.CarId < 0 || arrival.CarId >= CarCount) continue;
                CommuteCar car = _scheduler.Cars[arrival.CarId];
                if (_tripScheduler.TryGetActive(
                        car,
                        out SpecialTripJourney specialJourney))
                {
                    HandleSpecialTripArrival(
                        arrival.CarId,
                        specialJourney,
                        currentAbsoluteHour,
                        events);
                    continue;
                }

                bool paidArrival = car.State == CarState.Outbound;
                if (_commuteTripSource.TryComplete(
                        car,
                        out VehicleTripSnapshot completedTrip))
                {
                    events.QueueTripArrival(
                        new VehicleTripArrivedEvent(completedTrip));
                }
                int freeFlowStreakMax =
                    Mathf.Clamp(
                        _freeFlowStreakMax[arrival.CarId],
                        0,
                        FreeFlowStreakCap);
                _scheduler.NotifyArrived(car);
                ResetCarRuntimeState(arrival.CarId);
                if (paidArrival)
                {
                    int coins = CalculateFreeFlowReward(
                        _cfg.CoinPerTrip,
                        freeFlowStreakMax);
                    events.QueueArrival(new ArrivalEvent(car.Work, coins));
                }
            }
            ProcessLivenessWatchdog(net);
            roadTraffic?.ProcessLivenessWatchdog(
                _cfg.GetVehicleRerouteBlockedTicks(),
                _cfg.GetVehicleRestartBlockedTicks());
            SyncLocations(net);
            roadTraffic?.SynchronizeSnapshots();
            return result;
        }

        private void ReleaseOrphanedSpecialReservations()
        {
            IReadOnlyList<CommuteCar> cars = _scheduler.Cars;
            for (int index = 0; index < cars.Count; index++)
            {
                CommuteCar car = cars[index];
                if (car == null || car.IsTransient || !car.SpecialTripReserved ||
                    IsActiveSpecialTripOwner(car))
                {
                    continue;
                }

                car.SetSpecialTripReservation(false);
                Debug.LogWarning(
                    $"[CarSim] Orphaned special-trip reservation released " +
                    $"Home={car.Home} Work={car.Work} State={car.State}");
            }
        }

        private bool IsActiveSpecialTripOwner(CommuteCar car)
        {
            foreach (SpecialTripJourney journey in _tripScheduler.ActiveJourneys)
            {
                if (journey.RoutineOwner == car)
                {
                    return true;
                }
            }

            return false;
        }

        private void CancelStaleSpecialJourneys(float currentAbsoluteHour)
        {
            SpecialTripJourney stale;
            while ((stale = _tripScheduler.FindStaleJourney(
                        currentAbsoluteHour,
                        StaleSpecialJourneyHours)) != null)
            {
                Debug.LogWarning(
                    $"[CarSim] Stale special journey cancelled " +
                    $"Home={stale.RoutineOwner?.Home} " +
                    $"Work={stale.RoutineOwner?.Work}");
                CancelSpecialJourney(stale);
            }
        }

        public CarSnapshot GetCar(int index)
        {
            if (index < 0 || index >= CarCount) throw new ArgumentOutOfRangeException(nameof(index));
            CommuteCar car = _scheduler.Cars[index];
            if (_tripScheduler.TryGetActive(
                    car,
                    out SpecialTripJourney specialJourney))
            {
                VehicleTrip trip = specialJourney.CurrentTrip;
                bool dwelling = specialJourney.IsDwelling;
                bool returning =
                    specialJourney.Phase ==
                    SpecialTripJourneyPhase.Returning;
                return new CarSnapshot
                {
                    Home = trip.Origin,
                    Work = trip.Destination,
                    State = dwelling
                        ? CarState.ParkedWork
                        : CarState.Outbound,
                    RouteIndex = _rescueViewRouteIndices[index] >= 0
                        ? _rescueViewRouteIndices[index]
                        : specialJourney.ViewRouteIndex,
                    TileIndex = _tileIndices[index],
                    QueueSlot = _queueSlots[index],
                    QueueOffsetTiles = _queueOffsets[index],
                    HomeSlot = returning
                        ? specialJourney.VisitorSlot
                        : specialJourney.OriginSlot,
                    WorkSlot = returning
                        ? specialJourney.FinalSlot
                        : specialJourney.VisitorSlot,
                    IsVisible = true,
                    Purpose = VehicleTripPurpose.SpecialBuildingVisit,
                    AwaitingNextWave = false,
                    IntersectionProgress01 = _intersectionProgress[index],
                    LinkProgress01 = _linkProgress[index],
                    RoundaboutProgress01 = _roundaboutProgress[index],
                    SpeedFactorNumerator = car.SpeedFactorNumerator,
                    WaitingForSpeedCredit = _creditWaiting[index],
                    FreeFlowStreak = _freeFlowStreak[index],
                    FreeFlowStreakMax = _freeFlowStreakMax[index]
                };
            }

            int viewRouteIndex = -1;
            if (car.RouteIndex >= 0 &&
                car.RouteIndex < _plannerRouteIndices.Count)
            {
                viewRouteIndex = _rescueViewRouteIndices[index] >= 0
                    ? _rescueViewRouteIndices[index]
                    : _plannerRouteIndices[car.RouteIndex];
            }

            return new CarSnapshot
            {
                Home = car.Home,
                Work = car.Work,
                State = car.State,
                RouteIndex = viewRouteIndex,
                TileIndex = _tileIndices[index],
                QueueSlot = _queueSlots[index],
                QueueOffsetTiles = _queueOffsets[index],
                HomeSlot = car.HomeSlot,
                WorkSlot = car.WorkSlot,
                IsVisible = car.State != CarState.Inactive &&
                    !car.SpecialTripReserved,
                Purpose = car.RoutinePurpose,
                AwaitingNextWave = car.AwaitingNextWave,
                IntersectionProgress01 = _intersectionProgress[index],
                LinkProgress01 = _linkProgress[index],
                RoundaboutProgress01 = _roundaboutProgress[index],
                SpeedFactorNumerator = car.SpeedFactorNumerator,
                WaitingForSpeedCredit = _creditWaiting[index],
                FreeFlowStreak = _freeFlowStreak[index],
                FreeFlowStreakMax = _freeFlowStreakMax[index]
            };
        }

        private void UpdateFreeFlowStreaks(RoadQueueNetwork net)
        {
            for (int carId = 0; carId < CarCount; carId++)
            {
                if (!_enqueued[carId]
                    || !TryRoute(carId, out List<Vector2Int> route))
                {
                    continue;
                }

                int previousIndex = _tileIndices[carId];
                bool moved = net.MovedThisTick(carId);
                if (!moved)
                {
                    // WaitingForSpeedCredit means the slow vehicle is still making
                    // normal progress over time, not that it hit a traffic stop.
                    // A physical stop has no credit-wait marker and resets the streak.
                    if (!_creditWaiting[carId])
                    {
                        if (_freeFlowStreak[carId] > 0)
                        {
                            int resetTileIndex = previousIndex + 1;
                            if (resetTileIndex >= route.Count ||
                                !_grid.IsIntersection(route[resetTileIndex]))
                            {
                                resetTileIndex = previousIndex;
                            }

                            if (resetTileIndex >= 0 &&
                                resetTileIndex < route.Count &&
                                _grid.IsIntersection(route[resetTileIndex]))
                            {
                                _freeFlowStreakLedger?.RecordReset(
                                    route[resetTileIndex]);
                            }
                        }

                        _freeFlowStreak[carId] = 0;
                    }
                    continue;
                }

                if (previousIndex < 0
                    || previousIndex >= route.Count
                    || _grid == null
                    || !_grid.IsIntersection(route[previousIndex]))
                {
                    _freeFlowCountedIntersection[carId] = -1;
                    continue;
                }

                // IntersectionAdvance can move through multiple internal stages while
                // the route index is unchanged. Count that intersection once per route
                // index, while still using MovedThisTick as the sole movement signal.
                if (_freeFlowCountedIntersection[carId] == previousIndex) continue;
                _freeFlowCountedIntersection[carId] = previousIndex;
                _freeFlowStreak[carId] = Mathf.Min(
                    _freeFlowStreak[carId] + 1,
                    FreeFlowStreakCap);
                _freeFlowStreakMax[carId] = Mathf.Max(
                    _freeFlowStreakMax[carId],
                    _freeFlowStreak[carId]);
            }
        }

        public bool TryGetNextTile(int carId, Vector2Int current, out Vector2Int next, out Dir entryDirAtNext)
        {
            next = default;
            entryDirAtNext = default;
            if (!TryRoute(carId, out List<Vector2Int> route)) return false;
            int routeIndex = FindRouteIndexAtOrAfter(
                route,
                current,
                _tileIndices[carId]);
            if (routeIndex < 0 || routeIndex >= route.Count - 1)
            {
                return false;
            }

            next = route[routeIndex + 1];
            Vector2Int delta = next - current;
            return TryRouteDirection(delta, out entryDirAtNext);
        }

        public bool IsDestination(int carId, Vector2Int tile)
        {
            if (!TryRoute(carId, out List<Vector2Int> route) ||
                route.Count == 0)
            {
                return false;
            }

            int routeIndex = FindRouteIndexAtOrAfter(
                route,
                tile,
                _tileIndices[carId]);
            return routeIndex == route.Count - 1;
        }

        public bool IsTransient(int carId) =>
            carId >= 0 &&
            carId < _scheduler.Cars.Count &&
            _scheduler.Cars[carId].IsTransient;

        // 차급 배정(M1-2): (home, slot) 결정론 해시 — 같은 도시·같은 설정이면
        // 리빌드·재실행마다 동일하다. ratio 0(기본) = 전원 표준 60 = 기존 비트 동일.
        private void ApplyCommuterVehicleClasses()
        {
            IReadOnlyList<CommuteCar> cars = _scheduler.Cars;
            for (int i = 0; i < cars.Count; i++)
            {
                CommuteCar car = cars[i];
                if (car == null || car.IsTransient) continue;
                int seed = StableHash(
                    $"{car.Home.x}:{car.Home.y}:{car.HomeSlot}");
                car.SetSpeedNumerator(
                    IsTruckByHash(seed, _cfg.TruckCommuterRatio) ? 40 : 60);
            }
        }

        // 해시 하위 분포를 [0,1)로 사상해 ratio 미만이면 트럭.
        private static bool IsTruckByHash(int seed, float ratio)
        {
            if (ratio <= 0f) return false;
            if (ratio >= 1f) return true;
            return seed % 10000 / 10000f < ratio;
        }

        // 정수 크레딧 게이트(설계 Q1·Q4): 틱당 분자 적립, 60 도달 시 허가 후 차감.
        // 허가 시 소비(이동 성공 여부 무관) — 캡 120이 신호 대기 후 폭주를 최대
        // 1회 연속 전진으로 상한한다.
        public bool TryConsumeAdvanceCredit(int carId, int tick)
        {
            if (carId < 0 || carId >= CarCount) return true; // 버스 등 외부 에이전트
            CommuteCar car = _scheduler.Cars[carId];
            if (car.SpeedFactorNumerator >= 60) return true; // 표준 = 무비용 경로(기존 비트 동일)
            if (!_creditAccrued[carId])
            {
                car.SpeedCredit = Math.Min(120, car.SpeedCredit + car.SpeedFactorNumerator);
                _creditAccrued[carId] = true;
            }
            if (car.SpeedCredit < 60)
            {
                _creditWaiting[carId] = true;
                return false;
            }
            car.SpeedCredit -= 60;
            _creditWaiting[carId] = false; // 늦은 서비스 라운드에서 허가되면 대기 해제
            return true;
        }

        private void TryEnqueueDepartures(RoadQueueNetwork net)
        {
            for (int i = 0; i < CarCount; i++)
            {
                CommuteCar car = _scheduler.Cars[i];
                bool moving = car.State == CarState.Outbound || car.State == CarState.Inbound;
                if (!moving || _enqueued[i] || !TryRoute(i, out List<Vector2Int> route)) continue;
                // 리빌드 생존 차는 있던 자리에서 이어 달린다. 새 경로에 그 타일이 없으면
                // (도로가 헐렸다 등) 진행도를 포기하고 route[0]에서 다시 출발한다.
                bool enqueued = TryEnqueueRouteStart(
                        route,
                        car.ResumeTile,
                        ref car.HasResume,
                        net,
                        i,
                        out int start,
                        _roadNetwork,
                        DepartureBuilding(i),
                        ResumeIncomingDirection(i),
                        _resumeRouteIndices[i]);
                if (!car.HasResume)
                {
                    _hasResumeIncomingDirection[i] = false;
                }
                if (!enqueued)
                {
                    continue;
                }
                _enqueued[i] = true;
                _tileIndices[i] = start;
                _queueSlots[i] = 0;
                _queueOffsets[i] = 0f;
                _intersectionProgress[i] = -1f;
                _linkProgress[i] = 0f;
                _roundaboutProgress[i] = -1f;
                if (!_freeFlowTripActive[i])
                {
                    _freeFlowStreak[i] = 0;
                    _freeFlowStreakMax[i] = 0;
                    _freeFlowTripActive[i] = true;
                }
            }
        }

        private void ProcessLivenessWatchdog(RoadQueueNetwork net)
        {
            int rerouteThreshold =
                _cfg.GetVehicleRerouteBlockedTicks();
            int restartThreshold =
                _cfg.GetVehicleRestartBlockedTicks();

            for (int carId = 0; carId < CarCount; carId++)
            {
                CommuteCar car = _scheduler.Cars[carId];
                bool moving = car.State == CarState.Outbound
                    || car.State == CarState.Inbound;
                if (!moving)
                {
                    _rescueStages[carId] = 0;
                    _offNetworkBlockedTicks[carId] = 0;
                    continue;
                }

                int blockedTicks;
                Vector2Int rescueTile;
                if (_enqueued[carId])
                {
                    blockedTicks = net.GetBlockedTicks(carId);
                    if (!net.TryLocateCar(carId, out rescueTile, out _, out _))
                        rescueTile = RouteOrigin(carId);
                    _offNetworkBlockedTicks[carId] = 0;
                    if (blockedTicks <= 0)
                    {
                        _rescueStages[carId] = 0;
                        continue;
                    }
                }
                else
                {
                    blockedTicks = ++_offNetworkBlockedTicks[carId];
                    rescueTile = RouteOrigin(carId);
                }

                if (_rescueStages[carId] == 0 && blockedTicks >= rerouteThreshold)
                {
                    _rescueStages[carId] = 1;
                    RescueRerouteCount++;
                    RememberRescue(carId, rescueTile);
                    TryApplyRescueRoute(carId, rescueTile, net);
                }

                if (blockedTicks < restartThreshold) continue;

                RescueRestartCount++;
                RememberRescue(carId, rescueTile);
                if (_enqueued[carId]) net.TryRemoveCarForRescue(carId);
                _enqueued[carId] = false;
                _rescueRoutes[carId] = null;
                _rescueViewRouteIndices[carId] = -1;
                car.HasResume = false;
                _hasResumeIncomingDirection[carId] = false;
                ResetLocation(carId);
                _rescueStages[carId] = 0;
                _offNetworkBlockedTicks[carId] = 0;

                if (TryRoute(carId, out List<Vector2Int> route)
                    && TryEnqueueRouteStart(
                        route,
                        default,
                        ref car.HasResume,
                        net,
                        carId,
                        out int start,
                        _roadNetwork,
                        DepartureBuilding(carId)))
                {
                    MarkEnqueued(carId, start);
                }
            }
        }

        private bool TryApplyRescueRoute(
            int carId,
            Vector2Int current,
            RoadQueueNetwork net)
        {
            if (_planner == null
                || !TryRoute(carId, out List<Vector2Int> route)
                || route.Count == 0)
            {
                return false;
            }

            CommuteCar car = _scheduler.Cars[carId];
            Vector2Int? initialIncomingDirection =
                ResumeIncomingDirection(carId);
            if (_enqueued[carId] &&
                net.TryLocateCar(
                    carId,
                    out Vector2Int liveTile,
                    out Dir liveDirection,
                    out _))
            {
                current = liveTile;
                initialIncomingDirection = DirectionVector(liveDirection);
            }
            bool hasSpecialJourney = _tripScheduler.TryGetActive(
                car,
                out SpecialTripJourney specialJourney);
            List<Vector2Int> rerouted;
            if (hasSpecialJourney &&
                specialJourney.CurrentLegIndex == 0 &&
                UsesPetrolFrontageLane(specialJourney.Request))
            {
                if (!TryPlanRoadToBuilding(
                        current,
                        specialJourney.Request.Destination,
                        requiredFirstDirection: null,
                        requireDestinationFrontageDirection: true,
                        initialIncomingDirection:
                            initialIncomingDirection,
                        route: out rerouted))
                {
                    return false;
                }
            }
            else
            {
                Vector2Int? requiredFirstDirection = null;
                if (hasSpecialJourney &&
                    specialJourney.CurrentLegIndex > 0 &&
                    UsesPetrolFrontageLane(specialJourney.Request) &&
                    _roadNetwork.IsFrontageAccessRoad(
                        specialJourney.Request.Destination,
                        current) &&
                    _roadNetwork.TryGetFrontageTravelDirection(
                        specialJourney.Request.Destination,
                        out Vector2Int departureDirection))
                {
                    requiredFirstDirection = departureDirection;
                }

                rerouted = requiredFirstDirection.HasValue ||
                    initialIncomingDirection.HasValue
                    ? _planner.PlanVehicleTrip(
                        current,
                        route[route.Count - 1],
                        requiredFirstDirection,
                        requiredArrivalDirection: null,
                        initialIncomingDirection:
                            initialIncomingDirection)
                    : _planner.ReplanFrom(
                        current,
                        route[route.Count - 1]);
            }

            if (rerouted == null || rerouted.Count == 0) return false;

            _rescueRoutes[carId] = rerouted;
            RegisterRescueViewRoute(carId, rerouted);
            _tileIndices[carId] = 0;
            _resumeRouteIndices[carId] = 0;
            if (_enqueued[carId]) return true;

            car.ResumeTile = current;
            car.HasResume = initialIncomingDirection.HasValue;
            _hasResumeIncomingDirection[carId] =
                initialIncomingDirection.HasValue;
            if (initialIncomingDirection.HasValue)
            {
                _resumeIncomingDirections[carId] =
                    initialIncomingDirection.Value;
            }
            if (!TryEnqueueRouteStart(
                    rerouted,
                    current,
                    ref car.HasResume,
                    net,
                    carId,
                    out int start,
                    _roadNetwork,
                    DepartureBuilding(carId),
                    initialIncomingDirection))
            {
                if (!car.HasResume)
                {
                    _hasResumeIncomingDirection[carId] = false;
                }
                return true;
            }

            _hasResumeIncomingDirection[carId] = false;
            MarkEnqueued(carId, start);
            return true;
        }

        private void RegisterRescueViewRoute(int carId, List<Vector2Int> rerouted)
        {
            CommuteCar car = _scheduler.Cars[carId];
            if (_tripScheduler.TryGetActive(
                    car,
                    out SpecialTripJourney specialJourney))
            {
                int specialExisting = _rescueViewRouteIndices[carId];
                if (specialExisting < 0)
                {
                    specialExisting = _viewOutboundRoutes.Count;
                    _rescueViewRouteIndices[carId] = specialExisting;
                    _viewOutboundRoutes.Add(rerouted);
                    _viewReturnRoutes.Add(ReverseCopy(rerouted));
                    return;
                }

                _viewOutboundRoutes[specialExisting] = rerouted;
                _viewReturnRoutes[specialExisting] = ReverseCopy(rerouted);
                return;
            }

            int plannerIndex = _plannerRouteIndices[car.RouteIndex];
            List<Vector2Int> outbound = car.State == CarState.Outbound
                ? rerouted
                : _viewOutboundRoutes[plannerIndex];
            List<Vector2Int> inbound = car.State == CarState.Inbound
                ? rerouted
                : _viewReturnRoutes[plannerIndex];
            int existing = _rescueViewRouteIndices[carId];
            if (existing >= 0)
            {
                _viewOutboundRoutes[existing] = outbound;
                _viewReturnRoutes[existing] = inbound;
                return;
            }
            _rescueViewRouteIndices[carId] = _viewOutboundRoutes.Count;
            _viewOutboundRoutes.Add(outbound);
            _viewReturnRoutes.Add(inbound);
        }

        private Vector2Int RouteOrigin(int carId)
        {
            return TryRoute(carId, out List<Vector2Int> route) && route.Count > 0
                ? route[0]
                : default;
        }

        private void RememberRescue(int carId, Vector2Int tile)
        {
            LastRescueCarId = carId;
            LastRescueTile = tile;
        }

        private void MarkEnqueued(int carId, int start)
        {
            _enqueued[carId] = true;
            _tileIndices[carId] = start;
            _queueSlots[carId] = 0;
            _queueOffsets[carId] = 0f;
            _intersectionProgress[carId] = -1f;
            _linkProgress[carId] = 0f;
            _roundaboutProgress[carId] = -1f;
        }

        private void ResetLocation(int carId)
        {
            _tileIndices[carId] = 0;
            _queueSlots[carId] = -1;
            _queueOffsets[carId] = 0f;
            _intersectionProgress[carId] = -1f;
            _linkProgress[carId] = 0f;
            _roundaboutProgress[carId] = -1f;
        }

        internal static int FindResumeStart(
            IReadOnlyList<Vector2Int> route,
            Vector2Int resumeTile,
            RoadQueueNetwork net,
            int minimumRouteIndex = 0)
        {
            int resumeIndex = -1;
            int firstIndex = Mathf.Clamp(
                minimumRouteIndex,
                0,
                Mathf.Max(0, route.Count - 1));
            for (int p = firstIndex; p < route.Count; p++)
            {
                if (route[p] != resumeTile) continue;
                resumeIndex = p;
                break;
            }
            if (resumeIndex < 0) return -1;

            for (int p = resumeIndex; p >= 0; p--)
            {
                if (net.IsSafeResumeTile(route[p])) return p;
            }
            return -1;
        }

        // 이 차의 이번 여정 출발 건물. 진출 방향 산출용(설계 D2-1).
        // 특수 방문(transient) 차는 스케줄러 RouteIndex 대신 활성 여정의 현재
        // 출발 건물을 사용한다. authored 주차 슬롯에서 나오는 차량도 일반 건물과
        // 같은 진출 방향 계약을 타야 교차로 첫 칸에 올바르게 진입한다.
        private Vector2Int? DepartureBuilding(int carId)
        {
            CommuteCar car = _scheduler.Cars[carId];
            if (_tripScheduler.TryGetActive(
                    car,
                    out SpecialTripJourney specialJourney))
            {
                return specialJourney.CurrentTrip.Origin;
            }

            int ri = car.RouteIndex;
            if (ri < 0) return null;
            var list = car.State == CarState.Outbound ? _sources : _sinks;
            return ri < list.Count ? list[ri] : (Vector2Int?)null;
        }

        private Vector2Int? ResumeIncomingDirection(int carId) =>
            carId >= 0 &&
            carId < _hasResumeIncomingDirection.Length &&
            _hasResumeIncomingDirection[carId]
                ? _resumeIncomingDirections[carId]
                : (Vector2Int?)null;

        internal static bool TryEnqueueRouteStart(
            IReadOnlyList<Vector2Int> route,
            Vector2Int resumeTile,
            ref bool hasResume,
            RoadQueueNetwork net,
            int carId,
            out int start,
            RoadNetwork roadNetwork = null,
            Vector2Int? originBuilding = null,
            Vector2Int? resumeIncomingDirection = null,
            int resumeSearchStartIndex = 0)
        {
            start = 0;
            bool wasResumeRequest = hasResume;
            bool retryingResume = hasResume;
            if (retryingResume)
            {
                start = FindResumeStart(
                    route,
                    resumeTile,
                    net,
                    resumeSearchStartIndex);
                if (start < 0)
                {
                    // No safe tile at or behind the logical position: abandon the
                    // ambiguous mid-route resume and explicitly restart from origin.
                    hasResume = false;
                    retryingResume = false;
                    start = 0;
                }
            }

            // 신규 출발이고 원점이 교차로면 스테이지를 부여해 정식 진입한다(설계 D1·D4).
            // 재개 요청은 여기 오지 않는다 — 경로상 위치가 모호해 위험하다.
            // route 가 1칸이면 exit 방향을 못 구하므로 오늘 동작(오프네트워크)을 유지한다.
            if (!wasResumeRequest && start == 0 && route.Count > 1
                && net.IsIntersectionSpawnTile(route[0]))
            {
                if (!TryRouteDirection(route[1] - route[0], out Dir spawnExit)) return false;
                // 기본은 exit 폴백(설계 D2-2). 건물 정보가 주어졌고 직교 인접이면 그 방향을 쓴다.
                Dir spawnEntry = spawnExit;
                // originBuilding 이 null 이면 조회 자체를 건너뛴다. default 좌표 (0,0) 은
                // 유효 좌표라 그 옆이 route[0] 이면 엉뚱한 entry 가 나온다(2차 리뷰 P1).
                if (roadNetwork != null
                    && originBuilding.HasValue
                    && roadNetwork.TryGetDepartureEntryDir(
                        originBuilding.Value, route[0], out Dir fromBuilding))
                {
                    spawnEntry = fromBuilding;
                }
                if (!net.TryEnqueueAtIntersection(
                        route[0], spawnEntry, spawnExit, carId, 1))
                {
                    return false;   // 셀이 막혔다 — 다음 틱 재시도(수렴)
                }
                hasResume = false;
                return true;
            }

            // 로터리 바로 옆 건물의 출입로는 경로 첫 칸이 로터리 arm일 수 있다.
            // 중간 재개는 계속 금지하되, 신규 출발만 arm의 공유 1칸 큐에 올려 기존
            // 진입 선택·링 예약 절차가 차량을 받아가게 한다.
            if (!wasResumeRequest && start == 0 && route.Count > 0
                && net.TryGetRoundaboutArmSpawnDirection(
                    route[0],
                    out Dir armEntry))
            {
                if ((route.Count > 1
                        && !TryRouteDirection(
                            route[1] - route[0],
                            out armEntry))
                    || !net.TryEnqueueRoundaboutArmSpawn(
                        route[0],
                        armEntry,
                        carId))
                {
                    return false;
                }

                hasResume = false;
                return true;
            }

            // A route whose origin itself is an intersection/roundabout state-machine
            // tile has no valid queue-only spawn. Keep it off-network and retry; a
            // later watchdog owns convergence for routes with no ordinary tile.
            if (route.Count == 0 || !net.IsSafeResumeTile(route[start])) return false;

            Dir entry = Dir.N;
            bool useResumeIncoming = retryingResume &&
                route[start] == resumeTile &&
                resumeIncomingDirection.HasValue &&
                TryDirection(resumeIncomingDirection.Value, out entry);
            bool hasDirection = useResumeIncoming ||
                (start > 0
                    ? TryRouteDirection(
                        route[start] - route[start - 1],
                        out entry)
                    : route.Count <= 1 || TryRouteDirection(
                        route[1] - route[0],
                        out entry));
            if (!hasDirection)
            {
                if (retryingResume) hasResume = false;
                return false;
            }
            if (!net.TryEnqueue(route[start], entry, carId)) return false;

            // A temporarily full resume queue keeps HasResume armed until this point.
            hasResume = false;
            return true;
        }

        private void SyncLocations(RoadQueueNetwork net)
        {
            for (int i = 0; i < CarCount; i++)
            {
                CommuteCar car = _scheduler.Cars[i];
                if (!_enqueued[i] || !net.TryLocateCar(
                        i,
                        out Vector2Int tile,
                        out _,
                        out int slot,
                        out float intersectionProgress,
                        out float linkProgress,
                        out int roundaboutCell,
                        out float queueOffsetTiles))
                {
                    _linkProgress[i] = 0f;
                    _queueSlots[i] = -1;
                    _queueOffsets[i] = 0f;
                    _intersectionProgress[i] = -1f;
                    _roundaboutProgress[i] = -1f;
                    _tileIndices[i] =
                        car.State == CarState.ParkedWork &&
                        TryRoute(i, out List<Vector2Int> parkedRoute) &&
                        parkedRoute.Count > 0
                        ? parkedRoute.Count - 1
                        : 0;
                    continue;
                }
                _linkProgress[i] = linkProgress;
                _queueSlots[i] = slot;
                _queueOffsets[i] = queueOffsetTiles;
                _intersectionProgress[i] = intersectionProgress;
                _roundaboutProgress[i] = -1f;
                if (!TryRoute(i, out List<Vector2Int> route)) continue;
                int routeIndex = FindRouteIndexAtOrAfter(
                    route,
                    tile,
                    _tileIndices[i]);
                if (routeIndex >= 0)
                {
                    _tileIndices[i] = routeIndex;
                    _roundaboutProgress[i] = CalculateRoundaboutProgress(
                        route,
                        routeIndex,
                        roundaboutCell);
                }
            }
        }

        internal static int FindRouteIndexAtOrAfter(
            IReadOnlyList<Vector2Int> route,
            Vector2Int tile,
            int cursor)
        {
            if (route == null || route.Count == 0)
            {
                return -1;
            }

            int start = Mathf.Clamp(cursor, 0, route.Count - 1);
            for (int index = start; index < route.Count; index++)
            {
                if (route[index] == tile)
                {
                    return index;
                }
            }

            return -1;
        }

        private static float CalculateRoundaboutProgress(
            IReadOnlyList<Vector2Int> route,
            int tileIndex,
            int roundaboutCell)
        {
            if (roundaboutCell < 0 || tileIndex <= 0 || tileIndex >= route.Count - 1)
            {
                return -1f;
            }

            if (!TryDirection(route[tileIndex] - route[tileIndex - 1], out Dir entry)
                || !TryDirection(route[tileIndex + 1] - route[tileIndex], out Dir exitCell))
            {
                return -1f;
            }

            return RoundaboutTrafficState.Progress01(entry, exitCell, (Dir)roundaboutCell);
        }

        private void TryStartPendingSpecialTrip(
            long gameDay,
            float gameHour,
            int tick)
        {
            if (_grid == null || _demands == null ||
                _roadNetwork == null || _planner == null ||
                !_tripScheduler.TryTakeDue(
                    gameDay,
                    gameHour,
                    tick,
                    out SpecialBuildingVisitTripRequest request))
            {
                return;
            }

            if (!TileFootprint.IsSpecialBuilding(
                    _grid.GetTile(request.Destination)))
            {
                _tripScheduler.Forget(request);
                return;
            }

            if (!TryCreateSpecialJourney(
                    request,
                    gameDay,
                    gameHour,
                    out SpecialTripStartFailure failure))
            {
                int delay = failure == SpecialTripStartFailure.NoRoute
                    ? RouteRetryDelayTicks
                    : VehicleRetryDelayTicks;
                _tripScheduler.Requeue(request, tick, delay);
            }
        }

        private bool TryReserveVisitorParking(
            SpecialBuildingVisitTripRequest request,
            out int visitorSlot)
        {
            visitorSlot = -1;
            int firstSlot = request.VisitorParkingSlotStart;
            int slotCount = request.VisitorParkingSlotCount;
            for (int offset = 0; offset < slotCount; offset++)
            {
                int slot = firstSlot + offset;
                var reservation = (request.Destination, slot);
                if (!_reservedVisitorParking.Add(reservation))
                {
                    continue;
                }

                visitorSlot = slot;
                return true;
            }

            return false;
        }

        private void ReleaseVisitorParking(
            Vector2Int destination,
            int visitorSlot)
        {
            if (visitorSlot < 0)
            {
                return;
            }

            _reservedVisitorParking.Remove((destination, visitorSlot));
        }

        private bool TryCreateSpecialJourney(
            SpecialBuildingVisitTripRequest request,
            long gameDay,
            float gameHour,
            out SpecialTripStartFailure failure)
        {
            failure = SpecialTripStartFailure.NoEligibleOrigin;
            if (!TryReserveVisitorParking(
                    request,
                    out int visitorSlot))
            {
                failure = SpecialTripStartFailure.ParkingCapacity;
                return false;
            }

            bool keepParkingReservation = false;
            bool foundRouteFailure = false;
            int seed = StableHash(TripScheduler.CreateJourneyId(request));
            try
            {
                IReadOnlyList<CommuteCar> cars = _scheduler.Cars;
                if (cars.Count > 0)
                {
                    int start = PositiveModulo(seed, cars.Count);
                    for (int offset = 0; offset < cars.Count; offset++)
                    {
                        CommuteCar owner =
                            cars[(start + offset) % cars.Count];
                        if (owner == null || owner.IsTransient ||
                            owner.SpecialTripReserved ||
                            (owner.State != CarState.ParkedHome &&
                             owner.State != CarState.ParkedWork))
                        {
                            continue;
                        }

                        if (!TryResolveVisitContext(
                                owner,
                                gameHour,
                                out Vector2Int origin,
                                out Vector2Int finalDestination,
                                out CarState finalState,
                                out int originSlot,
                                out int finalSlot))
                        {
                            continue;
                        }

                        if (TryLaunchSpecialJourney(
                                request,
                                gameDay * 24f + gameHour,
                                owner,
                                origin,
                                finalDestination,
                                finalState,
                                originSlot,
                                visitorSlot,
                                finalSlot,
                                out SpecialTripStartFailure launchFailure))
                        {
                            keepParkingReservation = true;
                            failure = SpecialTripStartFailure.None;
                            return true;
                        }

                        if (launchFailure ==
                            SpecialTripStartFailure.VehicleCapacity)
                        {
                            failure = launchFailure;
                            return false;
                        }

                        foundRouteFailure |=
                            launchFailure ==
                            SpecialTripStartFailure.NoRoute;
                    }
                }

                IReadOnlyList<Vector2Int> houses = _demands.Houses;
                if (houses.Count <= 0)
                {
                    failure = foundRouteFailure
                        ? SpecialTripStartFailure.NoRoute
                        : SpecialTripStartFailure.NoEligibleOrigin;
                    return false;
                }

                int houseStart = PositiveModulo(seed, houses.Count);
                for (int offset = 0; offset < houses.Count; offset++)
                {
                    Vector2Int home =
                        houses[(houseStart + offset) % houses.Count];
                    if (TryLaunchSpecialJourney(
                            request,
                            gameDay * 24f + gameHour,
                            null,
                            home,
                            home,
                            CarState.ParkedHome,
                            0,
                            visitorSlot,
                            0,
                            out SpecialTripStartFailure launchFailure))
                    {
                        keepParkingReservation = true;
                        failure = SpecialTripStartFailure.None;
                        return true;
                    }

                    if (launchFailure ==
                        SpecialTripStartFailure.VehicleCapacity)
                    {
                        failure = launchFailure;
                        return false;
                    }

                    foundRouteFailure |=
                        launchFailure ==
                        SpecialTripStartFailure.NoRoute;
                }

                failure = foundRouteFailure
                    ? SpecialTripStartFailure.NoRoute
                    : SpecialTripStartFailure.NoEligibleOrigin;
                return false;
            }
            finally
            {
                if (!keepParkingReservation)
                {
                    ReleaseVisitorParking(
                        request.Destination,
                        visitorSlot);
                }
            }
        }

        private bool TryLaunchSpecialJourney(
            SpecialBuildingVisitTripRequest request,
            float startAbsoluteHour,
            CommuteCar routineOwner,
            Vector2Int origin,
            Vector2Int finalDestination,
            CarState finalRoutineState,
            int originSlot,
            int visitorSlot,
            int finalSlot,
            out SpecialTripStartFailure failure)
        {
            failure = SpecialTripStartFailure.None;
            int activeVehicleLimit = Math.Max(1, _cfg.MaxSimCars);
            if (routineOwner == null &&
                _scheduler.ActiveCount >= activeVehicleLimit)
            {
                failure = SpecialTripStartFailure.VehicleCapacity;
                return false;
            }

            bool usePetrolFrontageLane =
                UsesPetrolFrontageLane(request);
            if (!TryPlanBuildingRoute(
                    origin,
                    request.Destination,
                    requireOriginFrontageDirection: false,
                    requireDestinationFrontageDirection:
                        usePetrolFrontageLane,
                    out List<Vector2Int> firstRoute))
            {
                failure = SpecialTripStartFailure.NoRoute;
                return false;
            }

            List<Vector2Int> secondRoute = null;
            if (finalDestination != request.Destination &&
                !TryPlanBuildingRoute(
                    request.Destination,
                    finalDestination,
                    requireOriginFrontageDirection:
                        usePetrolFrontageLane,
                    requireDestinationFrontageDirection: false,
                    out secondRoute))
            {
                failure = SpecialTripStartFailure.NoRoute;
                return false;
            }

            CommuteCar vehicle = _scheduler.AcquireTransient(
                origin,
                _runtimeVehicleCapacity);
            if (vehicle == null)
            {
                failure = SpecialTripStartFailure.VehicleCapacity;
                return false;
            }

            // 방문 차급(M1-2): 여정 시드 재사용(TryCreateSpecialJourney의 owner 선택과
            // 같은 해시) — 같은 요청이면 같은 차급. ReleaseTransient가 60으로 복원한다.
            vehicle.SetSpeedNumerator(
                IsTruckByHash(
                    StableHash(TripScheduler.CreateJourneyId(request)),
                    _cfg.TruckCommuterRatio)
                    ? 40
                    : 60);

            var journey = new SpecialTripJourney(
                request,
                vehicle,
                routineOwner,
                origin,
                finalDestination,
                finalRoutineState,
                originSlot,
                visitorSlot,
                finalSlot,
                firstRoute,
                secondRoute,
                startAbsoluteHour);
            if (!_tripScheduler.TryActivate(journey))
            {
                _scheduler.ReleaseTransient(vehicle);
                failure = SpecialTripStartFailure.VehicleCapacity;
                return false;
            }

            if (routineOwner != null)
            {
                routineOwner.SetSpecialTripReservation(true);
            }

            RegisterSpecialViewRoute(journey);
            int carIndex = FindCarIndex(vehicle);
            if (carIndex >= 0)
            {
                ResetCarRuntimeState(carIndex);
            }

            return true;
        }

        private static bool TryResolveVisitContext(
            CommuteCar owner,
            float gameHour,
            out Vector2Int origin,
            out Vector2Int finalDestination,
            out CarState finalState,
            out int originSlot,
            out int finalSlot)
        {
            if (owner.State == CarState.ParkedWork)
            {
                if (gameHour < owner.DepartWorkHour)
                {
                    origin = owner.Work;
                    finalDestination = owner.Work;
                    finalState = CarState.ParkedWork;
                    originSlot = owner.WorkSlot;
                    finalSlot = owner.WorkSlot;
                    return true;
                }

                origin = owner.Work;
                finalDestination = owner.Home;
                finalState = CarState.ParkedHome;
                originSlot = owner.WorkSlot;
                finalSlot = owner.HomeSlot;
                return true;
            }

            bool workWindow = gameHour >= owner.DepartHomeHour &&
                gameHour < owner.DepartWorkHour;
            if (workWindow)
            {
                origin = default;
                finalDestination = default;
                finalState = CarState.ParkedHome;
                originSlot = 0;
                finalSlot = 0;
                return false;
            }

            origin = owner.Home;
            finalDestination = owner.Home;
            finalState = CarState.ParkedHome;
            originSlot = owner.HomeSlot;
            finalSlot = owner.HomeSlot;
            return true;
        }

        private bool TryPlanBuildingRoute(
            Vector2Int origin,
            Vector2Int destination,
            bool requireOriginFrontageDirection,
            bool requireDestinationFrontageDirection,
            out List<Vector2Int> route)
        {
            route = null;
            _originAccessRoads.Clear();
            _destinationAccessRoads.Clear();

            Vector2Int? requiredFirstDirection = null;
            if (requireOriginFrontageDirection)
            {
                if (!_roadNetwork.TryGetFrontageTravelDirection(
                        origin,
                        out Vector2Int originDirection))
                {
                    return false;
                }

                requiredFirstDirection = originDirection;
                _roadNetwork.CollectFrontageAccessRoads(
                    origin,
                    _originAccessRoads);
            }
            else
            {
                _roadNetwork.CollectAccessRoads(
                    origin,
                    _originAccessRoads);
            }

            Vector2Int? requiredArrivalDirection = null;
            if (requireDestinationFrontageDirection)
            {
                if (!_roadNetwork.TryGetFrontageTravelDirection(
                        destination,
                        out Vector2Int destinationDirection))
                {
                    return false;
                }

                requiredArrivalDirection = destinationDirection;
                _roadNetwork.CollectFrontageAccessRoads(
                    destination,
                    _destinationAccessRoads);
            }
            else
            {
                _roadNetwork.CollectAccessRoads(
                    destination,
                    _destinationAccessRoads);
            }

            for (int fromIndex = 0;
                 fromIndex < _originAccessRoads.Count;
                 fromIndex++)
            {
                Vector2Int from = _originAccessRoads[fromIndex];
                int region = _roadNetwork.RegionOf(from);
                if (region < 0)
                {
                    continue;
                }

                for (int toIndex = 0;
                     toIndex < _destinationAccessRoads.Count;
                     toIndex++)
                {
                    Vector2Int to = _destinationAccessRoads[toIndex];
                    if (_roadNetwork.RegionOf(to) < 0)
                    {
                        continue;
                    }

                    // RoadNetwork regions contain ordinary road adjacency only.
                    // Let RoutePlanner decide cross-region reachability so a
                    // configured highway link can connect the two access roads.
                    List<Vector2Int> candidate =
                        requiredFirstDirection.HasValue ||
                        requiredArrivalDirection.HasValue
                            ? _planner.PlanVehicleTrip(
                                from,
                                to,
                                requiredFirstDirection,
                                requiredArrivalDirection)
                            : _planner.PlanVehicleTrip(from, to);
                    if (candidate == null || candidate.Count == 0)
                    {
                        continue;
                    }

                    route = candidate;
                    return true;
                }
            }

            return false;
        }

        private bool TryPlanRoadToBuilding(
            Vector2Int originRoad,
            Vector2Int destination,
            Vector2Int? requiredFirstDirection,
            bool requireDestinationFrontageDirection,
            Vector2Int? initialIncomingDirection,
            out List<Vector2Int> route)
        {
            route = null;
            int region = _roadNetwork.RegionOf(originRoad);
            if (region < 0)
            {
                return false;
            }

            _destinationAccessRoads.Clear();
            Vector2Int? requiredArrivalDirection = null;
            if (requireDestinationFrontageDirection)
            {
                if (!_roadNetwork.TryGetFrontageTravelDirection(
                        destination,
                        out Vector2Int destinationDirection))
                {
                    return false;
                }

                requiredArrivalDirection = destinationDirection;
                _roadNetwork.CollectFrontageAccessRoads(
                    destination,
                    _destinationAccessRoads);
            }
            else
            {
                _roadNetwork.CollectAccessRoads(
                    destination,
                    _destinationAccessRoads);
            }

            for (int index = 0;
                 index < _destinationAccessRoads.Count;
                 index++)
            {
                Vector2Int destinationRoad = _destinationAccessRoads[index];
                if (_roadNetwork.RegionOf(destinationRoad) < 0)
                {
                    continue;
                }

                List<Vector2Int> candidate =
                    requiredFirstDirection.HasValue ||
                    requiredArrivalDirection.HasValue ||
                    initialIncomingDirection.HasValue
                        ? _planner.PlanVehicleTrip(
                            originRoad,
                            destinationRoad,
                            requiredFirstDirection,
                            requiredArrivalDirection,
                            initialIncomingDirection)
                        : _planner.PlanVehicleTrip(
                            originRoad,
                            destinationRoad);
                if (candidate == null || candidate.Count == 0)
                {
                    continue;
                }

                route = candidate;
                return true;
            }

            return false;
        }

        private static bool UsesPetrolFrontageLane(
            SpecialBuildingVisitTripRequest request) =>
            string.Equals(
                request.BuildingId,
                PetrolStationBuildingId,
                StringComparison.Ordinal);

        private void TryResumeDwellingSpecialTrips(
            float currentAbsoluteHour,
            RoadQueueNetwork net)
        {
            var active = new List<SpecialTripJourney>(
                _tripScheduler.ActiveJourneys);
            for (int index = 0; index < active.Count; index++)
            {
                SpecialTripJourney journey = active[index];
                if (!journey.IsDwellComplete(currentAbsoluteHour))
                {
                    continue;
                }

                if (!journey.HasContinuation)
                {
                    CompleteSpecialJourney(journey);
                    continue;
                }

                int carId = FindCarIndex(journey.Vehicle);
                List<Vector2Int> route = journey.ContinuationRoute;
                if (carId < 0 || route == null || route.Count == 0)
                {
                    CancelSpecialJourney(journey);
                    continue;
                }

                if (!TryEnqueueRouteStart(
                        route,
                        journey.Vehicle.ResumeTile,
                        ref journey.Vehicle.HasResume,
                        net,
                        carId,
                        out int start,
                        _roadNetwork,
                        journey.Request.Destination))
                {
                    // 출차 접근로가 가득 찬 동안에는 ParkedWork 상태와
                    // authored 슬롯 예약을 그대로 유지한다.
                    continue;
                }

                int speedNumerator =
                    journey.Vehicle.SpeedFactorNumerator;
                if (!journey.TryBeginContinuation())
                {
                    net.TryRemoveCarForRescue(carId);
                    CancelSpecialJourney(journey);
                    continue;
                }

                journey.Vehicle.ConfigureTransient(
                    journey.CurrentTrip.Origin);
                journey.Vehicle.SetSpeedNumerator(speedNumerator);
                RegisterSpecialViewRoute(journey);
                MarkEnqueued(carId, start);
                // Keep the authored slot reserved until the complete round
                // trip ends. The simulation can enter the access-road queue
                // before the presentation has visibly cleared the bay; an
                // early release lets the next visitor target the same pose.
            }
        }

        private void HandleSpecialTripArrival(
            int carId,
            SpecialTripJourney journey,
            float currentAbsoluteHour,
            SimEventBuffer events)
        {
            VehicleTripSnapshot completed = journey.CompleteCurrentLeg();
            events.QueueTripArrival(new VehicleTripArrivedEvent(completed));
            // 방문 도착 보상. ArrivalEvent 를 타면 주간 적립·HUD·피드·퀘스트가 기존 구독으로 따라온다.
            if (completed.RewardCoins > 0)
            {
                int freeFlowStreakMax =
                    Mathf.Clamp(
                        _freeFlowStreakMax[carId],
                        0,
                        FreeFlowStreakCap);
                events.QueueArrival(
                    new ArrivalEvent(
                        completed.Destination,
                        CalculateFreeFlowReward(
                            completed.RewardCoins,
                            freeFlowStreakMax)));
            }
            ResetCarRuntimeState(carId);

            if (journey.CurrentLegIndex == 0)
            {
                journey.BeginDwell(currentAbsoluteHour);
                if (!journey.HasContinuation &&
                    journey.Request.VisitDwellHours <= 0f)
                {
                    CompleteSpecialJourney(journey);
                }
                return;
            }

            CompleteSpecialJourney(journey);
        }

        private void CompleteSpecialJourney(SpecialTripJourney journey)
        {
            CommuteCar owner = journey.RoutineOwner;
            if (owner != null)
            {
                owner.State = journey.FinalRoutineState;
                owner.Distance = 0f;
                if (journey.FinalRoutineState == CarState.ParkedHome)
                {
                    owner.AwaitingNextWave = true;
                }
                owner.SetSpecialTripReservation(false);
            }

            ReleaseVisitorParking(
                journey.Request.Destination,
                journey.VisitorSlot);
            ReleaseSpecialViewRoute(journey);
            _tripScheduler.RemoveActive(journey.Vehicle);
            _scheduler.ReleaseTransient(journey.Vehicle);
        }

        private void RegisterSpecialViewRoute(SpecialTripJourney journey)
        {
            List<Vector2Int> route = journey.CurrentRoute;
            List<Vector2Int> reverse = ReverseCopy(route);
            int index = journey.ViewRouteIndex;
            if (index >= 0 &&
                index < _viewOutboundRoutes.Count &&
                index < _viewReturnRoutes.Count)
            {
                _viewOutboundRoutes[index] = route;
                _viewReturnRoutes[index] = reverse;
                return;
            }

            while (_freeSpecialViewRouteIndices.Count > 0)
            {
                int free = _freeSpecialViewRouteIndices.Pop();
                if (free < 0 || free >= _viewOutboundRoutes.Count ||
                    free >= _viewReturnRoutes.Count)
                {
                    continue;
                }

                journey.ViewRouteIndex = free;
                _viewOutboundRoutes[free] = route;
                _viewReturnRoutes[free] = reverse;
                return;
            }

            journey.ViewRouteIndex = _viewOutboundRoutes.Count;
            _viewOutboundRoutes.Add(route);
            _viewReturnRoutes.Add(reverse);
        }

        private void ReleaseSpecialViewRoute(SpecialTripJourney journey)
        {
            int index = journey.ViewRouteIndex;
            if (index < 0 || index >= _viewOutboundRoutes.Count ||
                index >= _viewReturnRoutes.Count)
            {
                journey.ViewRouteIndex = -1;
                return;
            }

            _viewOutboundRoutes[index] = null;
            _viewReturnRoutes[index] = null;
            _freeSpecialViewRouteIndices.Push(index);
            journey.ViewRouteIndex = -1;
        }

        private void RebuildActiveSpecialRoutes(bool preserveViewIndices)
        {
            var active = new List<SpecialTripJourney>(
                _tripScheduler.ActiveJourneys);
            if (!preserveViewIndices)
            {
                _freeSpecialViewRouteIndices.Clear();
                for (int index = 0; index < active.Count; index++)
                {
                    active[index].ViewRouteIndex = -1;
                }
            }

            for (int index = active.Count - 1; index >= 0; index--)
            {
                SpecialTripJourney journey = active[index];
                bool usePetrolFrontageLane =
                    UsesPetrolFrontageLane(journey.Request);
                if (journey.IsDwelling)
                {
                    if (_grid == null ||
                        !TileFootprint.IsSpecialBuilding(
                            _grid.GetTile(
                                journey.Request.Destination)))
                    {
                        CancelSpecialJourney(journey);
                        continue;
                    }

                    List<Vector2Int> dwellContinuation = null;
                    if (journey.FinalDestination !=
                            journey.Request.Destination &&
                        !TryPlanBuildingRoute(
                            journey.Request.Destination,
                            journey.FinalDestination,
                            requireOriginFrontageDirection:
                                usePetrolFrontageLane,
                            requireDestinationFrontageDirection: false,
                            out dwellContinuation))
                    {
                        CancelSpecialJourney(journey);
                        continue;
                    }

                    journey.ReplaceRoutes(
                        journey.CurrentRoute,
                        dwellContinuation);
                    RegisterSpecialViewRoute(journey);
                    continue;
                }

                Vector2Int? requiredResumeFirstDirection = null;
                if (journey.Vehicle.HasResume &&
                    usePetrolFrontageLane &&
                    journey.CurrentLegIndex > 0 &&
                    _roadNetwork.IsFrontageAccessRoad(
                        journey.Request.Destination,
                        journey.Vehicle.ResumeTile) &&
                    _roadNetwork.TryGetFrontageTravelDirection(
                        journey.Request.Destination,
                        out Vector2Int resumeDirection))
                {
                    requiredResumeFirstDirection = resumeDirection;
                }

                int vehicleId = FindCarIndex(journey.Vehicle);
                Vector2Int? resumeIncomingDirection =
                    ResumeIncomingDirection(vehicleId);
                List<Vector2Int> currentRoute;
                bool planned = journey.Vehicle.HasResume
                    ? TryPlanRoadToBuilding(
                        journey.Vehicle.ResumeTile,
                        journey.CurrentTrip.Destination,
                        requiredResumeFirstDirection,
                        requireDestinationFrontageDirection:
                            usePetrolFrontageLane &&
                            journey.CurrentLegIndex == 0,
                        initialIncomingDirection:
                            resumeIncomingDirection,
                        route: out currentRoute)
                    : TryPlanBuildingRoute(
                        journey.CurrentTrip.Origin,
                        journey.CurrentTrip.Destination,
                        requireOriginFrontageDirection:
                            usePetrolFrontageLane &&
                            journey.CurrentLegIndex > 0,
                        requireDestinationFrontageDirection:
                            usePetrolFrontageLane &&
                            journey.CurrentLegIndex == 0,
                        out currentRoute);
                if (!planned)
                {
                    CancelSpecialJourney(journey);
                    continue;
                }
                if (journey.Vehicle.HasResume && vehicleId >= 0)
                {
                    _resumeRouteIndices[vehicleId] = 0;
                }

                List<Vector2Int> continuation = null;
                if (journey.CurrentLegIndex == 0 &&
                    journey.FinalDestination != journey.Request.Destination &&
                    !TryPlanBuildingRoute(
                        journey.Request.Destination,
                        journey.FinalDestination,
                        requireOriginFrontageDirection:
                            usePetrolFrontageLane,
                        requireDestinationFrontageDirection: false,
                        out continuation))
                {
                    CancelSpecialJourney(journey);
                    continue;
                }

                journey.ReplaceRoutes(currentRoute, continuation);
                RegisterSpecialViewRoute(journey);
            }
        }

        internal static int CalculateFreeFlowReward(
            int baseCoins,
            int freeFlowStreakMax)
        {
            if (baseCoins <= 0)
            {
                return 0;
            }

            int stage = Mathf.Clamp(
                freeFlowStreakMax,
                0,
                FreeFlowStreakCap);
            return Mathf.RoundToInt(
                baseCoins * (1f + FreeFlowStreakBonus[stage]));
        }

        private void CancelAllSpecialJourneys()
        {
            var active = new List<SpecialTripJourney>(
                _tripScheduler.ActiveJourneys);
            for (int index = 0; index < active.Count; index++)
            {
                CancelSpecialJourney(active[index]);
            }
        }

        private void ReleaseAllSpecialJourneys()
        {
            CancelAllSpecialJourneys();
        }

        private void CancelSpecialJourney(SpecialTripJourney journey)
        {
            if (journey == null)
            {
                return;
            }

            journey.CurrentTrip.TryCancel();
            if (journey.RoutineOwner != null)
            {
                journey.RoutineOwner.SetSpecialTripReservation(false);
            }

            ReleaseVisitorParking(
                journey.Request.Destination,
                journey.VisitorSlot);

            int carIndex = FindCarIndex(journey.Vehicle);
            if (carIndex >= 0)
            {
                _net?.TryRemoveCarForRescue(carIndex);
                ResetCarRuntimeState(carIndex);
            }

            ReleaseSpecialViewRoute(journey);
            _tripScheduler.RemoveActive(journey.Vehicle);
            _scheduler.ReleaseTransient(journey.Vehicle);
        }

        private void ResetCarRuntimeState(int carId)
        {
            if (carId < 0 || carId >= _enqueued.Length)
            {
                return;
            }

            _enqueued[carId] = false;
            _tileIndices[carId] = 0;
            _queueSlots[carId] = -1;
            _queueOffsets[carId] = 0f;
            _intersectionProgress[carId] = -1f;
            _linkProgress[carId] = 0f;
            _roundaboutProgress[carId] = -1f;
            _rescueRoutes[carId] = null;
            _rescueViewRouteIndices[carId] = -1;
            _rescueStages[carId] = 0;
            _offNetworkBlockedTicks[carId] = 0;
            _freeFlowCountedIntersection[carId] = -1;
            _freeFlowTripActive[carId] = false;
        }

        private int FindCarIndex(CommuteCar target)
        {
            for (int index = 0; index < _scheduler.Cars.Count; index++)
            {
                if (ReferenceEquals(_scheduler.Cars[index], target))
                {
                    return index;
                }
            }

            return -1;
        }

        private static List<Vector2Int> ReverseCopy(
            IReadOnlyList<Vector2Int> route)
        {
            var reverse = route == null
                ? new List<Vector2Int>()
                : new List<Vector2Int>(route);
            reverse.Reverse();
            return reverse;
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                string safe = value ?? string.Empty;
                for (int index = 0; index < safe.Length; index++)
                {
                    hash ^= safe[index];
                    hash *= 16777619u;
                }

                return (int)(hash & 0x7fffffff);
            }
        }

        private static int PositiveModulo(int value, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            int result = value % count;
            return result < 0 ? result + count : result;
        }

        private bool TryRoute(int carId, out List<Vector2Int> route)
        {
            route = null;
            if (carId < 0 || carId >= CarCount) return false;
            CommuteCar car = _scheduler.Cars[carId];
            if (_rescueRoutes[carId] != null)
            {
                route = _rescueRoutes[carId];
                return true;
            }
            if (_tripScheduler.TryGetActive(
                    car,
                    out SpecialTripJourney specialJourney))
            {
                route = specialJourney.CurrentRoute;
                return route != null;
            }
            if (car.RouteIndex < 0 || car.RouteIndex >= _outboundRoutes.Count) return false;
            route = car.State == CarState.Inbound
                ? _returnRoutes[car.RouteIndex]
                : _outboundRoutes[car.RouteIndex];
            return route != null;
        }

        private static bool TryDirection(Vector2Int delta, out Dir direction)
        {
            if (delta == Vector2Int.up) direction = Dir.N;
            else if (delta == Vector2Int.right) direction = Dir.E;
            else if (delta == Vector2Int.down) direction = Dir.S;
            else if (delta == Vector2Int.left) direction = Dir.W;
            else { direction = default; return false; }
            return true;
        }

        private static Vector2Int DirectionVector(Dir direction) =>
            direction switch
            {
                Dir.N => Vector2Int.up,
                Dir.E => Vector2Int.right,
                Dir.S => Vector2Int.down,
                _ => Vector2Int.left
            };

        private static bool TryRouteDirection(Vector2Int delta, out Dir direction)
        {
            if (TryDirection(delta, out direction)) return true;
            if (delta == Vector2Int.zero) return false;
            if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
                direction = delta.x >= 0 ? Dir.E : Dir.W;
            else
                direction = delta.y >= 0 ? Dir.N : Dir.S;
            return true;
        }
    }
}
