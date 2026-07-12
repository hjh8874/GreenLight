using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Sim;
using UnityEngine;
using UnityEngine.InputSystem;   // 프로젝트가 new Input System 사용

namespace CityFlow.DebugTools
{
    // EngineSandbox_hwan 전용: 교차로 4형제(신호·로터리·입체·무신호)를 손으로 배치/철거하며 관찰하는 도구.
    // AutoDetectSignals=false(SimConfig_Sandbox)에서만 배치가 먹는다 — 라이브 씬은 자동감지라 대상이 아님.
    // 서비스 접근·OnGUI 스타일은 DebugSignalTuner.cs를 그대로 따름(팀 Debug 폴더 관례).
    // ponytail: 배치 UI(상점)가 붙으면 폐기 — 디버그 전용 임시 도구.
    public sealed class SandboxPlacementControls : MonoBehaviour, ICityFlowServiceConsumer
    {
        private const float TileSize = GridUtil.TileSize;

        private IReadOnlyTileData _data;
        private ISignalControl _signals;
        private SimEngine _engine;           // DeliveredTotal 조회용(DebugSignalTuner와 동일 패턴)
        private bool _ready;
        private string _lastResult = "대기 중";

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            _data = services?.TileData;
            _signals = services?.Placement as ISignalControl;
            _engine = services?.Placement as SimEngine;

            if (_data == null || _signals == null)
            {
                Debug.LogWarning("[SandboxPlacementControls] ISignalControl/TileData 없음 — 배치 컨트롤 비활성.");
                _ready = false;
                return;
            }

            _ready = true;
        }

        private void Update()
        {
            if (!_ready)
            {
                return;
            }

            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }

            if (!TryGetHoverTile(out Vector2Int hover))
            {
                return;
            }

            if (kb.digit1Key.wasPressedThisFrame)
            {
                _lastResult = TryPlace("신호", hover, () => _signals.TryPlaceSignal(hover, 8));
            }
            else if (kb.digit2Key.wasPressedThisFrame)
            {
                _lastResult = TryPlace("로터리", hover, () => _signals.TryPlaceRoundabout(hover));
            }
            else if (kb.digit3Key.wasPressedThisFrame)
            {
                _lastResult = TryPlace("입체교차", hover, () => _signals.TryPlaceOverpass(hover));
            }
            else if (kb.digit0Key.wasPressedThisFrame)
            {
                _lastResult = TryRemoveAny(hover);
            }
        }

        // 배치 성공/거부 사유(교차로 아님/이미 있음)를 사람이 읽을 문구로. AutoDetectSignals 자체는
        // ISignalControl 계약에 없어 조회 불가 — 이 샌드박스는 항상 false로 고정하므로 구분 생략(YAGNI).
        private string TryPlace(string label, Vector2Int tile, System.Func<bool> place)
        {
            if (HasAnyDevice(tile))
            {
                return $"{label} 배치 거부 {tile} — 이미 있음";
            }

            if (_data.GetTileType(tile) != TileType.Road)
            {
                return $"{label} 배치 거부 {tile} — 도로 아님";
            }

            return place()
                ? $"{label} 배치 성공 {tile}"
                : $"{label} 배치 거부 {tile} — 교차로 아님/자동 모드";
        }

        private string TryRemoveAny(Vector2Int tile)
        {
            if (_signals.TryRemoveSignal(tile))
            {
                return $"신호 철거 성공 {tile}";
            }

            if (_signals.TryRemoveRoundabout(tile))
            {
                return $"로터리 철거 성공 {tile}";
            }

            if (_signals.TryRemoveOverpass(tile))
            {
                return $"입체교차 철거 성공 {tile}";
            }

            return $"철거 거부 {tile} — 장치 없음";
        }

        private bool HasAnyDevice(Vector2Int tile)
        {
            return Contains(_signals.SignalTiles, tile)
                || Contains(_signals.RoundaboutTiles, tile)
                || Contains(_signals.OverpassTiles, tile);
        }

        private static bool Contains(System.Collections.Generic.IReadOnlyList<Vector2Int> tiles, Vector2Int tile)
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                if (tiles[i] == tile)
                {
                    return true;
                }
            }

            return false;
        }

        // MainCityView.WorldToGrid와 같은 산술: 화면→월드→floor(월드/타일크기). 씬은 XY 평면.
        private bool TryGetHoverTile(out Vector2Int tile)
        {
            tile = default;
            Mouse mouse = Mouse.current;
            if (mouse == null || Camera.main == null)
            {
                return false;
            }

            Vector3 world = Camera.main.ScreenToWorldPoint(mouse.position.ReadValue());
            tile = new Vector2Int(Mathf.FloorToInt(world.x / TileSize), Mathf.FloorToInt(world.y / TileSize));
            return true;
        }

        private void OnGUI()
        {
            if (!_ready)
            {
                return;
            }

            var style = new GUIStyle(GUI.skin.label) { fontSize = 20, normal = { textColor = Color.white } };

            GUI.Label(new Rect(12, 100, 900, 30),
                "1=신호  2=로터리  3=입체교차  0=철거(신호→로터리→입체 순)", style);

            if (TryGetHoverTile(out Vector2Int hover))
            {
                TileType type = _data.GetTileType(hover);
                CongestionLevel congestion = _data.GetCongestion(hover);
                string device = Contains(_signals.SignalTiles, hover) ? "신호"
                    : Contains(_signals.RoundaboutTiles, hover) ? "로터리"
                    : Contains(_signals.OverpassTiles, hover) ? "입체교차"
                    : "없음";

                GUI.Label(new Rect(12, 130, 900, 30),
                    $"호버 {hover}  타일 {type}  혼잡 {congestion}  장치 {device}", style);
            }
            else
            {
                GUI.Label(new Rect(12, 130, 900, 30), "호버 없음(마우스/카메라 확인)", style);
            }

            GUI.Label(new Rect(12, 160, 900, 30), $"결과: {_lastResult}", style);

            if (_engine != null)
            {
                GUI.Label(new Rect(12, 190, 900, 30), $"처리량: {_engine.DeliveredTotal:F2} 대/초", style);
            }
        }
    }
}
