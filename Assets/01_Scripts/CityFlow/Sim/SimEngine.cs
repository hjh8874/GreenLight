using System;
using System.Collections.Generic;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim
{
    // 엔진의 유일한 public 창구(파사드). Bootstrap이 생성하고 매 프레임 Tick(dt) 호출.
    // 내부 클래스(grid·network·demand·solver)는 전부 internal — 외부는 이 두 인터페이스만 봄.
    public sealed class SimEngine : IPlacementService, IReadOnlyTileData
    {
        readonly SimConfig _config;
        readonly CityGrid _grid;
        readonly RoadNetwork _network;
        readonly DemandMap _demand;
        readonly FlowSolver _solver;
        readonly SignalMap _signals = new SignalMap();
        double _simTime;   // 시뮬 누적 시간(초) — 신호 초록/빨강 판정용(뷰)
        readonly ArrivalEmitter _arrivals;
        readonly BurstDetector _bursts;
        readonly CongestionNotifier _congestion;
        readonly SimStats _stats = new SimStats();
        readonly SimEventBuffer _events;
        float _acc;   // 아직 소비되지 않고 저금된 시간

        // 테스트 관찰용 seam. internal이라 테스트 어셈블리만 봄(InternalsVisibleTo).
        internal int StepCount { get; private set; }

        public SimEngine(SimConfig config, SimEventHub hub)
        {
            _config = config;
            _grid = new CityGrid(config.GridWidth, config.GridHeight);
            _network = new RoadNetwork(_grid);
            _demand = new DemandMap(config);
            _solver = new FlowSolver(config.GridWidth, config.GridHeight);
            _arrivals = new ArrivalEmitter(config.GridWidth, config.GridHeight);
            _bursts = new BurstDetector(config.GridWidth, config.GridHeight);
            _congestion = new CongestionNotifier(config.GridWidth, config.GridHeight);
            _events = new SimEventBuffer(hub);   // 계산 중 발행 금지 — 큐/Drain으로 재진입 차단
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

        // 고정 0.1s 시뮬 한 칸. 순서가 곧 파이프라인(blueprint §2 Step).
        void Step()
        {
            StepCount++;

            _simTime += _config.TickInterval;

            // 배치도가 바뀐 틱에만 경로·수요 재계산(더티 플래그 — 매 틱 BFS 금지).
            if (_grid.TopologyDirty)
            {
                _network.Rebuild();
                _demand.Reassign(_grid);
                _signals.Rebuild(_grid);                  // 교차로 재감지(살아남은 신호 오프셋 보존)
                _grid.ClearTopologyDirty();
            }

            _solver.Assign(_demand, _network, _config);   // ① 수요→세그먼트 흐름 배정
            _solver.Resolve(_config, _signals);           // ② 혼잡·병목·그린웨이브·delivered
            _congestion.Scan(_solver, _events, _config);  // ②' 레벨 전이만 이벤트로
            _arrivals.Emit(_solver, _events, _config);    // ③ 도착 정수 방출(소수 이월)
            _bursts.Scan(_solver, _events, _config);      // ④ Jam→Free 감지 → 보상
            _stats.Update(_solver, _demand, _config);     // ⑤ 안정도 집계
            _events.Drain();                              // ⑥ 모인 이벤트 일괄 발행 (항상 마지막!)
        }

        // 복귀 정산: 마지막 도시 상태의 처리량으로 경과시간을 적분(상한 OfflineCapHours).
        // 세이브 시스템(한준희)이 앱 복귀 시 호출 → SettlementEvent로 결과 발행.
        public void SettleOffline(double elapsedSeconds)
        {
            // 마지막 배치가 아직 시뮬에 반영 전이면 반영부터(정산은 최신 도시 기준).
            if (_grid.TopologyDirty)
            {
                _network.Rebuild();
                _demand.Reassign(_grid);
                _signals.Rebuild(_grid);
                _grid.ClearTopologyDirty();
            }
            _solver.Assign(_demand, _network, _config);
            _solver.Resolve(_config, _signals);   // 오프라인도 신호 조율 상태 그대로 반영

            double capped = Math.Min(elapsedSeconds, _config.OfflineCapHours * 3600.0);
            long coins = _arrivals.SettleOffline(_solver, capped, _config);

            _events.QueueSettlement(new SettlementEvent(capped / 60.0, coins));
            _events.Drain();
        }

        // ── IPlacementService: CityGrid에 위임. 성공 시 PlacedEvent 큐잉(발행은 틱 끝 Drain) ──
        public bool CanPlace(Vector2Int tile, TileType type) => _grid.CanPlace(tile, type);

        public bool Place(Vector2Int tile, TileType type)
        {
            if (!_grid.Place(tile, type)) return false;
            _events.QueuePlaced(new PlacedEvent(tile, type, isRemove: false));
            return true;
        }

        public bool Remove(Vector2Int tile)
        {
            if (!_grid.TryRemove(tile, out var removed)) return false;
            _events.QueuePlaced(new PlacedEvent(tile, removed, isRemove: true));
            return true;
        }

        // 뷰 연동: 엔진이 이번 틱 계산한 실제 통근 경로들. 차를 이 위에 그리면 라우팅을 눈으로 검증.
        // ponytail: 지금은 디버그 뷰용 public. 진짜 View 붙을 때 Contracts로 승격.
        public IReadOnlyList<List<Vector2Int>> ActiveRoutes => _solver.Routes;

        // ── 신호 조작 창구 — 유저(UI)가 오프셋을 돌리는 유일한 레버. 자동/수동 모두 이 값 하나.
        // ponytail: ISignalControl로 Contracts 승격은 주석님·김건 합의 후(설계 §5).
        public IReadOnlyList<Vector2Int> SignalTiles => _signals.Tiles;

        public int GetSignalOffsetSlots(Vector2Int tile) =>
            _signals.TryGet(tile, out var s) ? s.OffsetSlots : 0;

        public bool TrySetSignalOffsetSlots(Vector2Int tile, int slots)
        {
            if (!_signals.TryGet(tile, out var s)) return false;
            s.OffsetSlots = slots;   // 다음 Resolve부터 반영(topology 재계산 불필요)
            return true;
        }

        // 뷰용: 이 교차로가 지금 초록인가(시뮬 시간 기준). 신호 없으면 항상 초록 취급.
        public bool IsSignalGreen(Vector2Int tile) =>
            !_signals.TryGet(tile, out var s) || SignalMath.IsGreen(s, _simTime);

        // 뷰용: 이 교차로의 이 방향 신호 3상태(초록/노랑/적색). 신호 없으면 항상 초록.
        public SignalPhase GetSignalPhase(Vector2Int tile, bool horizontal) =>
            _signals.TryGet(tile, out var s) ? SignalMath.PhaseForAxis(s, _simTime, horizontal) : SignalPhase.Green;

        // 진입 허가 = 초록만(노랑·적색은 진입 금지).
        public bool IsSignalGreen(Vector2Int tile, bool horizontal) =>
            GetSignalPhase(tile, horizontal) == SignalPhase.Green;

        // ── IReadOnlyTileData: solver/grid에 위임 ──
        public float Stability01 => _stats.Stability01;
        public CongestionLevel GetCongestion(Vector2Int tile) => _solver.GetCongestion(tile);
        public float GetDensity01(Vector2Int tile) => Mathf.Clamp01(_solver.GetRatio(tile));
        public TileType GetTileType(Vector2Int tile) => _grid.GetTile(tile);
    }
}
