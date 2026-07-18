using System;
using System.Collections.Generic;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim
{
    // 통근 수요 1건: 수요원(집) → 수요처(Office 등).
    // 수요는 차 토큰 생성의 배정 관계만 보관한다.
    // SourceRoad/SinkRoad: 배정 시 채택한 접점(감사 픽스 2 — 단일 출처화). RoutePlanner.Plan은
    // 이 값을 그대로 써서 경로를 잇는다 — 접점을 다시 계산하지 않음(두 시스템이 각자 접점을
    // 고르면 Region 불일치로 도달 가능한 수요가 흐름 0으로 죽는 버그의 온상이었음).
    // 프론티지가 전혀 없으면(맹지) DemandMap.NoRoad 센티널 — RoutePlanner의 IsRoad 경계 체크가
    // 자연히 걸러 null 경로(무사고)로 처리.
    internal struct Demand
    {
        public Vector2Int Source;
        public Vector2Int Sink;
        public Vector2Int SourceRoad;
        public Vector2Int SinkRoad;
    }

    // 집(House)을 가장 가까운 수요처에 배정. 맨해튼 최근접 + 용량 캡 + 차순위. topology 변경 시에만.
    // 도달성: 같은 도로 섬(RoadNetwork.RegionOf)의 수요처를 우선 — 길이 끊긴 섬에 수요를 주고
    // 흐름이 증발하는 것 방지. 도달 가능한 곳이 없으면 기존 최근접 폴백(흐름 0 = 안정도
    // 페널티 유지 → 유저에게 "길 고쳐라" 신호).
    // 확장: 수요처 종류 추가 = SinkTypes 배열 + CapacityFor + SimConfig 용량 한 줄. 로직 불변.
    internal sealed class DemandMap
    {
        // 수요처 종류 목록. 종류 추가 = 여기 한 줄 + CapacityFor + SimConfig 용량.
        static readonly TileType[] SinkTypes = { TileType.Office, TileType.School };

        SimConfig _config;   // seam(SimEngine.ApplyConfig, 스펙 2026-07-12)으로 갈아 끼워짐 — readonly 제거

        // 프론티지 전혀 없음(맹지) 센티널 — 항상 그리드 밖(x,y<0)이라 IsRoad 경계 체크가 자연히 걸러냄.
        static readonly Vector2Int NoRoad = new Vector2Int(-1, -1);

        // 선할당 재사용 버퍼(재배정은 드물지만 습관).
        readonly List<Vector2Int> _houses = new(64);
        readonly List<Vector2Int> _sinks = new(16);
        readonly List<Demand> _demands = new(128);
        // 홈타일+sink종류 → 배정 sink. sink 철거/도로 단절 때만 해제해 차량 순간이동을 막는다.
        readonly Dictionary<(Vector2Int home, TileType sink), Vector2Int> _sticky = new(128);
        readonly List<Vector2Int> _houseFrontageBuffer = new(8);   // 집 프론티지 전수 스캔용 재사용 버퍼

        public IReadOnlyList<Demand> Demands => _demands;

        public DemandMap(SimConfig config)
        {
            _config = config;
        }

        // SimEngine.ApplyConfig의 유일한 전파 지점(스펙 2026-07-12): 용량(CapacityFor)·
        // 배정 다양성(DemandChoicePool) 등 이 클래스가 들고 있는 config 사본을 갱신.
        // 실제 재배정은 SimEngine이 _grid.MarkTopologyDirty()로 다음 틱에 강제한다.
        internal void ApplyConfig(in SimConfig next)
        {
            _config = next;
        }

        public void Reassign(CityGrid grid, RoadNetwork net)
        {
            _demands.Clear();
            _houses.Clear();
            Collect(grid, TileType.House, _houses);

            // 다목적지: 집마다 각 수요처 종류로 1건씩.
            foreach (var sinkType in SinkTypes)
            {
                _sinks.Clear();
                Collect(grid, sinkType, _sinks);
                AssignType(_houses, _sinks, sinkType, CapacityFor(sinkType), net);
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
            TileType.School => _config.SchoolCapacity,
            _ => 0,
        };

        // 각 집을 '남은 용량이 있는, 도달 가능한(같은 섬)' sink 중 가까운 K곳(DemandChoicePool)
        // 하나에 배정 — 좌표 해시로 결정론적 선택(같은 도시 = 같은 배정, 세이브·테스트 안전).
        // K=1이면 항상 최근접. 도달 가능한 곳이 하나도 없으면 최근접 폴백(흐름 0).
        // 감사 픽스 2: 건물의 프론티지가 여러 개(다른 Region)일 수 있어 전수 검사 — 첫 접점만
        // 보면 실제로는 연결된 건물을 도달불가로 오판한다(막다른 스텁이 스캔 1순위일 때).
        void AssignType(List<Vector2Int> sources, List<Vector2Int> sinks, TileType sinkType, int capPerSink, RoadNetwork net)
        {
            if (sinks.Count == 0)
            {
                RemoveStickyForSinkType(sinkType);
                return;
            }

            var remaining = new int[sinks.Count]; // ponytail: 재배정 드물어 지역 할당 OK
            var sinkFrontages = new List<Vector2Int>[sinks.Count];   // 수요처 프론티지 전수(스캔 순서)
            var sinkIndices = new Dictionary<Vector2Int, int>(sinks.Count);
            for (int i = 0; i < sinks.Count; i++)
            {
                remaining[i] = capPerSink;
                sinkFrontages[i] = new List<Vector2Int>(4);
                net.CollectAccessRoads(sinks[i], sinkFrontages[i]);
                sinkIndices[sinks[i]] = i;
            }

            int pool = Mathf.Max(1, _config.DemandChoicePool);
            // 같은 Region 매칭 + 채택된 접점 쌍(RoutePlanner 단일 출처화용)까지 함께 후보에 담음.
            var candidates = new List<(int idx, int dist, Vector2Int houseRoad, Vector2Int sinkRoad)>(sinks.Count);
            PruneSticky(sources, sinkType, sinkIndices, sinkFrontages, net);
            int[] stickyIdxBySource = ReserveStickyAssignments(
                sources,
                sinkType,
                remaining,
                sinkIndices,
                sinkFrontages,
                net,
                out Vector2Int[] stickyHouseRoads,
                out Vector2Int[] stickySinkRoads);

            for (int h = 0; h < sources.Count; h++)
            {
                var house = sources[h];
                _houseFrontageBuffer.Clear();
                net.CollectAccessRoads(house, _houseFrontageBuffer);

                var key = (house, sinkType);
                int best;
                Vector2Int chosenHouseRoad, chosenSinkRoad;
                if (stickyIdxBySource[h] >= 0)
                {
                    best = stickyIdxBySource[h];
                    chosenHouseRoad = stickyHouseRoads[h];
                    chosenSinkRoad = stickySinkRoads[h];
                }
                else
                {
                    candidates.Clear();
                    int bestAny = -1, bestAnyDist = int.MaxValue;   // 섬 무관 최근접(폴백)
                    Vector2Int bestAnyHouseRoad = NoRoad, bestAnySinkRoad = NoRoad;
                    for (int i = 0; i < sinks.Count; i++)
                    {
                        if (remaining[i] <= 0) continue;
                        int d = Manhattan(house, sinks[i]);
                        if (d < bestAnyDist)   // strict < → 동점 시 낮은 인덱스 유지
                        {
                            bestAnyDist = d;
                            bestAny = i;
                            bestAnyHouseRoad = _houseFrontageBuffer.Count > 0 ? _houseFrontageBuffer[0] : NoRoad;
                            bestAnySinkRoad = sinkFrontages[i].Count > 0 ? sinkFrontages[i][0] : NoRoad;
                        }
                        if (TryFirstRegionMatch(net, _houseFrontageBuffer, sinkFrontages[i], out var houseRoad, out var sinkRoad))
                            candidates.Add((i, d, houseRoad, sinkRoad));
                    }

                    if (candidates.Count > 0)
                    {
                        // 거리순(동점은 flat 인덱스순) 상위 pool곳 중 집 좌표 해시로 택1.
                        candidates.Sort((a, b) => a.dist != b.dist ? a.dist - b.dist : a.idx - b.idx);
                        int span = Mathf.Min(pool, candidates.Count);
                        var picked = candidates[HashPick(house, span)];
                        best = picked.idx;
                        chosenHouseRoad = picked.houseRoad;
                        chosenSinkRoad = picked.sinkRoad;
                    }
                    else
                    {
                        best = bestAny;   // 도달 가능한 수요처 0개 → 기존 동작(배정하되 흐름 0)
                        if (best < 0) continue;   // 모든 sink 만석 → 이 집은 이 종류 수요 없음
                        chosenHouseRoad = bestAnyHouseRoad;
                        chosenSinkRoad = bestAnySinkRoad;
                    }

                    remaining[best]--;
                }

                _sticky[key] = sinks[best];
                _demands.Add(new Demand
                {
                    Source = house, Sink = sinks[best],
                    SourceRoad = chosenHouseRoad, SinkRoad = chosenSinkRoad,
                });
            }
        }

        void RemoveStickyForSinkType(TileType sinkType)
        {
            var dead = new List<(Vector2Int home, TileType sink)>();
            foreach (var pair in _sticky)
            {
                if (pair.Key.sink == sinkType)
                {
                    dead.Add(pair.Key);
                }
            }

            for (int i = 0; i < dead.Count; i++)
            {
                _sticky.Remove(dead[i]);
            }
        }

        int[] ReserveStickyAssignments(List<Vector2Int> sources, TileType sinkType, int[] remaining,
            Dictionary<Vector2Int, int> sinkIndices, List<Vector2Int>[] sinkFrontages, RoadNetwork net,
            out Vector2Int[] houseRoads, out Vector2Int[] sinkRoads)
        {
            var stickyIdxBySource = new int[sources.Count];
            houseRoads = new Vector2Int[sources.Count];
            sinkRoads = new Vector2Int[sources.Count];

            for (int h = 0; h < sources.Count; h++)
            {
                stickyIdxBySource[h] = -1;
                var house = sources[h];
                var key = (house, sinkType);
                if (!_sticky.TryGetValue(key, out var stickySink)
                    || !sinkIndices.TryGetValue(stickySink, out int stickyIdx)
                    || remaining[stickyIdx] <= 0)
                {
                    continue;
                }

                _houseFrontageBuffer.Clear();
                net.CollectAccessRoads(house, _houseFrontageBuffer);
                if (!TryFirstRegionMatch(net, _houseFrontageBuffer, sinkFrontages[stickyIdx], out var houseRoad, out var sinkRoad))
                {
                    continue;
                }

                stickyIdxBySource[h] = stickyIdx;
                houseRoads[h] = houseRoad;
                sinkRoads[h] = sinkRoad;
                remaining[stickyIdx]--;
            }

            return stickyIdxBySource;
        }

        void PruneSticky(List<Vector2Int> sources, TileType sinkType, Dictionary<Vector2Int, int> sinkIndices,
            List<Vector2Int>[] sinkFrontages, RoadNetwork net)
        {
            var liveSources = new HashSet<Vector2Int>(sources);
            var dead = new List<(Vector2Int home, TileType sink)>();

            foreach (var pair in _sticky)
            {
                if (pair.Key.sink != sinkType)
                {
                    continue;
                }

                if (!liveSources.Contains(pair.Key.home)
                    || !sinkIndices.TryGetValue(pair.Value, out int sinkIndex))
                {
                    dead.Add(pair.Key);
                    continue;
                }

                _houseFrontageBuffer.Clear();
                net.CollectAccessRoads(pair.Key.home, _houseFrontageBuffer);
                if (!TryFirstRegionMatch(net, _houseFrontageBuffer, sinkFrontages[sinkIndex], out _, out _))
                {
                    dead.Add(pair.Key);
                }
            }

            for (int i = 0; i < dead.Count; i++)
            {
                _sticky.Remove(dead[i]);
            }
        }

        // 집 프론티지 순서 우선 → 그 다음 수요처 프론티지 순서로, 같은 Region인 첫 쌍을 접점으로
        // 채택(결정론). 매칭 없으면 false(호출자가 폴백 처리).
        static bool TryFirstRegionMatch(RoadNetwork net, List<Vector2Int> houseFrontages,
            List<Vector2Int> sinkFrontages, out Vector2Int houseRoad, out Vector2Int sinkRoad)
        {
            for (int hi = 0; hi < houseFrontages.Count; hi++)
            {
                int hRegion = net.RegionOf(houseFrontages[hi]);
                if (hRegion < 0) continue;
                for (int si = 0; si < sinkFrontages.Count; si++)
                {
                    if (net.RegionOf(sinkFrontages[si]) != hRegion) continue;
                    houseRoad = houseFrontages[hi];
                    sinkRoad = sinkFrontages[si];
                    return true;
                }
            }
            houseRoad = default;
            sinkRoad = default;
            return false;
        }

        // 좌표 기반 결정론 난수(SampleDailyVisits와 같은 프라임 해시) — 프레임·순서 무관.
        static int HashPick(Vector2Int house, int count) =>
            count <= 1 ? 0 : (((house.x * 73856093) ^ (house.y * 19349663)) & int.MaxValue) % count;

        static int Manhattan(Vector2Int a, Vector2Int b) =>
            Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);
    }
}
