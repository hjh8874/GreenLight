using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;

namespace CityFlow.Sim.Tests
{
    // 도로 예산제(스펙 2026-07-17, 기획 결정 환): 유지비(러닝코스트)를 대체하는 도로 타일 스톡 상한.
    // 유지비 기반 도배 회귀 테스트(구 SettlementTests의 CarpetSpam·온오프 대칭)를 교체한다 —
    // 방어 방식이 "손해"에서 "물리적 불가"로 바뀌었으므로 방어 계약도 함께 옮긴다.
    public class RoadBudgetTests
    {
        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        static SimEngine Build(SimConfig c) => new SimEngine(c, new SimEventHub());

        static SimConfig Cfg(int maxRoad, int w = 10, int h = 10)
        {
            var c = SimConfig.Default();
            c.GridWidth = w;
            c.GridHeight = h;
            c.MaxRoadTiles = maxRoad;
            return c;
        }

        // 예산 한도까지 도로를 채운 뒤 그 다음 도로는 배치 거부(CanPlace + Place 양쪽).
        [Test]
        public void RoadBudget_CanPlaceRejectsBeyondMax()
        {
            var eng = Build(Cfg(maxRoad: 3));
            for (int x = 0; x < 3; x++)
                Assert.IsTrue(eng.Place(V(x, 0), TileType.Road), $"예산 내 {x}번째 도로는 허용돼야 함");

            Assert.IsFalse(eng.CanPlace(V(3, 0), TileType.Road), "예산 초과 도로는 CanPlace 거부");
            Assert.IsFalse(eng.Place(V(3, 0), TileType.Road), "예산 초과 도로는 Place도 거부(가드 우회 금지)");
        }

        // 예산 내 도로는 허용되고, 비도로 타입은 예산이 꽉 차도 영향 없음(예산은 도로만 제한).
        [Test]
        public void RoadBudget_UnderMaxAllowed()
        {
            var eng = Build(Cfg(maxRoad: 5));
            for (int x = 0; x < 4; x++)
                Assert.IsTrue(eng.Place(V(x, 0), TileType.Road));
            Assert.IsTrue(eng.CanPlace(V(4, 0), TileType.Road), "예산 내 마지막 한 칸은 허용");

            eng.Place(V(4, 0), TileType.Road);   // 예산 소진(5/5)
            Assert.IsFalse(eng.CanPlace(V(5, 0), TileType.Road), "도로는 소진되어 거부");
            Assert.IsTrue(eng.CanPlace(V(0, 2), TileType.House), "비도로(House)는 예산 무관 — 여전히 허용");
            Assert.IsTrue(eng.CanPlace(V(4, 2), TileType.Office), "비도로(Office)는 예산 무관 — 여전히 허용");
        }

        // Default 예산이 그리드 도로 가능 칸의 1/4 미만 = 도배(carpet spam) 자체가 물리적으로 불가.
        [Test]
        public void RoadBudget_DefaultIsScarce()
        {
            var c = SimConfig.Default();
            int placeable = c.GridWidth * c.GridHeight;   // 20×20 = 400 (전 칸 도로 가능)
            Assert.Less(c.MaxRoadTiles, placeable / 4,
                "Default 예산이 그리드의 1/4 이상이면 도배 봉인이 느슨함");
        }

        // 우아한 마이그레이션(스펙 2026-07-17 불변사항): 기존 세이브 도로 수가 예산을 넘어도
        // 철거 강제 없음 — 신규 배치만 차단. 복원은 배치 가드를 우회(직접 grid 재배치)하므로 초과분이 보존된다.
        [Test]
        public void RoadBudget_ExistingOverBudgetSurvives_OnlyNewPlacementBlocked()
        {
            var eng = Build(Cfg(maxRoad: 2));
            var snap = new SimSaveData
            {
                PlacedTiles = new[]
                {
                    new TileSaveData { X = 0, Y = 0, Type = TileType.Road },
                    new TileSaveData { X = 1, Y = 0, Type = TileType.Road },
                    new TileSaveData { X = 2, Y = 0, Type = TileType.Road },
                    new TileSaveData { X = 3, Y = 0, Type = TileType.Road },
                    new TileSaveData { X = 4, Y = 0, Type = TileType.Road },
                },
            };
            eng.RestoreSnapshot(snap);   // 예산 2인데 도로 5개 복원 = "예산 초과 세이브"

            Assert.AreEqual(5, eng.RoadTileCount, "예산 초과 세이브의 도로는 철거 강제 없이 보존");
            Assert.IsFalse(eng.CanPlace(V(0, 1), TileType.Road), "초과 상태에선 신규 도로만 차단");
        }

        // ── 2단계: 도로 확장권(스펙 2026-07-17 §2단계, 기획 결정 환) ──
        // "+10칸" 확장권을 코인으로 구매. 유효 캡 = MaxRoadTiles + 구매횟수×10.
        // 가격 = RoadExpandBaseCost × RoadExpandCostGrowth^구매횟수(반올림 정수), 구매횟수는 세이브 영속.

        // 잔고 소유 경계 검증용 최소 가짜 경제(차감은 항상 이쪽에서 일어남).
        sealed class FakeEconomy : IEconomyService
        {
            public long Coins { get; private set; }
            public event System.Action<long> CoinsChanged { add { } remove { } }
            public FakeEconomy(long coins) { Coins = coins; }

            public bool TrySpend(long amount)
            {
                if (amount <= 0L || Coins < amount) return false;
                Coins -= amount;
                return true;
            }

            public void AddCoins(long amount, string reason) => Coins += amount;
        }

        // ① 구매 성공: 유효 캡 +10(막혔던 배치가 풀림) + 코인은 경제 레이어에서 정확히 차감.
        [Test]
        public void RoadExpand_PurchaseRaisesCapAndDeductsCoins()
        {
            var eng = Build(Cfg(maxRoad: 2));
            var econ = new FakeEconomy(1000L);
            eng.Place(V(0, 0), TileType.Road);
            eng.Place(V(1, 0), TileType.Road);
            Assert.IsFalse(eng.CanPlace(V(2, 0), TileType.Road), "구매 전엔 예산 소진으로 차단");

            Assert.IsTrue(eng.TryPurchaseRoadExpansion(econ), "잔고 충분 → 구매 성공");
            Assert.AreEqual(900L, econ.Coins, "기본가 100이 경제 레이어에서 차감");
            Assert.AreEqual(12, eng.MaxRoadTiles, "유효 캡 = 2 + 1회×10 = 12");
            Assert.IsTrue(eng.CanPlace(V(2, 0), TileType.Road), "구매 후 막혔던 배치가 풀림");
        }

        // ② 잔액 부족: 구매 실패 + 캡·코인·구매횟수 전부 무변화(부분 상태 변경 금지).
        [Test]
        public void RoadExpand_InsufficientCoinsNoChange()
        {
            var eng = Build(Cfg(maxRoad: 2));
            var econ = new FakeEconomy(99L);   // 기본가 100 미달

            Assert.IsFalse(eng.TryPurchaseRoadExpansion(econ));
            Assert.AreEqual(99L, econ.Coins, "실패 시 코인 무변화");
            Assert.AreEqual(0, eng.RoadCapacityPurchases, "실패 시 구매횟수 무변화");
            Assert.AreEqual(2, eng.MaxRoadTiles, "실패 시 캡 무변화");
        }

        // ③ 가격 에스컬레이션: 100 → 150 → 225 (기본가 100 × 1.5^n, 반올림 정수).
        [Test]
        public void RoadExpand_PriceEscalates_100_150_225()
        {
            var eng = Build(Cfg(maxRoad: 60));
            var econ = new FakeEconomy(100000L);

            Assert.AreEqual(100L, eng.NextRoadExpandCost);
            Assert.IsTrue(eng.TryPurchaseRoadExpansion(econ));
            Assert.AreEqual(150L, eng.NextRoadExpandCost);
            Assert.IsTrue(eng.TryPurchaseRoadExpansion(econ));
            Assert.AreEqual(225L, eng.NextRoadExpandCost);
            Assert.AreEqual(100000L - 100L - 150L, econ.Coins, "차감 총액 = 100+150");
        }

        // ④ 세이브 라운드트립: 구매 2회 → 저장/로드 → 유효 캡·다음 가격 유지(로드 시 캡 리셋 금지).
        [Test]
        public void RoadExpand_SaveRoundtripPreservesCap()
        {
            var eng = Build(Cfg(maxRoad: 60));
            var econ = new FakeEconomy(1000L);
            Assert.IsTrue(eng.TryPurchaseRoadExpansion(econ));
            Assert.IsTrue(eng.TryPurchaseRoadExpansion(econ));
            var snap = eng.CreateSnapshot();

            var loaded = Build(Cfg(maxRoad: 60));
            loaded.RestoreSnapshot(snap);

            Assert.AreEqual(2, loaded.RoadCapacityPurchases, "구매횟수 세이브 영속");
            Assert.AreEqual(80, loaded.MaxRoadTiles, "유효 캡 = 60 + 2회×10 = 80");
            Assert.AreEqual(225L, loaded.NextRoadExpandCost, "가격 수열도 이어짐(리셋 금지)");
        }

        // ⑤ 도배 봉인(확장권 시대): Default 기준 맵 도배(400칸)까지 확장 총비용이 지수적으로
        // 비현실적 — 34회 구매(60→400) 누적 비용 > 10^7. 하드 캡 없이도 수학이 도배를 봉인한다.
        [Test]
        public void RoadExpand_CarpetExpansionCostAstronomical()
        {
            var c = SimConfig.Default();
            var eng = Build(c);
            int gridTiles = c.GridWidth * c.GridHeight;                 // 400
            int purchasesToCarpet = (gridTiles - c.MaxRoadTiles + 9) / 10;   // (400-60)/10 = 34

            long total = 0L;
            for (int i = 0; i < purchasesToCarpet; i++)
            {
                total += eng.NextRoadExpandCost;
                eng.AddRoadCapacity();
            }

            Assert.GreaterOrEqual(eng.MaxRoadTiles, gridTiles, "34회 구매면 맵 전체 도배 가능 캡");
            Assert.Greater(total, 10_000_000L, "맵 도배까지 누적 비용이 10^7 초과(지수 소프트 캡)");
        }
    }
}
