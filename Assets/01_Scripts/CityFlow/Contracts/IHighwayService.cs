using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Contracts
{
    // 기존 Road 위 고속도로 업그레이드. 방향 규칙이나 교차로 장치가 아니므로 전용 계약으로 분리한다.
    public interface IHighwayService
    {
        IReadOnlyList<Vector2Int> HighwayTiles { get; }
        bool IsHighway(Vector2Int tile);
        bool CanPlaceHighway(Vector2Int tile);
        bool TryPlaceHighway(Vector2Int tile);
        bool TryRemoveHighway(Vector2Int tile);
    }
}
