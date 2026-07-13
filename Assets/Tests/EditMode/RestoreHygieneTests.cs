using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    // RestoreSnapshot 유령 상태 가족(감사 2026-07-12): _pendingReward(BurstGuardTests가 지킴)와
    // 같은 유형의 flat-index 누산 상태 — ArrivalEmitter의 이월 소수, BurstDetector의 jam·쿨다운도
    // 복원 시 전부 소각돼야 이전 도시의 잔재가 새 도시로 새지 않는다.
    public class RestoreHygieneTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static void Tick(SimEngine e, int steps, float tick)
        {
            for (int i = 0; i < steps; i++) e.Tick(tick);
        }

        // 집(0,1) ─ 도로 (0,0)~(4,0) ─ 회사(4,1). rate 0.5/s × tick 0.25s = 틱당 0.125 이월.
        static SimConfig ArrivalCfg()
        {
            var c = SimConfig.Default();
            c.TickInterval = 0.25f;
            c.GridWidth = 5; c.GridHeight = 2;
            c.DemandPerHouse = 0.5f;
            c.RoadCapacity = 10f;
            c.CoinBase = 3f;
            return c;
        }

        static SimEngine BuildArrivalCity(SimConfig cfg, SimEventHub hub)
        {
            var e = new SimEngine(cfg, hub);
            for (int x = 0; x <= 4; x++) e.Place(V(x, 0), TileType.Road);
            e.Place(V(0, 1), TileType.House);
            e.Place(V(4, 1), TileType.Office);
            return e;
        }

        [Test]
        public void RestoreSnapshot_ClearsArrivalCarry()
        {
            // A: 도착 직전(0.875)까지 이월을 쌓은 뒤 깨끗한 스냅샷을 복원 → 이월이 남으면
            // 몇 틱 안에 조기 도착이 터진다. 신품 엔진 B와 똑같이 '이월 0'에서 시작해야 한다.
            var cfg = ArrivalCfg();
            var hubA = new SimEventHub();
            var a = BuildArrivalCity(cfg, hubA);
            var snapshot = a.CreateSnapshot();     // 깨끗한 상태(이월 0)에서 저장
            Tick(a, 7, cfg.TickInterval);          // 0.125×7 = 0.875 이월(도착 직전, 미발행)

            a.RestoreSnapshot(snapshot);           // 복원 → 이월도 소각돼야 함

            var hubB = new SimEventHub();
            var b = BuildArrivalCity(cfg, hubB);

            long coinsA = 0, coinsB = 0;
            hubA.Arrival += e => coinsA += e.Coins;
            hubB.Arrival += e => coinsB += e.Coins;

            Tick(a, 2, cfg.TickInterval);          // 이월 남아있으면(0.875+0.25=1.125) 조기 도착 1건
            Tick(b, 2, cfg.TickInterval);          // 신품은 0.25 → 도착 없음

            Assert.AreEqual(coinsB, coinsA);       // 유령 이월로 조기 도착 발생 금지
        }

        static SimEngine BuildBurstCity(out SimEventHub hub)
        {
            var cfg = SimConfig.Default();
            cfg.TickInterval = 0.25f;
            cfg.GridWidth = 9; cfg.GridHeight = 2;
            cfg.DemandPerHouse = 1f;
            cfg.RoadCapacity = 10f;
            hub = new SimEventHub();
            var e = new SimEngine(cfg, hub);
            for (int x = 0; x <= 8; x++) e.Place(V(x, 0), TileType.Road);
            e.Place(V(4, 1), TileType.Road);
            for (int x = 0; x <= 2; x++) e.Place(V(x, 1), TileType.House);
            e.Place(V(8, 1), TileType.Office);
            return e;
        }

        static void TickSeconds(SimEngine e, float seconds, float tick = 0.25f)
        {
            int steps = Mathf.RoundToInt(seconds / tick);
            for (int i = 0; i < steps; i++) e.Tick(tick);
        }

        [Test]
        public void RestoreSnapshot_ClearsBurstDetectorState()
        {
            // 복원 전에 Burst를 한 번 터뜨려 그 타일에 쿨다운을 걸어두고, 복원 후 같은 타일에서
            // 정당한 Jam→Free 전이가 이전 도시의 잔여 쿨다운·히스테리시스에 억눌리면 안 된다.
            var e = BuildBurstCity(out var hub);
            int bursts = 0;
            hub.FlowBurst += _ => bursts++;

            e.Tick(0.25f);                                       // 교차로 감지
            var snapshot = e.CreateSnapshot();                   // 깨끗한 상태(초록 기본값) 저장

            Assert.IsTrue(e.TrySetSignalGreenSlots(V(4, 0), 1));  // 고의 정체
            TickSeconds(e, 5f);                                   // Jam 진입 + pending 적립
            Assert.IsTrue(e.TrySetSignalGreenSlots(V(4, 0), 8));  // 복구
            TickSeconds(e, 1f);                                   // Jam→Free 전이 → Burst #1(쿨다운 시작)
            Assert.AreEqual(1, bursts);

            e.RestoreSnapshot(snapshot);                          // 복원 → jam 상태·쿨다운도 소각돼야 함

            Assert.IsTrue(e.TrySetSignalGreenSlots(V(4, 0), 1));  // 복원 후 다시 같은 타일에서 고의 정체
            TickSeconds(e, 5f);                                   // Jam 진입 + pending 재적립
            Assert.IsTrue(e.TrySetSignalGreenSlots(V(4, 0), 8));  // 복구
            TickSeconds(e, 1f);                                   // Jam→Free 전이 → Burst #2

            Assert.AreEqual(2, bursts);                           // 유령 쿨다운·히스테리시스가 억누르면 실패(1에 머묾)
        }
    }
}
