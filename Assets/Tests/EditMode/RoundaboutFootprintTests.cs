using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    // 로터리 풋프린트(스펙 2026-07-15): 십자 5칸(center + 상하좌우 4팔) 점유.
    // 흐름·세이브는 무변경 — 여기선 "팔 예약" 규칙만 검증(RoundaboutTests가 배치/흐름 담당).
    public class RoundaboutFootprintTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static SimConfig Cfg(int w, int h)
        {
            var c = SimConfig.Default();
            c.TickInterval = 0.25f;
            c.GridWidth = w; c.GridHeight = h;
            c.AutoDetectSignals = false;
            return c;
        }

        // 인접 교차로 (2,1)·(3,1) — (3,1)은 (2,1)의 오른쪽 팔.
        static SimEngine BuildAdjacent()
        {
            var e = new SimEngine(Cfg(7, 3), new SimEventHub());
            for (int x = 0; x <= 6; x++) e.Place(V(x, 1), TileType.Road);
            e.Place(V(2, 0), TileType.Road); e.Place(V(2, 2), TileType.Road);   // (2,1) 교차로
            e.Place(V(3, 0), TileType.Road); e.Place(V(3, 2), TileType.Road);   // (3,1) 교차로
            e.Tick(0.25f);
            return e;
        }

        [Test]
        public void Footprint_ReservesAdjacentIntersection_FromOtherDevices()
        {
            var e = BuildAdjacent();
            Assert.IsTrue(e.CanPlaceSignal(V(3, 1)));            // 로터리 전엔 팔 자리에 신호 가능
            Assert.IsTrue(e.TryPlaceRoundabout(V(2, 1)));       // (2,1) 로터리 → (3,1)은 팔

            Assert.IsTrue(e.IsInRoundaboutFootprint(V(3, 1)));  // 팔로 인식
            Assert.IsFalse(e.CanPlaceSignal(V(3, 1)));          // 팔에 신호 금지
            Assert.IsFalse(e.CanPlaceRoundabout(V(3, 1)));      // 팔에 로터리 금지(풋프린트 겹침)
            Assert.IsFalse(e.CanPlaceOverpass(V(3, 1)));
            Assert.IsFalse(e.CanPlacePriorityRoad(V(3, 1)));
            Assert.IsFalse(e.CanPlaceTurnSign(V(3, 1)));

            Assert.IsTrue(e.TryRemoveRoundabout(V(2, 1)));      // 철거 → 풋프린트 해제
            Assert.IsFalse(e.IsInRoundaboutFootprint(V(3, 1)));
            Assert.IsTrue(e.CanPlaceSignal(V(3, 1)));           // 다시 배치 가능
        }

        // (2,2) 교차로 — 위 팔(2,1)은 빈 땅(도로 아님).
        static SimEngine BuildEmptyArm()
        {
            var e = new SimEngine(Cfg(5, 5), new SimEventHub());
            for (int x = 0; x <= 4; x++) e.Place(V(x, 2), TileType.Road);      // 가로 간선
            e.Place(V(2, 3), TileType.Road);                                    // 지선 위 하나 → (2,2) 교차로, (2,1) 빈칸
            e.Tick(0.25f);
            return e;
        }

        [Test]
        public void BuildingArm_BlocksRoundabout()
        {
            var e = BuildEmptyArm();
            Assert.IsTrue(e.CanPlaceRoundabout(V(2, 2)));       // 팔이 도로/빈칸 → OK
            Assert.IsTrue(e.Place(V(2, 0), TileType.House));    // 1x2 거주지의 발자국이 빈 팔을 차지
            Assert.IsFalse(e.CanPlaceRoundabout(V(2, 2)));      // 건물 팔 → 배치 거부
        }

        [Test]
        public void Roundabout_BlocksBuildingOnArm_ButNotElsewhere()
        {
            var e = BuildEmptyArm();
            Assert.IsTrue(e.TryPlaceRoundabout(V(2, 2)));       // (2,1) 빈 팔 포함
            Assert.IsFalse(e.CanPlace(V(2, 1), TileType.House));
            Assert.IsFalse(e.Place(V(2, 1), TileType.House));   // 팔에 건물 금지
            Assert.IsTrue(e.Place(V(0, 0), TileType.House));    // 풋프린트 밖은 정상
        }

        [Test]
        public void EdgeIntersection_OobArmSkipped_StillPlaceable()
        {
            // 가장자리(y=0) 교차로 — 아래 팔(y=-1)은 OOB라 스킵, 나머지 인바운드 팔이 도로면 배치 허용.
            var e = new SimEngine(Cfg(7, 2), new SimEventHub());
            for (int x = 0; x <= 6; x++) e.Place(V(x, 0), TileType.Road);
            e.Place(V(3, 1), TileType.Road);                                    // (3,0) 교차로
            e.Tick(0.25f);
            Assert.IsTrue(e.CanPlaceRoundabout(V(3, 0)));       // OOB 팔 스킵 → 배치 가능
            Assert.IsTrue(e.TryPlaceRoundabout(V(3, 0)));
        }
    }
}
