using NUnit.Framework;
using UnityEngine;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.Feed;

namespace CityFlow.Sim.Tests
{
    public class CitizenFeedSaveDataTests
    {
        private static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        [Test]
        public void JsonRoundTrip_PreservesConcerns()
        {
            var ledger = new CitizenConcernLedger();
            ledger.Open("김민수", V(3, 4), CitizenFeedConcernKind.Congestion, 10.0);
            ledger.Open("이영희", V(7, 1), CitizenFeedConcernKind.Emergency, 11.0);

            // JsonUtility를 실제로 태운다 — 세이브가 지나가는 경로와 같아야
            // [Serializable] 누락 같은 실수를 잡는다.
            string json = JsonUtility.ToJson(ledger.ToSaveData());
            var decoded = JsonUtility.FromJson<CitizenFeedSaveData>(json);

            var restored = new CitizenConcernLedger();
            restored.RestoreFrom(decoded, 11.5);

            Assert.AreEqual(2, restored.Count);
            Assert.IsTrue(restored.TryResolve(
                V(7, 1), CitizenFeedConcernKind.Emergency, 12.0, out string author));
            Assert.AreEqual("이영희", author);
        }

        [Test]
        public void RoundTrip_PreservesTileCoordinates()
        {
            var ledger = new CitizenConcernLedger();
            ledger.Open("박철수", V(-5, 12), CitizenFeedConcernKind.Congestion, 10.0);

            var restored = new CitizenConcernLedger();
            restored.RestoreFrom(
                JsonUtility.FromJson<CitizenFeedSaveData>(
                    JsonUtility.ToJson(ledger.ToSaveData())),
                10.5);

            Assert.IsTrue(restored.TryResolve(
                V(-5, 12), CitizenFeedConcernKind.Congestion, 11.0, out _),
                "x/y가 뒤바뀌면 다른 타일이 된다");
        }

        [Test]
        public void NullSnapshot_YieldsEmptyLedger_WithoutThrowing()
        {
            var restored = new CitizenConcernLedger();

            Assert.DoesNotThrow(() => restored.RestoreFrom(null, 10.0));
            Assert.AreEqual(0, restored.Count);
        }

        [Test]
        public void EmptySaveData_YieldsEmptyLedger()
        {
            var restored = new CitizenConcernLedger();

            Assert.DoesNotThrow(
                () => restored.RestoreFrom(new CitizenFeedSaveData(), 10.0));
            Assert.AreEqual(0, restored.Count);
        }

        [Test]
        public void RestoreFrom_DropsExpiredEntries()
        {
            var ledger = new CitizenConcernLedger();
            ledger.Open("오래된시민", V(3, 4), CitizenFeedConcernKind.Congestion, 10.0);

            var restored = new CitizenConcernLedger();
            restored.RestoreFrom(ledger.ToSaveData(), 100.0);

            Assert.AreEqual(0, restored.Count, "복원 시점에 이미 만료된 것은 버린다");
        }

        [Test]
        public void GameSaveData_CitizenFeedDefaultsToNull_LikeAnOldSave()
        {
            var saveData = new GameSaveData();

            Assert.IsNull(
                saveData.CitizenFeed,
                "옛 세이브와 같은 상태를 재현한다 — 복원 쪽이 이걸 견뎌야 한다");
        }

        /// <summary>
        /// 저장 시각과 복원 시각이 다를 때 만료 판정이 **복원에 넘긴 시각**을 따르는지.
        /// SaveService가 달력보다 피드를 먼저 복원하면 여기 들어오는 nowHour가 이전
        /// 런타임 값이 되어, 살아있어야 할 항목이 버려지거나 그 반대가 된다.
        /// (복원 순서 자체는 SaveService.RestoreSnapshot에서 달력 → 피드로 고정했다.)
        /// </summary>
        [Test]
        public void RestoreFrom_UsesGivenHour_NotSaveHour()
        {
            var ledger = new CitizenConcernLedger();
            ledger.Open("김민수", V(3, 4), CitizenFeedConcernKind.Congestion, 100.0);
            CitizenFeedSaveData saved = ledger.ToSaveData();

            // 저장 시점(100h) 기준 12시간 뒤 — 살아 있어야 한다
            var fresh = new CitizenConcernLedger();
            fresh.RestoreFrom(saved, 112.0);
            Assert.AreEqual(1, fresh.Count, "24시간 이내면 살아남는다");

            // 같은 세이브를 30시간 뒤로 복원하면 만료
            var stale = new CitizenConcernLedger();
            stale.RestoreFrom(saved, 130.0);
            Assert.AreEqual(0, stale.Count, "24시간을 넘기면 버린다");
        }

        [Test]
        public void RestoreFrom_EarlierThanSaveHour_KeepsEntries()
        {
            // 달력보다 피드를 먼저 복원하면 nowHour가 저장 시각보다 **작을** 수도 있다.
            // 이때 음수 경과시간으로 만료 판정이 뒤집히면 안 된다.
            var ledger = new CitizenConcernLedger();
            ledger.Open("이영희", V(1, 1), CitizenFeedConcernKind.Congestion, 500.0);

            var restored = new CitizenConcernLedger();
            restored.RestoreFrom(ledger.ToSaveData(), 10.0);

            Assert.AreEqual(1, restored.Count, "경과시간이 음수면 만료가 아니다");
        }

        [Test]
        public void SaveVersion_IsUnchanged()
        {
            // 필드 추가는 옛 세이브에서 null로 들어와 ?? new로 흡수되므로
            // 버전을 올릴 이유가 없다. 올리면 옛 세이브가 통째로 거부된다.
            Assert.AreEqual(1, CityFlow.Save.SaveConstants.CurrentSaveVersion);
            Assert.AreEqual(1, CityFlow.Save.SaveConstants.MinimumSupportedSaveVersion);
        }
    }
}
