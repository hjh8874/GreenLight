using System.Collections.Generic;
using System.Reflection;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.View;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityFlow.DebugTools
{
    // 라이브 디버그 오버레이 — 시간 흐름(실측 속도)과 회사별 채용 현황(Filled/Capacity)을 표시한다.
    // 에디터 Play 전용 자동 생성이라 씬 편집이 필요 없다. F3 토글, 좌상단 패널에 배속 버튼.
    // "시간이 안 흐른다" 진단: 캘린더 서비스 누락 경고 + 게임분/실초 실측치를 같이 보여준다.
    public sealed class CompanyStaffingDebugOverlay : MonoBehaviour
    {
#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
            if (FindFirstObjectByType<CompanyStaffingDebugOverlay>() != null) return;
            var go = new GameObject("~CompanyStaffingDebugOverlay");
            go.AddComponent<CompanyStaffingDebugOverlay>();
            DontDestroyOnLoad(go);
        }
#endif

        const float RescanInterval = 2f;
        const long GoldPerClick = 100000;

        CityBootstrap _bootstrap;
        MainCityView _view;
        readonly List<Vector2Int> _companies = new List<Vector2Int>();
        int _gridW = 200;
        int _gridH = 200;
        float _nextScanAt;
        bool _visible = true;

        // 실측 시간 속도 — 캘린더가 실제로 흐르는지 눈으로 확인하는 용도
        double _lastCalendarDays = -1.0;
        float _measuredGameMinPerRealSec;

        GUIStyle _labelStyle;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f3Key.wasPressedThisFrame) _visible = !_visible;

            if (_bootstrap == null)
            {
                _bootstrap = FindFirstObjectByType<CityBootstrap>();
                if (_bootstrap != null) ReadGridSize();
            }
            if (_view == null) _view = FindFirstObjectByType<MainCityView>();

            var services = _bootstrap != null ? _bootstrap.Services : null;
            MeasureClock(services);

            if (services?.TileData != null && Time.unscaledTime >= _nextScanAt)
            {
                _nextScanAt = Time.unscaledTime + RescanInterval;
                RescanCompanies(services.TileData);
            }
        }

        void ReadGridSize()
        {
            // 그리드 크기는 공개 계약에 없다 — 디버그 전용이라 부트스트랩의 config 사본을 리플렉션으로 읽는다.
            var field = typeof(CityBootstrap).GetField(
                "engineConfig", BindingFlags.NonPublic | BindingFlags.Instance);
            object cfg = field?.GetValue(_bootstrap);
            if (cfg == null) return;
            _gridW = (int)cfg.GetType().GetField("GridWidth").GetValue(cfg);
            _gridH = (int)cfg.GetType().GetField("GridHeight").GetValue(cfg);
        }

        void MeasureClock(CityFlowServices services)
        {
            IGameCalendarService cal = services?.GameCalendar;
            if (cal == null) { _lastCalendarDays = -1.0; return; }

            double now = cal.TotalDays + cal.TimeOfDay01;
            if (_lastCalendarDays >= 0.0 && Time.unscaledDeltaTime > 0f)
            {
                float minPerSec = (float)((now - _lastCalendarDays) * 24.0 * 60.0 / Time.unscaledDeltaTime);
                _measuredGameMinPerRealSec = Mathf.Lerp(_measuredGameMinPerRealSec, minPerSec, 0.1f);
            }
            _lastCalendarDays = now;
        }

        void RescanCompanies(IReadOnlyTileData tiles)
        {
            _companies.Clear();
            for (int x = 0; x < _gridW; x++)
                for (int y = 0; y < _gridH; y++)
                {
                    var t = new Vector2Int(x, y);
                    if (tiles.GetTileType(t) == TileType.Office && tiles.IsFootprintAnchor(t))
                        _companies.Add(t);
                }
        }

        void OnGUI()
        {
            if (!_visible) return;
            var services = _bootstrap != null ? _bootstrap.Services : null;
            if (services == null) return;

            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white },
                };
            }

            DrawClockPanel(services);
            DrawCompanyLabels(services);
        }

        void DrawClockPanel(CityFlowServices services)
        {
            IGameCalendarService cal = services.GameCalendar;
            // 왼쪽 중앙 — 게임 자체 HUD(좌상단)와 겹치지 않게
            // 130 = 시계·배속, +44 = 통합해 온 지표 줄과 치트 버튼 줄
            float height = 174f + Mathf.Min(_companies.Count, 8) * 20f;
            GUILayout.BeginArea(
                new Rect(10, Screen.height * 0.5f - height * 0.5f, 340, height),
                GUI.skin.box);
            if (cal == null)
            {
                GUI.color = Color.red;
                GUILayout.Label("GameCalendar 서비스 없음 — 시간이 흐르지 않는 원인!");
                GUI.color = Color.white;
            }
            else
            {
                float hour = cal.TimeOfDay01 * cal.HoursPerDay;
                int hh = Mathf.FloorToInt(hour);
                int mm = Mathf.FloorToInt((hour - hh) * 60f);
                GUILayout.Label($"Day {cal.TotalDays}  {hh:00}:{mm:00}   (하루 {cal.RealSecondsPerGameDay:0}초, timeScale x{Time.timeScale:0.#})");

                bool frozen = _measuredGameMinPerRealSec < 0.01f;
                GUI.color = frozen ? Color.red : Color.green;
                GUILayout.Label(frozen
                    ? "실측: 0.0 게임분/실초 — 시간 정지 상태!"
                    : $"실측: {_measuredGameMinPerRealSec:0.0} 게임분/실초 (1시간 ≈ {60f / Mathf.Max(0.01f, _measuredGameMinPerRealSec):0}실초)");
                GUI.color = Color.white;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("배속:", GUILayout.Width(36));
            if (GUILayout.Button("x1")) Time.timeScale = 1f;
            if (GUILayout.Button("x5")) Time.timeScale = 5f;
            if (GUILayout.Button("x20")) Time.timeScale = 20f;
            GUILayout.EndHorizontal();

            // ── 구 DebugCityControls 통합 (그쪽은 삭제됨) ──
            // 창 두 개가 배속·시계를 각자 그리며 겹쳤다. 겹치던 것은 위 시계 패널로
            // 일원화하고, 저쪽에만 있던 지표와 치트를 여기로 옮긴 뒤 원본을 지웠다.
            // 남겨두면 같은 치트 버튼이 두 벌 뜨고 금액도 서로 갈라진다.
            var facility = services.Placement as IIntersectionFacilityService;
            GUILayout.Label(
                $"코인 {services.Economy?.Coins ?? 0:N0}" +
                $" · 차량 {services.Stats?.ActiveVehicleCount ?? 0}" +
                $" · 신호 {facility?.SignalTiles.Count ?? 0}개");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"골드 +{GoldPerClick:N0}"))
            {
                services.Economy?.AddCoins(GoldPerClick, "debug");
            }
            if (GUILayout.Button("장치 초기화"))
            {
                ClearAllDevices(services);
            }
            GUILayout.EndHorizontal();

            GUILayout.Label($"회사 {_companies.Count}곳 · F3 토글");

            // 채용 현황 상시 목록 — 월드 라벨이 안 보여도 여기서 항상 파악 가능
            IReadOnlyCityStats stats = services.Stats;
            IReadOnlyTileData tiles = services.TileData;
            if (stats != null && tiles != null)
            {
                int shown = 0;
                foreach (Vector2Int tile in _companies)
                {
                    if (shown >= 8) { GUILayout.Label($"… 외 {_companies.Count - shown}곳"); break; }
                    string line = CompanyStatusText(tile, stats, tiles, out Color color);
                    if (line == null) continue;
                    GUI.color = color;
                    GUILayout.Label($"({tile.x},{tile.y})  {line}");
                    GUI.color = Color.white;
                    shown++;
                }
            }
            GUILayout.EndArea();
        }

        // 신호·로터리·입체교차·우선도로·일방통행·회전표지를 한 번에 비운다.
        static void ClearAllDevices(CityFlowServices services)
        {
            var facility = services.Placement as IIntersectionFacilityService;
            var rule = services.Placement as ITrafficRuleService;
            if (facility == null || rule == null) return;

            foreach (var t in new List<Vector2Int>(facility.SignalTiles)) facility.TryRemoveSignal(t);
            foreach (var t in new List<Vector2Int>(facility.RoundaboutTiles)) facility.TryRemoveRoundabout(t);
            foreach (var t in new List<Vector2Int>(facility.OverpassTiles)) facility.TryRemoveOverpass(t);
            foreach (var t in new List<Vector2Int>(facility.PriorityRoadTiles)) facility.TryRemovePriorityRoad(t);
            foreach (var t in new List<Vector2Int>(rule.OnewayTiles)) rule.TryRemoveOneway(t);
            foreach (var t in new List<Vector2Int>(rule.TurnSignTiles)) rule.TryRemoveTurnSign(t);
        }

        static string CompanyStatusText(
            Vector2Int tile, IReadOnlyCityStats stats, IReadOnlyTileData tiles, out Color color)
        {
            if (tiles.TryGetConstructionProgress01(tile, out float progress01))
            {
                color = Color.cyan;
                return $"공사 {progress01:P0}";
            }
            if (stats.TryGetCompanyStaffing(tile, out CompanyStaffing st))
            {
                // 채용(좌석 배정)과 실제 차는 다르다 — 도달 불가 짝은 좌석만 먹고 차가 없다.
                // 채용 == 차면 초록, 차가 모자라면 빨강(도로 연결을 의심하라는 신호).
                int cars = 0;
                foreach (CommuterHomeCount h in stats.GetCompanyCommuterHomes(tile))
                    cars += h.Count;
                color = cars >= st.Capacity ? Color.green
                    : cars < st.Filled ? new Color(1f, 0.45f, 0.35f)
                    : Color.yellow;
                return $"채용 {st.Filled}/{st.Capacity} · 차 {cars}";
            }
            color = Color.white;
            return null;
        }

        void DrawCompanyLabels(CityFlowServices services)
        {
            Camera cam = Camera.main;
            IReadOnlyCityStats stats = services.Stats;
            IReadOnlyTileData tiles = services.TileData;
            if (stats == null || tiles == null) return;

            if (cam == null || _view == null) return;   // 패널 목록이 폴백을 겸한다
            float tileSize = _view.TileSize;

            foreach (Vector2Int tile in _companies)
            {
                string text = CompanyStatusText(tile, stats, tiles, out Color color);
                if (text == null) continue;

                Vector3 world = _view.transform.TransformPoint(new Vector3(
                    (tile.x + 0.5f) * tileSize, (tile.y + 0.5f) * tileSize, 0f));
                Vector3 sp = cam.WorldToScreenPoint(world);
                if (sp.z <= 0f) continue;

                GUI.color = color;
                var rect = new Rect(sp.x - 45f, Screen.height - sp.y - 34f, 90f, 22f);
                GUI.Box(rect, text, _labelStyle);
                GUI.color = Color.white;
            }
        }
    }
}
