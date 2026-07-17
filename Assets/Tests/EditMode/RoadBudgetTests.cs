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
    }
}
