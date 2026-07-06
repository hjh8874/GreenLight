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

            // 배치도가 바뀐 틱에만 경로·수요 재계산(더티 플래그 — 매 틱 BFS 금지).
            if (_grid.TopologyDirty)
            {
                _network.Rebuild();
                _demand.Reassign(_grid);
                _grid.ClearTopologyDirty();
            }

            _solver.Assign(_demand, _network, _config);   // ① 수요→세그먼트 흐름 배정
            _solver.Resolve(_config);                     // ② 혼잡·병목·delivered
            _congestion.Scan(_solver, _events, _config);  // ②' 레벨 전이만 이벤트로
            _arrivals.Emit(_solver, _events, _config);    // ③ 도착 정수 방출(소수 이월)
            _bursts.Scan(_solver, _events, _config);      // ④ Jam→Free 감지 → 보상
            _stats.Update(_solver, _demand, _config);     // ⑤ 안정도 집계
            _events.Drain();                              // ⑥ 모인 이벤트 일괄 발행 (항상 마지막!)
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

        // ── IReadOnlyTileData: solver/grid에 위임 ──
        public float Stability01 => _stats.Stability01;
        public CongestionLevel GetCongestion(Vector2Int tile) => _solver.GetCongestion(tile);
        public float GetDensity01(Vector2Int tile) => Mathf.Clamp01(_solver.GetRatio(tile));
        public TileType GetTileType(Vector2Int tile) => _grid.GetTile(tile);
    }
}
