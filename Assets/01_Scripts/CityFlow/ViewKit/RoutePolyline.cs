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

    // 베이크 = "위상 샘플링": MainCityView.EvaluateVehiclePose(중심선 Lerp + 코너 베지어 + 차선 오프셋)의
    // 순수 재현을 phase 축을 SamplesPerSegment 간격으로 훑어 정점화하고, 누적 아크렝스 테이블로
    // SampleAt(이진 탐색)을 지원한다. 원본(MainCityView)은 그대로 두고 시각 파리티만 목표 — 재발명 금지.
    // 예외: 로터리는 옛 SmoothStep 블렌드(중앙 딥 결함, 라이브 QA E-1)를 버리고 접선 기하 재구성
    // (ApplyRoundaboutGeometry — 전이 베지어 + 순수 CCW 원호)으로 대체한다.
    // forward 전용(단방향) — 퇴근 방향은 뷰가 타일 목록을 뒤집어 별도 베이크한다. Dir는 항상 베이크 방향 접선.
    //
    // ponytail: 베이크는 위상 샘플링 8/seg — 시각 파리티 우선. 해상도 문제 시 코너만 적응 샘플로 승급.
    public sealed class RoutePolyline
    {
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

            if (segmentCount > 0)
            {
                ApplyRoundaboutGeometry(input, tiles, segmentCount, vertices);
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
            // 단일 정점 폴리라인(0/1타일 + 앵커 없음) 가드: 이진탐색이 upper=1을 만들며
            // 배열 밖을 읽으므로 유일 정점을 그대로 반환한다(리뷰 Critical 픽스).
            if (_cumulative.Length == 1)
            {
                Vertex only = _vertices[0];
                return new Sample
                {
                    Pos = only.Pos,
                    Dir = only.Dir,
                    TileIndex = only.Seg,
                    SegT = only.SegT,
                    IsSpur = only.Spur,
                };
            }

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
            }

            Vector3 travelDir = routeTangent;
            Vector3 laneRight = new Vector3(travelDir.y, -travelDir.x, 0f);
            Vector3 position = centerline + laneRight * (input.TileSize * input.LaneOffset);

            // 로터리 포즈는 여기서 다루지 않는다 — 베이크 후처리(ApplyRoundaboutGeometry)가
            // 해당 구간 정점을 접선 기하(전이 베지어+원호)로 통째로 교체한다(QA E-1).
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

        // ── 로터리 접선 기하 재구성(라이브 QA E-1) ──────────────────────────────────────
        // 옛 SmoothStep 블렌드는 "차선 중심선(로터리 중앙 관통) ↔ 링" 위치 보간이라 궤도 창
        // 대부분에서 중앙 딥(실측 0.498 / 반경 0.68). 근본 교체: 로터리 타일 구간의 정점을
        //   [진입 전이 베지어] → [순수 CCW 원호(TryGetRoundaboutArc entryAngle/ccwSweep)] → [이탈 전이 베지어]
        // 로 통째로 재구성한다. 링/차선 교점(laneShift가 인코딩 — 중심 전방 √(R²−λ²) 지점)이
        // 반경 위 C0 접점이고, 차선 직선은 링과 λ<R라 진정한 접선이 될 수 없으므로(75° 꺾임)
        // 전이 베지어(양끝 접선 일치)로 C1을 만든다. 전이 최저 중심거리 ≈ 0.61R — 딥 해소.
        // 구간 경계 포즈는 실제 베이스 포즈(코너 베지어 포함)에서 취해 코너-인접 진입도 연속.
        private static void ApplyRoundaboutGeometry(
            in BakeInput input,
            IReadOnlyList<Vector2Int> tiles,
            int segmentCount,
            List<Vertex> vertices)
        {
            float radius = Mathf.Max(0.05f, input.OrbitRadius);
            // 링/차선 교점까지의 중심 전방 거리(타일=페이즈 단위). λ≥R 퇴화는 하한 가드.
            float halfSpan = Mathf.Sqrt(Mathf.Max(0.0001f, radius * radius - input.LaneOffset * input.LaneOffset));

            for (int ci = 1; ci < tiles.Count - 1; ci++)
            {
                if (!input.IsRoundabout(tiles[ci]))
                {
                    continue;
                }

                // Task 1 리뷰 지적: TryGetRoundaboutArc는 정규화 벡터 기대 — 대각 안전 명시 정규화.
                Vector3 incoming = new Vector3(
                    tiles[ci].x - tiles[ci - 1].x,
                    tiles[ci].y - tiles[ci - 1].y,
                    0f).normalized;
                Vector3 outgoing = new Vector3(
                    tiles[ci + 1].x - tiles[ci].x,
                    tiles[ci + 1].y - tiles[ci].y,
                    0f).normalized;

                if (!PolylineMath.TryGetRoundaboutArc(incoming, outgoing, input.LaneOffset, radius, out float entryAngle, out float ccwSweep))
                {
                    continue;
                }

                float startPhase = ci - halfSpan;   // ci ∈ [1, Count-2], halfSpan < 1 → 항상 경로 내부
                float endPhase = ci + halfSpan;
                PoseAt(input, tiles, segmentCount, startPhase, out Vector3 entryPos, out Vector3 entryDir);
                PoseAt(input, tiles, segmentCount, endPhase, out Vector3 exitPos, out Vector3 exitDir);

                // 전이 스윕: 양끝 각각 min(29°, 전체 스윕 30%) — 남는 가운데가 순수 원호.
                float beta = Mathf.Min(0.5f, ccwSweep * 0.3f);
                float arcStartAngle = entryAngle + beta;
                float arcEndAngle = entryAngle + ccwSweep - beta;
                Vector3 center = TileToLocal(tiles[ci], input.TileSize, input.Z);
                float worldRadius = input.TileSize * radius;

                var built = new List<Vertex>(32);
                AppendTransitionBezier(built, entryPos, entryDir,
                    RingPoint(center, worldRadius, arcStartAngle), RingTangent(arcStartAngle), includeStart: true);

                // 원호: 스윕 12°당 1정점 이상.
                int arcSamples = Mathf.Max(2, Mathf.CeilToInt((arcEndAngle - arcStartAngle) * Mathf.Rad2Deg / 12f));
                for (int k = 1; k <= arcSamples; k++)   // k=0은 전이 베지어 끝점과 중복 — 스킵
                {
                    float angle = Mathf.Lerp(arcStartAngle, arcEndAngle, (float)k / arcSamples);
                    built.Add(new Vertex { Pos = RingPoint(center, worldRadius, angle), Dir = RingTangent(angle) });
                }

                AppendTransitionBezier(built, RingPoint(center, worldRadius, arcEndAngle), RingTangent(arcEndAngle),
                    exitPos, exitDir, includeStart: false);

                AssignPhasesByChordLength(built, startPhase, endPhase, segmentCount);
                SpliceVertices(vertices, built, startPhase, endPhase);
            }
        }

        // 접선 일치 전이(C1): from/to의 접선을 핸들로 쓰는 쿼터 베지어. 링 안쪽 최저 ≈ 0.61R.
        private static void AppendTransitionBezier(
            List<Vertex> built, Vector3 from, Vector3 fromDir, Vector3 to, Vector3 toDir, bool includeStart)
        {
            float distance = Vector3.Distance(from, to);
            if (distance < 1e-4f)
            {
                if (includeStart)
                {
                    built.Add(new Vertex { Pos = from, Dir = fromDir });
                }
                return;
            }

            Vector3 controlIn = from + fromDir * (distance * PolylineMath.QuarterCircleHandle);
            Vector3 controlOut = to - toDir * (distance * PolylineMath.QuarterCircleHandle);
            const int samples = 6;
            for (int k = includeStart ? 0 : 1; k <= samples; k++)
            {
                float t = (float)k / samples;
                built.Add(new Vertex
                {
                    Pos = PolylineMath.EvaluateCubicBezier(from, controlIn, controlOut, to, t),
                    Dir = PolylineMath.EvaluateCubicBezierTangent(from, controlIn, controlOut, to, t),
                });
            }
        }

        // 구성 정점의 Seg/SegT를 공간 진행(누적 현 길이) 비례로 배정 — 혼잡 타일·신호 진행률 의미 보존.
        private static void AssignPhasesByChordLength(List<Vertex> built, float startPhase, float endPhase, int segmentCount)
        {
            float total = 0f;
            var cumulative = new float[built.Count];
            for (int j = 1; j < built.Count; j++)
            {
                total += Vector3.Distance(built[j - 1].Pos, built[j].Pos);
                cumulative[j] = total;
            }

            for (int j = 0; j < built.Count; j++)
            {
                float u = total > 1e-5f ? cumulative[j] / total : (built.Count > 1 ? (float)j / (built.Count - 1) : 0f);
                float phase = Mathf.Lerp(startPhase, endPhase, u);
                int seg = Mathf.Clamp(Mathf.FloorToInt(phase), 0, segmentCount - 1);
                Vertex v = built[j];
                v.Seg = seg;
                v.SegT = phase - seg;
                v.Spur = false;
                built[j] = v;
            }
        }

        // (startPhase, endPhase) 안의 베이스 정점을 제거하고 구성 정점으로 교체.
        // 로터리 풋프린트는 배타(center 인접 불가) → 서로 다른 로터리의 구간은 겹치지 않는다.
        private static void SpliceVertices(List<Vertex> vertices, List<Vertex> built, float startPhase, float endPhase)
        {
            int first = 0;
            while (first < vertices.Count && vertices[first].Seg + vertices[first].SegT < startPhase - 1e-4f)
            {
                first++;
            }

            int last = vertices.Count - 1;
            while (last >= 0 && vertices[last].Seg + vertices[last].SegT > endPhase + 1e-4f)
            {
                last--;
            }

            if (first <= last)
            {
                vertices.RemoveRange(first, last - first + 1);
            }

            vertices.InsertRange(first, built);
        }

        private static Vector3 RingPoint(Vector3 center, float worldRadius, float angle)
        {
            return center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * worldRadius;
        }

        private static Vector3 RingTangent(float angle)
        {
            return new Vector3(-Mathf.Sin(angle), Mathf.Cos(angle), 0f);   // CCW 접선
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
