using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Sim;
using UnityEngine;

namespace CityFlow.DebugTools
{
    // 미니 모터웨이즈풍 간이 렌더러: 크림 배경 + 회색 도로 + 작은 건물 + 엔진 경로 위를 달리는 차.
    // ★차는 엔진이 계산한 실제 통근 경로(ActiveRoutes) 위를 이동 → 라우팅을 눈으로 검증 가능.
    // 겹치는 간선엔 차가 몰려서 정체가 진짜로 보임. 한주석 View 대체 전 임시 도구.
    public sealed class SimTileRenderer : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private int size = 20;
        private const int CellPx = 12;
        private const float CarSpeed = 1.1f;   // 타일/초

        private static readonly Color32 Cream  = new Color32(238, 232, 210, 255);
        private static readonly Color32 Road   = new Color32(150, 150, 156, 255);
        private static readonly Color32 Car    = new Color32( 30,  30,  40, 255);
        private static readonly Color32 House   = new Color32( 90, 150, 220, 255);
        private static readonly Color32 Office  = new Color32(235, 150,  60, 255);
        private static readonly Color32 School  = new Color32(170, 110, 210, 255);
        private static readonly Color32 SigGreen = new Color32( 40, 200,  70, 255); // 신호 초록
        private static readonly Color32 SigRed   = new Color32(230,  50,  50, 255); // 신호 빨강

        private IReadOnlyTileData _data;
        private SimEngine _engine;   // 실제 경로를 읽기 위해(디버그 캐스트)
        private Texture2D _tex;
        private Color32[] _px;
        private int _w;
        private float[] _carPhase = new float[0];   // 경로별 차 진행 위상(왕복·감속 누적)

        // 차간 간격 체크용 현재 프레임 포즈(위치+진행방향)
        private struct CarPose { public Vector2 Pos; public Vector2 Dir; public bool Valid; public bool Horizontal; }
        private CarPose[] _poses = new CarPose[0];
        private const float MinGap = 0.68f;      // 앞차와 최소 간격(타일). 차 길이(7px≈0.58)보다 약간 크게
        private const float LaneTolerance = 0.15f; // 같은 차선 판정 폭(타일)
        private const float CrossGap = 0.55f;     // 교차로 양보 반경(타일) — 방향 무관, 앞이 점유돼 있으면 대기

        public void Initialize(CityFlowServices services)
        {
            _data = services.TileData;
            _engine = services.Placement as SimEngine;   // 진짜 엔진일 때만 차 그림(Fake면 null)
            _w = size * CellPx;
            _tex = new Texture2D(_w, _w, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            _px = new Color32[_w * _w];

            var sr = gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(_tex, new Rect(0, 0, _w, _w), Vector2.zero, CellPx);
            transform.position = Vector3.zero;
        }

        private void Update()
        {
            if (_data == null) return;

            for (int i = 0; i < _px.Length; i++) _px[i] = Cream;

            // 1) 타일(도로·건물)
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    switch (_data.GetTileType(new Vector2Int(x, y)))
                    {
                        case TileType.Road:   FillCell(x, y, 0, Road);   break;
                        case TileType.House:  FillCell(x, y, 3, House);  break;
                        case TileType.Office: FillCell(x, y, 3, Office); break;
                        case TileType.School: FillCell(x, y, 3, School); break;
                    }
                }

            // 2) 엔진 실제 경로 위를 달리는 차
            DrawRouteCars();

            // 3) 교차로 신호 마커(엔진 시뮬 시간 기준 초록/빨강)
            DrawSignals();

            _tex.SetPixels32(_px);
            _tex.Apply(false);
        }

        // 신호 = 교차로 타일 모서리의 작은 등. 엔진이 자동 감지한 교차로에만 뜬다.
        private void DrawSignals()
        {
            if (_engine == null) return;
            foreach (var t in _engine.SignalTiles)
            {
                var col = _engine.IsSignalGreen(t) ? SigGreen : SigRed;
                int x0 = t.x * CellPx + CellPx - 5, y0 = t.y * CellPx + CellPx - 5;   // 우상단 모서리
                for (int py = y0; py < y0 + 4; py++)
                    for (int px = x0; px < x0 + 4; px++)
                        if (px >= 0 && px < _w && py >= 0 && py < _w) _px[py * _w + px] = col;
            }
        }

        // 경로마다 차 1대: 왕복(삼각파) + 차간 간격 유지 — 같은 차선 앞에 차가 있으면 멈춰서 줄을 섬.
        private void DrawRouteCars()
        {
            if (_engine == null) return;
            IReadOnlyList<List<Vector2Int>> routes = _engine.ActiveRoutes;

            if (_carPhase.Length < routes.Count)   // 경로 늘면 확장 + 초기 위상 분산(다 뭉치지 않게)
            {
                int old = _carPhase.Length;
                System.Array.Resize(ref _carPhase, routes.Count);
                for (int k = old; k < routes.Count; k++) _carPhase[k] = k * 0.618f;
                _poses = new CarPose[routes.Count];
            }

            // 1패스: 모든 차의 현재 포즈 계산(간격 판정 기준은 전부 '이번 프레임 시작 위치')
            for (int r = 0; r < routes.Count; r++)
                _poses[r] = PoseOf(routes[r], r);

            // 2패스: 앞차 없으면 전진, 있으면 대기 → 병목 뒤로 줄이 생김
            float dt = Time.deltaTime;
            for (int r = 0; r < routes.Count; r++)
            {
                if (!_poses[r].Valid) continue;
                var path = routes[r];
                int segs = path.Count - 1;

                float p = Fold(_carPhase[r], segs, segs * 2);
                int at = Mathf.Clamp((int)p, 0, segs);
                float speed = CarSpeed * Mathf.Lerp(1f, 0.25f, _data.GetDensity01(path[at]));
                if (IsBlocked(r)) speed = 0f;               // 같은 차선 앞차 → 정지(대기)
                if (RedLightAhead(r)) speed = 0f;           // 빨간불 교차로 진입 직전 → 정지
                _carPhase[r] += speed * dt;

                // 갱신된 위치로 그림
                var pose = PoseOf(path, r);
                DrawCar((int)(pose.Pos.x * CellPx), (int)(pose.Pos.y * CellPx), pose.Horizontal, Car);
            }
        }

        // 위상 → 월드 포즈(차선 오프셋 포함).
        private CarPose PoseOf(List<Vector2Int> path, int r)
        {
            if (path == null || path.Count < 2) return default;
            int segs = path.Count - 1;
            int cycle = segs * 2;

            float q = _carPhase[r] % cycle;
            if (q < 0) q += cycle;
            bool forward = q <= segs;
            float p = Fold(_carPhase[r], segs, cycle);
            int i = Mathf.Clamp((int)p, 0, segs - 1);
            float f = p - i;

            Vector2 a = new Vector2(path[i].x + 0.5f, path[i].y + 0.5f);
            Vector2 b = new Vector2(path[i + 1].x + 0.5f, path[i + 1].y + 0.5f);
            Vector2 dir = ((b - a) * (forward ? 1f : -1f)).normalized;
            Vector2 lane = new Vector2(dir.y, -dir.x) * (2f / CellPx);   // 우측통행 차선 오프셋

            return new CarPose
            {
                Pos = Vector2.Lerp(a, b, f) + lane,
                Dir = dir,
                Valid = true,
                Horizontal = Mathf.Abs(path[i + 1].x - path[i].x) >= Mathf.Abs(path[i + 1].y - path[i].y),
            };
        }

        // 진입하려는 다음 타일이 빨간불 교차로인가. 이미 교차로 안이면 통과(교차로 안에서 안 멈춤).
        private bool RedLightAhead(int r)
        {
            var me = _poses[r];
            var cur = new Vector2Int(Mathf.FloorToInt(me.Pos.x), Mathf.FloorToInt(me.Pos.y));
            var next = new Vector2Int(
                Mathf.FloorToInt(me.Pos.x + me.Dir.x * 0.6f),
                Mathf.FloorToInt(me.Pos.y + me.Dir.y * 0.6f));
            return next != cur && !_engine.IsSignalGreen(next);   // 비신호 타일은 항상 초록 취급
        }

        // 전진 가능? 두 규칙: ①같은 차선 앞차(추종) ②교차로 몸통 점유(방향 무관 양보).
        private bool IsBlocked(int r)
        {
            var me = _poses[r];
            for (int s = 0; s < _poses.Length; s++)
            {
                if (s == r || !_poses[s].Valid) continue;
                Vector2 rel = _poses[s].Pos - me.Pos;
                float ahead = Vector2.Dot(rel, me.Dir);
                if (ahead <= 0.02f) continue;                             // 뒤차 무시(0.02=동일점 상호봉쇄 방지)

                float lateral = Mathf.Abs(rel.x * me.Dir.y - rel.y * me.Dir.x);
                bool sameLane = ahead < MinGap && lateral < LaneTolerance; // ① 추종
                bool crossing = rel.sqrMagnitude < CrossGap * CrossGap;    // ② 교차 차량이 코앞
                if (!sameLane && !crossing) continue;

                if (crossing && !sameLane)
                {
                    // 서로가 서로를 양보하는 교착 방지: 상호 상황이면 낮은 인덱스가 우선권(먼저 지나감).
                    float aheadOther = Vector2.Dot(-rel, _poses[s].Dir);
                    if (aheadOther > 0.02f && r < s) continue;
                }
                return true;
            }
            return false;
        }

        // 위상 → [0,segs] 삼각파(왕복): 앞으로 갔다 뒤로 돌아옴.
        private static float Fold(float phase, int segs, int cycle)
        {
            float q = phase % cycle;
            if (q < 0) q += cycle;
            return q <= segs ? q : cycle - q;
        }

        private void FillCell(int tx, int ty, int pad, Color32 col)
        {
            int x0 = tx * CellPx + pad, x1 = (tx + 1) * CellPx - pad;
            int y0 = ty * CellPx + pad, y1 = (ty + 1) * CellPx - pad;
            for (int py = y0; py < y1; py++)
                for (int px = x0; px < x1; px++)
                    _px[py * _w + px] = col;
        }

        // 차 = 진행 방향으로 길쭉한 사각(가로 7x3 / 세로 3x7).
        private void DrawCar(int px, int py, bool horizontal, Color32 col)
        {
            int hx = horizontal ? 3 : 1;
            int hy = horizontal ? 1 : 3;
            for (int j = py - hy; j <= py + hy; j++)
                for (int k = px - hx; k <= px + hx; k++)
                    if (k >= 0 && k < _w && j >= 0 && j < _w) _px[j * _w + k] = col;
        }
    }
}
