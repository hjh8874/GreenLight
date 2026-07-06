using System;
using System.Collections.Generic;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim
{
    // 통근 수요 1건: 수요원(집) → 수요처(Office 등).
    // ponytail: demandRate는 나중(FlowSolver)에서 SimConfig로. 지금은 배정 관계만.
    internal struct Demand
    {
        public Vector2Int Source;
        public Vector2Int Sink;
    }

    // 집(House)을 가장 가까운 수요처에 배정. 맨해튼 최근접 + 용량 캡 + 차순위. topology 변경 시에만.
    // 확장: 수요처 종류 추가 = SinkTypes 배열 + CapacityFor + SimConfig 용량 한 줄. 로직 불변.
    internal sealed class DemandMap
    {
        // 수요처 종류 목록. 지금은 Office 하나 — School 등 추가하면 다목적지로 자동 확장.
        static readonly TileType[] SinkTypes = { TileType.Office };

        readonly SimConfig _config;

        // 선할당 재사용 버퍼(재배정은 드물지만 습관).
        readonly List<Vector2Int> _houses = new(64);
        readonly List<Vector2Int> _sinks = new(16);
        readonly List<Demand> _demands = new(128);

        public IReadOnlyList<Demand> Demands => _demands;

        public DemandMap(SimConfig config)
        {
            _config = config;
        }

        public void Reassign(CityGrid grid)
        {
            _demands.Clear();
            _houses.Clear();
            Collect(grid, TileType.House, _houses);

            // 다목적지: 집마다 각 수요처 종류로 1건씩.
            foreach (var sinkType in SinkTypes)
            {
                _sinks.Clear();
                Collect(grid, sinkType, _sinks);
                AssignType(_houses, _sinks, CapacityFor(sinkType));
            }
        }

        // flat 순서(y, x)로 특정 종류 타일 수집 → 배정·tie-break가 결정론적.
        static void Collect(CityGrid grid, TileType type, List<Vector2Int> into)
        {
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var v = new Vector2Int(x, y);
                    if (grid.GetTile(v) == type) into.Add(v);
                }
            }
        }

        int CapacityFor(TileType sinkType) => sinkType switch
        {
            TileType.Office => _config.OfficeCapacity,
            _ => 0,
        };

        // 각 집을 '남은 용량이 있는 가장 가까운' sink에 배정. 꽉 차면 다음 가까운 곳(차순위).
        void AssignType(List<Vector2Int> sources, List<Vector2Int> sinks, int capPerSink)
        {
            if (sinks.Count == 0) return;

            var remaining = new int[sinks.Count]; // ponytail: 재배정 드물어 지역 할당 OK
            for (int i = 0; i < sinks.Count; i++) remaining[i] = capPerSink;

            for (int h = 0; h < sources.Count; h++)
            {
                var house = sources[h];
                int best = -1, bestDist = int.MaxValue;
                for (int i = 0; i < sinks.Count; i++)
                {
                    if (remaining[i] <= 0) continue;
                    int d = Manhattan(house, sinks[i]);
                    if (d < bestDist) { bestDist = d; best = i; } // strict < → 동점 시 낮은 인덱스 유지
                }
                if (best < 0) continue; // 모든 sink 만석 → 이 집은 이 종류 수요 없음

                remaining[best]--;
                _demands.Add(new Demand { Source = house, Sink = sinks[best] });
            }
        }

        static int Manhattan(Vector2Int a, Vector2Int b) =>
            Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);
    }
}
