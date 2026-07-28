using UnityEngine;

namespace CityFlow.Contracts
{
    public interface IWorldCoordinateSpace
    {
        WorldCoordinatePlane Plane { get; }
        float TileSize { get; }
        Vector3 Origin { get; }
        Vector3 GridXAxis { get; }
        Vector3 GridYAxis { get; }
        Vector3 GroundNormal { get; }
        Quaternion CoordinateRotation { get; }

        Vector3 GridToWorld(Vector2Int tile, float surfaceOffset = 0f);
        Vector3 GridPointToWorld(Vector2 gridPoint, float surfaceOffset = 0f);
        Vector2 WorldToGridPoint(Vector3 worldPosition);
        Vector2Int WorldToGrid(Vector3 worldPosition);
        bool TryRayToGrid(
            Ray ray,
            out Vector2Int tile,
            out Vector3 worldHitPoint);
    }
}

// Unity setup: Access this contract through CityFlowServices.WorldCoordinates.
