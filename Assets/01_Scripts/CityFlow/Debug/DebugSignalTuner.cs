using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Sim;
using UnityEngine;
using UnityEngine.InputSystem;   // 프로젝트가 new Input System 사용

namespace CityFlow.DebugTools
{
    // QA용: 신호 오프셋을 런타임에 돌려 "보는 것 = 버는 것"을 검증. 화면에 안정도 + 선택 신호 표시.
    //  Tab=다음 신호 선택, ,/.=선택 신호 오프셋 -/+, R=전체 오프셋 0.
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

            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.tabKey.wasPressedThisFrame)
                _sel = (_sel + 1) % tiles.Count;                   // 다음 신호

            var t = tiles[_sel];
            int off = _engine.GetSignalOffsetSlots(t);
            if (kb.commaKey.wasPressedThisFrame)  _engine.TrySetSignalOffsetSlots(t, off - 1);
            if (kb.periodKey.wasPressedThisFrame) _engine.TrySetSignalOffsetSlots(t, off + 1);
            if (kb.rKey.wasPressedThisFrame)
                foreach (var s in tiles) _engine.TrySetSignalOffsetSlots(s, 0);
        }

        private void OnGUI()
        {
            if (_engine == null || _data == null) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 20, normal = { textColor = Color.white } };

            GUI.Label(new Rect(12, 10, 700, 30), $"처리량(안정도): {_data.Stability01:P0}", style);

            var tiles = _engine.SignalTiles;
            if (tiles.Count > 0)
            {
                if (_sel >= tiles.Count) _sel = 0;
                var t = tiles[_sel];
                GUI.Label(new Rect(12, 40, 700, 30),
                    $"선택 신호 [{_sel + 1}/{tiles.Count}] {t}  오프셋 {_engine.GetSignalOffsetSlots(t)}슬롯", style);
            }
            GUI.Label(new Rect(12, 70, 700, 30), "Tab=다음신호   ,/. = 오프셋 -/+   R=전체리셋", style);
        }
    }
}
