using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim.Tests
{
    public class CongestionLedgerTests
    {
        [Test]
        public void JamHours_AccumulateOnlySlowOrWorse()
        {
            var ledger = new CongestionLedger();
            ledger.Configure(4, 4);
            ledger.Record(5, CongestionLevel.Free, 1f);
            ledger.Record(5, CongestionLevel.Slow, 2f);
            ledger.Record(5, CongestionLevel.Jam, 3f);
            Assert.AreEqual(5f, ledger.TodayJamHours(5), 1e-4f);
        }

        [Test]
        public void DayWrap_SnapshotsYesterday_AndResetsToday()
        {
            var ledger = new CongestionLedger();
            ledger.Configure(4, 4);
            ledger.Record(5, CongestionLevel.Jam, 6f);
            ledger.OnDayWrap();
            Assert.AreEqual(0.25f, ledger.LastDayJamRatio01(5), 1e-4f); // 6h/24h
            Assert.AreEqual(0f, ledger.TodayJamHours(5), 1e-4f);
            ledger.Record(5, CongestionLevel.Slow, 12f);
            ledger.OnDayWrap();
            Assert.AreEqual(0.5f, ledger.LastDayJamRatio01(5), 1e-4f);
        }

        [Test]
        public void Ratio_ClampsToOne_WhenOverAccumulated()
        {
            var ledger = new CongestionLedger();
            ledger.Configure(2, 2);
            ledger.Record(0, CongestionLevel.Jam, 30f);
            ledger.OnDayWrap();
            Assert.AreEqual(1f, ledger.LastDayJamRatio01(0));
        }
    }
}
