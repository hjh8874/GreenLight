using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    // pendingReward 연출 가드: Burst magnitude는 잃은 처리량을 보여주는 휘발 연출값이다.
    //  - 연출 중립성: magnitude가 고의 정체로 잃은 도착량을 부풀리지 않는다.
    //  - 철거 소각: 병목 타일을 부수면 장부도 소각 — "부수면 폭죽" 금지
    //  - 복원 소각: 세이브 복원 시 이전 도시의 유령 장부 금지
    public class BurstGuardTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // 직선 도시: 도로 y=0(x=0..8), 곁가지 (4,1) → (4,0) 신호 교차로 하나.
        // 집 3채(0..2,1) → Office(8,1). 수요 3/s, 용량 10 → 평시 신호 ratio 0.6(복귀선 정확히).
        static SimEngine BuildCity(out SimEventHub hub, out SimConfig cfg)
        {
            cfg = SimConfig.Default();
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

        static void Tick(SimEngine e, float seconds, float tick = 0.25f)
        {
            int steps = Mathf.RoundToInt(seconds / tick);
            for (int i = 0; i < steps; i++) e.Tick(tick);
        }

        [Test]
        public void JamFarming_LosesArrivalCoins_DespiteReliefMagnitude()
        {
            // 고의 정체는 Arrival 코인을 실제로 줄이고, 해소 시에는 별도 연출 magnitude만 남긴다.
            long steadyArrivalCoins = 0;
            var steady = BuildCity(out var hubA, out _);
            hubA.Arrival += a => steadyArrivalCoins += a.Coins;
            Tick(steady, 8f);

            long jammedArrivalCoins = 0;
            int reliefMagnitude = 0;
            var farm = BuildCity(out var hubB, out _);
            hubB.Arrival += a => jammedArrivalCoins += a.Coins;
            hubB.FlowBurst += b => reliefMagnitude += b.Reward;
            farm.Tick(0.25f);                                        // 교차로 감지
            Assert.IsTrue(farm.TrySetSignalGreenSlots(V(4, 0), 1));  // 고의 정체 제조(공짜)
            Tick(farm, 5f);                                          // 잃은 처리량 적립
            Assert.IsTrue(farm.TrySetSignalGreenSlots(V(4, 0), 8));  // 복구 → Burst 발행
            Tick(farm, 2.75f);                                       // 총 8초 동률(감지 틱 포함)

            Assert.Less(jammedArrivalCoins, steadyArrivalCoins);
            Assert.Greater(reliefMagnitude, 0);
        }

        [Test]
        public void FlowBurstEvent_DoesNotAddPlayerCoins()
        {
            // Sim 이벤트 경계에서 플레이어 코인은 ArrivalEvent만 합산한다.
            // FlowBurst Reward는 발행 여부와 magnitude만 관찰하며 코인 합계에는 넣지 않는다.
            long playerCoins = 0;
            long arrivalCoins = 0;
            int bursts = 0;
            int magnitude = 0;
            var e = BuildCity(out var hub, out _);
            hub.Arrival += a =>
            {
                arrivalCoins += a.Coins;
                playerCoins += a.Coins;
            };
            hub.FlowBurst += b =>
            {
                bursts++;
                magnitude += b.Reward;
            };

            e.Tick(0.25f);
            Assert.IsTrue(e.TrySetSignalGreenSlots(V(4, 0), 1));
            Tick(e, 5f);
            Assert.IsTrue(e.TrySetSignalGreenSlots(V(4, 0), 8));
            Tick(e, 2.75f);

            Assert.Greater(bursts, 0);
            Assert.Greater(magnitude, 0);
            Assert.AreEqual(arrivalCoins, playerCoins);
            Assert.AreNotEqual(arrivalCoins + magnitude, playerCoins);
        }

        [Test]
        public void RemovingBottleneckTile_NoBurstFireworks()
        {
            // 정체로 장부가 쌓인 병목 타일을 철거 → 폭죽(Burst) 금지. 철거는 조용해야 한다.
            var e = BuildCity(out var hub, out _);
            int bursts = 0;
            hub.FlowBurst += _ => bursts++;
            e.Tick(0.25f);
            Assert.IsTrue(e.TrySetSignalGreenSlots(V(4, 0), 1));
            Tick(e, 5f);                                             // 장부 적립 + Jam 상태

            Assert.IsTrue(e.Remove(V(4, 0)));                        // 병목 도로 철거
            Tick(e, 1f);                                             // ratio 0 → Jam→Free 전이 지점

            Assert.AreEqual(0, bursts);                              // 부순 행위에 연출 없음
        }

        [Test]
        public void RestoreSnapshot_ClearsGhostLedger()
        {
            // 스냅샷 이후 쌓인 장부는 복원 시 소각 — 이전 도시의 밀린 돈이 새 도시에서 터지면 안 됨.
            var e = BuildCity(out var hub, out _);
            int bursts = 0;
            hub.FlowBurst += _ => bursts++;
            e.Tick(0.25f);
            var snapshot = e.CreateSnapshot();                       // 평시(초록 8) 저장

            Assert.IsTrue(e.TrySetSignalGreenSlots(V(4, 0), 1));
            Tick(e, 5f);                                             // 장부 적립 + Jam 상태
            e.RestoreSnapshot(snapshot);                             // 복원 → 초록 8로 복귀
            Tick(e, 1f);                                             // ratio 0.6 → 전이 지점(감지기 상태는 유지됨)

            Assert.AreEqual(0, bursts);                              // 유령 장부로 폭죽 금지
        }
    }
}
