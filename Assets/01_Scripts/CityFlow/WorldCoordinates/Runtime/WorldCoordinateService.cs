using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.WorldCoordinates
{
    [DisallowMultipleComponent]
    public sealed class WorldCoordinateService :
        MonoBehaviour,
        ICityFlowServiceConsumer,
        IWorldCoordinateSpace
    {
        [SerializeField] private WorldCoordinateProfileSO profile;

        private bool initialized;
        private Vector3 registeredOrigin;
        private Quaternion registeredRotation;

        public WorldCoordinateProfileSO Profile => profile;
        public WorldCoordinatePlane Plane => profile != null
            ? profile.Plane
            : WorldCoordinatePlane.XY;
        public float TileSize => GridUtil.TileSize;
        public Vector3 Origin => initialized
            ? registeredOrigin
            : transform.position;
        public Quaternion CoordinateRotation => initialized
            ? registeredRotation
            : transform.rotation * ProfileRotation;
        public Vector3 GridXAxis =>
            (CoordinateRotation * Vector3.right).normalized;
        public Vector3 GridYAxis =>
            (CoordinateRotation * Vector3.up).normalized;
        public Vector3 GroundNormal =>
            (CoordinateRotation * Vector3.back).normalized;

        private Quaternion ProfileRotation => profile != null
            ? profile.PlaneRotation
            : Quaternion.identity;

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled || initialized)
            {
                return;
            }

            if (services == null || profile == null)
            {
                Debug.LogWarning(
                    "[WorldCoordinateService] Services or profile is missing. " +
                    "Coordinate registration was skipped.",
                    this);
                return;
            }

            registeredOrigin = transform.position;
            registeredRotation = transform.rotation * ProfileRotation;
            if (!services.RegisterWorldCoordinates(this))
            {
                Debug.LogWarning(
                    "[WorldCoordinateService] Another coordinate service is " +
                    "already registered.",
                    this);
                return;
            }

            initialized = true;
            Debug.Log(
                $"[WorldCoordinateService] Registered {Plane} plane with " +
                $"tile size {TileSize:0.###}.",
                this);
        }

        public Vector3 GridToWorld(
            Vector2Int tile,
            float surfaceOffset = 0f)
        {
            return GridPointToWorld(
                new Vector2(tile.x + 0.5f, tile.y + 0.5f),
                surfaceOffset);
        }

        public Vector3 GridPointToWorld(
            Vector2 gridPoint,
            float surfaceOffset = 0f)
        {
            return Origin +
                   GridXAxis * (gridPoint.x * TileSize) +
                   GridYAxis * (gridPoint.y * TileSize) +
                   GroundNormal * surfaceOffset;
        }

        public Vector2 WorldToGridPoint(Vector3 worldPosition)
        {
            Vector3 fromOrigin = worldPosition - Origin;
            return new Vector2(
                Vector3.Dot(fromOrigin, GridXAxis) / TileSize,
                Vector3.Dot(fromOrigin, GridYAxis) / TileSize);
        }

        public Vector2Int WorldToGrid(Vector3 worldPosition)
        {
            Vector2 gridPoint = WorldToGridPoint(worldPosition);
            return new Vector2Int(
                Mathf.FloorToInt(gridPoint.x),
                Mathf.FloorToInt(gridPoint.y));
        }

        public bool TryRayToGrid(
            Ray ray,
            out Vector2Int tile,
            out Vector3 worldHitPoint)
        {
            var groundPlane = new Plane(GroundNormal, Origin);
            if (!groundPlane.Raycast(ray, out float distance))
            {
                tile = default;
                worldHitPoint = default;
                return false;
            }

            worldHitPoint = ray.GetPoint(distance);
            tile = WorldToGrid(worldHitPoint);
            return true;
        }
    }
}

// Unity setup: Place WorldCoordinateSystem.prefab beside CityBootstrap.
