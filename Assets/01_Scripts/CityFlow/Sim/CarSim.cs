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
    }

    internal sealed class CarSim : ICarRouteProvider
    {
        private const float JumpThresholdHours = 1f;
        // Watchdog thresholds are deliberately derived from the existing L1
        // contract: L2 at 3x and L3 at 6x GridlockValveTicks. Promote them to
        // SimConfig only if tuning ownership is approved later.
        private const int RescueRerouteMultiplier = 3;
        private const int RescueRestartMultiplier = 6;
        private const int VehicleRetryDelayTicks = 10;
        private const int RouteRetryDelayTicks = 30;

        private enum SpecialTripStartFailure
        {
            None = 0,
            NoEligibleOrigin = 1,
            NoRoute = 2,
            VehicleCapacity = 3
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
        private readonly int[] _queueSlots;
        private readonly float[] _queueOffsets;
        private readonly float[] _intersectionProgress;
        private readonly float[] _linkProgress;
        private readonly float[] _roundaboutProgress;
        private readonly List<Vector2Int>[] _rescueRoutes;
        private readonly int[] _rescueViewRouteIndices;
        private readonly byte[] _rescueStages;
        private readonly int[] _offNetworkBlockedTicks;
        private readonly List<Vector2Int> _originAccessRoads = new(8);
        private readonly List<Vector2Int> _destinationAccessRoads = new(8);
        private readonly Stack<int> _freeSpecialViewRouteIndices = new();
        private RoadQueueNetwork _net;
        private RoutePlanner _planner;
        private CityGrid _grid;
        private DemandMap _demands;
        private RoadNetwork _roadNetwork;
        private float _lastHour;
        private bool _hasLastHour;
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

        public CarSim(in SimConfig cfg)
        {
            _cfg = cfg;
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
            Array.Fill(_queueSlots, -1);
            Array.Clear(_queueOffsets, 0, _queueOffsets.Length);
            Array.Fill(_intersectionProgress, -1f);
            Array.Clear(_linkProgress, 0, _linkProgress.Length);
            Array.Fill(_roundaboutProgress, -1f);
            Array.Fill(_rescueViewRouteIndices, -1);
        }

        internal void ClearPopulation()
        {
            ReleaseAllSpecialJourneys();
            _scheduler.Clear();
            _commuteTripSource.Clear();
            _tripScheduler.Clear();
            Array.Clear(_enqueued, 0, _enqueued.Length);
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
            _hasLastHour = false;
            _needsSnap = false;
            _populationInitialized = false;
            _freeSpecialViewRouteIndices.Clear();
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
            RoadNetwork roadNetwork = null)
        {
            if (demands == null) throw new ArgumentNullException(nameof(demands));
            if (planner == null) throw new ArgumentNullException(nameof(planner));
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _planner = planner;
            _grid = grid ?? _grid;
            _demands = demands;
            _roadNetwork = roadNetwork ?? _roadNetwork;
            var previousAssignments = new List<PreviousAssignment>(CarCount);
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
                survivor.ResumeTile = currentRoute[Mathf.Clamp(_tileIndices[i], 0, currentRoute.Count - 1)];
                survivor.HasResume = true;
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
                        previous = new PreviousAssignment(
                            car,
                            previous.Outbound,
                            PrepareWorkLostReturn(car, reason, previous.Inbound),
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
                    List<Vector2Int> inbound =
                        PrepareWorkLostReturn(car, reason, previous.Inbound);
                    // preserve가 아닌 리빌드 = 라우팅 입력(도로·일방통행·턴 표지판·config)이
                    // 바뀐 리빌드다. 은퇴 carry-over의 잔여 구간도 최신 규칙으로 재계획해야
                    // 구 경로가 새 규칙을 위반하거나 사라진 도로를 밟지 않는다.
                    // (WorkLost+Outbound는 위 Prepare가 이미 최신 스냅샷으로 재계획함.
                    //  preserve 리빌드는 라우팅 불변이라 구 경로 = 재계획 결과 — 재계획 불요.)
                    if (car.State == CarState.Outbound)
                        outbound = ReplanLeg(outbound, car, fromResume: true) ?? outbound;
                    else if (car.State == CarState.Inbound
                        && ReferenceEquals(inbound, previous.Inbound))
                        inbound = ReplanLeg(inbound, car, fromResume: true) ?? inbound;
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
                Math.Max(1, _cfg.CarsPerHouse),
                Math.Min(_enqueued.Length, Math.Max(1, _cfg.MaxSimCars)),
                _cfg.MorningStartHour,
                _cfg.MorningEndHour,
                _cfg.EveningStartHour,
                _cfg.EveningEndHour,
                deferNewAssignments: _populationInitialized,
                purposes: _routinePurposes,
                transientStorageCapacity: _specialTransientCapacity);
            _populationInitialized = true;
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
            List<Vector2Int> replanned = _planner.ReplanFrom(
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
            List<Vector2Int> replanned = _planner.ReplanFrom(
                car.ResumeTile,
                inboundRoute[inboundRoute.Count - 1]);
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
            return inboundRoute;
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
                    for (int i = 0; i < CarCount; i++) _scheduler.Cars[i].HasResume = false;
                    Array.Clear(_rescueRoutes, 0, _rescueRoutes.Length);
                    Array.Fill(_rescueViewRouteIndices, -1);
                    Array.Clear(_rescueStages, 0, _rescueStages.Length);
                    Array.Clear(_offNetworkBlockedTicks, 0, _offNetworkBlockedTicks.Length);
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
                _scheduler.NotifyArrived(car);
                ResetCarRuntimeState(arrival.CarId);
                if (paidArrival)
                    events.QueueArrival(new ArrivalEvent(car.Work, _cfg.CoinPerTrip));
            }
            ProcessLivenessWatchdog(net);
            SyncLocations(net);
            roadTraffic?.SynchronizeSnapshots();
            return result;
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
                return new CarSnapshot
                {
                    Home = trip.Origin,
                    Work = trip.Destination,
                    State = CarState.Outbound,
                    RouteIndex = _rescueViewRouteIndices[index] >= 0
                        ? _rescueViewRouteIndices[index]
                        : specialJourney.ViewRouteIndex,
                    TileIndex = _tileIndices[index],
                    QueueSlot = _queueSlots[index],
                    QueueOffsetTiles = _queueOffsets[index],
                    HomeSlot = 0,
                    WorkSlot = 0,
                    IsVisible = true,
                    Purpose = VehicleTripPurpose.SpecialBuildingVisit,
                    AwaitingNextWave = false,
                    IntersectionProgress01 = _intersectionProgress[index],
                    LinkProgress01 = _linkProgress[index],
                    RoundaboutProgress01 = _roundaboutProgress[index]
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
                RoundaboutProgress01 = _roundaboutProgress[index]
            };
        }

        public bool TryGetNextTile(int carId, Vector2Int current, out Vector2Int next, out Dir entryDirAtNext)
        {
            next = default;
            entryDirAtNext = default;
            if (!TryRoute(carId, out List<Vector2Int> route)) return false;
            for (int i = 0; i < route.Count - 1; i++)
            {
                if (route[i] != current) continue;
                next = route[i + 1];
                Vector2Int delta = next - current;
                return TryRouteDirection(delta, out entryDirAtNext);
            }
            return false;
        }

        public bool IsDestination(int carId, Vector2Int tile) =>
            TryRoute(carId, out List<Vector2Int> route)
            && route.Count > 0
            && route[route.Count - 1] == tile;

        private void TryEnqueueDepartures(RoadQueueNetwork net)
        {
            for (int i = 0; i < CarCount; i++)
            {
                CommuteCar car = _scheduler.Cars[i];
                bool moving = car.State == CarState.Outbound || car.State == CarState.Inbound;
                if (!moving || _enqueued[i] || !TryRoute(i, out List<Vector2Int> route)) continue;
                // 리빌드 생존 차는 있던 자리에서 이어 달린다. 새 경로에 그 타일이 없으면
                // (도로가 헐렸다 등) 진행도를 포기하고 route[0]에서 다시 출발한다.
                if (!TryEnqueueRouteStart(
                        route,
                        car.ResumeTile,
                        ref car.HasResume,
                        net,
                        i,
                        out int start))
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
            }
        }

        private void ProcessLivenessWatchdog(RoadQueueNetwork net)
        {
            int rerouteThreshold = Math.Max(1, _cfg.GridlockValveTicks)
                * RescueRerouteMultiplier;
            int restartThreshold = Math.Max(1, _cfg.GridlockValveTicks)
                * RescueRestartMultiplier;

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
                        out int start))
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

            List<Vector2Int> rerouted = _planner.ReplanFrom(
                current,
                route[route.Count - 1]);
            if (rerouted == null || rerouted.Count == 0) return false;

            _rescueRoutes[carId] = rerouted;
            RegisterRescueViewRoute(carId, rerouted);
            _tileIndices[carId] = 0;
            if (_enqueued[carId]) return true;

            CommuteCar car = _scheduler.Cars[carId];
            car.HasResume = false;
            if (!TryEnqueueRouteStart(
                    rerouted,
                    default,
                    ref car.HasResume,
                    net,
                    carId,
                    out int start))
            {
                return true;
            }

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
            RoadQueueNetwork net)
        {
            int resumeIndex = -1;
            for (int p = 0; p < route.Count; p++)
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

        internal static bool TryEnqueueRouteStart(
            IReadOnlyList<Vector2Int> route,
            Vector2Int resumeTile,
            ref bool hasResume,
            RoadQueueNetwork net,
            int carId,
            out int start)
        {
            start = 0;
            bool retryingResume = hasResume;
            if (retryingResume)
            {
                start = FindResumeStart(route, resumeTile, net);
                if (start < 0)
                {
                    // No safe tile at or behind the logical position: abandon the
                    // ambiguous mid-route resume and explicitly restart from origin.
                    hasResume = false;
                    retryingResume = false;
                    start = 0;
                }
            }

            // A route whose origin itself is an intersection/roundabout state-machine
            // tile has no valid queue-only spawn. Keep it off-network and retry; a
            // later watchdog owns convergence for routes with no ordinary tile.
            if (route.Count == 0 || !net.IsSafeResumeTile(route[start])) return false;

            Dir entry = Dir.N;
            bool hasDirection = start > 0
                ? TryRouteDirection(route[start] - route[start - 1], out entry)
                : route.Count <= 1 || TryRouteDirection(route[1] - route[0], out entry);
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
                    _tileIndices[i] = car.State == CarState.ParkedWork
                        ? _outboundRoutes[car.RouteIndex].Count - 1
                        : 0;
                    continue;
                }
                _linkProgress[i] = linkProgress;
                _queueSlots[i] = slot;
                _queueOffsets[i] = queueOffsetTiles;
                _intersectionProgress[i] = intersectionProgress;
                _roundaboutProgress[i] = -1f;
                if (!TryRoute(i, out List<Vector2Int> route)) continue;
                for (int p = 0; p < route.Count; p++)
                {
                    if (route[p] != tile) continue;
                    _tileIndices[i] = p;
                    _roundaboutProgress[i] = CalculateRoundaboutProgress(route, p, roundaboutCell);
                    break;
                }
            }
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

            if (_grid.GetTile(request.Destination) != TileType.SpecialBuilding)
            {
                _tripScheduler.Forget(request);
                return;
            }

            if (!TryCreateSpecialJourney(
                    request,
                    gameHour,
                    out SpecialTripStartFailure failure))
            {
                int delay = failure == SpecialTripStartFailure.NoRoute
                    ? RouteRetryDelayTicks
                    : VehicleRetryDelayTicks;
                _tripScheduler.Requeue(request, tick, delay);
            }
        }

        private bool TryCreateSpecialJourney(
            SpecialBuildingVisitTripRequest request,
            float gameHour,
            out SpecialTripStartFailure failure)
        {
            failure = SpecialTripStartFailure.NoEligibleOrigin;
            bool foundRouteFailure = false;
            int seed = StableHash(TripScheduler.CreateJourneyId(request));
            IReadOnlyList<CommuteCar> cars = _scheduler.Cars;
            if (cars.Count > 0)
            {
                int start = PositiveModulo(seed, cars.Count);
                for (int offset = 0; offset < cars.Count; offset++)
                {
                    CommuteCar owner = cars[(start + offset) % cars.Count];
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
                            out CarState finalState))
                    {
                        continue;
                    }

                    if (TryLaunchSpecialJourney(
                            request,
                            owner,
                            origin,
                            finalDestination,
                            finalState,
                            out SpecialTripStartFailure launchFailure))
                    {
                        failure = SpecialTripStartFailure.None;
                        return true;
                    }

                    if (launchFailure == SpecialTripStartFailure.VehicleCapacity)
                    {
                        failure = launchFailure;
                        return false;
                    }

                    foundRouteFailure |=
                        launchFailure == SpecialTripStartFailure.NoRoute;
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
                Vector2Int home = houses[(houseStart + offset) % houses.Count];
                if (TryLaunchSpecialJourney(
                        request,
                        null,
                        home,
                        home,
                        CarState.ParkedHome,
                        out SpecialTripStartFailure launchFailure))
                {
                    failure = SpecialTripStartFailure.None;
                    return true;
                }

                if (launchFailure == SpecialTripStartFailure.VehicleCapacity)
                {
                    failure = launchFailure;
                    return false;
                }

                foundRouteFailure |=
                    launchFailure == SpecialTripStartFailure.NoRoute;
            }

            failure = foundRouteFailure
                ? SpecialTripStartFailure.NoRoute
                : SpecialTripStartFailure.NoEligibleOrigin;
            return false;
        }

        private bool TryLaunchSpecialJourney(
            SpecialBuildingVisitTripRequest request,
            CommuteCar routineOwner,
            Vector2Int origin,
            Vector2Int finalDestination,
            CarState finalRoutineState,
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

            if (!TryPlanBuildingRoute(
                    origin,
                    request.Destination,
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

            var journey = new SpecialTripJourney(
                request,
                vehicle,
                routineOwner,
                origin,
                finalDestination,
                finalRoutineState,
                firstRoute,
                secondRoute);
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
            out CarState finalState)
        {
            if (owner.State == CarState.ParkedWork)
            {
                if (gameHour < owner.DepartWorkHour)
                {
                    origin = default;
                    finalDestination = default;
                    finalState = CarState.ParkedWork;
                    return false;
                }

                origin = owner.Work;
                finalDestination = owner.Home;
                finalState = CarState.ParkedHome;
                return true;
            }

            bool workWindow = gameHour >= owner.DepartHomeHour &&
                gameHour < owner.DepartWorkHour;
            if (workWindow)
            {
                origin = default;
                finalDestination = default;
                finalState = CarState.ParkedHome;
                return false;
            }

            origin = owner.Home;
            finalDestination = owner.Home;
            finalState = CarState.ParkedHome;
            return true;
        }

        private bool TryPlanBuildingRoute(
            Vector2Int origin,
            Vector2Int destination,
            out List<Vector2Int> route)
        {
            route = null;
            _originAccessRoads.Clear();
            _destinationAccessRoads.Clear();
            _roadNetwork.CollectAccessRoads(origin, _originAccessRoads);
            _roadNetwork.CollectAccessRoads(destination, _destinationAccessRoads);

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
                    if (_roadNetwork.RegionOf(to) != region)
                    {
                        continue;
                    }

                    List<Vector2Int> candidate =
                        _planner.PlanVehicleTrip(from, to);
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
            out List<Vector2Int> route)
        {
            route = null;
            int region = _roadNetwork.RegionOf(originRoad);
            if (region < 0)
            {
                return false;
            }

            _destinationAccessRoads.Clear();
            _roadNetwork.CollectAccessRoads(destination, _destinationAccessRoads);
            for (int index = 0;
                 index < _destinationAccessRoads.Count;
                 index++)
            {
                Vector2Int destinationRoad = _destinationAccessRoads[index];
                if (_roadNetwork.RegionOf(destinationRoad) != region)
                {
                    continue;
                }

                List<Vector2Int> candidate =
                    _planner.PlanVehicleTrip(originRoad, destinationRoad);
                if (candidate == null || candidate.Count == 0)
                {
                    continue;
                }

                route = candidate;
                return true;
            }

            return false;
        }

        private void HandleSpecialTripArrival(
            int carId,
            SpecialTripJourney journey,
            SimEventBuffer events)
        {
            VehicleTripSnapshot completed = journey.CompleteCurrentLeg();
            events.QueueTripArrival(new VehicleTripArrivedEvent(completed));
            // 방문 도착 보상. ArrivalEvent 를 타면 주간 적립·HUD·피드·퀘스트가 기존 구독으로 따라온다.
            if (completed.RewardCoins > 0)
                events.QueueArrival(new ArrivalEvent(completed.Destination, completed.RewardCoins));
            ResetCarRuntimeState(carId);

            if (journey.TryBeginContinuation())
            {
                journey.Vehicle.ConfigureTransient(
                    journey.CurrentTrip.Origin);
                RegisterSpecialViewRoute(journey);
                return;
            }

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
                List<Vector2Int> currentRoute;
                bool planned = journey.Vehicle.HasResume
                    ? TryPlanRoadToBuilding(
                        journey.Vehicle.ResumeTile,
                        journey.CurrentTrip.Destination,
                        out currentRoute)
                    : TryPlanBuildingRoute(
                        journey.CurrentTrip.Origin,
                        journey.CurrentTrip.Destination,
                        out currentRoute);
                if (!planned)
                {
                    CancelSpecialJourney(journey);
                    continue;
                }

                List<Vector2Int> continuation = null;
                if (journey.CurrentLegIndex == 0 &&
                    journey.FinalDestination != journey.Request.Destination &&
                    !TryPlanBuildingRoute(
                        journey.Request.Destination,
                        journey.FinalDestination,
                        out continuation))
                {
                    CancelSpecialJourney(journey);
                    continue;
                }

                journey.ReplaceRoutes(currentRoute, continuation);
                RegisterSpecialViewRoute(journey);
            }
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

            int carIndex = FindCarIndex(journey.Vehicle);
            if (carIndex >= 0)
            {
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
