using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    // 계획 9: 동일 입력 2회 실행 → 상태 해시·이벤트 스트림 완전 동일.
    // 방치형 오프라인 정산 신뢰의 기반 — Random·Dictionary 순회·시간 의존이 숨어들면 여기서 터진다.
    public class DeterminismTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static SimConfig Cfg()
        {
            var c = SimConfig.Default();          // tick 0.1
            c.GridWidth = 8; c.GridHeight = 6;
            c.DemandPerHouse = 4f;                // 집 3채가 겹치면 12 > 용량 10 → Jam 유발
            c.RoadCapacity = 10f;
            c.BurstRewardThreshold = 0.1f;
            return c;
        }

        // 같은 조작 시퀀스: 배치 → 30틱(정체) → 철거+신축(토폴로지 변경) → 30틱(해소·Burst)
        static (ulong hash, List<string> events) Run()
        {
            var cfg = Cfg();
            var hub = new SimEventHub();
            var events = new List<string>();
            hub.Arrival += e => events.Add($"A:{e.Destination}:{e.Coins}");
            hub.FlowBurst += e => events.Add($"B:{e.Tile}:{e.Reward}");
            var eng = new SimEngine(cfg, hub);

            for (int x = 0; x < 8; x++) eng.Place(V(x, 2), TileType.Road);   // 가로축
            for (int y = 0; y < 6; y++) eng.Place(V(3, y), TileType.Road);   // 세로축(십자)
            eng.Place(V(0, 1), TileType.House);
            eng.Place(V(1, 1), TileType.House);
            eng.Place(V(5, 1), TileType.House);
            eng.Place(V(7, 1), TileType.Office);
            eng.Place(V(0, 3), TileType.School);

            for (int t = 0; t < 30; t++) eng.Tick(0.1f);

            eng.Remove(V(1, 1));                  // 재개발: 수요 완화 → Jam→Free 전이 유발
            eng.Place(V(6, 3), TileType.House);   // 신축: 재배정 경로 변화

            for (int t = 0; t < 30; t++) eng.Tick(0.1f);

            return (HashState(eng, cfg), events);
        }

        // FNV-1a 64bit. float은 비트 그대로 — 동일 연산이면 동일 비트여야 한다.
        static ulong HashState(SimEngine eng, in SimConfig cfg)
        {
            const ulong prime = 1099511628211UL;
            ulong h = 14695981039346656037UL;
            void Mix(int v) { h = (h ^ (uint)v) * prime; }

            for (int y = 0; y < cfg.GridHeight; y++)
            {
                for (int x = 0; x < cfg.GridWidth; x++)
                {
                    var t = V(x, y);
                    Mix((int)eng.GetTileType(t));
                    Mix((int)eng.GetCongestion(t));
                    Mix(BitConverter.SingleToInt32Bits(eng.GetDensity01(t)));
                }
            }
            Mix(BitConverter.SingleToInt32Bits(eng.Stability01));
            return h;
        }

        [Test]
        public void SameInputsTwice_SameHashAndEventStream()
        {
            var r1 = Run();
            var r2 = Run();

            Assert.AreEqual(r1.hash, r2.hash);
            CollectionAssert.AreEqual(r1.events, r2.events);
            Assert.IsNotEmpty(r1.events); // 이벤트가 아예 없으면 스트림 비교가 무의미
        }

        [Test]
        public void DifferentCity_DifferentHash()
        {
            // 해시 함수 자체의 자기검증 — 상태를 무시하는 고장난 해시면 여기서 걸린다.
            var busy = Run().hash;
            var empty = HashState(new SimEngine(Cfg(), new SimEventHub()), Cfg());
            Assert.AreNotEqual(busy, empty);
        }
    }
}
