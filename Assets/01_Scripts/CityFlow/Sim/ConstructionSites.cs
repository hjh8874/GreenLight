using System.Collections.Generic;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Sim
{
    internal readonly struct ConstructionSite
    {
        public readonly Vector2Int Anchor;
        public readonly TileType TargetType;
        public readonly PlacementDirection Direction;
        // StartedAt은 Task 7의 진행도 계산(경과/전체)에 필요하다. 처음부터 넣어 재작업을 피한다.
        public readonly double StartedAtSimSeconds;
        public readonly double CompleteAtSimSeconds;

        public ConstructionSite(
            Vector2Int anchor,
            TileType targetType,
            PlacementDirection direction,
            double startedAtSimSeconds,
            double completeAtSimSeconds)
        {
            Anchor = anchor;
            TargetType = targetType;
            Direction = direction;
            StartedAtSimSeconds = startedAtSimSeconds;
            CompleteAtSimSeconds = completeAtSimSeconds;
        }
    }

    // 공사 중인 건물 사이트 보관소. 진행은 시각 비교뿐이라 상태가 없다(결정론 안전).
    // DemandMap에 넣지 않는 이유: DemandMap의 책임은 수요 배정이고 집·학교·병원은 그 관심사가 아니다.
    internal sealed class ConstructionSites
    {
        private readonly List<ConstructionSite> _sites = new(16);

        public int Count => _sites.Count;
        public IReadOnlyList<ConstructionSite> Sites => _sites;

        public void Register(
            Vector2Int anchor,
            TileType targetType,
            PlacementDirection direction,
            double startedAtSimSeconds,
            double completeAtSimSeconds)
        {
            Cancel(anchor);   // 같은 앵커 중복 방지
            _sites.Add(new ConstructionSite(
                anchor, targetType, direction, startedAtSimSeconds, completeAtSimSeconds));
        }

        public bool Cancel(Vector2Int anchor)
        {
            for (int i = 0; i < _sites.Count; i++)
            {
                if (_sites[i].Anchor != anchor) continue;
                _sites.RemoveAt(i);
                return true;
            }
            return false;
        }

        public bool TryGet(Vector2Int anchor, out ConstructionSite site)
        {
            for (int i = 0; i < _sites.Count; i++)
            {
                if (_sites[i].Anchor != anchor) continue;
                site = _sites[i];
                return true;
            }
            site = default;
            return false;
        }

        // 완성된 사이트를 목록에서 빼서 반환한다. 호출자가 승격 후처리를 실행한다.
        // 역순 순회 — 제거하면서 순회하기 위함.
        public void DrainCompleted(double simSeconds, List<ConstructionSite> completed)
        {
            completed.Clear();
            for (int i = _sites.Count - 1; i >= 0; i--)
            {
                if (_sites[i].CompleteAtSimSeconds > simSeconds) continue;
                completed.Add(_sites[i]);
                _sites.RemoveAt(i);
            }
        }

        public void Clear() => _sites.Clear();
    }
}
