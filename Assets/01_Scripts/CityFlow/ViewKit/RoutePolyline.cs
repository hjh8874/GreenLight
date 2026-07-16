using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.ViewKit
{
    // Task 5(뷰 통합)가 소비하는 베이크 입력. Tiles는 대각 브리지 적용이 끝난 표시 경로.
    public struct BakeInput
    {
        public IReadOnlyList<Vector2Int> Tiles;
        public float TileSize;
        public float LaneOffset;
        public float CornerRadiusFraction;
        public float OrbitRadius;
        public float Z;
        public Func<Vector2Int, bool> IsRoundabout;
        public Vector3? StartAnchor;
        public Vector3? EndAnchor;
        public int SamplesPerSegment;
    }

    // TileIndex = 현재 세그먼트 시작 타일 인덱스(혼잡 판정 — 구 Fold 인덱스와 동일 의미).
    // SegT = 타일 중심→다음 타일 중심 진행률 0..1 (신호 정지선 판정 — 구 progress와 동일 의미).
    // IsSpur = 주차 진입/이탈 구간(신호·혼잡 판정 제외 대상).
    public struct Sample
    {
        public Vector3 Pos;
        public Vector3 Dir;
        public int TileIndex;
        public float SegT;
        public bool IsSpur;
    }

    // 베이크 = "위상 샘플링": MainCityView.EvaluateVehiclePose(중심선 Lerp + 코너 베지어 + 차선 오프셋)와
    // 로터리 궤도 오버라이드(에지 블렌드 + TryRoundaboutOrbit)의 순수 재현을 phase 축을
    // SamplesPerSegment 간격으로 훑어 정점화하고, 누적 아크렝스 테이블로 SampleAt(이진 탐색)을 지원한다.
    // 원본(MainCityView)은 그대로 두고 시각 파리티만 목표로 한다 — 재발명 금지.
    // forward 전용(단방향) — 퇴근 방향은 뷰가 타일 목록을 뒤집어 별도 베이크한다. Dir는 항상 베이크 방향 접선.
    //
    // ponytail: 베이크는 위상 샘플링 8/seg — 시각 파리티 우선. 해상도 문제 시 코너만 적응 샘플로 승급.
    public sealed class RoutePolyline
    {
        private const float RoundaboutBlendEdge = 0.35f;

        private struct Vertex
        {
            public Vector3 Pos;
            public Vector3 Dir;
            public int Seg;      // 세그먼트 시작 타일 인덱스
            public float SegT;   // 세그먼트 내 진행률 0..1
            public bool Spur;    // 주차 스퍼 구간
        }

        private readonly IReadOnlyList<Vector2Int> _tiles;
        private readonly Vertex[] _vertices;
        private readonly float[] _cumulative;

        public float Length => _cumulative[_cumulative.Length - 1];
        public int TileCount => _tiles.Count;

        private RoutePolyline(IReadOnlyList<Vector2Int> tiles, Vertex[] vertices, float[] cumulative)
        {
            _tiles = tiles;
            _vertices = vertices;
            _cumulative = cumulative;
        }

        public static RoutePolyline Bake(in BakeInput input)
        {
            IReadOnlyList<Vector2Int> tiles = input.Tiles;
            int segmentCount = Mathf.Max(0, tiles.Count - 1);
            int samplesPerSegment = Mathf.Max(1, input.SamplesPerSegment);

            var vertices = new List<Vertex>();

            if (segmentCount <= 0)
            {
                vertices.Add(new Vertex
                {
                    Pos = TileToLocal(tiles.Count > 0 ? tiles[0] : Vector2Int.zero, input.TileSize, input.Z),
                    Dir = Vector3.right,
                    Seg = 0,
                    SegT = 0f,
                    Spur = false,
                });
            }
            else
            {
                for (int seg = 0; seg < segmentCount; seg++)
                {
                    for (int k = 0; k < samplesPerSegment; k++)
                    {
                        float segT = (float)k / samplesPerSegment;
                        PoseAt(input, tiles, segmentCount, seg + segT, out Vector3 pos, out Vector3 dir);
                        vertices.Add(new Vertex { Pos = pos, Dir = dir, Seg = seg, SegT = segT, Spur = false });
                    }
                }

                PoseAt(input, tiles, segmentCount, segmentCount, out Vector3 lastPos, out Vector3 lastDir);
                vertices.Add(new Vertex { Pos = lastPos, Dir = lastDir, Seg = segmentCount - 1, SegT = 1f, Spur = false });
            }

            if (input.StartAnchor.HasValue)
            {
                PrependAnchorSpur(input.StartAnchor.Value, vertices, samplesPerSegment);
            }

            if (input.EndAnchor.HasValue)
            {
                AppendAnchorSpur(input.EndAnchor.Value, vertices, samplesPerSegment, Mathf.Max(0, segmentCount - 1));
            }

            var cumulative = new float[vertices.Count];
            for (int i = 1; i < vertices.Count; i++)
            {
                cumulative[i] = cumulative[i - 1] + Vector3.Distance(vertices[i - 1].Pos, vertices[i].Pos);
            }

            return new RoutePolyline(tiles, vertices.ToArray(), cumulative);
        }

        public Sample SampleAt(float distance)
        {
            float clamped = Mathf.Clamp(distance, 0f, Length);

            int lo = 0;
            int hi = _cumulative.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_cumulative[mid] < clamped) lo = mid + 1;
                else hi = mid;
            }

            int upper = Mathf.Max(1, lo);
            int lower = upper - 1;

            float segLength = _cumulative[upper] - _cumulative[lower];
            float t = segLength > 1e-5f ? (clamped - _cumulative[lower]) / segLength : 0f;

            Vertex a = _vertices[lower];
            Vertex b = _vertices[upper];

            Vector3 dir = Vector3.Lerp(a.Dir, b.Dir, t);
            dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : a.Dir;

            // SegT는 Lerp, TileIndex/IsSpur는 구간 시작 정점 값을 쓴다(이산 값은 보간 불가).
            return new Sample
            {
                Pos = Vector3.Lerp(a.Pos, b.Pos, t),
                Dir = dir,
                TileIndex = a.Seg,
                SegT = Mathf.Lerp(a.SegT, b.SegT, t),
                IsSpur = a.Spur,
            };
        }

        public Vector2Int TileAt(int tileIndex) => _tiles[tileIndex];

        // MainCityView.EvaluateVehiclePose(L1646-1689) + 로터리 궤도 오버라이드(L1419-1439,
        // TryRoundaboutOrbit L1794-1817)의 순수 재현.
        private static void PoseAt(
            in BakeInput input,
            IReadOnlyList<Vector2Int> tiles,
            int segmentCount,
            float phase,
            out Vector3 pos,
            out Vector3 dir)
        {
            float folded = Mathf.Clamp(phase, 0f, segmentCount);
            int segmentIndex = Mathf.Clamp(Mathf.FloorToInt(folded), 0, segmentCount - 1);
            float segmentT = folded - segmentIndex;

            Vector3 a = TileToLocal(tiles[segmentIndex], input.TileSize, input.Z);
            Vector3 b = TileToLocal(tiles[segmentIndex + 1], input.TileSize, input.Z);
            Vector3 centerline = Vector3.Lerp(a, b, segmentT);
            Vector3 routeTangent = (b - a).normalized;
            int insideTileIndex = segmentT < 0.5f ? segmentIndex : segmentIndex + 1;

            float radiusFraction = input.CornerRadiusFraction;
            int cornerIndex = -1;
            float curveT = 0f;
            if (segmentT >= 1f - radiusFraction && segmentIndex + 2 < tiles.Count)
            {
                cornerIndex = segmentIndex + 1;
                curveT = (segmentT - (1f - radiusFraction)) / (radiusFraction * 2f);
            }
            else if (segmentT < radiusFraction && segmentIndex > 0)
            {
                cornerIndex = segmentIndex;
                curveT = 0.5f + segmentT / (radiusFraction * 2f);
            }

            if (cornerIndex >= 0
                && TryEvaluateTurnBezier(input, tiles, cornerIndex, curveT, radiusFraction, out Vector3 curvePosition, out Vector3 curveTangent))
            {
                centerline = curvePosition;
                routeTangent = curveTangent;
                insideTileIndex = cornerIndex;
            }

            Vector3 travelDir = routeTangent;
            Vector3 laneRight = new Vector3(travelDir.y, -travelDir.x, 0f);
            Vector3 position = centerline + laneRight * (input.TileSize * input.LaneOffset);

            // 로터리 경계에서는 차선 포즈를 유지하고 안쪽에서만 CCW 링 포즈로 부드럽게 전환한다.
            if (input.IsRoundabout(tiles[insideTileIndex]))
            {
                int ci = segmentT < 0.5f ? segmentIndex : segmentIndex + 1;
                if (TryRoundaboutOrbit(input, tiles, ci, folded, out Vector3 ringPos, out Vector3 ringDir))
                {
                    float arcU = Mathf.Clamp01(folded - ci + 0.5f);
                    float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(arcU / RoundaboutBlendEdge))
                                * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - arcU) / RoundaboutBlendEdge));
                    position = Vector3.Lerp(position, ringPos, blend);
                    if (ringDir.sqrMagnitude > 0.0001f)
                    {
                        Vector3 blendedDir = Vector3.Lerp(travelDir.normalized, ringDir.normalized, blend);
                        if (blendedDir.sqrMagnitude > 0.0001f)
                        {
                            travelDir = blendedDir.normalized;
                        }
                    }
                }
            }

            pos = position;
            dir = travelDir.sqrMagnitude > 0.0001f ? travelDir.normalized : Vector3.right;
        }

        // 일반 교차로 회전(90도)만 대상 — 로터리 타일은 전용 궤도 연출을 쓰므로 여기서 제외한다.
        private static bool TryGetTurnDirections(
            IReadOnlyList<Vector2Int> tiles,
            Func<Vector2Int, bool> isRoundabout,
            float tileSize,
            float z,
            int cornerIndex,
            out Vector3 incoming,
            out Vector3 outgoing)
        {
            incoming = default;
            outgoing = default;

            if (cornerIndex <= 0 || cornerIndex >= tiles.Count - 1 || isRoundabout(tiles[cornerIndex]))
            {
                return false;
            }

            Vector3 previous = TileToLocal(tiles[cornerIndex - 1], tileSize, z);
            Vector3 corner = TileToLocal(tiles[cornerIndex], tileSize, z);
            Vector3 next = TileToLocal(tiles[cornerIndex + 1], tileSize, z);
            incoming = corner - previous;
            outgoing = next - corner;

            if (incoming.sqrMagnitude < 0.0001f || outgoing.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            incoming.Normalize();
            outgoing.Normalize();
            return Mathf.Abs(Vector3.Dot(incoming, outgoing)) < 0.001f;
        }

        private static bool TryEvaluateTurnBezier(
            in BakeInput input,
            IReadOnlyList<Vector2Int> tiles,
            int cornerIndex,
            float curveT,
            float radiusFraction,
            out Vector3 position,
            out Vector3 tangent)
        {
            position = default;
            tangent = default;

            if (!TryGetTurnDirections(tiles, input.IsRoundabout, input.TileSize, input.Z, cornerIndex, out Vector3 incoming, out Vector3 outgoing))
            {
                return false;
            }

            Vector3 corner = TileToLocal(tiles[cornerIndex], input.TileSize, input.Z);
            float radius = input.TileSize * radiusFraction;
            Vector3 entry = corner - incoming * radius;
            Vector3 exit = corner + outgoing * radius;
            Vector3 controlIn = entry + incoming * (radius * PolylineMath.QuarterCircleHandle);
            Vector3 controlOut = exit - outgoing * (radius * PolylineMath.QuarterCircleHandle);
            float u = PolylineMath.RemapBezierParameterByArcLength(entry, controlIn, controlOut, exit, Mathf.Clamp01(curveT));

            position = PolylineMath.EvaluateCubicBezier(entry, controlIn, controlOut, exit, u);
            tangent = PolylineMath.EvaluateCubicBezierTangent(entry, controlIn, controlOut, exit, u);
            return tangent.sqrMagnitude > 0.0001f;
        }

        private static bool TryRoundaboutOrbit(
            in BakeInput input,
            IReadOnlyList<Vector2Int> tiles,
            int centerIndex,
            float folded,
            out Vector3 position,
            out Vector3 tangent)
        {
            position = default;
            tangent = default;

            if (centerIndex <= 0 || centerIndex >= tiles.Count - 1)
            {
                return false;
            }

            Vector2Int previous = tiles[centerIndex - 1];
            Vector2Int next = tiles[centerIndex + 1];
            // Task 1 리뷰 지적: TryGetRoundaboutArc는 정규화된 벡터를 기대(sqrMagnitude<0.5 가드).
            // 축정렬 타일 스텝은 이미 단위벡터지만 대각 안전을 위해 명시적으로 정규화한다.
            Vector3 incoming = new Vector3(
                tiles[centerIndex].x - previous.x,
                tiles[centerIndex].y - previous.y,
                0f).normalized;
            Vector3 outgoing = new Vector3(
                next.x - tiles[centerIndex].x,
                next.y - tiles[centerIndex].y,
                0f).normalized;

            if (!PolylineMath.TryGetRoundaboutArc(incoming, outgoing, input.LaneOffset, input.OrbitRadius, out float entryAngle, out float ccwSweep))
            {
                return false;
            }

            float arcU = Mathf.Clamp01(folded - centerIndex + 0.5f);
            float angle = entryAngle + arcU * ccwSweep;
            float radius = input.TileSize * input.OrbitRadius;
            Vector3 center = TileToLocal(tiles[centerIndex], input.TileSize, input.Z);
            position = center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
            tangent = new Vector3(-Mathf.Sin(angle), Mathf.Cos(angle), 0f);
            return true;
        }

        // 주차 앵커 스퍼: 앵커 ↔ 경로 끝 정점을 쿼터 베지어로 잇는다(핸들 길이 = 거리 × QuarterCircleHandle).
        // 앵커 쪽 접선은 앵커→경로 직선 방향, 경로 쪽 접선은 인접 정점의 진행 방향을 그대로 쓴다.
        // 스퍼 정점: IsSpur=true, TileIndex는 인접 끝 세그먼트 시작 인덱스 상속, SegT는 시작=0f/끝=1f 고정.
        private static void PrependAnchorSpur(Vector3 anchor, List<Vertex> vertices, int samples)
        {
            Vertex first = vertices[0];
            Vector3 delta = first.Pos - anchor;
            float distance = delta.magnitude;
            if (distance < 1e-5f)
            {
                return;
            }

            Vector3 anchorTangent = delta.normalized;
            Vector3 controlIn = anchor + anchorTangent * (distance * PolylineMath.QuarterCircleHandle);
            Vector3 controlOut = first.Pos - first.Dir * (distance * PolylineMath.QuarterCircleHandle);

            var spur = new List<Vertex>(samples);
            for (int k = 0; k < samples; k++)
            {
                float nd = (float)k / samples;
                float u = PolylineMath.RemapBezierParameterByArcLength(anchor, controlIn, controlOut, first.Pos, nd);
                spur.Add(new Vertex
                {
                    Pos = PolylineMath.EvaluateCubicBezier(anchor, controlIn, controlOut, first.Pos, u),
                    Dir = PolylineMath.EvaluateCubicBezierTangent(anchor, controlIn, controlOut, first.Pos, u),
                    Seg = first.Seg,
                    SegT = 0f,
                    Spur = true,
                });
            }

            vertices.InsertRange(0, spur);
        }

        private static void AppendAnchorSpur(Vector3 anchor, List<Vertex> vertices, int samples, int endSegIndex)
        {
            Vertex last = vertices[vertices.Count - 1];
            Vector3 delta = anchor - last.Pos;
            float distance = delta.magnitude;
            if (distance < 1e-5f)
            {
                return;
            }

            Vector3 anchorTangent = delta.normalized;
            Vector3 controlIn = last.Pos + last.Dir * (distance * PolylineMath.QuarterCircleHandle);
            Vector3 controlOut = anchor - anchorTangent * (distance * PolylineMath.QuarterCircleHandle);

            for (int k = 1; k <= samples; k++)
            {
                float nd = (float)k / samples;
                float u = PolylineMath.RemapBezierParameterByArcLength(last.Pos, controlIn, controlOut, anchor, nd);
                vertices.Add(new Vertex
                {
                    Pos = PolylineMath.EvaluateCubicBezier(last.Pos, controlIn, controlOut, anchor, u),
                    Dir = PolylineMath.EvaluateCubicBezierTangent(last.Pos, controlIn, controlOut, anchor, u),
                    Seg = endSegIndex,
                    SegT = 1f,
                    Spur = true,
                });
            }
        }

        private static Vector3 TileToLocal(Vector2Int tile, float tileSize, float z)
        {
            return new Vector3((tile.x + 0.5f) * tileSize, (tile.y + 0.5f) * tileSize, z);
        }
    }
}
