using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim
{
    // 타일 혼잡 레벨이 '바뀐 순간'만 CongestionEvent로 알림 — 매 틱 전 타일 스팸 금지.
    // 뷰(도로 색칠)가 폴링 대신 구독으로 반응할 수 있게 하는 diff 스캐너.
    internal sealed class CongestionNotifier
    {
        readonly int _w;
        readonly CongestionLevel[] _prev;   // 지난 틱 레벨. 기본값 Free = 도로 초기 상태와 일치

        public CongestionNotifier(int width, int height)
        {
            _w = width;
            _prev = new CongestionLevel[width * height];
        }

        public void Scan(FlowSolver solver, SimEventBuffer events, in SimConfig cfg)
        {
            for (int i = 0; i < _prev.Length; i++)
            {
                var level = solver.GetCongestion(new Vector2Int(i % _w, i / _w));
                if (level == _prev[i]) continue;
                _prev[i] = level;
                events.QueueCongestion(new CongestionEvent(new Vector2Int(i % _w, i / _w), level));
            }
        }
    }
}
