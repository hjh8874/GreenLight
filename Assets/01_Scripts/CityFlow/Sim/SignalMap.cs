using System.Collections.Generic;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim
{
    // 교차로(도로 이웃 ≥3 = T자·십자) 자동 감지 + 신호 상태 보관. DemandMap의 자매.
    // 유저는 신호를 '짓지' 않고 있는 교차로를 '조율'한다(설계 §1). topology 변경 시 Rebuild.
    // 살아남은 교차로의 오프셋(유저 조율)은 보존, 사라진 교차로의 신호는 제거.
    internal sealed class SignalMap
    {
        readonly List<Vector2Int> _tiles = new(32);                    // flat 순서 = 결정론
        readonly Dictionary<Vector2Int, Signal> _signals = new();      // 조회 전용(순회 안 함)
        readonly HashSet<Vector2Int> _alive = new();                   // Rebuild 중 생존 표시 버퍼

        public IReadOnlyList<Vector2Int> Tiles => _tiles;

        public void Rebuild(CityGrid grid)
        {
            _tiles.Clear();
            _alive.Clear();

            for (int y = 0; y < grid.Height; y++)                      // flat(y,x) 순서 고정
                for (int x = 0; x < grid.Width; x++)
                {
                    var t = new Vector2Int(x, y);
                    if (grid.GetTile(t) != TileType.Road) continue;
                    if (!grid.IsIntersection(t)) continue;             // 교차로 규칙은 CityGrid가 오너

                    _tiles.Add(t);
                    _alive.Add(t);
                    if (!_signals.ContainsKey(t)) _signals[t] = new Signal();   // 기존이면 오프셋 보존
                }

            // 더는 교차로가 아닌 신호 제거(유저 조율도 함께 소멸 — 교차로가 없어졌으니 당연)
            var dead = new List<Vector2Int>();                          // ponytail: Rebuild는 드묾, 지역 할당 OK
            foreach (var key in _signals.Keys)
                if (!_alive.Contains(key)) dead.Add(key);
            foreach (var key in dead) _signals.Remove(key);
        }

        public bool TryGet(Vector2Int tile, out Signal signal) => _signals.TryGetValue(tile, out signal);
    }
}
