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
        public int HomeSlot;
        public int WorkSlot;
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

        private readonly SimConfig _cfg;
        private readonly CommuteScheduler _scheduler = new();
        private readonly List<Vector2Int> _sources = new(96);
        private readonly List<Vector2Int> _sinks = new(96);
        private readonly List<List<Vector2Int>> _outboundRoutes = new(96);
        private readonly List<List<Vector2Int>> _returnRoutes = new(96);
        private readonly List<List<Vector2Int>> _viewOutboundRoutes = new(96);
        private readonly List<List<Vector2Int>> _viewReturnRoutes = new(96);
        private readonly List<int> _plannerRouteIndices = new(96);
        private readonly bool[] _enqueued;
        private readonly int[] _tileIndices;
        private readonly int[] _queueSlots;
        private readonly float[] _intersectionProgress;
        private readonly float[] _linkProgress;
        private readonly float[] _roundaboutProgress;
        private readonly List<Vector2Int>[] _rescueRoutes;
        private readonly int[] _rescueViewRouteIndices;
        private readonly byte[] _rescueStages;
        private readonly int[] _offNetworkBlockedTicks;
        private RoadQueueNetwork _net;
        private RoutePlanner _planner;
        private float _lastHour;
        private bool _hasLastHour;
        private bool _needsSnap;

        public int CarCount => _scheduler.Cars.Count;
        internal IReadOnlyList<List<Vector2Int>> ActiveRoutes => _viewOutboundRoutes;
        internal IReadOnlyList<List<Vector2Int>> ActiveReturnRoutes => _viewReturnRoutes;
        public bool AllParkedHome
        {
            get
            {
                for (int i = 0; i < _scheduler.Cars.Count; i++)
                    if (_scheduler.Cars[i].State != CarState.ParkedHome) return false;
                return true;
            }
        }
        internal bool LastStepJumped { get; private set; }
        internal int RescueRerouteCount { get; private set; }
        internal int RescueRestartCount { get; private set; }
        internal int LastRescueCarId { get; private set; } = -1;
        internal Vector2Int LastRescueTile { get; private set; }

        public CarSim(in SimConfig cfg)
        {
            _cfg = cfg;
            int maxCars = Math.Max(1, cfg.MaxSimCars);
            _enqueued = new bool[maxCars];
            _tileIndices = new int[maxCars];
            _queueSlots = new int[maxCars];
            _intersectionProgress = new float[maxCars];
            _linkProgress = new float[maxCars];
            _roundaboutProgress = new float[maxCars];
            _rescueRoutes = new List<Vector2Int>[maxCars];
            _rescueViewRouteIndices = new int[maxCars];
            _rescueStages = new byte[maxCars];
            _offNetworkBlockedTicks = new int[maxCars];
            Array.Fill(_queueSlots, -1);
            Array.Fill(_intersectionProgress, -1f);
            Array.Clear(_linkProgress, 0, _linkProgress.Length);
            Array.Fill(_roundaboutProgress, -1f);
            Array.Fill(_rescueViewRouteIndices, -1);
        }

        public void Rebuild(DemandMap demands, RoutePlanner planner, RoadQueueNetwork net)
        {
            if (demands == null) throw new ArgumentNullException(nameof(demands));
            if (planner == null) throw new ArgumentNullException(nameof(planner));
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _planner = planner;
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
            _sources.Clear();
            _sinks.Clear();
            _outboundRoutes.Clear();
            _returnRoutes.Clear();
            _viewOutboundRoutes.Clear();
            _viewReturnRoutes.Clear();
            _plannerRouteIndices.Clear();
            for (int i = 0; i < planner.CarRoutes.Count; i++)
            {
                _viewOutboundRoutes.Add(planner.CarRoutes[i]);
                _viewReturnRoutes.Add(planner.ReturnRoutes[i]);
            }

            IReadOnlyList<Demand> pairs = demands.Demands;
            int count = Math.Min(pairs.Count, planner.CarRoutes.Count);
            for (int i = 0; i < count; i++)
            {
                List<Vector2Int> outbound = planner.CarRoutes[i];
                List<Vector2Int> inbound = planner.ReturnRoutes[i];
                if (outbound == null || inbound == null || outbound.Count == 0 || inbound.Count == 0) continue;
                _sources.Add(pairs[i].Source);
                _sinks.Add(pairs[i].Sink);
                _outboundRoutes.Add(outbound);
                _returnRoutes.Add(inbound);
                _plannerRouteIndices.Add(i);
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
                _cfg.EveningEndHour);
            Array.Clear(_enqueued, 0, _enqueued.Length);
            Array.Clear(_tileIndices, 0, _tileIndices.Length);
            Array.Fill(_queueSlots, -1);
            Array.Fill(_intersectionProgress, -1f);
            Array.Clear(_linkProgress, 0, _linkProgress.Length);
            Array.Fill(_roundaboutProgress, -1f);
            Array.Clear(_rescueRoutes, 0, _rescueRoutes.Length);
            Array.Fill(_rescueViewRouteIndices, -1);
            Array.Clear(_rescueStages, 0, _rescueStages.Length);
            Array.Clear(_offNetworkBlockedTicks, 0, _offNetworkBlockedTicks.Length);
            _needsSnap = true;
        }

        public StepResult Step(float gameHour, RoadQueueNetwork net, SimEventBuffer events)
            => Step(gameHour, net, events, null, 0);

        internal StepResult Step(
            float gameHour,
            RoadQueueNetwork net,
            SimEventBuffer events,
            ISignalGate signalGate,
            int tick)
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
                if (jumped)
                {
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
                Array.Fill(_intersectionProgress, -1f);
                Array.Clear(_linkProgress, 0, _linkProgress.Length);
                Array.Fill(_roundaboutProgress, -1f);
                _needsSnap = false;
            }
            _lastHour = gameHour;
            _hasLastHour = true;

            _scheduler.UpdateDepartures(gameHour);
            TryEnqueueDepartures(net);
            StepResult result = net.Step(this, signalGate, tick);
            for (int i = 0; i < net.ArrivalCount; i++)
            {
                ArrivalRecord arrival = net.GetArrival(i);
                if (arrival.CarId < 0 || arrival.CarId >= CarCount) continue;
                CommuteCar car = _scheduler.Cars[arrival.CarId];
                bool paidArrival = car.State == CarState.Outbound;
                _scheduler.NotifyArrived(car);
                _enqueued[arrival.CarId] = false;
                _queueSlots[arrival.CarId] = -1;
                _intersectionProgress[arrival.CarId] = -1f;
                _linkProgress[arrival.CarId] = 0f;
                _roundaboutProgress[arrival.CarId] = -1f;
                _rescueRoutes[arrival.CarId] = null;
                _rescueViewRouteIndices[arrival.CarId] = -1;
                _rescueStages[arrival.CarId] = 0;
                _offNetworkBlockedTicks[arrival.CarId] = 0;
                if (paidArrival)
                    events.QueueArrival(new ArrivalEvent(car.Work, _cfg.CoinPerTrip));
            }
            ProcessLivenessWatchdog(net);
            SyncLocations(net);
            return result;
        }

        public CarSnapshot GetCar(int index)
        {
            if (index < 0 || index >= CarCount) throw new ArgumentOutOfRangeException(nameof(index));
            CommuteCar car = _scheduler.Cars[index];
            return new CarSnapshot
            {
                Home = car.Home,
                Work = car.Work,
                State = car.State,
                RouteIndex = _rescueViewRouteIndices[index] >= 0
                    ? _rescueViewRouteIndices[index]
                    : _plannerRouteIndices[car.RouteIndex],
                TileIndex = _tileIndices[index],
                QueueSlot = _queueSlots[index],
                HomeSlot = car.HomeSlot,
                WorkSlot = car.WorkSlot,
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
            _intersectionProgress[carId] = -1f;
            _linkProgress[carId] = 0f;
            _roundaboutProgress[carId] = -1f;
        }

        private void ResetLocation(int carId)
        {
            _tileIndices[carId] = 0;
            _queueSlots[carId] = -1;
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
                        out int roundaboutCell))
                {
                    _linkProgress[i] = 0f;
                    _queueSlots[i] = -1;
                    _intersectionProgress[i] = -1f;
                    _roundaboutProgress[i] = -1f;
                    _tileIndices[i] = car.State == CarState.ParkedWork
                        ? _outboundRoutes[car.RouteIndex].Count - 1
                        : 0;
                    continue;
                }
                _linkProgress[i] = linkProgress;
                _queueSlots[i] = slot;
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

        private bool TryRoute(int carId, out List<Vector2Int> route)
        {
            route = null;
            if (carId < 0 || carId >= CarCount) return false;
            CommuteCar car = _scheduler.Cars[carId];
            if (car.RouteIndex < 0 || car.RouteIndex >= _outboundRoutes.Count) return false;
            if (_rescueRoutes[carId] != null)
            {
                route = _rescueRoutes[carId];
                return true;
            }
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
