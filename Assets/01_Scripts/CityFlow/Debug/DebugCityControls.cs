using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Sim;
using UnityEngine;

namespace CityFlow.DebugTools
{
    // 디버그 씬 전용 종합 계기판: 통합 상태 HUD + 골드 치트 + 배속 컨트롤 + 전체 장치 초기화.
    // OnGUI 버튼(화면 우측)이라 키 충돌 없음(DebugSignalTuner=좌상단, SandboxPlacementControls=좌하단).
    // 세이브는 DebugDisableSaving가 격리하므로 골드 치트가 라이브 슬롯을 오염시키지 않음.
    // ponytail: 디버그 전용 — 라이브(출시) 씬엔 절대 붙이지 말 것.
    public sealed class DebugCityControls : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private long goldPerClick = 100000;
        private static readonly float[] Speeds = { 0.25f, 0.5f, 1f, 2f, 4f, 8f };

        private CityFlowServices _services;
        private SimEngine _engine;
        private IIntersectionFacilityService _facility;
        private ITrafficRuleService _rule;

        public void Initialize(CityFlowServices services)
        {
            _services = services;
            _engine = services?.Placement as SimEngine;
            _facility = services?.Placement as IIntersectionFacilityService;
            _rule = services?.Placement as ITrafficRuleService;
        }

        private void OnDisable()
        {
            // 씬을 나갈 때 배속을 되돌려 다른 씬에 새지 않게.
            Time.timeScale = 1f;
        }

        private void AddGold()
        {
            _services?.Economy?.AddCoins(goldPerClick, "debug");
        }

        private void StepSpeed(int dir)
        {
            int idx = System.Array.IndexOf(Speeds, Time.timeScale);
            if (idx < 0) idx = 2;                       // 목록 밖이면 x1 기준
            idx = Mathf.Clamp(idx + dir, 0, Speeds.Length - 1);
            Time.timeScale = Speeds[idx];
        }

        private void ClearAllDevices()
        {
            if (_facility == null || _rule == null) return;
            foreach (var t in new List<Vector2Int>(_facility.SignalTiles)) _facility.TryRemoveSignal(t);
            foreach (var t in new List<Vector2Int>(_facility.RoundaboutTiles)) _facility.TryRemoveRoundabout(t);
            foreach (var t in new List<Vector2Int>(_facility.OverpassTiles)) _facility.TryRemoveOverpass(t);
            foreach (var t in new List<Vector2Int>(_facility.PriorityRoadTiles)) _facility.TryRemovePriorityRoad(t);
            foreach (var t in new List<Vector2Int>(_rule.OnewayTiles)) _rule.TryRemoveOneway(t);
            foreach (var t in new List<Vector2Int>(_rule.TurnSignTiles)) _rule.TryRemoveTurnSign(t);
        }

        private void OnGUI()
        {
            if (_services == null) return;

            const float w = 300f;
            float x = Screen.width - w - 12f;
            var label = new GUIStyle(GUI.skin.label) { fontSize = 18, normal = { textColor = Color.white } };
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 16 };

            // ── 통합 상태 HUD ──
            long coins = _services.Economy?.Coins ?? 0;
            var cal = _services.GameCalendar;
            int vehicles = _services.Stats?.ActiveVehicleCount ?? 0;
            float delivered = _engine != null ? _engine.DeliveredTotal : 0f;
            float stability = _services.TileData?.Stability01 ?? 0f;
            int signals = _facility?.SignalTiles.Count ?? 0;
            string calText = cal != null ? $"Y{cal.Year} M{cal.Month} D{cal.Day} {cal.Hour:00}시" : "-";

            GUI.Label(new Rect(x, 10, w, 26), $"💰 코인 {coins:N0}", label);
            GUI.Label(new Rect(x, 34, w, 26), $"📅 {calText}", label);
            GUI.Label(new Rect(x, 58, w, 26), $"🚗 차량 {vehicles}   처리량 {delivered:F2}/s", label);
            GUI.Label(new Rect(x, 82, w, 26), $"📈 안정도 {stability:P0}   신호 {signals}개", label);
            GUI.Label(new Rect(x, 106, w, 26), $"⏱ 배속 x{Time.timeScale:0.##}", label);

            // ── 버튼 ──
            if (GUI.Button(new Rect(x, 136, w, 30), $"골드 +{goldPerClick:N0}", btn)) AddGold();

            float bw = (w - 8f) / 3f;
            if (GUI.Button(new Rect(x, 170, bw, 30), "배속 −", btn)) StepSpeed(-1);
            if (GUI.Button(new Rect(x + bw + 4, 170, bw, 30), "x1", btn)) Time.timeScale = 1f;
            if (GUI.Button(new Rect(x + 2 * (bw + 4), 170, bw, 30), "배속 +", btn)) StepSpeed(1);

            if (GUI.Button(new Rect(x, 204, w, 30), "장치 전체 초기화", btn)) ClearAllDevices();
        }
    }
}
