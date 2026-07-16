using System.Collections.Generic;
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
            TileSize = 1f, LaneOffset = 0.18f, CornerRadiusFraction = 0.75f,
            OrbitRadius = 0.9f, Z = 0f, IsRoundabout = _ => false,   // 풋프린트 차도 중앙(QA F — 뷰 기본값과 일치)
            EndAnchor = end, SamplesPerSegment = 8,
        };

        [Test]
        public void StraightRoute_LengthIsTileDistance()
        {
            var p = RoutePolyline.Bake(Straight3());
            Assert.AreEqual(2f, p.Length, 0.01f, "직선 3타일 = 2 tileSize");
        }

        [Test]
        public void StraightRoute_LaneOffsetIsRightOfTravel()
        {
            var p = RoutePolyline.Bake(Straight3());
            Sample s = p.SampleAt(1f);
            // 진행 +x → 오른쪽은 -y. 중심선 y=0.5(타일 중심) 기준 -0.18.
            Assert.AreEqual(0.5f - 0.18f, s.Pos.y, 0.02f);
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

        // 로터리 링: 경계 ±0.15 세그 창(구 완전 블렌드 창)의 샘플은 링 반경 위 —
        // 접선 기하 재구성(QA E-1) 이후 이 창은 항상 순수 원호라 더 강하게 성립.
        // 반경은 input.OrbitRadius 파생(QA F — 하드코딩 제거).
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
    }
}
