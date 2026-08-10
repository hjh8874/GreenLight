using NUnit.Framework;
using UnityEngine;

namespace CityFlow.Sim.Tests
{
    // PR #241 리뷰(hjh8874)에서 지적된 P1 3건 중 Sim 쪽 회귀를 고정한다.
    // 좌표계·차량 색은 View 라 EditMode 로 못 잡으므로 별도 기록으로 남긴다.
    public sealed class FreeFlowStreakLedgerTests
    {
        private static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // [P1-①] 밀집 배열을 매 틱 도는 대신 병목이 실제로 생긴 타일만 관리한다.
        // 병목이 하나도 없으면 감쇠 대상도 0이어야 한다 — 200×200 이면 40,000회를 아낀다.
        [Test]
        public void Ledger_WithoutAnyReset_TracksNothing()
        {
            var ledger = new FreeFlowStreakLedger(200, 200);

            for (int tick = 0; tick < 50; tick++)
            {
                ledger.Decay();
            }

            Assert.AreEqual(
                0,
                ledger.TrackedTileCountForTest,
                "병목이 없으면 감쇠 대상이 하나도 없어야 한다");
        }

        [Test]
        public void Ledger_TracksOnlyTilesThatReset()
        {
            var ledger = new FreeFlowStreakLedger(200, 200);

            ledger.RecordReset(V(10, 10));
            ledger.RecordReset(V(10, 10));
            ledger.RecordReset(V(11, 10));

            Assert.AreEqual(
                2,
                ledger.TrackedTileCountForTest,
                "리셋이 일어난 타일만 관리해야 한다");
        }

        // 감쇠가 충분히 진행되면 사전에서 빠져 다음 틱부터 순회 대상이 아니다.
        [Test]
        public void Ledger_DecayedBelowThreshold_IsPrunedFromTracking()
        {
            var ledger = new FreeFlowStreakLedger(50, 50);
            ledger.RecordReset(V(5, 5));

            // 0.995^n < 0.01 → n > 917. 여유 있게 돌린다.
            for (int tick = 0; tick < 1200; tick++)
            {
                ledger.Decay();
            }

            Assert.AreEqual(
                0,
                ledger.TrackedTileCountForTest,
                "임계 아래로 내려간 누적은 관리 대상에서 빠져야 한다");
            Assert.AreEqual(
                0f,
                ledger.GetBottleneckIntensity(V(5, 5)),
                "제거된 타일의 강도는 0이다");
        }

        [Test]
        public void Ledger_IntensityRisesWithResetsAndFallsWithDecay()
        {
            var ledger = new FreeFlowStreakLedger(50, 50);
            Vector2Int tile = V(7, 9);

            for (int i = 0; i < 4; i++)
            {
                ledger.RecordReset(tile);
            }

            float afterResets = ledger.GetBottleneckIntensity(tile);
            Assert.Greater(afterResets, 0f, "리셋이 쌓이면 강도가 올라간다");

            for (int tick = 0; tick < 200; tick++)
            {
                ledger.Decay();
            }

            Assert.Less(
                ledger.GetBottleneckIntensity(tile),
                afterResets,
                "시간이 지나면 강도가 내려간다");
        }

        [Test]
        public void Ledger_OutOfBoundsTile_IsIgnored()
        {
            var ledger = new FreeFlowStreakLedger(20, 20);

            ledger.RecordReset(V(-1, 5));
            ledger.RecordReset(V(20, 5));
            ledger.RecordReset(V(5, -1));
            ledger.RecordReset(V(5, 20));

            Assert.AreEqual(
                0,
                ledger.TrackedTileCountForTest,
                "격자 밖 타일은 기록하지 않는다");
            Assert.AreEqual(0f, ledger.GetBottleneckIntensity(V(-1, 5)));
        }
    }
}
