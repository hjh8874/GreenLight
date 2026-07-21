using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Sim;
using UnityEngine;
using UnityEngine.InputSystem;   // 프로젝트가 new Input System 사용

namespace CityFlow.DebugTools
{
    // QA용: 신호 두 레버(오프셋·초록 길이)를 런타임에 돌려 "보는 것 = 버는 것"을 검증. 화면에 처리량·안정도·선택 신호 표시.
    //  마우스 좌클릭 또는 Tab=신호 선택, ,/.=오프셋 -/+, [/]=초록 길이 -/+, R=전체 오프셋 0.
    // 두 레버 모두 ISignalControl 계약 메서드(SimEngine 구현)로 조작 — 김건 Game뷰 UI가 붙을 창구와 동일.
    // ponytail: 김건 Game뷰 UI 나오면 폐기 — 디버그 전용 임시 도구.
    public sealed class DebugSignalTuner : MonoBehaviour, ICityFlowServiceConsumer
    {
        private SimEngine _engine;                 // 진짜 엔진일 때만 동작(Fake면 null)
        private IReadOnlyTileData _data;
        private int _sel;                          // 선택된 신호 인덱스

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            _engine = services.Placement as SimEngine;
            _data = services.TileData;
        }

        private void Update()
        {
            if (_engine == null) return;
            var tiles = _engine.SignalTiles;
            if (tiles.Count == 0) return;
            if (_sel >= tiles.Count) _sel = 0;

            // 마우스 좌클릭으로 신호 선택: 클릭 지점을 월드→타일로 환산해, 그 타일이 신호면 선택.
            // 보드는 XY 평면(z=0)에 있고 카메라만 기울어지므로(MainCityView.ApplyCameraView의
            // isIsometricView), ScreenToWorldPoint로는 지면을 맞출 수 없다 — 보드 평면과의
            // 레이 교차로 구한다. 타일 1칸 = 월드 1유닛(GridUtil.TileSize)이라 floor면 타일 좌표.
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Camera.main != null)
            {
                Ray ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
                if (new Plane(Vector3.forward, Vector3.zero).Raycast(ray, out float enter))
                {
                    Vector3 wp = ray.GetPoint(enter);
                    var clicked = new Vector2Int(
                        Mathf.FloorToInt(wp.x / GridUtil.TileSize),
                        Mathf.FloorToInt(wp.y / GridUtil.TileSize));
                    for (int i = 0; i < tiles.Count; i++)
                        if (tiles[i] == clicked) { _sel = i; break; }
                }
            }

            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.tabKey.wasPressedThisFrame)
                _sel = (_sel + 1) % tiles.Count;                   // 다음 신호(마우스 대안)

            var t = tiles[_sel];
            int off = _engine.GetSignalOffsetSlots(t);
            if (kb.commaKey.wasPressedThisFrame)  _engine.TrySetSignalOffsetSlots(t, off - 1);
            if (kb.periodKey.wasPressedThisFrame) _engine.TrySetSignalOffsetSlots(t, off + 1);

            int green = _engine.GetSignalGreenSlots(t);                              // 초록 길이 레버(듀티=유효 용량)
            if (kb.leftBracketKey.wasPressedThisFrame)  _engine.TrySetSignalGreenSlots(t, green - 1);
            if (kb.rightBracketKey.wasPressedThisFrame) _engine.TrySetSignalGreenSlots(t, green + 1);   // 엔진이 [1,주기-1]로 클램프

            // 오버라이드 스킬(기획 §2-D): 양축 강제 초록으로 체증 세척. 쿨다운은 엔진이 거절. (G/V는 코리도어 걷기 방향만 다름)
            if (kb.gKey.wasPressedThisFrame) _engine.TryOverrideSignal(t, horizontal: true);
            if (kb.vKey.wasPressedThisFrame) _engine.TryOverrideSignal(t, horizontal: false);

            if (kb.rKey.wasPressedThisFrame)
                foreach (var s in tiles) _engine.TrySetSignalOffsetSlots(s, 0);
        }

        private void OnGUI()
        {
            if (_engine == null || _data == null) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 20, normal = { textColor = Color.white } };

            GUI.Label(new Rect(12, 10, 700, 30), $"처리량: {_engine.DeliveredTotal:F2} 대/초   안정도: {_data.Stability01:P0}", style);

            var tiles = _engine.SignalTiles;
            if (tiles.Count > 0)
            {
                if (_sel >= tiles.Count) _sel = 0;
                var t = tiles[_sel];
                float ovr = _engine.GetOverrideSecondsLeft(t);
                float cd = _engine.GetOverrideCooldownLeft(t);
                string ovrText = ovr > 0f ? $"  ⚡오버라이드 {ovr:F0}s" : cd > 0f ? $"  쿨다운 {cd:F0}s" : "";
                GUI.Label(new Rect(12, 40, 900, 30),
                    $"선택 신호 [{_sel + 1}/{tiles.Count}] {t}  오프셋 {_engine.GetSignalOffsetSlots(t)}슬롯  초록 {_engine.GetSignalGreenSlots(t)}슬롯{ovrText}", style);
            }
            GUI.Label(new Rect(12, 70, 900, 30), "좌클릭/Tab=신호선택   ,/. = 오프셋   [/] = 초록   G/V=오버라이드(가로/세로)   R=리셋", style);
        }
    }
}
