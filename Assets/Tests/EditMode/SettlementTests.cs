using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    // 계획 7: 오프라인 정산 = 공식 산술 + 상한 + 소수 이월. 온라인과 같은 누산기로 적분해
    // "복귀 보상 = 계속 켜놨을 때 보상"이 구조적으로 일치해야 한다.
    public class SettlementTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static SimEngine BuildCity(out List<SettlementEvent> settlements,
            out List<ArrivalEvent> arrivals, float demand = 0.5f, float capHours = 8f)
        {
            var c = SimConfig.Default();
            c.TickInterval = 0.25f;
            c.GridWidth = 5; c.GridHeight = 2;
            c.DemandPerHouse = demand;    // rate 0.5/s → 2초당 도착 1건
            c.RoadCapacity = 10f;
            c.CoinBase = 1f;
            c.OfflineCapHours = capHours;

            var hub = new SimEventHub();
            var s = new List<SettlementEvent>();
            var a = new List<ArrivalEvent>();
            hub.SettlementComputed += e => s.Add(e);
            hub.Arrival += e => a.Add(e);
            settlements = s; arrivals = a;

            var eng = new SimEngine(c, hub);
            for (int x = 0; x <= 4; x++) eng.Place(V(x, 0), TileType.Road);
            eng.Place(V(0, 1), TileType.House);
            eng.Place(V(4, 1), TileType.Office);
            return eng;
        }

        [Test]
        public void Settle_ComputesCoinsFromRate()
        {
            // rate 0.5/s × 100초 = 도착 50 → 코인 50, Minutes = 100/60
            var eng = BuildCity(out var settlements, out _);
            eng.SettleOffline(100.0);

            Assert.AreEqual(1, settlements.Count);
            Assert.AreEqual(50L, settlements[0].Coins);
            Assert.AreEqual(100.0 / 60.0, settlements[0].Minutes, 1e-9);
        }

        [Test]
        public void Settle_CapsAtOfflineHours()
        {
            // 상한 1시간: 2시간 방치해도 3600초만 정산 = 코인 1800, Minutes도 상한 기준 60
            var eng = BuildCity(out var settlements, out _, capHours: 1f);
            eng.SettleOffline(7200.0);

            Assert.AreEqual(1800L, settlements[0].Coins);
            Assert.AreEqual(60.0, settlements[0].Minutes, 1e-9);
        }

        [Test]
        public void Settle_CarriesFraction_AndMatchesOnline()
        {
            // 온라인 8틱(2초) = 도착 1 → 이어서 오프라인 3초 정산: 누산 이월이 이어져
            // 총합 = rate 0.5 × 5초 = 2.5 → 정수 2. 오프라인 몫 = 2 - 1 = 1코인, 이월 0.5 유지.
            var eng = BuildCity(out var settlements, out var arrivals);
            for (int t = 0; t < 8; t++) eng.Tick(0.25f);
            Assert.AreEqual(1, arrivals.Count);        // 온라인 몫

            eng.SettleOffline(3.0);
            Assert.AreEqual(1L, settlements[0].Coins); // 오프라인 몫(이월 이어받아 1.5→1, 잔여 0.5)

            eng.SettleOffline(1.0);                    // 잔여 0.5 + 0.5 = 1.0 → 1코인
            Assert.AreEqual(1L, settlements[1].Coins);
        }
    }
}
