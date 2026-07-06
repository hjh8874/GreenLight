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
        float _acc;   // 아직 소비되지 않고 저금된 시간

        // 테스트 관찰용 seam. internal이라 테스트 어셈블리만 봄(InternalsVisibleTo).
        internal int StepCount { get; private set; }

        public SimEngine(SimConfig config)
        {
            _config = config;
            _grid = new CityGrid(config.GridWidth, config.GridHeight);
            _network = new RoadNetwork(_grid);
            _demand = new DemandMap(config);
            _solver = new FlowSolver(config.GridWidth, config.GridHeight);
        }
        // ponytail: SimEventBuffer 주입은 발행자(ArrivalEmitter, D4)가 생길 때.

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
            // ponytail: ③Arrival ④Burst ⑤Stats ⑥Drain은 D4에서 이 뒤에 이어 붙임.
        }

        // ── IPlacementService: CityGrid에 위임 ──
        public bool CanPlace(Vector2Int tile, TileType type) => _grid.CanPlace(tile, type);
        public bool Place(Vector2Int tile, TileType type) => _grid.Place(tile, type);
        public bool Remove(Vector2Int tile) => _grid.Remove(tile);

        // ── IReadOnlyTileData: solver/grid에 위임 ──
        public float Stability01 => 1f;   // ponytail: SimStats(D4) 전까지 항상 안정
        public CongestionLevel GetCongestion(Vector2Int tile) => _solver.GetCongestion(tile);
        public float GetDensity01(Vector2Int tile) => Mathf.Clamp01(_solver.GetRatio(tile));
        public TileType GetTileType(Vector2Int tile) => _grid.GetTile(tile);
    }
}
