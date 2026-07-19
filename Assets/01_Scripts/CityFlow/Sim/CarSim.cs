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
        // 리빌드 연속성(2026-07-18): 생존 차가 서 있던 타일 → 리빌드 후 그 자리에서 재개.
        // carId가 리스트 인덱스라 리빌드 때 재사상되므로 큐는 비워야 하지만, 위치까지 잃을 필요는 없다.
        private readonly Dictionary<CommuteCar, Vector2Int> _resumeTiles = new(96);
        private readonly Vector2Int[] _resumeTile;
        private readonly bool[] _hasResume;
        private RoadQueueNetwork _net;
        private float _lastHour;
        private bool _hasLastHour;
        private bool _needsFullSnap;   // 시각 점프·세이브 복원 = 전 차량 주차 수렴
        private bool _needsNewSnap;    // 토폴로지 리빌드 = 신규 차만 수렴(생존 차 상태 보존)

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
            _resumeTile = new Vector2Int[maxCars];
            _hasResume = new bool[maxCars];
            Array.Fill(_queueSlots, -1);
        }

        // 세이브 복원 = 전체 교체. sticky 생존 매칭에 로드 이전 차가 새어들면 안 되므로 스케줄러를
        // 비우고 전체 스냅을 예약한다(복원 후 첫 Step에서 전 차량 주차 수렴).
        // 이전에는 Rebuild가 무조건 전체 스냅이라 이 누수가 가려져 있었다(감사 nit 2026-07-18).
        internal void ResetForRestore()
        {
            _scheduler.Clear();
            _resumeTiles.Clear();
            Array.Clear(_hasResume, 0, _hasResume.Length);
            Array.Clear(_enqueued, 0, _enqueued.Length);
            Array.Fill(_queueSlots, -1);
            _needsFullSnap = true;
            _needsNewSnap = false;
        }

        public void Rebuild(DemandMap demands, RoutePlanner planner, RoadQueueNetwork net)
        {
            if (demands == null) throw new ArgumentNullException(nameof(demands));
            if (planner == null) throw new ArgumentNullException(nameof(planner));
            _net = net ?? throw new ArgumentNullException(nameof(net));
            CaptureResumeTiles();   // 경로 리스트를 지우기 전에 — 생존 차의 현재 타일 보존
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
                // 캡 통일(2026-07-18): work 슬롯 상한 = 일자리 용량(DemandMap이 배정에 쓰는 그 수).
                // 예전엔 OfficeParkingSlots(6)를 넘겨서, DemandMap이 OfficeCapacity(20)까지 배정해도
                // 6대만 통근하고 나머지는 사장됐다(단핵 도시 측정: 배정 76 → 통근 47, 38% 증발).
                // 일자리 수 = 실제 통근 수 = 코인 지급 횟수가 되도록 단일 레버로 통일한다.
                // 스케줄러는 sink 종류를 모르므로 두 용량의 최대값 — 종류별 상한은 DemandMap이 이미 건다.
                Math.Max(1, Math.Max(_cfg.OfficeCapacity, _cfg.SchoolCapacity)),
                Math.Max(1, _cfg.CarsPerHouse),
                Math.Min(_enqueued.Length, Math.Max(1, _cfg.MaxSimCars)),
                _cfg.MorningStartHour,
                _cfg.MorningEndHour,
                _cfg.EveningStartHour,
                _cfg.EveningEndHour);
            Array.Clear(_enqueued, 0, _enqueued.Length);
            Array.Clear(_tileIndices, 0, _tileIndices.Length);
            Array.Fill(_queueSlots, -1);
            ApplyResumeTiles();
            // 토폴로지 리빌드는 전체 스냅이 아니다 — 생존 차는 상태·위치 유지, 신규 차만 수렴.
            // (전체 스냅은 시각 점프·세이브 복원에서만: _needsFullSnap)
            _needsNewSnap = true;
        }

        // 이동 중이던 차가 지금 서 있는 실제 타일을 기억한다(인덱스가 아니라 타일 — 리빌드로
        // 경로가 바뀌어도 같은 타일이 새 경로에 있으면 이어서 달릴 수 있게).
        private void CaptureResumeTiles()
        {
            _resumeTiles.Clear();
            IReadOnlyList<CommuteCar> cars = _scheduler.Cars;
            for (int i = 0; i < cars.Count && i < _enqueued.Length; i++)
            {
                if (!_enqueued[i]) continue;
                CommuteCar car = cars[i];
                if (car.RouteIndex < 0 || car.RouteIndex >= _outboundRoutes.Count) continue;
                List<Vector2Int> route = car.State == CarState.Inbound
                    ? _returnRoutes[car.RouteIndex]
                    : _outboundRoutes[car.RouteIndex];
                int p = _tileIndices[i];
                if (route != null && p > 0 && p < route.Count) _resumeTiles[car] = route[p];
            }
        }

        // 새 인덱스 기준으로 재개 타일을 재사상(생존 차 객체가 키라 인덱스 변경에 안전).
        private void ApplyResumeTiles()
        {
            Array.Clear(_hasResume, 0, _hasResume.Length);
            if (_resumeTiles.Count == 0) return;
            IReadOnlyList<CommuteCar> cars = _scheduler.Cars;
            for (int i = 0; i < cars.Count && i < _hasResume.Length; i++)
            {
                if (!_resumeTiles.TryGetValue(cars[i], out Vector2Int tile)) continue;
                _resumeTile[i] = tile;
                _hasResume[i] = true;
            }
            _resumeTiles.Clear();
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
            if (jumped || _needsFullSnap)
            {
                // 시각 점프·세이브 복원: 이동 연출을 버리고 전 차량을 주차 상태로 조대 수렴.
                net.RemoveAllCars();
                _scheduler.SnapToHour(gameHour);
                Array.Clear(_enqueued, 0, _enqueued.Length);
                Array.Fill(_queueSlots, -1);
                Array.Clear(_hasResume, 0, _hasResume.Length);
                _needsFullSnap = false;
                _needsNewSnap = false;
            }
            else if (_needsNewSnap)
            {
                // 토폴로지 리빌드(건설/철거): carId가 재사상되므로 큐는 비우되, 생존 차의 상태는
                // 보존하고 신규 차만 현재 시각으로 수렴한다. 재개 위치는 TryEnqueueDepartures가 복원.
                // 이게 없으면 무언가 배치할 때마다 도로 위 차가 전부 주차장으로 텔레포트한다(환 라이브 2026-07-18).
                net.RemoveAllCars();
                _scheduler.SnapNewToHour(gameHour);
                Array.Clear(_enqueued, 0, _enqueued.Length);
                Array.Fill(_queueSlots, -1);
                _needsNewSnap = false;
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

                int start = ResumeIndex(i, route);
                Dir entry = Dir.N;
                // 진입 방향: 재개 지점은 직전 세그먼트로, 출발점은 첫 세그먼트로 계산.
                if (start > 0)
                {
                    if (!TryDirection(route[start] - route[start - 1], out entry))
                    {
                        _hasResume[i] = false;   // 재개 불가 — 다음 틱에 출발점부터
                        continue;
                    }
                }
                else if (route.Count > 1 && !TryDirection(route[1] - route[0], out entry)) continue;

                if (!net.TryEnqueue(route[start], entry, i)) continue;   // 만석이면 재개 정보 유지하고 재시도
                _hasResume[i] = false;
                _enqueued[i] = true;
                _tileIndices[i] = start;
                _queueSlots[i] = 0;
            }
        }

        // 리빌드 직후 1회: 기억한 타일이 새 경로에 있으면 그 지점에서 재개(없으면 출발점부터).
        private int ResumeIndex(int carId, List<Vector2Int> route)
        {
            if (carId >= _hasResume.Length || !_hasResume[carId]) return 0;
            Vector2Int tile = _resumeTile[carId];
            for (int p = 0; p < route.Count; p++)
                if (route[p] == tile) return p;
            return 0;
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
