using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using CityFlow.ViewKit;

namespace CityFlow.Sim.Tests
{
    public class RoutePolylineTests
    {
        static BakeInput Straight3(Vector3? end = null) => new BakeInput
        {
            Tiles = new List<Vector2Int> { new(0, 0), new(1, 0), new(2, 0) },
            TileSize = 1f, LaneOffset = 0.25f, CornerRadiusFraction = 0.75f,
            OrbitRadius = 0.775f,                            // 풋프린트 차도 중앙(QA F — 뷰 기본값과 일치)
            EntryExitOffsetRad = 45f * Mathf.Deg2Rad,       // α — 뷰 기본값(QA G)
            TransitionLength = 0.66f,                        // 전이 창(뷰 기본값·내부 하한)
            Z = 0f, IsRoundabout = _ => false,
            EndAnchor = end, SamplesPerSegment = 8,
        };

        [Test]
        public void StraightRoute_LengthIsTileDistance()
        {
            var p = RoutePolyline.Bake(Straight3());
            Assert.AreEqual(2f, p.Length, 0.01f, "직선 3타일 = 2 tileSize");
        }

        [TestCase(0.1f, RoutePolyline.MinTransitionSpan)]
        [TestCase(0.8f, 0.8f)]
        [TestCase(1.2f, RoutePolyline.MaxTransitionSpan)]
        public void ClampTransitionSpan_UsesSharedGeometryBounds(float input, float expected)
        {
            Assert.AreEqual(expected, RoutePolyline.ClampTransitionSpan(input), 1e-4f);
        }

        [Test]
        public void StraightRoute_LaneOffsetIsRightOfTravel()
        {
            var p = RoutePolyline.Bake(Straight3());
            Sample s = p.SampleAt(1f);
            // 진행 +x → 오른쪽은 -y. 중심선 y=0.5(타일 중심) 기준 -0.25.
            Assert.AreEqual(0.5f - 0.25f, s.Pos.y, 0.02f);
            Assert.AreEqual(1f, Vector3.Dot(s.Dir, Vector3.right), 0.01f);
        }

        // L자 코너: 인접 샘플 방향 각차 < 25° (급꺾임 없음 = C1 근사).
        [Test]
        public void CornerRoute_TangentContinuity()
        {
            var input = Straight3();
            input.Tiles = new List<Vector2Int> { new(0, 0), new(1, 0), new(1, 1), new(1, 2) };
            var p = RoutePolyline.Bake(input);
            Sample prev = p.SampleAt(0f);
            for (float d = 0.05f; d <= p.Length; d += 0.05f)
            {
                Sample cur = p.SampleAt(d);
                Assert.Greater(Vector3.Dot(prev.Dir, cur.Dir), Mathf.Cos(25f * Mathf.Deg2Rad),
                    $"d={d:F2}에서 급꺾임");
                prev = cur;
            }
        }

        // 주차 스퍼: EndAnchor 지정 시 마지막 샘플 = 앵커 위치.
        [Test]
        public void EndAnchor_LastSampleReachesAnchor()
        {
            Vector3 anchor = new Vector3(2.5f, -0.6f, 0f);
            var p = RoutePolyline.Bake(Straight3(anchor));
            Sample last = p.SampleAt(p.Length);
            Assert.AreEqual(0f, Vector3.Distance(anchor, last.Pos), 0.02f);
            Assert.IsTrue(last.IsSpur, "스퍼 구간 플래그");
        }

        // 단일 타일(앵커 없음) 퇴화 경로: Length 0 + SampleAt이 예외 없이 유일 정점 반환.
        [Test]
        public void SingleTile_NoAnchors_SampleAtDoesNotThrow()
        {
            var input = Straight3();
            input.Tiles = new List<Vector2Int> { new(0, 0) };
            input.StartAnchor = null; input.EndAnchor = null;
            var p = RoutePolyline.Bake(input);
            Assert.AreEqual(0f, p.Length, 1e-4f);
            Sample s = p.SampleAt(0f);
            Assert.AreEqual(0, s.TileIndex);
            Sample s2 = p.SampleAt(5f);   // 범위 밖 클램프도 안전
            Assert.AreEqual(s.Pos, s2.Pos);
        }

        // SegT: 세그먼트 중간에서 ≈0.5 (신호 정지선 판정용 진행률).
        [Test]
        public void SegT_TracksTileBoundaryProgress()
        {
            var p = RoutePolyline.Bake(Straight3());
            Sample s = p.SampleAt(0.5f);
            Assert.AreEqual(0, s.TileIndex);
            Assert.AreEqual(0.5f, s.SegT, 0.06f);
            Assert.IsFalse(s.IsSpur);
        }

        // Sim 도착이 뷰보다 앞서도 현재 월드 위치→주차 앵커 직선(chord)으로 날아가지 않고,
        // 기존 누적거리에서 폴리라인 끝까지 진행해야 한다.
        [Test]
        public void AdvanceTowardEnd_FollowsPolyline_NotWorldChord()
        {
            var input = Straight3();
            input.Tiles = new List<Vector2Int>
            {
                new(0, 0), new(1, 0), new(1, 1), new(1, 2)
            };
            var p = RoutePolyline.Bake(input);
            float distance = 0.1f;
            const float step = 0.4f;
            Sample before = p.SampleAt(distance);
            Vector3 chord = Vector3.MoveTowards(before.Pos, p.SampleAt(p.Length).Pos, step);

            Sample advanced = p.AdvanceTowardEnd(ref distance, step);

            Assert.AreEqual(0.5f, distance, 1e-4f);
            Assert.AreEqual(p.SampleAt(distance).Pos, advanced.Pos);
            Assert.Greater(Vector3.Distance(chord, advanced.Pos), 0.05f,
                "도착 후 보간은 주차 앵커 직선이 아니라 폴리라인 위여야 한다");
        }

        [Test]
        public void DistanceAtTile_IgnoresPrependedParkingSpur()
        {
            var input = Straight3();
            input.StartAnchor = new Vector3(-0.5f, -0.5f, 0f);
            var p = RoutePolyline.Bake(input);

            Sample tileOne = p.SampleAt(p.DistanceAtTile(1));

            Assert.IsFalse(tileOne.IsSpur);
            Assert.AreEqual(1, tileOne.TileIndex);
            Assert.AreEqual(new Vector2Int(1, 0), p.TileAt(tileOne.TileIndex));
        }

        [Test]
        public void DistanceAtQueueSlot_IntersectionHeadUsesApproachStopLine()
        {
            var p = RoutePolyline.Bake(Straight3());
            float tileCenter = p.DistanceAtTile(1);

            Assert.AreEqual(tileCenter - 0.25f,
                p.DistanceAtQueueSlot(1, queueSlot: 0, slotGap: 0.4f, headInset: 0.25f), 1e-4f);
            Assert.AreEqual(tileCenter - 0.65f,
                p.DistanceAtQueueSlot(1, queueSlot: 1, slotGap: 0.4f, headInset: 0.25f), 1e-4f);
        }

        [Test]
        public void DistanceAtQueueSlot_SlotsBeforeRouteStartClampToStart()
        {
            var p = RoutePolyline.Bake(Straight3());

            Assert.AreEqual(0f,
                p.DistanceAtQueueSlot(1, queueSlot: 2, slotGap: 0.55f), 1e-4f);
            Assert.AreEqual(0f,
                p.DistanceAtQueueSlot(1, queueSlot: 3, slotGap: 0.55f), 1e-4f);
        }

        // 로터리 링: 경계 ±0.15 세그 창(구 완전 블렌드 창)의 샘플은 링 반경 위 —
        // 접선 기하 재구성(QA E-1) 이후 이 창은 항상 순수 원호라 더 강하게 성립.
        // 반경은 input.OrbitRadius 파생(QA F — 하드코딩 제거).
        [Test]
        public void DistanceAtPhase_ReturnsBakedRouteBoundaryAndIgnoresParkingSpur()
        {
            var input = Straight3();
            input.StartAnchor = new Vector3(-0.5f, -0.5f, 0f);
            var p = RoutePolyline.Bake(input);

            float phaseDistance = p.DistanceAtPhase(0.5f);
            Sample sample = p.SampleAt(phaseDistance);

            Assert.IsFalse(sample.IsSpur);
            Assert.AreEqual(0, sample.TileIndex);
            Assert.AreEqual(0.5f, sample.SegT, 0.06f);
            Assert.Less(p.DistanceAtPhase(0.25f), phaseDistance);
            Assert.Less(phaseDistance, p.DistanceAtPhase(0.75f));
        }

        [Test]
        public void RoundaboutTile_SamplesOnRing()
        {
            var input = Straight3();
            input.Tiles = new List<Vector2Int> { new(0, 0), new(1, 0), new(2, 0), new(3, 0) };
            input.IsRoundabout = t => t == new Vector2Int(2, 0);
            float radius = input.OrbitRadius;
            var p = RoutePolyline.Bake(input);
            Vector3 center = new Vector3(2.5f, 0.5f, 0f);
            int ringSamples = 0;
            for (float d = 0f; d <= p.Length; d += 0.02f)
            {
                Sample s = p.SampleAt(d);
                bool ringWindow = (s.TileIndex == 1 && s.SegT >= 0.9f)
                               || (s.TileIndex == 2 && s.SegT <= 0.1f);
                if (!ringWindow) continue;
                ringSamples++;
                Assert.AreEqual(radius, Vector3.Distance(s.Pos, center), radius * 0.15f,
                    $"링 창(d={d:F2})은 링 반경 위");
            }
            Assert.Greater(ringSamples, 0, "링 창 샘플 존재");
        }

        // 섬 침범 절대 금지(QA F): 로터리 구간 전체(전이 베지어 포함 — 경로 전 샘플 스캔이 자연히 포함)에서
        // 중심 거리 최소값 > 0.62타일(섬 0.45 + 차 반폭 ~0.15 + 여유). 구간 밖 직선은 항상 이보다 멀다.
        [Test]
        public void RoundaboutTile_NoCenterDip()
        {
            var input = Straight3();
            input.Tiles = new List<Vector2Int> { new(0, 0), new(1, 0), new(2, 0), new(3, 0) };
            input.IsRoundabout = t => t == new Vector2Int(2, 0);
            var p = RoutePolyline.Bake(input);
            Vector3 center = new Vector3(2.5f, 0.5f, 0f);
            float minDist = float.MaxValue;
            for (float d = 0f; d <= p.Length; d += 0.02f)
            {
                Sample s = p.SampleAt(d);
                minDist = Mathf.Min(minDist, Vector3.Distance(s.Pos, center));
            }
            Assert.Greater(minDist, 0.62f, "로터리 구간(전이 포함)이 섬을 침범하면 회귀");
        }

        // 우회전은 sweep≈0이라 링을 생략하고 코너 베지어를 섬 하한으로 클램프한다.
        // 클램프 뒤 위치만 바꾸고 Dir을 원래 베지어 접선으로 두면 차량이 옆으로 미끄러져 보인다.
        [Test]
        public void RoundaboutRightTurn_ClampedPathKeepsClearanceAndChordHeading()
        {
            var input = Straight3();
            input.Tiles = new List<Vector2Int>
            {
                new(0, 1), new(1, 1), new(2, 1), new(2, 0), new(2, -1)
            };
            input.IsRoundabout = t => t == new Vector2Int(2, 1);
            var p = RoutePolyline.Bake(input);
            Vector3 center = new Vector3(2.5f, 1.5f, 0f);

            const float step = 0.01f;
            const float clearanceTolerance = 0.005f; // 원호 정점 사이 현 보간의 sagitta 허용
            float minDist = float.MaxValue;

            for (float d = 0f; d + step <= p.Length; d += step)
            {
                Sample current = p.SampleAt(d);
                float centerDistance = Vector3.Distance(current.Pos, center);
                minDist = Mathf.Min(minDist, centerDistance);
                Assert.GreaterOrEqual(centerDistance, 0.62f - clearanceTolerance,
                    $"d={d:F2}에서 우회전 경로가 섬 하한을 침범");
            }

            FieldInfo verticesField = typeof(RoutePolyline).GetField("_vertices", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(verticesField);
            var vertices = (System.Array)verticesField.GetValue(p);
            Assert.Greater(vertices.Length, 1);
            System.Type vertexType = vertices.GetValue(0).GetType();
            FieldInfo posField = vertexType.GetField("Pos", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo dirField = vertexType.GetField("Dir", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(posField);
            Assert.NotNull(dirField);

            int clampedVertices = 0;
            int clampedEntrySegments = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 pos = (Vector3)posField.GetValue(vertices.GetValue(i));
                float centerDistance = Vector3.Distance(pos, center);
                Assert.GreaterOrEqual(centerDistance, 0.62f - 1e-4f,
                    $"정점 {i}가 섬 하한을 침범");

                if (i + 1 < vertices.Length)
                {
                    Vector3 entryNextPos = (Vector3)posField.GetValue(vertices.GetValue(i + 1));
                    float nextCenterDistance = Vector3.Distance(entryNextPos, center);
                    if (centerDistance > 0.62f + 1e-4f &&
                        nextCenterDistance <= 0.62f + 1e-4f)
                    {
                        Vector3 entryChord = entryNextPos - pos;
                        if (entryChord.sqrMagnitude > 1e-8f)
                        {
                            clampedEntrySegments++;
                            Vector3 entryDir = (Vector3)dirField.GetValue(vertices.GetValue(i));
                            Assert.GreaterOrEqual(
                                Vector3.Dot(entryDir, entryChord.normalized),
                                0.9999f,
                                $"클램프 진입 직전 정점 {i}의 Dir과 진입 현이 불일치");
                        }
                    }
                }

                if (centerDistance > 0.62f + 1e-4f || i + 1 >= vertices.Length)
                {
                    continue;
                }

                Vector3 nextPos = (Vector3)posField.GetValue(vertices.GetValue(i + 1));
                Vector3 chord = nextPos - pos;
                if (chord.sqrMagnitude < 1e-8f) continue;

                clampedVertices++;
                Vector3 dir = (Vector3)dirField.GetValue(vertices.GetValue(i));
                Assert.GreaterOrEqual(Vector3.Dot(dir, chord.normalized), 0.9999f,
                    $"클램프 정점 {i}의 Dir과 다음 정점 현이 불일치");
            }

            Assert.Greater(clampedVertices, 0, "우회전 클램프 정점 존재");
            Assert.Greater(clampedEntrySegments, 0, "우회전 클램프 진입 구간 존재");
            Assert.LessOrEqual(minDist, 0.62f + clearanceTolerance, "회귀 케이스가 클램프 경로를 통과해야 함");
        }
    }
}
