using System;
using System.Collections.Generic;
using UnityEngine;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;

namespace CityFlow.Sim
{
    // 엔진의 유일한 public 창구(파사드). Bootstrap이 생성하고 매 프레임 Tick(dt) 호출.
    // 내부 클래스(grid·network·demand·solver)는 전부 internal — 외부는 이 인터페이스들만 봄.
    public sealed class SimEngine : IPlacementService, IReadOnlyTileData, IReadOnlyCityStats, ISimSaveSource, ISignalControl, IOfflineSettlementSource
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
        float _lastStability = -1f;   // 직전 발행한 안정도(-1=아직 없음 → 첫 틱은 무조건 발행)

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
                _demand.Reassign(_grid, _network);        // 도달성(같은 섬) 우선 배정
                _signals.Rebuild(_grid);                  // 교차로 재감지(살아남은 신호 오프셋 보존)
                _grid.ClearTopologyDirty();
            }

            // ① 수요→세그먼트 흐름 배정 (러시아워 맥동 배율 반영 — 기획 §1 '수요의 맥동')
            _solver.Assign(_demand, _network, _config, SimConfig.DemandPulse(_simTime, _config));
            _solver.Resolve(_config, _signals, _grid, _simTime); // ② 혼잡·병목·그린웨이브·오버라이드·delivered
            _congestion.Scan(_solver, _events, _config);  // ②' 레벨 전이만 이벤트로
            _arrivals.Emit(_solver, _events, _config);    // ③ 도착 정수 방출(소수 이월)
            _bursts.Scan(_solver, _events, _config);      // ④ Jam→Free 감지 → 보상
            _stats.Update(_solver, _demand, _config);     // ⑤ 안정도 집계
            // ⑤' 안정도가 바뀐 틱만 이벤트로(매 틱 스팸 방지 — 혼잡 diff와 같은 철학).
            // Update 뒤에 체크해야 첫 틱·복원 직후에도 이번 틱의 진짜 값이 나간다.
            if (Mathf.Abs(_stats.Stability01 - _lastStability) > 0.001f)
            {
                _lastStability = _stats.Stability01;
                _events.QueueStability(new StabilityEvent(_stats.Stability01));
            }
            _events.Drain();                              // ⑥ 모인 이벤트 일괄 발행 (항상 마지막!)
        }

        // 복귀 정산: 마지막 도시 상태의 처리량으로 경과시간을 적분(상한 OfflineCapHours).
        // 세이브 시스템(한준희)이 앱 복귀 시 호출 → SettlementEvent로 결과 발행.
        public double SettleOffline(double elapsedSeconds)
        {
            if (elapsedSeconds <= 0.0)
            {
                return 0.0;
            }

            // 마지막 배치가 아직 시뮬에 반영 전이면 반영부터(정산은 최신 도시 기준).
            if (_grid.TopologyDirty)
            {
                _network.Rebuild();
                _demand.Reassign(_grid, _network);
                _signals.Rebuild(_grid);
                _grid.ClearTopologyDirty();
            }
            // 정산은 평상 신호 기준 = 공정(맥동 무시와 같은 철학). 복귀 시 잔여 오버라이드는 소멸 —
            // 시뮬 시간이 멈춰 있어 그대로 두면 최대 8h가 강제초록 처리량으로 정산되는 누수.
            foreach (var t in _signals.Tiles)
                if (_signals.TryGet(t, out var sig)) sig.OverrideUntil = 0;

            _solver.Assign(_demand, _network, _config);   // 정산은 평균 수요(맥동 무시 = 공정)
            _solver.Resolve(_config, _signals, _grid, _simTime); // 오프라인도 신호 조율(오프셋·초록)은 그대로 반영

            double capSeconds = Math.Max(0.0, _config.OfflineCapHours * 3600.0);
            double capped = Math.Min(elapsedSeconds, capSeconds);
            long coins = _arrivals.SettleOffline(_solver, capped, _config);

            _events.QueueSettlement(new SettlementEvent(capped / 60.0, coins));
            _events.Drain();
            return capped;
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
        public int ActiveVehicleCount => _solver.Routes.Count;
        
        // 뷰용 : 이번 틱 처리량 (대/초) 튜너가 오프셋 조율 효과를 숫자로 보게 
        public float DeliveredTotal => _solver.DeliveredTotal;

        // ── ISignalControl(신호 조작 창구): 유저가 교차로를 조율하는 두 레버 — 오프셋·초록 길이 ──
        // 제안 단계: 계약으로 승격(설계 §5), 최종 확정은 주석·김건 합의. 김건 Game뷰 UI가 이 계약에 붙음.
        public IReadOnlyList<Vector2Int> SignalTiles => _signals.Tiles;

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
        readonly Dictionary<Vector2Int, double> _overrideReadyAt = new();
        readonly List<Vector2Int> _corridorBuf = new();   // 코리도어 수집 재사용 버퍼(비-재진입)

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
                    tiles.Add(new TileSaveData { X = x, Y = y, Type = type });
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

            return new SimSaveData { PlacedTiles = tiles.ToArray(), SignalOffsets = signals.ToArray() };
        }

        // 주의: _overrideReadyAt은 복원해도 유지(의도) — 세이브 로드로 쿨다운을 리셋하는 악용 방지.
        // _simTime도 리셋하지 않으므로 잔여 쿨다운은 자연 만료로 수렴(무한 잠금 없음).
        public void RestoreSnapshot(SimSaveData snapshot)
        {
            if (snapshot == null) return;                        // SaveService도 거르지만 방어 한 겹

            // 복원 = 전체 교체: 비우고 → 재배치 → 교차로 재감지 → 조율 복원 (PR#8 합의 흐름)
            _grid.Clear();
            if (snapshot.PlacedTiles != null)
                foreach (var t in snapshot.PlacedTiles)
                    _grid.Place(new Vector2Int(t.X, t.Y), t.Type);   // OOB·중복은 Place가 거름(무사고)
            // 참고: PlacedEvent는 안 쏨 — 복원은 '건설'이 아니고, 뷰는 폴링이라 다음 프레임 자동 갱신.

            // 조율 적용 전에 교차로부터 감지(Rebuild 전 TrySet은 실패 — SignalMap 계약).
            _signals.Rebuild(_grid);
            if (snapshot.SignalOffsets != null)
                foreach (var s in snapshot.SignalOffsets)
                {
                    var tile = new Vector2Int(s.X, s.Y);
                    TrySetSignalOffsetSlots(tile, s.OffsetSlots);
                    // 구세이브 호환: GreenSlots 필드 없던 세이브는 0으로 옴 → 기본 초록 유지(안 덮음).
                    if (s.GreenSlots > 0) TrySetSignalGreenSlots(tile, s.GreenSlots);
                }
            // TopologyDirty는 남긴다: 다음 Step/SettleOffline이 경로·수요 재구축(Rebuild가 오프셋 보존).
        }

        // ── IReadOnlyTileData: solver/grid에 위임 ──
        public float Stability01 => _stats.Stability01;
        public CongestionLevel GetCongestion(Vector2Int tile) => _solver.GetCongestion(tile);
        public float GetDensity01(Vector2Int tile) => Mathf.Clamp01(_solver.GetRatio(tile));
        public TileType GetTileType(Vector2Int tile) => _grid.GetTile(tile);
    }
}
