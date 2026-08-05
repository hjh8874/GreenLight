using System.Collections.Generic;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Feed
{
    public readonly struct CitizenConcernRecord
    {
        public CitizenConcernRecord(
            string authorName,
            Vector2Int tile,
            CitizenFeedConcernKind kind,
            double openedAtHour)
        {
            AuthorName = authorName;
            Tile = tile;
            Kind = kind;
            OpenedAtHour = openedAtHour;
        }

        public string AuthorName { get; }
        public Vector2Int Tile { get; }
        public CitizenFeedConcernKind Kind { get; }
        public double OpenedAtHour { get; }
    }

    /// <summary>
    /// 시민이 무엇을 불평했는지 기억했다가, 그 문제가 해결되면 같은 시민을 돌려준다.
    /// "제가 저번에 말한 동쪽 사거리, 이제 괜찮네요" 같은 후속 글의 근거다.
    ///
    /// Unity 서비스에 의존하지 않는 순수 로직이다. 이게 별도 어셈블리(CityFlow.Feed)에
    /// 있는 이유이기도 하다 — CityFlow.Sim.Tests는 Assembly-CSharp을 참조할 수 없어서,
    /// Feed/Runtime에 두면 현재 완주하지 못하는 스위트에서만 검증 가능해진다.
    ///
    /// 틀려도 조용히 실패하는 것이 원칙이다. 짝을 못 찾으면 false를 돌려주고
    /// 호출자는 지금처럼 아무 시민이 쓰는 일반 글로 떨어진다. 후속 글이 안 나가는 건
    /// 버그가 아니라 폴백이다.
    /// </summary>
    public sealed class CitizenConcernLedger
    {
        public const double ExpiryGameHours = 24.0;
        public const int Capacity = 32;

        // 삽입 순서를 유지한다 — 상한 초과 시 가장 오래된 것부터 버리기 위해서다.
        private readonly List<CitizenConcernRecord> records =
            new List<CitizenConcernRecord>(Capacity);

        public int Count => records.Count;

        public void Open(
            string authorName,
            Vector2Int tile,
            CitizenFeedConcernKind kind,
            double atHour)
        {
            // 이름이 없으면 나중에 짝지을 수 없다. 등록 자체를 하지 않는다.
            if (string.IsNullOrEmpty(authorName)) return;

            // 같은 (타일, Kind)는 하나만 유지한다. 최신 불만이 이긴다 —
            // 오래된 걸 남겨두면 해결 글이 엉뚱한 사람에게 간다.
            RemoveMatch(tile, kind);

            if (records.Count >= Capacity)
            {
                records.RemoveAt(0);
            }

            records.Add(new CitizenConcernRecord(authorName, tile, kind, atHour));
        }

        public bool TryResolve(
            Vector2Int tile,
            CitizenFeedConcernKind kind,
            double atHour,
            out string authorName)
        {
            authorName = null;
            for (int i = 0; i < records.Count; i++)
            {
                CitizenConcernRecord record = records[i];
                if (record.Tile != tile || record.Kind != kind) continue;

                // 만료됐어도 항목은 치운다 — 안 그러면 죽은 항목이 자리를 계속 먹는다.
                records.RemoveAt(i);
                if (IsExpired(record, atHour)) return false;

                authorName = record.AuthorName;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 도로가 철거되는 등 타일이 사라질 때 부른다.
        /// 없는 곳을 가리키는 후속 글을 막는다.
        /// </summary>
        public void DropTile(Vector2Int tile)
        {
            for (int i = records.Count - 1; i >= 0; i--)
            {
                if (records[i].Tile == tile) records.RemoveAt(i);
            }
        }

        public IReadOnlyList<CitizenConcernRecord> Snapshot() => records;

        public void Restore(IEnumerable<CitizenConcernRecord> restored, double nowHour)
        {
            records.Clear();
            if (restored == null) return;

            foreach (CitizenConcernRecord record in restored)
            {
                if (string.IsNullOrEmpty(record.AuthorName)) continue;
                if (IsExpired(record, nowHour)) continue;
                if (records.Count >= Capacity) break;
                records.Add(record);
            }
        }

        private void RemoveMatch(Vector2Int tile, CitizenFeedConcernKind kind)
        {
            for (int i = records.Count - 1; i >= 0; i--)
            {
                if (records[i].Tile == tile && records[i].Kind == kind)
                {
                    records.RemoveAt(i);
                }
            }
        }

        private static bool IsExpired(CitizenConcernRecord record, double atHour)
        {
            return atHour - record.OpenedAtHour > ExpiryGameHours;
        }
    }
}
