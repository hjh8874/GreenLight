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
        public float LinkProgress01;
    }

    internal sealed class CarSim : ICarRouteProvider
    {
        private const float JumpThresholdHours = 1f;

        private readonly SimConfig _cfg;
        private readonly CommuteScheduler _scheduler = new();
        private readonly List<Vector2Int> _sources = new(96);
        private readonly List<Vector2Int> _sinks = new(96);
        private readonly List<List<Vector2Int>> _outboundRoutes = new(96);
        private readonly List<List<Vector2Int>> _returnRoutes = new(96);
        private readonly List<int> _plannerRouteIndices = new(96);
        private readonly bool[] _enqueued;
        private readonly int[] _tileIndices;
        private readonly int[] _queueSlots;
        private readonly float[] _linkProgress;
        private RoadQueueNetwork _net;
        private float _lastHour;
        private bool _hasLastHour;
        private bool _needsSnap;

        public int CarCount => _scheduler.Cars.Count;
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

        public CarSim(in SimConfig cfg)
        {
            _cfg = cfg;
            int maxCars = Math.Max(1, cfg.MaxSimCars);
            _enqueued = new bool[maxCars];
            _tileIndices = new int[maxCars];
            _queueSlots = new int[maxCars];
            _linkProgress = new float[maxCars];
            Array.Fill(_queueSlots, -1);
            Array.Clear(_linkProgress, 0, _linkProgress.Length);
        }

        public void Rebuild(DemandMap demands, RoutePlanner planner, RoadQueueNetwork net)
        {
            if (demands == null) throw new ArgumentNullException(nameof(demands));
            if (planner == null) throw new ArgumentNullException(nameof(planner));
            _net = net ?? throw new ArgumentNullException(nameof(net));
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
            _plannerRouteIndices.Clear();

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
                Math.Max(1, _cfg.OfficeParkingSlots),
                Math.Max(1, _cfg.CarsPerHouse),
                Math.Min(_enqueued.Length, Math.Max(1, _cfg.MaxSimCars)),
                _cfg.MorningStartHour,
                _cfg.MorningEndHour,
                _cfg.EveningStartHour,
                _cfg.EveningEndHour);
            Array.Clear(_enqueued, 0, _enqueued.Length);
            Array.Clear(_tileIndices, 0, _tileIndices.Length);
            Array.Fill(_queueSlots, -1);
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
                }
                else
                {
                    // 건설(토폴로지 리빌드): 신규 차만 수렴시키고 생존 차의 상태·진행도는 지킨다.
                    // 전체 SnapToHour를 쓰면 주행 중이던 차가 전부 주차로 되돌아가 순간이동한다.
                    _scheduler.SnapNewToHour(gameHour);
                }
                Array.Clear(_enqueued, 0, _enqueued.Length);
                Array.Fill(_queueSlots, -1);
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
                if (paidArrival)
                    events.QueueArrival(new ArrivalEvent(car.Work, _cfg.CoinPerTrip));
            }
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
                RouteIndex = _plannerRouteIndices[car.RouteIndex],
                TileIndex = _tileIndices[index],
                QueueSlot = _queueSlots[index],
                HomeSlot = car.HomeSlot,
                WorkSlot = car.WorkSlot,
                LinkProgress01 = _linkProgress[index]
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
                if (TryDirection(delta, out entryDirAtNext)) return true;
                if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
                    entryDirAtNext = delta.x >= 0 ? Dir.E : Dir.W;
                else entryDirAtNext = delta.y >= 0 ? Dir.N : Dir.S;
                return delta != Vector2Int.zero;
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
                int start = 0;
                if (car.HasResume)
                {
                    car.HasResume = false;
                    for (int p = 0; p < route.Count; p++)
                        if (route[p] == car.ResumeTile) { start = p; break; }
                }
                Dir entry = Dir.N;
                if (start > 0 && !TryDirection(route[start] - route[start - 1], out entry)) start = 0;
                if (start == 0 && route.Count > 1 && !TryDirection(route[1] - route[0], out entry)) continue;
                if (!net.TryEnqueue(route[start], entry, i)) continue;
                _enqueued[i] = true;
                _tileIndices[i] = start;
                _queueSlots[i] = 0;
            }
        }

        private void SyncLocations(RoadQueueNetwork net)
        {
            for (int i = 0; i < CarCount; i++)
            {
                CommuteCar car = _scheduler.Cars[i];
                if (!_enqueued[i] || !net.TryLocateCar(i, out Vector2Int tile, out _, out int slot, out float linkProgress))
                {
                    _linkProgress[i] = 0f;
                    _queueSlots[i] = -1;
                    _tileIndices[i] = car.State == CarState.ParkedWork
                        ? _outboundRoutes[car.RouteIndex].Count - 1
                        : 0;
                    continue;
                }
                _linkProgress[i] = linkProgress;
                _queueSlots[i] = slot;
                List<Vector2Int> route = car.State == CarState.Inbound
                    ? _returnRoutes[car.RouteIndex]
                    : _outboundRoutes[car.RouteIndex];
                for (int p = 0; p < route.Count; p++)
                    if (route[p] == tile) { _tileIndices[i] = p; break; }
            }
        }

        private bool TryRoute(int carId, out List<Vector2Int> route)
        {
            route = null;
            if (carId < 0 || carId >= CarCount) return false;
            CommuteCar car = _scheduler.Cars[carId];
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
    }
}
