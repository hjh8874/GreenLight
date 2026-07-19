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
            Array.Fill(_queueSlots, -1);
        }

        public void Rebuild(DemandMap demands, RoutePlanner planner, RoadQueueNetwork net)
        {
            if (demands == null) throw new ArgumentNullException(nameof(demands));
            if (planner == null) throw new ArgumentNullException(nameof(planner));
            _net = net ?? throw new ArgumentNullException(nameof(net));
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
                _scheduler.SnapToHour(gameHour);
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
                WorkSlot = car.WorkSlot
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
                return TryDirection(next - current, out entryDirAtNext);
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
                Dir entry = Dir.N;
                if (route.Count > 1 && !TryDirection(route[1] - route[0], out entry)) continue;
                if (!net.TryEnqueue(route[0], entry, i)) continue;
                _enqueued[i] = true;
                _tileIndices[i] = 0;
                _queueSlots[i] = 0;
            }
        }

        private void SyncLocations(RoadQueueNetwork net)
        {
            for (int i = 0; i < CarCount; i++)
            {
                CommuteCar car = _scheduler.Cars[i];
                if (!_enqueued[i] || !net.TryLocateCar(i, out Vector2Int tile, out _, out int slot))
                {
                    _queueSlots[i] = -1;
                    _tileIndices[i] = car.State == CarState.ParkedWork
                        ? _outboundRoutes[car.RouteIndex].Count - 1
                        : 0;
                    continue;
                }
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
