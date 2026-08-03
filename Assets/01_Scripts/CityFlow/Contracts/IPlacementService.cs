using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Contracts
{
    public interface IPlacementService
    {
        bool CanPlace(Vector2Int tile, TileType type, PlacementDirection direction = PlacementDirection.North);

        bool Place(Vector2Int tile, TileType type, PlacementDirection direction = PlacementDirection.North);

        // 기본 구현은 유형을 무시해 기존 구현체에 영향을 주지 않는다. SimEngine은 같은
        // 시그니처를 오버라이드해 회사 유형을 싣는다(리뷰 P1 대응 2026-07-30).
        bool Place(
            Vector2Int tile,
            TileType type,
            PlacementDirection direction,
            string companyTypeId
        ) => Place(tile, type, direction);

        bool TryResolveAutoDirection(
            Vector2Int tile,
            TileType type,
            out PlacementDirection direction,
            IReadOnlyList<PlacementDirection> priority = null)
        {
            direction = PlacementDirection.North;
            return false;
        }

        bool Remove(Vector2Int tile);
    }
}
