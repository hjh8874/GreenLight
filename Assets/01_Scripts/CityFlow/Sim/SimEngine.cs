using System;
using System.Collections.Generic;
using UnityEngine;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;

namespace CityFlow.Sim
{
    // 엔진의 유일한 public 창구(파사드). Bootstrap이 생성하고 매 프레임 Tick(dt) 호출.
    // 내부 클래스(grid·network·demand·solver)는 전부 internal — 외부는 이 인터페이스들만 봄.
    public sealed class SimEngine : IPlacementService, IReadOnlyTileData,
        ICongestionHistory,
        IReadOnlyCityStats, ISimSaveSource, ISignalControl,
        IIntersectionFacilityService, ITrafficRuleService,
        IRouteDistanceProvider, IHighwayService,
        IBusStopInfrastructureService, IVehicleTripService,
        IRoadRoutePlanningService
    {
        SimConfig _config;   // seam(스펙 2026-07-12)으로 재주입 가능 — readonly 제거, ApplyConfig 참고
        readonly CityGrid _grid;
        readonly IWorldGridAccess _worldGridAccess;
        readonly RoadNetwork _network;
        readonly DemandMap _demand;
        private readonly ConstructionSites _construction = new();
        private readonly List<ConstructionSite> _completedBuffer = new(16);
        readonly RoutePlanner _planner;
        readonly RoadQueueNetwork _roadQueues;
        readonly RoadTrafficCoordinator _roadTraffic;
        readonly CarSim _carSim;
        readonly VehicleFootprint _standardVehicleFootprint;
        readonly DeviceStateAdapter _deviceState;
        readonly SignalGateAdapter _signalGate;
        readonly CongestionLevel[] _carCongestion;
        readonly CongestionLedger _congestionLedger;
        readonly InfrastructureEffectTracker _infrastructureEffectTracker;
        readonly SignalMap _signals = new SignalMap();
        // 배치 모드(AutoDetectSignals=false) 소유 상태: flat 정렬 유지 = SignalMap 순회 순서(결정론).
        readonly List<Vector2Int> _placedSignals = new();
        readonly HashSet<Vector2Int> _placedSet = new();
        // 회전교차로(스펙 2026-07-11): 신호와 배타 배치. SignalMap과 독립된 장치 상태.
        readonly List<Vector2Int> _placedRoundabouts = new();
        readonly HashSet<GreenWaveSegment> _activeGreenWaves = new HashSet<GreenWaveSegment>();
        readonly HashSet<Vector2Int> _roundaboutSet = new();
        // 입체교차(스펙 2026-07-12): 신호·로터리와 3자 배타. 로터리와 동형(SignalMap 무관, Rebuild 불필요).
        readonly List<Vector2Int> _placedOverpasses = new();
        readonly HashSet<Vector2Int> _overpassSet = new();
        // 우선도로(스펙 2026-07-13): 교차로 전용(로터리처럼) + 축 우선순위(Dictionary).
        // 간섭 계수만 바꿈 — 라우팅 무관이라 MarkTopologyDirty 안 함(로터리 규약).
        readonly List<Vector2Int> _placedPriorityRoads = new();
        readonly Dictionary<Vector2Int, Axis> _priorityDirs = new();
        // 일방통행(스펙 2026-07-12): 교차로 3형제와 달리 일반 도로 전용 — 조건이 반대라 자연 배타.
        // 방향값이 있어 좌표-전용 셋이 아니라 Dictionary(좌표→단위 방향) + flat 정렬 List(순회·세이브 순서).
        readonly Dictionary<Vector2Int, Vector2Int> _onewayDirs = new();
        readonly List<Vector2Int> _placedOneways = new();
        static readonly Vector2Int[] OnewayUnitDirs =
            { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
        // 턴 제한 표지판(스펙 2026-07-12): 5번째 배치 가족 — 교차로 전용이되 신호와 공존(로터리·입체와만 배타).
        // 일방통행과 동형(Dictionary<좌표,값> + flat 정렬 List) — 값이 방향 벡터 대신 TurnMode.
        readonly Dictionary<Vector2Int, TurnMode> _turnSigns = new();
        readonly List<Vector2Int> _placedTurnSigns = new();
        readonly List<HighwayLink> _highwayLinks = new();
        readonly Dictionary<Vector2Int, Vector2Int> _highwayPartners = new();
        readonly List<Vector2Int> _placedBusStops = new();
        readonly HashSet<Vector2Int> _busStopSet = new();
        readonly HashSet<Vector2Int> _busStopPlatformSet = new();
        static readonly Vector2Int[] BusStopNeighborDirections =
        {
            Vector2Int.right,
            Vector2Int.up,
            Vector2Int.left,
            Vector2Int.down
        };
        double _simTime;   // 시뮬 누적 시간(초) — 신호 초록/빨강 판정용(뷰)
        public double SimTime => _simTime;
        readonly SimStats _stats = new SimStats();
        readonly SimEventBuffer _events;
        float _acc;   // 아직 소비되지 않고 저금된 시간
        float _gameHour;
        float _lastCongestionHour;
        bool _hasLastCongestionHour;
        long _gameDay;
        int _lastStepArrivals;
        bool _demandRebalancePending;
        bool _buildingAssignmentChangePending;
        bool _roadTopologyChangePending;

        // 테스트 관찰용 seam. internal이라 테스트 어셈블리만 봄(InternalsVisibleTo).
        internal int StepCount { get; private set; }
        // 관찰 seam: ApplyConfig의 구조 필드 보존을 엔진의 실제 config로 직접 핀(우회 관찰 방지).
        internal SimConfig CurrentConfig => _config;
        // 관찰 seam: 일방 배치/철거가 재계획(dirty)을 강제하는지 테스트가 직접 핀(리뷰 위임분).
        internal bool TopologyDirtyForTest => _grid.TopologyDirty;
        internal float TripSuccessRateForTest => _stats.TripSuccessRate;
        internal RoadQueueNetwork RoadQueuesForTest => _roadQueues;
        internal DemandMap DemandForTest => _demand;
        public IRoadTrafficService RoadTraffic => _roadTraffic;
        public VehicleFootprint StandardVehicleFootprint =>
            _standardVehicleFootprint;
        internal int ConstructionSiteCountForTest => _construction.Count;

        public SimEngine(
            SimConfig config,
            SimEventHub hub,
            IWorldGridAccess worldGridAccess = null,
            VehicleFootprint? standardVehicleFootprint = null)
        {
            _worldGridAccess = worldGridAccess;
            if (_worldGridAccess != null)
            {
                config.GridWidth = _worldGridAccess.WorldWidth;
                config.GridHeight = _worldGridAccess.WorldHeight;
            }

            _config = config;
            _standardVehicleFootprint = standardVehicleFootprint ??
                VehicleFootprint.StandardDefault;
            _grid = new CityGrid(config.GridWidth, config.GridHeight);
            _network = new RoadNetwork(_grid);
            _demand = new DemandMap(config);
            _planner = new RoutePlanner(config.GridWidth, config.GridHeight);
            _roadQueues = new RoadQueueNetwork(
                config.GridWidth,
                config.GridHeight,
                config,
                _standardVehicleFootprint);
            _roadTraffic = new RoadTrafficCoordinator(
                _roadQueues,
                () => TickInterval,
                () => TickProgress01);
            _carSim = new CarSim(config);
            _deviceState = new DeviceStateAdapter(this);
            _signalGate = new SignalGateAdapter(this);
            _carCongestion = new CongestionLevel[config.GridWidth * config.GridHeight];
            _congestionLedger = new CongestionLedger();
            _congestionLedger.Configure(config.GridWidth, config.GridHeight);
            _infrastructureEffectTracker = new InfrastructureEffectTracker(_congestionLedger);
            hub.InfrastructureChanged += OnInfrastructureChanged;
            _events = new SimEventBuffer(hub);   // 계산 중 발행 금지 — 큐/Drain으로 재진입 차단
            RefreshBusCoverageReduction();
        }

        private void OnInfrastructureChanged(InfrastructureChangedEvent e)
        {
            if (e.IsRemove)
            {
                _infrastructureEffectTracker.OnRemoved(e.Tile);
            }
            else
            {
                _infrastructureEffectTracker.OnPlaced(e.Tile, _congestionLedger);
            }
        }

        public float LastDayJamRatio01(Vector2Int tile)
        {
            int index = tile.x + tile.y * _grid.Width;
            return _congestionLedger.LastDayJamRatio01(index);
        }

        private sealed class DeviceStateAdapter : IDeviceState
        {
            private readonly SimEngine _engine;
            public DeviceStateAdapter(SimEngine engine) => _engine = engine;
            public bool IsRoundabout(Vector2Int tile) => _engine._roundaboutSet.Contains(tile);
            public bool IsOverpass(Vector2Int tile) => _engine._overpassSet.Contains(tile);
            public RoadAxis PriorityAxis(Vector2Int tile)
            {
                if (_engine._priorityDirs.TryGetValue(tile, out Axis axis))
                    return axis == Axis.Horizontal ? RoadAxis.Horizontal : RoadAxis.Vertical;
                return RoadAxis.None;
            }
            public Vector2Int OnewayDir(Vector2Int tile) =>
                _engine._onewayDirs.TryGetValue(tile, out Vector2Int direction)
                    ? direction
                    : Vector2Int.zero;
            public bool IsTurnAllowed(Vector2Int tile, Dir entry, Dir exit)
            {
                if (!_engine._turnSigns.TryGetValue(tile, out TurnMode mode)) return true;
                Dir allowed = mode == TurnMode.LeftOnly
                    ? (Dir)(((int)entry + 3) % 4)
                    : (Dir)(((int)entry + 1) % 4);
                return exit == allowed;
            }
            public bool TryGetHighwayPartner(Vector2Int ramp, out Vector2Int partner) =>
                _engine._highwayPartners.TryGetValue(ramp, out partner);
        }

        private sealed class SignalGateAdapter : ISignalGate
        {
            private readonly SimEngine _engine;
            public SignalGateAdapter(SimEngine engine) => _engine = engine;
            public bool IsServiceOpen(Vector2Int tile, Dir entryDir, int tick) =>
                _engine.IsSignalGreen(tile, entryDir == Dir.E || entryDir == Dir.W);
            public bool HasSignal(Vector2Int tile) => _engine._signals.TryGet(tile, out _);
        }

        // SimConfig 런타임 재주입 seam(스펙 2026-07-12) — 정책 서비스(진우) 창구.
        // 계약 승격(팀 소비자 확정)은 합의 후. 지금은 public이지만 아직 "제안" 단계.
        // 구조 필드 3종(GridWidth/GridHeight/AutoDetectSignals)은 기존 값으로 강제 보존한다:
        // 그리드 크기는 RoutePlanner·RoadQueueNetwork가 생성 시점에
        // 고정 크기 배열로 굳혀서 런타임 리사이즈는 이 seam의 스코프 밖(재구축 필요)이고,
        // AutoDetectSignals는 세션 부트 스위치라 정책이 흔들면 배치 상태가 증발한다(지뢰).
        // Drain 핸들러 경유 호출 시 같은 프레임의 잔여 Step부터 반영(결정론 무해 — 순서가 고정이므로).
        // 리뷰 픽스(PR#53): Debug.Assert는 릴리스 빌드에서 스트립되어 퇴화 config가 조용히 통과 —
        // 시뮬이 멈춘 채 아무 신호도 없이 방치된다. bool 반환으로 실패를 호출자가 확인할 수 있게 한다.
        public bool ApplyConfig(in SimConfig next)
        {
            if (!(next.TickInterval > 0f && next.MaxStepsPerFrame >= 1))
            {
                UnityEngine.Debug.LogWarning(
                    "ApplyConfig: 퇴화 config 거부(TickInterval>0, MaxStepsPerFrame>=1 필요) — 적용 안 함");
                return false;
            }

            var merged = next;
            merged.GridWidth = _config.GridWidth;
            merged.GridHeight = _config.GridHeight;
            merged.AutoDetectSignals = _config.AutoDetectSignals;

            _config = merged;
            _demand.ApplyConfig(_config);
            RefreshBusCoverageReduction();
            MarkRoutingChangePending();   // 다음 틱에 Reassign+Plan 강제(즉시 재계산은 안 함 — 파이프라인 순서 보존)
            return true;
        }

        // 고정 틱 누산기: 프레임 dt가 들쭉날쭉해도 Step은 정확히 TickInterval마다 1번.
        public void Tick(float deltaTime)
        {
            _acc += deltaTime;
            int steps = 0;
            // steps 캡: 렉으로 dt가 튀어도 한 프레임에 폭주하지 않게(죽음의 나선 방지).
            while (_acc >= _config.TickInterval && steps < _config.MaxStepsPerFrame)
            {
                _acc -= _config.TickInterval;
                Step();
                steps++;
            }
            // ponytail: 캡에 걸린 잔여 _acc는 다음 프레임들로 이월(백로그). 폭주보단 지연 선택.
        }

        // 뷰가 고정 Sim 스냅샷 사이를 같은 위상으로만 번역하도록 제공한다.
        // 교통 판단에는 사용하지 않는 읽기 전용 관찰값이다.
        public float TickProgress01 => _config.TickInterval > 0f
            ? Mathf.Clamp01(_acc / _config.TickInterval)
            : 1f;
        public float TickInterval => _config.TickInterval;

        public void SetGameHour(float gameHour) =>
            _gameHour = Mathf.Repeat(gameHour, 24f);

        public void SetGameTime(long gameDay, float gameHour)
        {
            _gameDay = Math.Max(0L, gameDay);
            SetGameHour(gameHour);
        }

        // 고정 0.1s 시뮬 한 칸. 순서가 곧 파이프라인(blueprint §2 Step).
        void Step()
        {
            StepCount++;

            _simTime += _config.TickInterval;

            AdvanceConstruction();   // 공사 완성 → 승격. 채용 램프보다 먼저.

            if (_demand.AdvanceCompanyCapacities(
                _simTime
            ))
            {
                // 열린 자리만 늘어날 때는 기존 sticky 목적지를 해제할 필요가 없다.
                // 기존 차량을 보존한 채 새로 열린 자리만 다음 정상 리빌드에서 채운다.
                _grid.MarkTopologyDirty();
            }

            // 신규 회사/학교 배정은 운행 중 목적지를 바꾸지 않는다. 배치 이벤트나 하루 경계에서
            // pending을 세운 뒤, 전 차가 집에 돌아온 안전시점에 sticky를 한 번만 풀고 재구축한다.
            if (_demandRebalancePending && _carSim.AllParkedHome)
            {
                _demand.ClearStickyAssignments();
                _demandRebalancePending = false;
                // 전원 귀가 뒤의 명시적 sticky clear는 전체 재최적화 경계다.
                // 직전 건물 추가의 생존 짝 고정 모드는 여기서 끝낸다.
                _buildingAssignmentChangePending = false;
                _grid.MarkTopologyDirty();
            }

            // 배치도가 바뀐 틱에만 경로·수요 재계산(더티 플래그 — 매 틱 재계획 금지).
            if (_grid.TopologyDirty)
            {
                _demand.Reassign(_grid, _network);            // 도달성(같은 섬) 우선 배정
                RebuildSignals();                              // 교차로 재감지(살아남은 신호 오프셋 보존)
                _planner.Plan(_demand, _network, _grid, _config, _onewayDirs, _turnSigns, _highwayLinks);   // 방향 규칙 + 고가 링크
                _roadQueues.RebuildTopology(_grid, _deviceState);
                _carSim.Rebuild(
                    _demand,
                    _planner,
                    _roadQueues,
                    PreserveExistingAssignmentsForRebuild(),
                    _grid,
                    _network,
                    _events);
                ClearRebuildChangeKinds();
                _grid.ClearTopologyDirty();
            }

            StepResult carResult = _carSim.Step(
                _gameDay,
                _gameHour,
                _roadQueues,
                _events,
                _signalGate,
                StepCount,
                _roadTraffic);
            if (_carSim.HasCompletedRetirements)
            {
                _buildingAssignmentChangePending = true;
                _grid.MarkTopologyDirty();
            }
            _lastStepArrivals = carResult.Arrivals;
            float jamRatio = ScanCarCongestion();
            bool gameDayWrapped = _stats.UpdateCarSim(
                _gameHour,
                carResult.Arrivals,
                _carSim.SimulatedVehicleCount,
                _carSim.LastStepJumped,
                jamRatio,
                _config);
            if (gameDayWrapped)
                _demandRebalancePending = true;

            if (_config.GreenWaveScanInterval > 0 && StepCount % _config.GreenWaveScanInterval == 0)
            {
                ScanGreenWaves();
            }

            _events.Drain();
        }

        private float ScanCarCongestion()
        {
            bool wrapped = _hasLastCongestionHour && _gameHour < _lastCongestionHour;
            if (wrapped && !_carSim.LastStepJumped)
            {
                _congestionLedger.OnDayWrap();
                foreach (var effect in _infrastructureEffectTracker.EvaluateOnDayWrap(_congestionLedger))
                {
                    _events.QueueInfrastructureEffect(effect);
                }
            }

            float stepGameHours = 0f;
            if (_hasLastCongestionHour && !_carSim.LastStepJumped)
            {
                stepGameHours = wrapped
                    ? (24f - _lastCongestionHour) + _gameHour
                    : Mathf.Max(0f, _gameHour - _lastCongestionHour);
                // A calendar jump is not a period for which a road state was observed.
                if (stepGameHours > 1f) stepGameHours = 0f;
            }

            int jammed = 0;
            int roads = _grid.RoadTileCount;
            for (int i = 0; i < roads; i++)
            {
                int index = _grid.GetRoadTileIndex(i);
                var tile = new Vector2Int(
                    index % _grid.Width,
                    index / _grid.Width);
                float occupancy = _roadQueues.MaxOccupancy01(tile);
                CongestionLevel level = CongestionForOccupancy(occupancy, _config);
                _congestionLedger.Record(index, level, stepGameHours);
                if (_carCongestion[index] != level)
                {
                    _carCongestion[index] = level;
                    _events.QueueCongestion(new CongestionEvent(tile, level));
                }
                if (level == CongestionLevel.Jam) jammed++;
            }
            _lastCongestionHour = _gameHour;
            _hasLastCongestionHour = true;
            return roads <= 0 ? 0f : (float)jammed / roads;
        }

        private void ResetCarCongestion()
        {
            int roads = _grid.RoadTileCount;
            for (int i = 0; i < roads; i++)
            {
                int index = _grid.GetRoadTileIndex(i);
                if (_carCongestion[index] == CongestionLevel.Free)
                {
                    continue;
                }

                var tile = new Vector2Int(
                    index % _grid.Width,
                    index / _grid.Width);
                _events.QueueCongestion(new CongestionEvent(
                    tile,
                    CongestionLevel.Free));
            }

            Array.Clear(_carCongestion, 0, _carCongestion.Length);
            _congestionLedger.Clear();
            _infrastructureEffectTracker.ClearPending();
            _hasLastCongestionHour = false;
        }

        private struct GreenWaveSegment : System.IEquatable<GreenWaveSegment>
        {
            public readonly Vector2Int A;
            public readonly Vector2Int B;
            public GreenWaveSegment(Vector2Int a, Vector2Int b)
            {
                if (a.x < b.x || (a.x == b.x && a.y < b.y))
                {
                    A = a;
                    B = b;
                }
                else
                {
                    A = b;
                    B = a;
                }
            }

            public bool Equals(GreenWaveSegment other) => A == other.A && B == other.B;
            public override bool Equals(object obj) => obj is GreenWaveSegment other && Equals(other);
            public override int GetHashCode() => (A.GetHashCode() * 397) ^ B.GetHashCode();
        }

        private static readonly Vector2Int[] _scanDirections = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };

        private void ScanGreenWaves()
        {
            HashSet<GreenWaveSegment> currentWaves = new HashSet<GreenWaveSegment>();

            foreach (var fromTile in _signals.Tiles)
            {
                if (!_signals.TryGet(fromTile, out var fromSignal)) continue;

                foreach (Vector2Int dir in _scanDirections)
                {
                    Vector2Int current = fromTile + dir;
                    int dist = 1;
                    Signal toSignal = null;
                    Vector2Int toTile = current;

                    while (_grid.InBounds(current) && _grid.GetTile(current) == TileType.Road)
                    {
                        if (_signals.TryGet(current, out toSignal))
                        {
                            toTile = current;
                            break;
                        }
                        current += dir;
                        dist++;
                    }

                    if (toSignal != null)
                    {
                        // 1. 유저가 오프셋을 조작하여 상대 위상이 변경되어야만 보상 허용 (조작 전 기본 상태 및 단순 전체 쉬프트에서 남발 방지)
                        if (fromSignal.OffsetSlots == toSignal.OffsetSlots)
                            continue;

                        // 2. 단일 헬퍼를 사용하여 일관된 이동 시간(초) 산출
                        float travelSecs = _config.GetTravelSeconds(dist);
                        float travelSlots = travelSecs / SignalMath.SlotSeconds;

                        float eff = SignalMath.GreenWaveEfficiency(fromSignal, toSignal, travelSlots, _config.GreenWaveFloor);

                        if (eff >= _config.GreenWaveThreshold)
                        {
                            var seg = new GreenWaveSegment(fromTile, toTile);
                            if (currentWaves.Add(seg) && !_activeGreenWaves.Contains(seg))
                            {
                                int magnitude = (int)((eff - _config.GreenWaveMagnitudeOffset) * _config.GreenWaveMagnitudeScale);
                                if (magnitude < 1) magnitude = 1;
                                _events.QueueBurst(new FlowBurstEvent(toTile, magnitude));
                            }
                        }
                    }
                }
            }

            _activeGreenWaves.Clear();
            foreach (var wave in currentWaves)
            {
                _activeGreenWaves.Add(wave);
            }
        }

        internal static CongestionLevel CongestionForOccupancy(float occupancy, in SimConfig cfg) =>
            occupancy >= cfg.QueueJamRatio
                ? CongestionLevel.Jam
                : occupancy >= cfg.QueueSlowRatio
                    ? CongestionLevel.Slow
                    : CongestionLevel.Free;


        // 신호 재구축 단일 창구: 자동 = 전 교차로 스캔 / 배치 = 배치 목록(비교차로는 먼저 소멸).
        void RebuildSignals()
        {
            if (_config.AutoDetectSignals)
            {
                _signals.Rebuild(_grid);
                return;
            }
            _placedSignals.RemoveAll(t =>
            {
                if (_grid.IsIntersection(t)) return false;
                _placedSet.Remove(t);          // 도로 철거로 교차로 해제 → 배치도 소멸(환불은 경제 영역)
                return true;
            });
            _placedRoundabouts.RemoveAll(t =>
            {
                if (_grid.IsIntersection(t)) return false;
                _roundaboutSet.Remove(t);      // 교차로 해제 → 로터리도 소멸(신호와 동일 규약)
                return true;
            });
            _placedOverpasses.RemoveAll(t =>
            {
                if (_grid.IsIntersection(t)) return false;
                _overpassSet.Remove(t);        // 교차로 해제 → 입체교차도 소멸(동일 규약)
                return true;
            });
            _placedPriorityRoads.RemoveAll(t =>
            {
                if (_grid.IsIntersection(t)) return false;
                _priorityDirs.Remove(t);       // 교차로 해제 → 우선도로도 소멸(동일 규약)
                return true;
            });
            _placedOneways.RemoveAll(t =>
            {
                // 조건이 반대(비교차로 유지) — 도로 철거든 교차로화든 배치 조건 위반이면 소멸.
                if (_grid.GetTile(t) == TileType.Road && !_grid.IsIntersection(t)) return false;
                _onewayDirs.Remove(t);
                return true;
            });
            _placedTurnSigns.RemoveAll(t =>
            {
                if (_grid.IsIntersection(t)) return false;
                _turnSigns.Remove(t);          // 교차로 해제 → 표지판도 소멸(신호 가족과 동일 규약)
                return true;
            });
            _signals.Rebuild(_grid, _placedSignals);
        }

        internal void EnsureCarTopologyCurrent()
        {
            if (!_grid.TopologyDirty) return;
            _demand.Reassign(_grid, _network);
            RebuildSignals();
            _planner.Plan(_demand, _network, _grid, _config, _onewayDirs, _turnSigns, _highwayLinks);
            _roadQueues.RebuildTopology(_grid, _deviceState);
            _carSim.Rebuild(
                _demand,
                _planner,
                _roadQueues,
                PreserveExistingAssignmentsForRebuild(),
                events: _events);
            ClearRebuildChangeKinds();
            _grid.ClearTopologyDirty();
        }

        private bool PreserveExistingAssignmentsForRebuild() =>
            _buildingAssignmentChangePending && !_roadTopologyChangePending;

        // 경로 계획 입력(도로·일방통행·턴 표지판·고가 링크·config)이 바뀌면 preserve 리빌드
        // 금지 — 구 경로가 새 규칙을 위반한 채 유지된다. 같은 틱 윈도우에 건물 배치가 겹쳐도
        // 이 플래그가 이긴다. 로터리·입체·우선도로는 Plan 입력이 아니라(큐 토폴로지만 변경)
        // 구 경로 = 재계획 결과이므로 제외.
        private void MarkRoutingChangePending()
        {
            _roadTopologyChangePending = true;
            _grid.MarkTopologyDirty();
        }

        private void ClearRebuildChangeKinds()
        {
            _buildingAssignmentChangePending = false;
            _roadTopologyChangePending = false;
        }



        // ── IPlacementService: CityGrid에 위임. 성공 시 PlacedEvent 큐잉(발행은 틱 끝 Drain) ──
        public bool CanPlace(Vector2Int tile, TileType type, PlacementDirection direction = PlacementDirection.North) =>
            type != TileType.UnderConstruction
            && IsAreaUnlocked(tile, type, direction)
            && !OverlapsRoundaboutFootprint(tile, type, direction)
            && !OverlapsBusStopFootprint(tile, type, direction)
            && _grid.CanPlace(tile, type, direction);

        // IPlacementService 계약 그대로(3인자). 유형은 아래 오버로드로 넘긴다 —
        // 계약에 인자를 더하면 FakePlacementService·UI 구현체가 함께 깨진다.
        public bool Place(Vector2Int tile, TileType type,
                         PlacementDirection direction = PlacementDirection.North)
            => Place(tile, type, direction, null);

        // companyTypeId: Office 의 회사 유형(사무실·공장·물류창고). 미지정은 거부하지 않고 폴백 창을 쓴다
        // (환 결정 2026-07-30 — UI 상점이 3종으로 갈리면 미지정 경로 자체가 없어진다).
        // 등록되지 않은 id 는 오타이므로 경고를 남긴다 — 조용히 묻히지 않게.
        public bool Place(Vector2Int tile, TileType type,
                         PlacementDirection direction, string companyTypeId)
        {
            if (type == TileType.UnderConstruction) return false;
            if (!IsAreaUnlocked(tile, type, direction)) return false;
            if (OverlapsRoundaboutFootprint(tile, type, direction)) return false;   // 로터리 풋프린트에 건물 금지
            if (OverlapsBusStopFootprint(tile, type, direction)) return false;
            if (!_grid.Place(tile, type, direction)) return false;

            // 건물은 공사부터 시작한다. 공사시간 0이면 아래 분기를 타지 않고 현행대로 즉시 완성.
            double constructionSeconds = TileFootprint.IsBuilding(type)
                ? ConstructionSeconds(type)
                : 0d;
            if (constructionSeconds > 0d)
            {
                // 현재는 둘 다 2x2 건물이라 실패할 수 없지만, 타입별 풋프린트 분화에 대비해 원자적으로 되돌린다.
                if (!_grid.Promote(tile, TileType.UnderConstruction))
                {
                    _grid.TryRemove(tile, out _, out _);
                    return false;
                }
                _construction.Register(
                    tile, type, direction, _simTime, _simTime + constructionSeconds, companyTypeId);
                _events.QueuePlaced(
                    new PlacedEvent(tile, TileType.UnderConstruction, isRemove: false, direction));
                return true;
            }

            if (type == TileType.Office || type == TileType.School)
                RegisterCompanyOfType(tile, type, companyTypeId);
            if (type == TileType.Office || type == TileType.School)
                _demandRebalancePending = true;
            if (TileFootprint.IsBuilding(type))
                _buildingAssignmentChangePending = true;
            else if (type == TileType.Road)
                _roadTopologyChangePending = true;
            _events.QueuePlaced(new PlacedEvent(tile, type, isRemove: false, direction));
            return true;
        }

        // 게임시간 → 시뮬초. 채용 램프(CompanyCapacityCalculator)의 환산식 역산이다.
        private double ConstructionSeconds(TileType type)
        {
            float hours = type switch
            {
                TileType.House           => _config.ConstructionHoursHouse,
                TileType.Office          => _config.ConstructionHoursOffice,
                TileType.School          => _config.ConstructionHoursSchool,
                TileType.Hospital        => _config.ConstructionHoursHospital,
                TileType.SpecialBuilding => _config.ConstructionHoursSpecial,
                _ => 0f
            };
            if (hours <= 0f || _config.DayLengthSeconds <= 0f) return 0d;
            return hours * _config.DayLengthSeconds / 24d;
        }

        // 완성 = 현재 Place 후처리의 발화 시점을 뒤로 민 것. 새 인과관계를 만들지 않는다.
        private void AdvanceConstruction()
        {
            if (_construction.Count == 0) return;
            _construction.CollectCompleted(_simTime, _completedBuffer);
            for (int i = 0; i < _completedBuffer.Count; i++)
            {
                ConstructionSite site = _completedBuffer[i];
                // 실패 사이트는 목록에 남겨 다음 틱에 재시도한다. 도달 불가 불변식이 깨져도 영구 소실시키지 않는다.
                if (!_grid.Promote(site.Anchor, site.TargetType)) continue;
                _construction.Cancel(site.Anchor);

                if (site.TargetType == TileType.Office || site.TargetType == TileType.School)
                    RegisterCompanyOfType(site.Anchor, site.TargetType, site.CompanyTypeId);
                if (site.TargetType == TileType.Office || site.TargetType == TileType.School)
                    _demandRebalancePending = true;
                _buildingAssignmentChangePending = true;
                _events.QueuePlaced(
                    new PlacedEvent(site.Anchor, site.TargetType, isRemove: false, site.Direction));
            }
        }

        private bool IsAreaUnlocked(
            Vector2Int tile,
            TileType type,
            PlacementDirection direction)
        {
            if (_worldGridAccess == null)
            {
                return true;
            }

            Vector2Int footprint = TileFootprint.GetRotatedSize(
                type,
                direction);
            footprint.x = Mathf.Max(1, footprint.x);
            footprint.y = Mathf.Max(1, footprint.y);
            return _worldGridAccess.IsAreaUnlocked(tile, footprint);
        }

        public bool Remove(Vector2Int tile)
        {
            PlacementDirection removedDir = _grid.GetDirection(tile);
            if (_grid.GetTile(tile) == TileType.Road &&
                WouldOrphanBusStopIfRoadRemoved(tile))
            {
                return false;
            }

            if (!_grid.TryRemove(tile, out var removed, out Vector2Int anchor)) return false;
            _construction.Cancel(anchor);   // 공사 중 철거 — 사이트 제거(환불은 UI 층 기존 경로)
            if (removed == TileType.Office)
                _demand.RemoveCompany(anchor);
            if (TileFootprint.IsBuilding(removed))
            {
                _demandRebalancePending = true;
                _buildingAssignmentChangePending = true;
            }
            else if (removed == TileType.Road)
            {
                _roadTopologyChangePending = true;
                int index = anchor.y * _grid.Width + anchor.x;
                if (_carCongestion[index] != CongestionLevel.Free)
                {
                    _carCongestion[index] = CongestionLevel.Free;
                    _events.QueueCongestion(new CongestionEvent(
                        anchor,
                        CongestionLevel.Free));
                }
            }
            // 철거 = 조용: 그 타일의 연출 원료(pending)도 소각 — "부수면 폭죽" 방지(리뷰 2026-07-11).
            _events.QueuePlaced(new PlacedEvent(anchor, removed, isRemove: true, removedDir));
            return true;
        }

        private bool OverlapsRoundaboutFootprint(Vector2Int tile, TileType type, PlacementDirection direction = PlacementDirection.North)
        {
            if (!IsBuildingTile(type)) return false;

            Vector2Int size = TileFootprint.GetRotatedSize(type, direction);
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    if (IsInRoundaboutFootprint(tile + new Vector2Int(x, y)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool OverlapsBusStopFootprint(
            Vector2Int tile,
            TileType type,
            PlacementDirection direction)
        {
            Vector2Int size =
                TileFootprint.GetRotatedSize(type, direction);

            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    if (_busStopPlatformSet.Contains(
                            tile + new Vector2Int(x, y)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // 뷰 연동: 엔진이 이번 틱 계산한 실제 통근 경로들. 차를 이 위에 그리면 라우팅을 눈으로 검증.
        // ponytail: 지금은 디버그 뷰용 public. 진짜 View 붙을 때 Contracts로 승격.
        // 유형 최대 정원이 하한 — ApplyConfig 로 전역 OfficeCapacity 를 낮춰도 유형 정원은
        // 함께 내려가지 않으므로(리뷰 P2), 뷰 슬롯이 정원 아래로 떨어지면 초과분이 겹쳐 주차된다.
        public int CarSimOfficeParkingSlots =>
            Math.Max(Math.Max(1, _config.OfficeCapacity), _maxCompanyTypeCapacity);
        public int CarSimHomeParkingSlots => Math.Max(1, _config.CarsPerHouse);
        public int CarSimMaxCars => Math.Max(1, _config.MaxSimCars);
        public int CarSimVehicleStorageCount => _carSim.CarCount;
        // 뷰가 큐 표시 간격을 타일 안에 담기 위해 필요(한 타일에 몇 대까지 서는가).
        public int CarSimQueueCapacity => Math.Max(1, _config.QueueCapacityPerTile);
        public int RescueRerouteCount => _carSim.RescueRerouteCount;
        public int RescueRestartCount => _carSim.RescueRestartCount;
        public int LastRescueCarId => _carSim.LastRescueCarId;
        public Vector2Int LastRescueTile => _carSim.LastRescueTile;
        public IReadOnlyList<List<Vector2Int>> ActiveRoutes => _carSim.ActiveRoutes;
        public IReadOnlyList<List<Vector2Int>> ActiveReturnRoutes => _carSim.ActiveReturnRoutes;
        public int ActiveVehicleCount => _carSim.SimulatedVehicleCount;
        public int LastDayArrivalCount => _stats.LastDayArrivalCount;
        public int PendingTripCount => _carSim.PendingTripCount;
        public int ActiveTripCount => _carSim.ActiveTripCount;
        public CarSnapshot GetCarSnapshot(int index) => _carSim.GetCar(index);

        public bool TryScheduleSpecialBuildingVisit(
            SpecialBuildingVisitTripRequest request) =>
            _carSim.TryScheduleSpecialBuildingVisit(request);

        public bool TrySetCompanyCapacity(
            Vector2Int tile,
            int capacity
        )
        {
            if (!_grid.InBounds(tile) ||
                _grid.GetTile(tile) != TileType.Office)
            {
                return false;
            }

            _demand.SetCompanyCapacity(tile, capacity);
            _demandRebalancePending = true;
            return true;
        }

        public bool TryGetCompanyStaffing(
            Vector2Int tile,
            out CompanyStaffing staffing
        )
        {
            bool found =
                _demand.TryGetCompanyStaffing(
                    tile,
                    out int filled,
                    out int capacity
                );

            staffing = found
                ? new CompanyStaffing(
                    filled,
                    capacity
                )
                : default;
            return found;
        }

        public bool TryGetCompanyTypeId(Vector2Int tile, out string companyTypeId) =>
            _demand.TryGetCompanyTypeId(tile, out companyTypeId);

        public IReadOnlyList<CommuterHomeCount> GetCompanyCommuterHomes(Vector2Int tile)
        {
            var counts = new Dictionary<Vector2Int, int>();
            for (int i = 0; i < _carSim.CarCount; i++)
            {
                CarSnapshot snap = _carSim.GetCar(i);
                if (snap.Work != tile) continue;
                counts.TryGetValue(snap.Home, out int n);
                counts[snap.Home] = n + 1;
            }
            var list = new List<CommuterHomeCount>(counts.Count);
            foreach (var kv in counts)
                list.Add(new CommuterHomeCount(kv.Key, kv.Value));
            return list;
        }

        public bool IsSharedCarIntersection(Vector2Int tile) =>
            _grid.IsIntersection(tile)
            && !_roundaboutSet.Contains(tile)
            && !_overpassSet.Contains(tile);



        // 뷰용 : 이번 틱 처리량 (대/초) 튜너가 오프셋 조율 효과를 숫자로 보게
        public float DeliveredTotal => _config.TickInterval > 0f ? _lastStepArrivals / _config.TickInterval : 0f;

        public bool TryGetAverageRouteDistance(
            Vector2Int destination,
            out float distanceTiles) =>
            _planner.TryGetAverageRouteDistance(_demand, destination, out distanceTiles);

        public bool TryGetCityAverageRouteDistance(out float distanceTiles) =>
            _planner.TryGetCityAverageRouteDistance(out distanceTiles);

        public bool TryPlanRoadRoute(
            Vector2Int originRoad,
            Vector2Int destinationRoad,
            out RoadRoutePlan route)
        {
            route = default;
            EnsureCarTopologyCurrent();

            List<Vector2Int> routeTiles = _planner.PlanVehicleTrip(
                originRoad,
                destinationRoad);
            if (routeTiles == null || routeTiles.Count == 0)
            {
                return false;
            }

            route = new RoadRoutePlan(routeTiles);
            return true;
        }

        // ── ISignalControl(신호 조작 창구): 유저가 교차로를 조율하는 두 레버 — 오프셋·초록 길이 ──
        // 제안 단계: 계약으로 승격(설계 §5), 최종 확정은 주석·김건 합의. 김건 Game뷰 UI가 이 계약에 붙음.
        public IReadOnlyList<Vector2Int> SignalTiles => _signals.Tiles;

        public int GetSignalCycleSlots(Vector2Int tile) =>
            _signals.TryGet(tile, out var s) ? s.CycleSlots : 0;

        public float GetCurrentCycleProgress(Vector2Int tile)
        {
            if (!_signals.TryGet(tile, out var s) || s.CycleSlots <= 0) return 0f;
            // 오버라이드(양축 강제 초록) 중에는 게이지가 정상 주기를 표시할 수 없음 → UI에 알림
            if (s.OverrideUntil > _simTime) return -1f;
            float cycle = s.CycleSlots * SignalMath.SlotSeconds;
            double openTime = (s.OffsetSlots * SignalMath.SlotSeconds) % cycle;
            double localTime = (SimTime - openTime) % cycle;
            if (localTime < 0) localTime += cycle;
            return (float)(localTime / cycle);
        }

        public int GetSignalOffsetSlots(Vector2Int tile) =>
            _signals.TryGet(tile, out var s) ? s.OffsetSlots : 0;

        public bool TrySetSignalOffsetSlots(Vector2Int tile, int slots)
        {
            if (!_signals.TryGet(tile, out var s)) return false;
            s.OffsetSlots = slots;   // 다음 Resolve부터 반영(topology 재계산 불필요)
            return true;
        }

        public int GetSignalGreenSlots(Vector2Int tile) =>
            _signals.TryGet(tile, out var s) ? s.GreenSlots : 0;

        public bool TrySetSignalGreenSlots(Vector2Int tile, int slots)
        {
            if (!_signals.TryGet(tile, out var s)) return false;
            // 최소 통과 보장(Hard limit): 초록 0슬롯이면 그 축이 영원히 빨강 = 신호 데드락.
            // 반대 축(주기-초록)도 같은 이유로 최소 1슬롯 → [1, 주기-1] 클램프.
            s.GreenSlots = Mathf.Clamp(slots, 1, Mathf.Max(1, s.CycleSlots - 1));
            return true;
        }

        // ── 신호 배치(구매 피벗 2단계, 스펙 2026-07-11): 배치 모드에서만. 가격·UI는 팀(김건·진우) ──
        public bool CanPlaceSignal(Vector2Int tile) =>
            !_config.AutoDetectSignals && _grid.IsIntersection(tile)
            && !_placedSet.Contains(tile) && !IsInRoundaboutFootprint(tile)   // 로터리 풋프린트(팔 포함) 예약
            && !_overpassSet.Contains(tile)                                   // 3자 배타
            && !_priorityDirs.ContainsKey(tile);   // 우선도로와 배타(4자 배타, 스펙 2026-07-13)

        public bool TryPlaceSignal(Vector2Int tile, int greenSlots)
        {
            if (!CanPlaceSignal(tile)) return false;
            int flat = tile.y * _config.GridWidth + tile.x;
            int idx = _placedSignals.FindIndex(t => t.y * _config.GridWidth + t.x > flat);
            if (idx < 0) _placedSignals.Add(tile); else _placedSignals.Insert(idx, tile);
            _placedSet.Add(tile);
            RebuildSignals();
            TrySetSignalGreenSlots(tile, greenSlots);   // 구매 파라미터(방향+초) — 기존 클램프 재사용
            return true;
        }

        public bool TryRemoveSignal(Vector2Int tile)
        {
            if (_config.AutoDetectSignals || !_placedSet.Remove(tile)) return false;
            _placedSignals.Remove(tile);
            RebuildSignals();
            return true;
        }

        // ── 회전교차로 배치(스펙 2026-07-11 + 풋프린트 2026-07-15): 십자 5칸 점유(center + 상하좌우 4팔) ──
        //   저장은 center만(_roundaboutSet), 풋프린트는 파생. 흐름은 center 1노드(무변경).
        public IReadOnlyList<Vector2Int> RoundaboutTiles => _placedRoundabouts;

        // 로터리 팔 방향(상하좌우). 대각 제외 — 십자 풋프린트.
        static readonly Vector2Int[] RoundaboutArmDirs =
            { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

        static bool IsBuildingTile(TileType t) => TileFootprint.IsBuilding(t);

        // tile이 배치된 어떤 로터리의 풋프린트(center 또는 그 4팔)에 속하나. 타 장치·건물 배치가 이걸로 예약 검사.
        public bool IsInRoundaboutFootprint(Vector2Int tile)
        {
            if (_roundaboutSet.Contains(tile)) return true;
            for (int i = 0; i < RoundaboutArmDirs.Length; i++)
                if (_roundaboutSet.Contains(tile + RoundaboutArmDirs[i])) return true;   // 이웃이 center면 tile은 그 로터리의 팔
            return false;
        }

        // center가 교차로 + center 배타(기존 4형제·표지판) + 인바운드 팔이 전부 비어야(건물X·장치X·타풋프린트X).
        // OOB 팔은 스킵 — 가장자리 교차로는 부분 풋프린트로 허용(MM: 있는 만큼만 주변 비우기).
        public bool CanPlaceRoundabout(Vector2Int tile)
        {
            if (_config.AutoDetectSignals || !_grid.IsIntersection(tile)) return false;
            if (_roundaboutSet.Contains(tile) || _placedSet.Contains(tile) || _overpassSet.Contains(tile)
                || _turnSigns.ContainsKey(tile) || _priorityDirs.ContainsKey(tile)) return false;   // center 배타
            if (IsInRoundaboutFootprint(tile)) return false;                 // center가 남의 로터리 풋프린트(팔)와 겹침
            for (int i = 0; i < RoundaboutArmDirs.Length; i++)
            {
                var arm = tile + RoundaboutArmDirs[i];
                if (!_grid.InBounds(arm)) continue;                          // 가장자리 팔 스킵
                if (IsBuildingTile(_grid.GetTile(arm))) return false;       // 건물 팔 거부(Road·Empty는 OK)
                if (_placedSet.Contains(arm) || _overpassSet.Contains(arm) || _priorityDirs.ContainsKey(arm)
                    || _turnSigns.ContainsKey(arm) || _onewayDirs.ContainsKey(arm)) return false;   // 팔에 타 장치
                if (IsInRoundaboutFootprint(arm)) return false;             // 팔이 남의 로터리 풋프린트와 겹침
            }
            return true;
        }

        public bool TryPlaceRoundabout(Vector2Int tile)
        {
            if (!CanPlaceRoundabout(tile)) return false;
            int flat = tile.y * _config.GridWidth + tile.x;
            int idx = _placedRoundabouts.FindIndex(t => t.y * _config.GridWidth + t.x > flat);
            if (idx < 0) _placedRoundabouts.Add(tile); else _placedRoundabouts.Insert(idx, tile);
            _roundaboutSet.Add(tile);
            _grid.MarkTopologyDirty();
            return true;
        }

        public bool TryRemoveRoundabout(Vector2Int tile)
        {
            if (_config.AutoDetectSignals || !_roundaboutSet.Remove(tile)) return false;
            _placedRoundabouts.Remove(tile);
            _grid.MarkTopologyDirty();
            return true;
        }

        // ── 입체교차 배치(스펙 2026-07-12): 로터리 3종의 자매 — 교차로 4형제 완성 ──
        public IReadOnlyList<Vector2Int> OverpassTiles => _placedOverpasses;

        public bool CanPlaceOverpass(Vector2Int tile) =>
            !_config.AutoDetectSignals && _grid.IsIntersection(tile)
            && !_overpassSet.Contains(tile) && !_placedSet.Contains(tile)
            && !IsInRoundaboutFootprint(tile)                              // 3자 배타(로터리 풋프린트)
            && !_turnSigns.ContainsKey(tile)    // 표지판과 배타(양방향 — 계획 정정 2026-07-12)
            && !_priorityDirs.ContainsKey(tile);   // 우선도로와 배타(4자 배타, 스펙 2026-07-13)

        public bool TryPlaceOverpass(Vector2Int tile)
        {
            if (!CanPlaceOverpass(tile)) return false;
            int flat = tile.y * _config.GridWidth + tile.x;
            int idx = _placedOverpasses.FindIndex(t => t.y * _config.GridWidth + t.x > flat);
            if (idx < 0) _placedOverpasses.Add(tile); else _placedOverpasses.Insert(idx, tile);
            _overpassSet.Add(tile);
            _grid.MarkTopologyDirty();
            return true;
        }

        public bool TryRemoveOverpass(Vector2Int tile)
        {
            if (_config.AutoDetectSignals || !_overpassSet.Remove(tile)) return false;
            _placedOverpasses.Remove(tile);
            _grid.MarkTopologyDirty();
            return true;
        }

        // ── 우선도로 배치(스펙 2026-07-13): 로터리 3종의 자매 — 교차로 4형제 완성(신호·로터리·입체와 4자 배타) ──
        public IReadOnlyList<Vector2Int> PriorityRoadTiles => _placedPriorityRoads;

        public Axis GetPriorityAxis(Vector2Int tile) =>
            _priorityDirs.TryGetValue(tile, out var a) ? a : Axis.Horizontal;

        // 턴 표지판은 검사 안 함(공존 의도) — 표지판은 라우팅 필터, 우선도로는 솔버 간섭 분기라
        // 메커니즘이 달라 이중계산 없음(신호↔표지판 공존과 동일 규약). 4자 배타는 신호·로터리·입체만.
        public bool CanPlacePriorityRoad(Vector2Int tile) =>
            !_config.AutoDetectSignals && _grid.IsIntersection(tile)
            && !_priorityDirs.ContainsKey(tile) && !_placedSet.Contains(tile)
            && !IsInRoundaboutFootprint(tile) && !_overpassSet.Contains(tile);   // 4자 배타(로터리 풋프린트)

        public bool TryPlacePriorityRoad(Vector2Int tile, Axis mainAxis)
        {
            if (!CanPlacePriorityRoad(tile)) return false;
            int flat = tile.y * _config.GridWidth + tile.x;
            int idx = _placedPriorityRoads.FindIndex(t => t.y * _config.GridWidth + t.x > flat);
            if (idx < 0) _placedPriorityRoads.Add(tile); else _placedPriorityRoads.Insert(idx, tile);
            _priorityDirs[tile] = mainAxis;
            _grid.MarkTopologyDirty();
            return true; // 유량은 무관, CarSim은 공유 예산 캡처를 갱신해야 하므로 스위치 on에서만 dirty.
        }

        public bool TryRemovePriorityRoad(Vector2Int tile)
        {
            if (_config.AutoDetectSignals || !_priorityDirs.Remove(tile)) return false;
            _placedPriorityRoads.Remove(tile);
            _grid.MarkTopologyDirty();
            return true;
        }

        // ── 일방통행 배치(스펙 2026-07-12): 4번째 배치 가족 — 교차로 3형제와 정반대 조건(일반 도로 전용) ──
        // 조건이 반대라 교차로 3형제와는 자연 배타(별도 HashSet 교차 검사 불요).
        public IReadOnlyList<Vector2Int> OnewayTiles => _placedOneways;

        public bool CanPlaceOneway(Vector2Int tile) =>
            !_config.AutoDetectSignals && _grid.InBounds(tile) && _grid.GetTile(tile) == TileType.Road
            && !_grid.IsIntersection(tile) && !_onewayDirs.ContainsKey(tile)
            && !IsInRoundaboutFootprint(tile);   // 로터리 팔(도로) 예약

        public bool TryPlaceOneway(Vector2Int tile, Vector2Int dir)
        {
            if (!CanPlaceOneway(tile)) return false;
            if (System.Array.IndexOf(OnewayUnitDirs, dir) < 0) return false;   // 대각·zero·비단위 거부
            int flat = tile.y * _config.GridWidth + tile.x;
            int idx = _placedOneways.FindIndex(t => t.y * _config.GridWidth + t.x > flat);
            if (idx < 0) _placedOneways.Add(tile); else _placedOneways.Insert(idx, tile);
            _onewayDirs[tile] = dir;
            MarkRoutingChangePending();   // 라우팅에 영향 — 신호 가족과 다른 점(다음 틱 재계획 강제)
            return true;
        }

        public bool TryRemoveOneway(Vector2Int tile)
        {
            if (_config.AutoDetectSignals || !_onewayDirs.Remove(tile)) return false;
            _placedOneways.Remove(tile);
            MarkRoutingChangePending();   // 라우팅에 영향 — 배치와 동일 이유
            return true;
        }

        // 뷰·저장용 조회: 없으면 zero(방향 없음을 뜻함, 예외 아님).
        public Vector2Int GetOnewayDir(Vector2Int tile) =>
            _onewayDirs.TryGetValue(tile, out var d) ? d : Vector2Int.zero;

        // ── 턴 제한 표지판 배치(스펙 2026-07-12): 5번째 배치 가족 — 교차로 전용, 신호와 공존(로터리·입체와만 배타) ──
        public IReadOnlyList<Vector2Int> TurnSignTiles => _placedTurnSigns;

        public bool CanPlaceTurnSign(Vector2Int tile) =>
            !_config.AutoDetectSignals && _grid.IsIntersection(tile)
            && !IsInRoundaboutFootprint(tile) && !_overpassSet.Contains(tile)   // 로터리 풋프린트와 배타
            && !_turnSigns.ContainsKey(tile);                                 // 신호는 검사 안 함(공존)

        // 배치 API·세이브 복원 양쪽이 공유(비대칭 방지) — enum 캐스팅으로 미정의 값(예: (TurnMode)2)이
        // 들어오는 경로를 여기서 함께 거른다.
        private static bool IsValidTurnMode(TurnMode mode) =>
            mode == TurnMode.LeftOnly || mode == TurnMode.RightOnly;

        public bool TryPlaceTurnSign(Vector2Int tile, TurnMode mode)
        {
            if (!IsValidTurnMode(mode) || !CanPlaceTurnSign(tile)) return false;
            int flat = tile.y * _config.GridWidth + tile.x;
            int idx = _placedTurnSigns.FindIndex(t => t.y * _config.GridWidth + t.x > flat);
            if (idx < 0) _placedTurnSigns.Add(tile); else _placedTurnSigns.Insert(idx, tile);
            _turnSigns[tile] = mode;
            MarkRoutingChangePending();   // 라우팅에 영향 — 일방통행과 동일 이유(다음 틱 재계획 강제)
            return true;
        }

        public bool TryRemoveTurnSign(Vector2Int tile)
        {
            if (_config.AutoDetectSignals || !_turnSigns.Remove(tile)) return false;
            _placedTurnSigns.Remove(tile);
            MarkRoutingChangePending();   // 라우팅에 영향 — 배치와 동일 이유
            return true;
        }

        // 뷰·저장용 조회: 없으면 null(표지판 없음을 뜻함, 예외 아님) — GetOnewayDir과 동형.
        public TurnMode? GetTurnMode(Vector2Int tile) =>
            _turnSigns.TryGetValue(tile, out var m) ? m : (TurnMode?)null;

        // 두 일반 도로 램프를 잇는 비인접 양방향 고가 링크.
        public IReadOnlyList<HighwayLink> HighwayLinks => _highwayLinks;
        public bool IsHighwayRamp(Vector2Int tile) => _highwayPartners.ContainsKey(tile);

        public bool CanSelectHighwayRamp(Vector2Int tile) =>
            _grid.InBounds(tile) && _grid.GetTile(tile) == TileType.Road
            && !_grid.IsIntersection(tile) && !_highwayPartners.ContainsKey(tile)
            && !IsInRoundaboutFootprint(tile) && !_overpassSet.Contains(tile)
            && !_onewayDirs.ContainsKey(tile) && !_turnSigns.ContainsKey(tile)
            && !_priorityDirs.ContainsKey(tile);

        private bool CanPlaceHighwayGeometry(Vector2Int a, Vector2Int b) =>
            a != b && CanSelectHighwayRamp(a) && CanSelectHighwayRamp(b)
            && Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) >= 5;

        public bool CanPlaceHighway(Vector2Int a, Vector2Int b) =>
            CanPlaceHighwayGeometry(a, b);

        private static int HighwayDistance(Vector2Int a, Vector2Int b) =>
            Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

        public int HighwayCost(Vector2Int a, Vector2Int b) =>
            HighwayDistance(a, b) * 25;

        public bool TryPlaceHighway(Vector2Int a, Vector2Int b)
        {
            if (!CanPlaceHighwayGeometry(a, b)) return false;
            _highwayLinks.Add(new HighwayLink(a, b));
            _highwayPartners[a] = b;
            _highwayPartners[b] = a;
            MarkRoutingChangePending();
            return true;
        }

        public bool TryRemoveHighway(Vector2Int ramp)
        {
            if (!_highwayPartners.TryGetValue(ramp, out Vector2Int partner)) return false;
            int linkIndex = _highwayLinks.FindIndex(link => link.Contains(ramp));
            if (linkIndex < 0) return false;
            _highwayLinks.RemoveAt(linkIndex);
            _highwayPartners.Remove(ramp);
            _highwayPartners.Remove(partner);
            MarkRoutingChangePending();
            return true;
        }

        // 뷰용: 이 교차로가 지금 초록인가(시뮬 시간 기준). 신호 없으면 항상 초록 취급.
        public bool IsSignalGreen(Vector2Int tile) =>
            !_signals.TryGet(tile, out var s) || s.OverrideUntil > _simTime || SignalMath.IsGreen(s, _simTime);

        // 뷰용: 이 교차로의 이 방향 신호 3상태(초록/노랑/적색). 신호 없으면 항상 초록.
        // 오버라이드 중엔 양축 초록(정령 마법 — 충돌 소멸, 스펙 2026-07-11 §3).
        public SignalPhase GetSignalPhase(Vector2Int tile, bool horizontal)
        {
            if (!_signals.TryGet(tile, out var s)) return SignalPhase.Green;
            if (s.OverrideUntil > _simTime)
                return SignalPhase.Green;   // 정령 마법: 양축 초록(충돌 소멸) — 스펙 2026-07-11 §3
            return SignalMath.PhaseForAxis(s, _simTime, horizontal);
        }

        // ── 오버라이드 스킬(기획 §2-D): duration초 양축 강제 초록 + 엔진 강제 쿨다운 ──
        // 능동 개입의 손맛 레버. 쿨다운을 엔진이 들고 있는 이유: UI는 트러스트 경계 밖.
        // 배치 모드에서 신호를 철거+재구매해도 이 맵은 유지 — "재설치로 쿨다운 리셋" 악용 불가(의도).
        readonly Dictionary<Vector2Int, double> _overrideReadyAt = new();
        readonly List<Vector2Int> _corridorBuf = new();   // 코리도어 수집 재사용 버퍼(비-재진입)

        public event System.Action<Vector2Int, bool, float, float> OnOverrideTriggered;

        public bool TryOverrideSignal(Vector2Int tile, bool horizontal)
        {
            if (!_signals.TryGet(tile, out _)) return false;
            if (_overrideReadyAt.TryGetValue(tile, out var ready) && _simTime < ready) return false;

            CollectCorridor(tile, horizontal, _corridorBuf);   // anchor + 일자 라인 최근접 신호
            double until = _simTime + _config.OverrideDurationSeconds;
            for (int i = 0; i < _corridorBuf.Count; i++)
            {
                if (!_signals.TryGet(_corridorBuf[i], out var s)) continue;
                s.OverrideUntil = until;
                _overrideReadyAt[_corridorBuf[i]] = until + _config.OverrideCooldownSeconds;
            }

            // 이벤트 발행 (UI 애니메이션용)
            OnOverrideTriggered?.Invoke(tile, horizontal, _config.OverrideDurationSeconds, _config.OverrideCooldownSeconds);

            return true;
        }

        // 코리도어: anchor에서 선택 축(가로=x, 세로=y)으로 연속 도로를 걸으며 교차로 신호를
        // 양방향 최근접부터 번갈아 수집(anchor 포함 최대 OverrideCorridorSignals개). "직진만".
        void CollectCorridor(Vector2Int anchor, bool horizontal, List<Vector2Int> outTiles)
        {
            outTiles.Clear();
            outTiles.Add(anchor);
            int max = Mathf.Max(1, _config.OverrideCorridorSignals);
            var step = horizontal ? new Vector2Int(1, 0) : new Vector2Int(0, 1);
            Vector2Int fwd = anchor, bwd = anchor;
            bool fwdAlive = true, bwdAlive = true;
            while (outTiles.Count < max && (fwdAlive || bwdAlive))
            {
                if (fwdAlive)
                {
                    if (TryNextSignalAlong(ref fwd, step, out var sf)) outTiles.Add(sf);
                    else fwdAlive = false;
                }
                if (outTiles.Count >= max) break;
                if (bwdAlive)
                {
                    if (TryNextSignalAlong(ref bwd, -step, out var sb)) outTiles.Add(sb);
                    else bwdAlive = false;
                }
            }
        }

        // cursor에서 step 방향으로 도로를 걸으며 다음 교차로 신호를 찾는다. 도로 끊기면 false.
        bool TryNextSignalAlong(ref Vector2Int cursor, Vector2Int step, out Vector2Int signal)
        {
            signal = default;
            var t = cursor + step;
            while (t.x >= 0 && t.x < _grid.Width && t.y >= 0 && t.y < _grid.Height
                   && _grid.GetTile(t) == TileType.Road)   // GetTile은 OOB 미검사 → 직접 가드
            {
                if (_signals.TryGet(t, out _)) { cursor = t; signal = t; return true; }
                t += step;
            }
            return false;
        }

        // 뷰용: 오버라이드 남은 시간(0 = 비활성) / 쿨다운 남은 시간(0 = 사용 가능).
        public float GetOverrideSecondsLeft(Vector2Int tile) =>
            _signals.TryGet(tile, out var s) && s.OverrideUntil > _simTime
                ? (float)(s.OverrideUntil - _simTime) : 0f;

        public float GetOverrideCooldownLeft(Vector2Int tile) =>
            _overrideReadyAt.TryGetValue(tile, out var ready) && ready > _simTime
                ? (float)(ready - _simTime) : 0f;

        public float GetTotalOverrideCooldown() => _config.OverrideCooldownSeconds;

        // ── 신호 타이밍 상수 접근자 (ISignalControl 계약) ── SignalMath 단일 진실원 경유
        public float GetSlotSeconds() => SignalMath.SlotSeconds;
        public float GetYellowFraction() => SignalMath.YellowFrac;
        public float GetClearFraction() => SignalMath.ClearFrac;

        // 진입 허가 = 초록만(노랑·적색은 진입 금지).
        public bool IsSignalGreen(Vector2Int tile, bool horizontal) =>
            GetSignalPhase(tile, horizontal) == SignalPhase.Green;

        // ── ISimSaveSource(한준희 세이브 계약): 배치 타일 + 신호 오프셋만 저장, 계산값은 로드 후 재계산 ──
        public int GridWidth => _grid.Width;
        public int GridHeight => _grid.Height;

        public SimSaveData CreateSnapshot()
        {
            var tiles = new List<TileSaveData>();
            for (int y = 0; y < _grid.Height; y++)              // flat(y,x) 순서 = 결정론
                for (int x = 0; x < _grid.Width; x++)
                {
                    var type = _grid.GetTile(new Vector2Int(x, y));
                    if (type == TileType.Empty) continue;       // 계약: Empty 미저장
                    if (!_grid.IsFootprintAnchor(new Vector2Int(x, y))) continue;
                    var anchor = new Vector2Int(x, y);
                    _demand.TryGetCompanyTypeId(anchor, out string companyTypeId);
                    tiles.Add(new TileSaveData
                    {
                        X = x, Y = y, Type = type,
                        Direction = _grid.GetDirection(anchor),
                        CompanyTypeId = companyTypeId,
                    });
                }

            // 모든 신호를 두 레버(오프셋·초록) 다 저장 — 복원 시 덮어쓰기만으로 이전 조율 잔존을 지운다.
            // 오버라이드는 초 단위 일시 상태라 저장 안 함(로드 시 자연 소멸이 옳음).
            var signals = new List<SignalSaveData>();
            foreach (var t in _signals.Tiles)
                signals.Add(new SignalSaveData
                {
                    X = t.x,
                    Y = t.y,
                    OffsetSlots = GetSignalOffsetSlots(t),
                    GreenSlots = GetSignalGreenSlots(t),
                });

            var roundabouts = new RoundaboutSaveData[_placedRoundabouts.Count];
            for (int i = 0; i < _placedRoundabouts.Count; i++)
                roundabouts[i] = new RoundaboutSaveData { X = _placedRoundabouts[i].x, Y = _placedRoundabouts[i].y };

            var overpasses = new OverpassSaveData[_placedOverpasses.Count];
            for (int i = 0; i < _placedOverpasses.Count; i++)
                overpasses[i] = new OverpassSaveData { X = _placedOverpasses[i].x, Y = _placedOverpasses[i].y };

            var oneways = new OnewaySaveData[_placedOneways.Count];
            for (int i = 0; i < _placedOneways.Count; i++)
            {
                var t = _placedOneways[i];
                var d = _onewayDirs[t];
                oneways[i] = new OnewaySaveData { X = t.x, Y = t.y, DirX = d.x, DirY = d.y };
            }

            var turnSigns = new TurnSignSaveData[_placedTurnSigns.Count];
            for (int i = 0; i < _placedTurnSigns.Count; i++)
            {
                var t = _placedTurnSigns[i];
                turnSigns[i] = new TurnSignSaveData { X = t.x, Y = t.y, Mode = (int)_turnSigns[t] };
            }

            var priorityRoads = new PriorityRoadSaveData[_placedPriorityRoads.Count];
            for (int i = 0; i < _placedPriorityRoads.Count; i++)
            {
                var t = _placedPriorityRoads[i];
                priorityRoads[i] = new PriorityRoadSaveData { X = t.x, Y = t.y, Axis = (int)_priorityDirs[t] };
            }

            var busStops = new BusStopSaveData[_placedBusStops.Count];
            for (int i = 0; i < _placedBusStops.Count; i++)
                busStops[i] = new BusStopSaveData
                {
                    X = _placedBusStops[i].x,
                    Y = _placedBusStops[i].y
                };

            var highways = new HighwaySaveData[_highwayLinks.Count];
            for (int i = 0; i < _highwayLinks.Count; i++)
                highways[i] = new HighwaySaveData
                {
                    AX = _highwayLinks[i].A.x, AY = _highwayLinks[i].A.y,
                    BX = _highwayLinks[i].B.x, BY = _highwayLinks[i].B.y
                };

            var constructions = new ConstructionSaveData[_construction.Count];
            for (int i = 0; i < _construction.Sites.Count; i++)
            {
                ConstructionSite site = _construction.Sites[i];
                constructions[i] = new ConstructionSaveData
                {
                    X = site.Anchor.x,
                    Y = site.Anchor.y,
                    TargetType = site.TargetType,
                    Direction = site.Direction,
                    RemainingSimSeconds =
                        (float)System.Math.Max(0d, site.CompleteAtSimSeconds - _simTime),
                    CompanyTypeId = site.CompanyTypeId,
                };
            }

            return new SimSaveData
            {
                GridWidth = _grid.Width,
                GridHeight = _grid.Height,
                PlacedTiles = tiles.ToArray(),
                SignalOffsets = signals.ToArray(),
                Roundabouts = roundabouts,
                Overpasses = overpasses,
                Oneways = oneways,
                TurnSigns = turnSigns,
                PriorityRoads = priorityRoads,
                Highways = highways,
                BusStops = busStops,
                Constructions = constructions,
                HasCarSimStats = true,
                CarTripSuccessRate = _stats.TripSuccessRate,
                CarDayArrivalCount = _stats.DayArrivalCount,
                CarSkipCurrentDay = _stats.SkipCurrentDay,
            };
        }

        // 주의: _overrideReadyAt은 복원해도 유지(의도) — 세이브 로드로 쿨다운을 리셋하는 악용 방지.
        // _simTime도 리셋하지 않으므로 잔여 쿨다운은 자연 만료로 수렴(무한 잠금 없음).
        public void RestoreSnapshot(SimSaveData snapshot)
        {
            if (snapshot == null) return;
            Vector2Int restoreOffset = GetRestoreOffset(snapshot);

            // 복원 = 전체 교체: 비우고 → 재배치 → 교차로 재감지 → 조율 복원 (PR#8 합의 흐름)
            ResetCarCongestion();
            _grid.Clear();
            _demand.ClearCompanies();
            _highwayLinks.Clear();
            _highwayPartners.Clear();
            _placedBusStops.Clear();
            _busStopSet.Clear();
            _busStopPlatformSet.Clear();
            _roadQueues.RemoveAllCars();
            _roadTraffic.ResetNetworkOccupancy();
            Array.Clear(_carCongestion, 0, _carCongestion.Length);
            _carSim.ClearPopulation();
            _construction.Clear();
            _buildingAssignmentChangePending = false;
            _roadTopologyChangePending = false;
            _stats.RestoreCarSim(
                snapshot.CarTripSuccessRate,
                snapshot.CarDayArrivalCount,
                snapshot.CarSkipCurrentDay,
                snapshot.HasCarSimStats);

            if (snapshot.PlacedTiles != null)
                foreach (var t in snapshot.PlacedTiles)
                {
                    var tile = RestoreTile(t.X, t.Y, restoreOffset);
                    if (!_grid.Place(tile, t.Type, t.Direction)) continue;   // OOB·중복은 Place가 거름(무사고)
                    if (t.Type == TileType.Office || t.Type == TileType.School)
                        _demand.RegisterRestoredCompany(
                            tile, t.Type, CompanyTypeOrNull(t.CompanyTypeId));
                }
            // 참고: PlacedEvent는 안 쏨 — 복원은 '건설'이 아니고, 뷰는 폴링이라 다음 프레임 자동 갱신.

            // 공사 사이트 복원. 구세이브(null)는 공사 0건으로 우아 복원.
            if (snapshot.Constructions != null)
                foreach (var c in snapshot.Constructions)
                {
                    var anchor = RestoreTile(c.X, c.Y, restoreOffset);
                    if (_grid.GetTile(anchor) != TileType.UnderConstruction) continue;   // 불일치 방어
                    double remaining = System.Math.Max(0f, c.RemainingSimSeconds);
                    double total = ConstructionSeconds(c.TargetType);
                    // 이미 지난 만큼(total - remaining)을 뒤로 물려 진행도(Task 7)가 이어지게 한다.
                    double started = _simTime - System.Math.Max(0d, total - remaining);
                    _construction.Register(
                        anchor, c.TargetType, c.Direction, started, _simTime + remaining,
                        c.CompanyTypeId);
                }

            // 조율 적용 전에 교차로부터 감지(Rebuild 전 TrySet은 실패 — SignalMap 계약).
            // 배치 모드: 저장된 신호 목록 = 배치 기록(스펙 §3). 구세이브(자동 시절 = 전 교차로 신호)도
            // 같은 경로로 전부 배치 복원 — 포맷·마이그레이션 공짜. 자동 모드는 현행 스캔.
            if (!_config.AutoDetectSignals)
            {
                _placedSignals.Clear();
                _placedSet.Clear();
                if (snapshot.SignalOffsets != null)
                    foreach (var s in snapshot.SignalOffsets)
                    {
                        var tile = RestoreTile(s.X, s.Y, restoreOffset);
                        if (_placedSet.Add(tile)) _placedSignals.Add(tile);
                    }
                _placedSignals.Sort((a, b) =>
                    (a.y * _config.GridWidth + a.x).CompareTo(b.y * _config.GridWidth + b.x));   // flat 정렬 복구

                _placedRoundabouts.Clear();
                _roundaboutSet.Clear();
                if (snapshot.Roundabouts != null)
                    foreach (var r in snapshot.Roundabouts)
                    {
                        var tile = RestoreTile(r.X, r.Y, restoreOffset);
                        // 손상 세이브 방어: 같은 타일에 신호가 있으면 신호 우선(한 타일 한 장치)
                        if (!_placedSet.Contains(tile) && _roundaboutSet.Add(tile)) _placedRoundabouts.Add(tile);
                    }
                _placedRoundabouts.Sort((a, b) =>
                    (a.y * _config.GridWidth + a.x).CompareTo(b.y * _config.GridWidth + b.x));

                _placedOverpasses.Clear();
                _overpassSet.Clear();
                if (snapshot.Overpasses != null)
                    foreach (var o in snapshot.Overpasses)
                    {
                        var tile = RestoreTile(o.X, o.Y, restoreOffset);
                        // 손상 세이브 방어: 신호·로터리가 선점한 타일이면 입체는 양보(한 타일 한 장치)
                        if (!_placedSet.Contains(tile) && !_roundaboutSet.Contains(tile)
                            && _overpassSet.Add(tile)) _placedOverpasses.Add(tile);
                    }
                _placedOverpasses.Sort((a, b) =>
                    (a.y * _config.GridWidth + a.x).CompareTo(b.y * _config.GridWidth + b.x));
                // 비교차로 잔재는 직후 RebuildSignals()의 소멸 프루닝이 청소(신호와 동일 경로).

                _placedOneways.Clear();
                _onewayDirs.Clear();
                if (snapshot.Oneways != null)
                    foreach (var o in snapshot.Oneways)
                    {
                        var tile = RestoreTile(o.X, o.Y, restoreOffset);
                        var dir = new Vector2Int(o.DirX, o.DirY);
                        // 손상 세이브 방어: 배치 조건 재검증(교차로·비도로면 버림) + dir 검증(대각·zero면 버림).
                        // CanPlaceOneway가 _onewayDirs.ContainsKey도 함께 봐서 중복 엔트리도 자연히 거른다.
                        if (CanPlaceOneway(tile) && System.Array.IndexOf(OnewayUnitDirs, dir) >= 0)
                        {
                            _onewayDirs[tile] = dir;
                            _placedOneways.Add(tile);
                        }
                    }
                _placedOneways.Sort((a, b) =>
                    (a.y * _config.GridWidth + a.x).CompareTo(b.y * _config.GridWidth + b.x));

                _placedTurnSigns.Clear();
                _turnSigns.Clear();
                if (snapshot.TurnSigns != null)
                    foreach (var s in snapshot.TurnSigns)
                    {
                        var tile = RestoreTile(s.X, s.Y, restoreOffset);
                        var mode = (TurnMode)s.Mode;
                        // 손상 세이브 방어: 배치 조건 재검증(교차로·로터리/입체 선점 좌표는 버림) + 모드값 검증.
                        // CanPlaceTurnSign이 _turnSigns.ContainsKey도 함께 봐서 중복 엔트리도 자연히 거른다.
                        // 순서 의미(양방향 배타 후에도 유지): 로터리/입체가 이 블록보다 먼저 복원되고
                        // (그쪽은 인라인 검사라 잔존 _turnSigns의 영향도 없음), 표지판은 여기서
                        // CanPlaceTurnSign 재검증으로 거부 — 같은 좌표 충돌 시 로터리/입체 선점 승.
                        if (CanPlaceTurnSign(tile) && IsValidTurnMode(mode))
                        {
                            _turnSigns[tile] = mode;
                            _placedTurnSigns.Add(tile);
                        }
                    }
                _placedTurnSigns.Sort((a, b) =>
                    (a.y * _config.GridWidth + a.x).CompareTo(b.y * _config.GridWidth + b.x));

                _placedPriorityRoads.Clear();
                _priorityDirs.Clear();
                if (snapshot.PriorityRoads != null)
                    foreach (var p in snapshot.PriorityRoads)
                    {
                        var tile = RestoreTile(p.X, p.Y, restoreOffset);
                        // 손상 세이브 방어: 배치 조건 재검증(4자 배타·교차로) + Axis 값 범위 검증.
                        if (CanPlacePriorityRoad(tile) && (p.Axis == 0 || p.Axis == 1))
                        {
                            _priorityDirs[tile] = (Axis)p.Axis;
                            _placedPriorityRoads.Add(tile);
                        }
                    }
                _placedPriorityRoads.Sort((a, b) =>
                    (a.y * _config.GridWidth + a.x).CompareTo(b.y * _config.GridWidth + b.x));
            }
            if (snapshot.BusStops != null)
            {
                foreach (var stop in snapshot.BusStops)
                {
                    var tile = RestoreTile(stop.X, stop.Y, restoreOffset);
                    if (!TryRestoreBusStop(tile))
                    {
                        Debug.LogWarning(
                            $"[SimEngine] 저장된 버스 정류장 {tile}을(를) " +
                            "복원할 수 없습니다. 빈 타일과 인접 도로를 확인하세요.");
                    }
                }
            }

            if (snapshot.Highways != null)
                foreach (var h in snapshot.Highways)
                {
                    var a = RestoreTile(h.AX, h.AY, restoreOffset);
                    var b = RestoreTile(h.BX, h.BY, restoreOffset);
                    // 복원은 신규 건설이 아니다. 구세이브가 새 예산을 넘더라도 시설은 보존하고,
                    // 좌표에서 사용량을 재계산해 이후 신규 도로/고속도로만 차단한다.
                    if (!CanPlaceHighwayGeometry(a, b)) continue;
                    var link = new HighwayLink(a, b);
                    _highwayLinks.Add(link);
                    _highwayPartners[a] = b;
                    _highwayPartners[b] = a;
                }
            RebuildSignals();
            if (snapshot.SignalOffsets != null)
                foreach (var s in snapshot.SignalOffsets)
                {
                    var tile = RestoreTile(s.X, s.Y, restoreOffset);
                    TrySetSignalOffsetSlots(tile, s.OffsetSlots);
                    // 구세이브 호환: GreenSlots 필드 없던 세이브는 0으로 옴 → 기본 초록 유지(안 덮음).
                    if (s.GreenSlots > 0) TrySetSignalGreenSlots(tile, s.GreenSlots);
                }
            // TopologyDirty는 남긴다: 다음 Step이 경로·수요를 재구축한다(Rebuild가 오프셋 보존).
        }

        // ── IReadOnlyTileData: 차 큐/grid에 위임 ──

        // OOB는 예외가 아니라 중립값 — 뷰의 화면 밖 클릭/스캔이 트러스트 경계(감사 2026-07-12).
        private Vector2Int GetRestoreOffset(SimSaveData snapshot)
        {
            if (_worldGridAccess == null ||
                snapshot.GridWidth <= 0 ||
                snapshot.GridHeight <= 0 ||
                (snapshot.GridWidth == _grid.Width &&
                 snapshot.GridHeight == _grid.Height))
            {
                return Vector2Int.zero;
            }

            return snapshot.GridWidth ==
                       _worldGridAccess.InitialPlayableSize.x &&
                   snapshot.GridHeight ==
                       _worldGridAccess.InitialPlayableSize.y
                ? _worldGridAccess.InitialPlayableOrigin
                : Vector2Int.zero;
        }

        private static Vector2Int RestoreTile(
            int x,
            int y,
            Vector2Int offset)
        {
            return new Vector2Int(x + offset.x, y + offset.y);
        }

        public CongestionLevel GetCongestion(Vector2Int tile) =>
            _grid.InBounds(tile)
                ? _carCongestion[tile.y * _grid.Width + tile.x]
                : CongestionLevel.Free;
        public float GetDensity01(Vector2Int tile) =>
            _grid.InBounds(tile) ? _roadQueues.MaxOccupancy01(tile) : 0f;
        public int GetQueueCount(Vector2Int tile, Dir entryDir) =>
            _grid.InBounds(tile) ? _roadQueues.QueueCount(tile, entryDir) : 0;
        public TileType GetTileType(Vector2Int tile) =>
            _grid.InBounds(tile) ? _grid.GetTile(tile) : TileType.Empty;

        public PlacementDirection GetDirection(Vector2Int tile) =>
            _grid.InBounds(tile) ? _grid.GetDirection(tile) : PlacementDirection.North;

        public Vector2Int GetFootprintSize(TileType type) => TileFootprint.GetSize(type);

        public bool TryGetFootprintAnchor(Vector2Int tile, out Vector2Int anchor) =>
            _grid.TryGetFootprintAnchor(tile, out anchor);

        public bool IsFootprintAnchor(Vector2Int tile) => _grid.IsFootprintAnchor(tile);

        public bool TryGetConstructionProgress01(Vector2Int tile, out float progress01)
        {
            progress01 = 0f;
            if (!_grid.TryGetFootprintAnchor(tile, out Vector2Int anchor)) return false;
            if (!_construction.TryGet(anchor, out ConstructionSite site)) return false;

            double total = site.CompleteAtSimSeconds - site.StartedAtSimSeconds;
            if (total <= 0d) { progress01 = 1f; return true; }

            double elapsed = _simTime - site.StartedAtSimSeconds;
            progress01 = Mathf.Clamp01((float)(elapsed / total));
            return true;
        }

        public bool TryGetConstructionTargetType(
            Vector2Int tile,
            out TileType targetType)
        {
            targetType = TileType.Empty;
            if (!_grid.TryGetFootprintAnchor(
                    tile,
                    out Vector2Int anchor) ||
                !_construction.TryGet(
                    anchor,
                    out ConstructionSite site))
            {
                return false;
            }

            targetType = site.TargetType;
            return true;
        }

        public IReadOnlyList<Vector2Int> BusStopTiles => _placedBusStops;

        public bool CanPlaceBusStop(Vector2Int tile) =>
            CanPlaceBusStop(tile, requireUnlockedTile: true);

        private bool CanPlaceBusStop(
            Vector2Int tile,
            bool requireUnlockedTile)
        {
            if (!_grid.InBounds(tile) ||
                (requireUnlockedTile &&
                 _worldGridAccess != null &&
                 !_worldGridAccess.IsTileUnlocked(tile)) ||
                _grid.GetTile(tile) != TileType.Empty ||
                _busStopPlatformSet.Contains(tile) ||
                !BusStopInfrastructurePolicy.TryGetPlatformPair(
                    tile,
                    IsRoad,
                    out _,
                    out Vector2Int oppositePlatform) ||
                !CanUseBusStopPlatform(
                    oppositePlatform,
                    requireUnlockedTile))
            {
                return false;
            }

            return true;
        }

        private bool CanUseBusStopPlatform(
            Vector2Int tile,
            bool requireUnlockedTile)
        {
            return _grid.InBounds(tile) &&
                (!requireUnlockedTile ||
                 _worldGridAccess == null ||
                 _worldGridAccess.IsTileUnlocked(tile)) &&
                _grid.GetTile(tile) == TileType.Empty &&
                !_busStopPlatformSet.Contains(tile);
        }

        public bool TryPlaceBusStop(Vector2Int tile)
        {
            if (!TryRegisterBusStop(tile, requireUnlockedTile: true))
            {
                return false;
            }

            RefreshBusCoverageReduction();
            _grid.MarkTopologyDirty();
            return true;
        }

        private bool TryRestoreBusStop(Vector2Int tile)
        {
            if (!_grid.InBounds(tile) ||
                _grid.GetTile(tile) != TileType.Empty ||
                _busStopPlatformSet.Contains(tile) ||
                (!CanPlaceBusStop(
                     tile,
                     requireUnlockedTile: false) &&
                 !BusStopInfrastructurePolicy.HasAdjacentRoad(
                     tile,
                     IsRoad)) ||
                !_busStopSet.Add(tile))
            {
                return false;
            }

            RegisterBusStopPlatforms(tile);
            InsertSorted(_placedBusStops, tile);
            RefreshBusCoverageReduction();
            return true;
        }

        private bool TryRegisterBusStop(
            Vector2Int tile,
            bool requireUnlockedTile)
        {
            if (!CanPlaceBusStop(tile, requireUnlockedTile) ||
                !_busStopSet.Add(tile))
            {
                return false;
            }

            RegisterBusStopPlatforms(tile);
            InsertSorted(_placedBusStops, tile);
            return true;
        }

        private void RegisterBusStopPlatforms(Vector2Int stopTile)
        {
            _busStopPlatformSet.Add(stopTile);
            if (BusStopInfrastructurePolicy.TryGetPlatformPair(
                    stopTile,
                    IsRoad,
                    out _,
                    out Vector2Int oppositePlatform) &&
                _grid.InBounds(oppositePlatform) &&
                _grid.GetTile(oppositePlatform) == TileType.Empty)
            {
                _busStopPlatformSet.Add(oppositePlatform);
            }
        }

        public bool TryRemoveBusStop(Vector2Int tile)
        {
            if (!_busStopSet.Remove(tile))
            {
                return false;
            }

            _busStopPlatformSet.Remove(tile);
            if (BusStopInfrastructurePolicy.TryGetPlatformPair(
                    tile,
                    IsRoad,
                    out _,
                    out Vector2Int oppositePlatform))
            {
                _busStopPlatformSet.Remove(oppositePlatform);
            }

            _placedBusStops.Remove(tile);
            RefreshBusCoverageReduction();
            _grid.MarkTopologyDirty();
            return true;
        }

        void RefreshBusCoverageReduction()
        {
            int radius = _config.BusCoverageRadius;
            if (radius <= 0 || _placedBusStops.Count < 2)
            {
                _demand.SetCommuterReduction(null);
                return;
            }

            _demand.SetCommuterReduction(home =>
            {
                for (int i = 0; i < _placedBusStops.Count; i++)
                {
                    Vector2Int stop = _placedBusStops[i];
                    int distance = Mathf.Max(
                        Mathf.Abs(home.x - stop.x),
                        Mathf.Abs(home.y - stop.y));
                    if (distance <= radius) return 1;
                }

                return 0;
            });
        }

        private bool WouldOrphanBusStopIfRoadRemoved(
            Vector2Int roadTile)
        {
            foreach (Vector2Int stopTile in _placedBusStops)
            {
                if (BusStopInfrastructurePolicy.TryGetPlatformPair(
                        stopTile,
                        IsRoad,
                        out Vector2Int accessRoad,
                        out Vector2Int oppositePlatform))
                {
                    bool keepsSamePlatformPair =
                        BusStopInfrastructurePolicy.TryGetPlatformPair(
                            stopTile,
                            candidate =>
                                candidate != roadTile &&
                                IsRoad(candidate),
                            out Vector2Int remainingAccessRoad,
                            out Vector2Int remainingOppositePlatform) &&
                        remainingAccessRoad == accessRoad &&
                        remainingOppositePlatform == oppositePlatform;
                    if (!keepsSamePlatformPair)
                    {
                        return true;
                    }

                    continue;
                }

                bool currentlyHasStrictApproach =
                    BusStopInfrastructurePolicy
                        .HasRoadsideApproach(
                            stopTile,
                            IsRoad);
                bool remainsReachable =
                    currentlyHasStrictApproach
                        ? BusStopInfrastructurePolicy
                            .HasRoadsideApproach(
                                stopTile,
                                candidate =>
                                    candidate != roadTile &&
                                    IsRoad(candidate))
                        : BusStopInfrastructurePolicy
                            .HasAdjacentRoad(
                                stopTile,
                                candidate =>
                                    candidate != roadTile &&
                                    IsRoad(candidate));
                if (!remainsReachable)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsRoad(Vector2Int tile) =>
            _grid.InBounds(tile) &&
            _grid.GetTile(tile) == TileType.Road;

        private void InsertSorted(List<Vector2Int> tiles, Vector2Int tile)
        {
            int flat = tile.y * _config.GridWidth + tile.x;
            int index = tiles.FindIndex(
                candidate => candidate.y * _config.GridWidth + candidate.x > flat);

            if (index < 0)
            {
                tiles.Add(tile);
            }
            else
            {
                tiles.Insert(index, tile);
            }
        }

        // ── 회사 유형 표 (환) ─────────────────────────────────────────────
        // 오서링 SO 카탈로그는 Assembly-CSharp 에 있고 CityFlow.Sim 은 그 어셈블리를 참조할 수 없다.
        // 배선 계층(CityBootstrap)이 SO → CompanyTypeInfo 로 옮겨 여기에 주입한다.
        // 표가 비어 있으면 전역 창 폴백 = 종전 동작. 배선 없는 씬은 영향받지 않는다.
        readonly Dictionary<string, CompanyTypeInfo> _companyTypes = new(StringComparer.Ordinal);
        int _maxCompanyTypeCapacity;   // 뷰 주차 슬롯 계약의 하한 — CarSimOfficeParkingSlots 참조

        public void SetCompanyTypes(IReadOnlyList<CompanyTypeInfo> types)
        {
            _companyTypes.Clear();
            _maxCompanyTypeCapacity = 0;
            if (types == null) return;
            for (int i = 0; i < types.Count; i++)
            {
                string id = types[i].Window.CompanyTypeId;
                if (string.IsNullOrWhiteSpace(id)) continue;   // 무명 유형은 조회할 수 없다
                _companyTypes[id.Trim()] = types[i];
                if (types[i].Capacity > _maxCompanyTypeCapacity)
                    _maxCompanyTypeCapacity = types[i].Capacity;
            }
        }

        internal bool TryGetCompanyType(string companyTypeId, out CompanyTypeInfo info)
        {
            info = default;
            if (string.IsNullOrWhiteSpace(companyTypeId)) return false;
            return _companyTypes.TryGetValue(companyTypeId.Trim(), out info);
        }

        // 유형 없는 목적지(School 등)·표 미주입 상황의 폴백 — 종전 전역 창 그대로.
        internal CommuteWindow FallbackCommuteWindow() => CommuteWindow.FromConfig(_config);

        internal int CompanyTypeCountForTest => _companyTypes.Count;

        // 유형 id 를 정원과 함께 DemandMap 에 싣는다. 미등록 id 는 경고 후 유형 없이 등록한다.
        void RegisterCompanyOfType(Vector2Int tile, TileType type, string companyTypeId)
        {
            if (string.IsNullOrWhiteSpace(companyTypeId))
            {
                _demand.RegisterCompany(tile, type, _simTime);
                return;
            }

            if (!TryGetCompanyType(companyTypeId, out CompanyTypeInfo info))
            {
                Debug.LogWarning(
                    $"[SimEngine] 등록되지 않은 회사 유형 id '{companyTypeId}' — 폴백 창으로 배치한다.");
                _demand.RegisterCompany(tile, type, _simTime);
                return;
            }

            _demand.RegisterCompany(
                tile, type, _simTime,
                capacityOverride: null,
                companyType: info);
        }

        internal bool TryGetCompanyTypeIdForTest(Vector2Int tile, out string companyTypeId) =>
            _demand.TryGetCompanyTypeId(tile, out companyTypeId);

        internal CommuteWindow CommuteWindowAtForTest(Vector2Int tile) =>
            _demand.CommuteWindowAt(tile);

        // 세이브에서 온 id → 유형. 표에 없으면 null(폴백) — 로드가 실패하지는 않게 한다.
        CompanyTypeInfo? CompanyTypeOrNull(string companyTypeId) =>
            TryGetCompanyType(companyTypeId, out CompanyTypeInfo info)
                ? info
                : (CompanyTypeInfo?)null;
    }
}
