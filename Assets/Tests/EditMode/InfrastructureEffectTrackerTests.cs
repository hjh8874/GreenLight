using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class InfrastructureEffectTrackerTests
    {
        private static readonly Vector2Int Center = new Vector2Int(5, 5);

        private static void RecordRadius(CongestionLedger ledger, float hours)
        {
            for (int y = Center.y - 3; y <= Center.y + 3; y++)
            {
                for (int x = Center.x - 3; x <= Center.x + 3; x++)
                {
                    int index = x + y * 11;
                    ledger.Record(index, CongestionLevel.Jam, hours);
                }
            }
        }

        private static CongestionLedger NewLedgerWithLastDay(float hours)
        {
            var ledger = new CongestionLedger();
            ledger.Configure(11, 11);
            RecordRadius(ledger, hours);
            ledger.OnDayWrap();
            return ledger;
        }

        [Test]
        public void OnPlaced_DoesNotPublishImmediately()
        {
            var ledger = NewLedgerWithLastDay(24f);
            var tracker = new InfrastructureEffectTracker(ledger);

            tracker.OnPlaced(Center);

            var effects = tracker.EvaluateOnDayWrap(ledger);
            Assert.AreEqual(0, effects.Count);
        }

        [Test]
        public void EvaluateOnDayWrap_SkipsFirstBoundary_AndPublishesOnSecond()
        {
            var ledger = NewLedgerWithLastDay(24f);
            var tracker = new InfrastructureEffectTracker(ledger);
            tracker.OnPlaced(Center);

            RecordRadius(ledger, 24f);
            ledger.OnDayWrap();
            Assert.AreEqual(0, tracker.EvaluateOnDayWrap(ledger).Count,
                "설치 시점부터 첫 경계까지는 완성된 비교 하루가 아니다");

            RecordRadius(ledger, 6f);
            ledger.OnDayWrap();
            var effects = tracker.EvaluateOnDayWrap(ledger);

            Assert.AreEqual(1, effects.Count);
            Assert.AreEqual(Center, effects[0].Tile);
            Assert.AreEqual(1f, effects[0].BeforeRatio01, 1e-4f);
            Assert.AreEqual(0.25f, effects[0].AfterRatio01, 1e-4f);
        }

        [Test]
        public void OnPlaced_SameTile_RefreshesPendingBaseline()
        {
            var ledger = NewLedgerWithLastDay(24f);
            var tracker = new InfrastructureEffectTracker(ledger);
            tracker.OnPlaced(Center);

            RecordRadius(ledger, 12f);
            ledger.OnDayWrap();
            Assert.AreEqual(0, tracker.EvaluateOnDayWrap(ledger).Count);

            // 재설치 시점의 어제치(50%)가 기존 설치의 기준(100%)을 대체해야 한다.
            tracker.OnPlaced(Center);
            RecordRadius(ledger, 6f);
            ledger.OnDayWrap();
            Assert.AreEqual(0, tracker.EvaluateOnDayWrap(ledger).Count);

            RecordRadius(ledger, 3f);
            ledger.OnDayWrap();
            var effects = tracker.EvaluateOnDayWrap(ledger);

            Assert.AreEqual(1, effects.Count);
            Assert.AreEqual(0.5f, effects[0].BeforeRatio01, 1e-4f);
            Assert.AreEqual(0.125f, effects[0].AfterRatio01, 1e-4f);
        }
    }
}
