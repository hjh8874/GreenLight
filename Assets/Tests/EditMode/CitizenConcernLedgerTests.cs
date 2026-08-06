using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;
using CityFlow.Feed;

namespace CityFlow.Sim.Tests
{
    public class CitizenConcernLedgerTests
    {
        private static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        [Test]
        public void OpenThenResolve_ReturnsSameAuthor()
        {
            var ledger = new CitizenConcernLedger();
            ledger.Open("김민수", V(3, 4), CitizenFeedConcernKind.Congestion, 10.0);

            bool found = ledger.TryResolve(
                V(3, 4), CitizenFeedConcernKind.Congestion, 12.0, out string author);

            Assert.IsTrue(found);
            Assert.AreEqual("김민수", author);
        }

        [Test]
        public void Resolve_WithoutOpen_ReturnsFalse()
        {
            var ledger = new CitizenConcernLedger();

            bool found = ledger.TryResolve(
                V(1, 1), CitizenFeedConcernKind.Congestion, 5.0, out string author);

            Assert.IsFalse(found);
            Assert.IsNull(author);
        }

        [Test]
        public void Resolve_DifferentTileOrKind_DoesNotMatch()
        {
            var ledger = new CitizenConcernLedger();
            ledger.Open("이영희", V(3, 4), CitizenFeedConcernKind.Congestion, 10.0);

            Assert.IsFalse(
                ledger.TryResolve(V(9, 9), CitizenFeedConcernKind.Congestion, 11.0, out _),
                "다른 타일은 물리면 안 된다");
            Assert.IsFalse(
                ledger.TryResolve(V(3, 4), CitizenFeedConcernKind.Emergency, 11.0, out _),
                "다른 Kind는 물리면 안 된다");
            Assert.AreEqual(1, ledger.Count, "실패한 조회가 항목을 소비하면 안 된다");
        }

        [Test]
        public void Resolve_ConsumesEntry_SoFollowUpHappensOnce()
        {
            var ledger = new CitizenConcernLedger();
            ledger.Open("박철수", V(2, 2), CitizenFeedConcernKind.Congestion, 10.0);

            Assert.IsTrue(
                ledger.TryResolve(V(2, 2), CitizenFeedConcernKind.Congestion, 11.0, out _));
            Assert.IsFalse(
                ledger.TryResolve(V(2, 2), CitizenFeedConcernKind.Congestion, 12.0, out _),
                "한 번 꺼내면 사라져야 후속 글이 1회만 나간다");
        }

        [Test]
        public void TryPeek_DoesNotConsume()
        {
            var ledger = new CitizenConcernLedger();
            ledger.Open("김민수", V(3, 4), CitizenFeedConcernKind.Congestion, 10.0);

            Assert.IsTrue(
                ledger.TryPeek(V(3, 4), CitizenFeedConcernKind.Congestion, 11.0, out string peeked));
            Assert.AreEqual("김민수", peeked);
            Assert.AreEqual(1, ledger.Count, "조회는 항목을 소비하지 않는다");

            Assert.IsTrue(
                ledger.TryResolve(V(3, 4), CitizenFeedConcernKind.Congestion, 11.0, out _),
                "조회 뒤에도 여전히 꺼낼 수 있어야 한다");
        }

        [Test]
        public void TryPeek_IgnoresExpired()
        {
            var ledger = new CitizenConcernLedger();
            ledger.Open("최민호", V(5, 5), CitizenFeedConcernKind.Congestion, 10.0);

            Assert.IsFalse(
                ledger.TryPeek(V(5, 5), CitizenFeedConcernKind.Congestion, 34.1, out _),
                "만료된 항목을 후속 글 대상으로 보면 안 된다");
        }

        [Test]
        public void Entries_ExpireAfterTwentyFourGameHours()
        {
            var ledger = new CitizenConcernLedger();
            ledger.Open("최민호", V(5, 5), CitizenFeedConcernKind.Congestion, 10.0);

            Assert.IsFalse(
                ledger.TryResolve(V(5, 5), CitizenFeedConcernKind.Congestion, 34.1, out _),
                "24시간을 넘기면 만료된다");
        }

        [Test]
        public void Entries_JustUnderExpiry_StillMatch()
        {
            var ledger = new CitizenConcernLedger();
            ledger.Open("최민호", V(5, 5), CitizenFeedConcernKind.Congestion, 10.0);

            Assert.IsTrue(
                ledger.TryResolve(V(5, 5), CitizenFeedConcernKind.Congestion, 33.9, out _),
                "24시간 이내는 살아 있다");
        }

        [Test]
        public void Capacity_DropsOldestFirst()
        {
            var ledger = new CitizenConcernLedger();
            for (int i = 0; i < 33; i++)
            {
                ledger.Open(
                    $"시민{i}", V(i, 0), CitizenFeedConcernKind.Congestion, 10.0 + i * 0.01);
            }

            Assert.AreEqual(32, ledger.Count);
            Assert.IsFalse(
                ledger.TryResolve(V(0, 0), CitizenFeedConcernKind.Congestion, 11.0, out _),
                "가장 오래된 것이 밀려나야 한다");
            Assert.IsTrue(
                ledger.TryResolve(V(32, 0), CitizenFeedConcernKind.Congestion, 11.0, out _),
                "가장 최근 것은 남아야 한다");
        }

        [Test]
        public void OpenSameTileTwice_ReplacesInsteadOfDuplicating()
        {
            var ledger = new CitizenConcernLedger();
            ledger.Open("첫번째", V(1, 1), CitizenFeedConcernKind.Congestion, 10.0);
            ledger.Open("두번째", V(1, 1), CitizenFeedConcernKind.Congestion, 11.0);

            Assert.AreEqual(1, ledger.Count, "같은 (타일, Kind)는 하나만 유지한다");
            Assert.IsTrue(
                ledger.TryResolve(V(1, 1), CitizenFeedConcernKind.Congestion, 12.0, out string author));
            Assert.AreEqual("두번째", author, "최신 불만이 이긴다");
        }

        [Test]
        public void Open_WithBlankAuthor_IsIgnored()
        {
            var ledger = new CitizenConcernLedger();
            ledger.Open(null, V(1, 1), CitizenFeedConcernKind.Congestion, 10.0);
            ledger.Open("", V(2, 2), CitizenFeedConcernKind.Congestion, 10.0);

            Assert.AreEqual(0, ledger.Count, "이름이 없으면 짝지을 수 없으므로 등록하지 않는다");
        }

        [Test]
        public void DropTile_RemovesEveryKindOnThatTile()
        {
            var ledger = new CitizenConcernLedger();
            ledger.Open("가", V(4, 4), CitizenFeedConcernKind.Congestion, 10.0);
            ledger.Open("나", V(4, 4), CitizenFeedConcernKind.Emergency, 10.0);
            ledger.Open("다", V(8, 8), CitizenFeedConcernKind.Congestion, 10.0);

            ledger.DropTile(V(4, 4));

            Assert.AreEqual(1, ledger.Count, "도로가 철거되면 그 타일의 관심사는 전부 버린다");
            Assert.IsTrue(
                ledger.TryResolve(V(8, 8), CitizenFeedConcernKind.Congestion, 11.0, out _),
                "다른 타일은 건드리지 않는다");
        }

        [Test]
        public void ApplyLimits_ChangesExpiryAndCapacity()
        {
            var ledger = new CitizenConcernLedger();
            ledger.ApplyLimits(2.0, 3);

            Assert.AreEqual(2.0, ledger.ExpiryGameHours);
            Assert.AreEqual(3, ledger.Capacity);

            ledger.Open("가", V(1, 1), CitizenFeedConcernKind.Congestion, 10.0);
            Assert.IsFalse(
                ledger.TryResolve(V(1, 1), CitizenFeedConcernKind.Congestion, 12.5, out _),
                "설정 만료(2시간)를 넘기면 버려야 한다");

            for (int i = 0; i < 5; i++)
            {
                ledger.Open($"시민{i}", V(i, 5), CitizenFeedConcernKind.Congestion, 20.0);
            }

            Assert.AreEqual(3, ledger.Count, "설정 상한(3)을 지켜야 한다");
        }

        [Test]
        public void ApplyLimits_ShrinkingCapacity_TrimsImmediately()
        {
            var ledger = new CitizenConcernLedger();
            for (int i = 0; i < 6; i++)
            {
                ledger.Open($"시민{i}", V(i, 0), CitizenFeedConcernKind.Congestion, 10.0);
            }

            ledger.ApplyLimits(24.0, 2);

            Assert.AreEqual(2, ledger.Count, "상한을 줄이면 즉시 오래된 것부터 잘라낸다");
            Assert.IsTrue(
                ledger.TryResolve(V(5, 0), CitizenFeedConcernKind.Congestion, 11.0, out _),
                "가장 최근 것은 남는다");
        }

        [Test]
        public void ApplyLimits_RejectsNonPositive_FallsBackToDefaults()
        {
            var ledger = new CitizenConcernLedger();
            ledger.ApplyLimits(0.0, 0);

            Assert.AreEqual(CitizenConcernLedger.DefaultExpiryGameHours, ledger.ExpiryGameHours);
            Assert.AreEqual(CitizenConcernLedger.DefaultCapacity, ledger.Capacity);
        }

        [Test]
        public void SnapshotRestore_RoundTrips()
        {
            var ledger = new CitizenConcernLedger();
            ledger.Open("김민수", V(3, 4), CitizenFeedConcernKind.Congestion, 10.0);
            ledger.Open("이영희", V(7, 1), CitizenFeedConcernKind.Emergency, 11.0);

            var snapshot = new List<CitizenConcernRecord>(ledger.Snapshot());
            var restored = new CitizenConcernLedger();
            restored.Restore(snapshot, 11.5);

            Assert.AreEqual(2, restored.Count);
            Assert.IsTrue(
                restored.TryResolve(V(3, 4), CitizenFeedConcernKind.Congestion, 12.0, out string author));
            Assert.AreEqual("김민수", author);
        }

        [Test]
        public void Restore_DropsExpiredRecords()
        {
            var ledger = new CitizenConcernLedger();
            ledger.Open("오래된시민", V(3, 4), CitizenFeedConcernKind.Congestion, 10.0);

            var snapshot = new List<CitizenConcernRecord>(ledger.Snapshot());
            var restored = new CitizenConcernLedger();
            restored.Restore(snapshot, 100.0);

            Assert.AreEqual(0, restored.Count, "복원 시점에 이미 만료된 것은 버린다");
        }

        [Test]
        public void Restore_IgnoresBlankAuthorNames()
        {
            var records = new[]
            {
                new CitizenConcernRecord(null, V(1, 1), CitizenFeedConcernKind.Congestion, 10.0),
                new CitizenConcernRecord("", V(2, 2), CitizenFeedConcernKind.Congestion, 10.0),
                new CitizenConcernRecord("정상", V(3, 3), CitizenFeedConcernKind.Congestion, 10.0)
            };

            var ledger = new CitizenConcernLedger();
            ledger.Restore(records, 10.5);

            Assert.AreEqual(1, ledger.Count, "이름이 비면 짝지을 수 없으므로 버린다");
        }

        [Test]
        public void Restore_ClearsPreviousContent()
        {
            var ledger = new CitizenConcernLedger();
            ledger.Open("이전", V(1, 1), CitizenFeedConcernKind.Congestion, 10.0);

            ledger.Restore(new CitizenConcernRecord[0], 10.5);

            Assert.AreEqual(0, ledger.Count, "복원은 덮어쓰기다 — 세이브를 불러오면 이전 상태는 사라진다");
        }
    }
}
