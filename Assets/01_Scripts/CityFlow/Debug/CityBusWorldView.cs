using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Content.Transit;
using CityFlow.View;
using UnityEngine;

namespace CityFlow.DebugTools
{
    /// <summary>
    /// Presentation-only world view for the city bus prototype.
    /// The route owns simulation state; this component only translates it
    /// into a vehicle that follows the copied Debug scene's city grid.
    /// </summary>
    public sealed class CityBusWorldView :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        [SerializeField] private BusRoute busRoute;
        [SerializeField] private CityBusService cityBusService;
        [SerializeField] private MainCityView cityView;
        [SerializeField] private GameObject busVisualPrefab;
        [SerializeField] private Material busMaterial;

        [Header("Presentation")]
        [SerializeField, Min(0.01f)]
        private float visualScale = 0.085f;
        [SerializeField]
        private float visualDepth = -0.38f;
        [SerializeField, Min(0.01f)]
        private float movementDuration = 0.65f;

        private IReadOnlyTileData tileData;
        private Transform visual;
        private Vector3 movementStartPosition;
        private Vector3 targetLocalPosition;
        private Quaternion movementStartRotation;
        private Quaternion targetLocalRotation;
        private float movementElapsed;
        private bool hasTarget;
        private Vector2Int lastRoadTile;
        private Vector2 lastTravelDirection;
        private Vector2 currentVisualDirection;
        private bool hasLastRoadTile;
        private bool subscribed;
        private Vector3 previousVisualPosition;
        private float currentVisualSpeed;
        private bool visualBlockedByTraffic;

        public bool HasVisibleBus =>
            visual != null &&
            visual.gameObject.activeInHierarchy;

        public void Initialize(CityFlowServices services)
        {
            tileData = services?.TileData;
            ResolveReferences();
            EnsureVisual();
            Subscribe();
        }

        private void Awake()
        {
            ResolveReferences();
            EnsureVisual();
        }

        private void OnEnable()
        {
            ResolveReferences();
            EnsureVisual();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (!hasTarget || visual == null)
            {
                return;
            }

            float activeMovementDuration =
                GetMovementDuration();
            if (movementElapsed >= activeMovementDuration)
            {
                visualBlockedByTraffic = false;
                currentVisualSpeed = 0f;
                previousVisualPosition =
                    visual.localPosition;
                PublishExternalTraffic();
                return;
            }

            float nextMovementElapsed =
                Mathf.Min(
                    movementElapsed + Time.deltaTime,
                    activeMovementDuration);
            float progress = Mathf.Clamp01(
                nextMovementElapsed /
                activeMovementDuration);
            Vector3 candidatePosition = Vector3.Lerp(
                movementStartPosition,
                targetLocalPosition,
                progress);
            Quaternion candidateRotation = Quaternion.Slerp(
                movementStartRotation,
                targetLocalRotation,
                progress);
            Vector2 candidateDirection =
                lastTravelDirection.sqrMagnitude > 0.5f
                    ? lastTravelDirection
                    : currentVisualDirection;

            float allowedMovementFraction = 1f;
            if (cityView != null &&
                TryGetTrafficFootprint(
                    out float collisionHalfLength,
                    out float collisionHalfWidth))
            {
                float proposedAdvance =
                    Vector3.Distance(
                        visual.localPosition,
                        candidatePosition);
                float allowedAdvance =
                    cityView
                        .LimitExternalTrafficVisualAdvance(
                            this,
                            visual.localPosition,
                            candidatePosition,
                            new Vector3(
                                candidateDirection.x,
                                candidateDirection.y,
                                0f),
                            GetMinimumHeadway(),
                            collisionHalfLength,
                            collisionHalfWidth);

                if (proposedAdvance > 0.0001f)
                {
                    allowedMovementFraction =
                        Mathf.Clamp01(
                            allowedAdvance /
                            proposedAdvance);
                }

                if (allowedMovementFraction <= 0.0001f)
                {
                    visualBlockedByTraffic = true;
                    currentVisualSpeed = 0f;
                    previousVisualPosition =
                        visual.localPosition;
                    PublishExternalTraffic();
                    return;
                }

                if (allowedMovementFraction <
                    1f - 0.0001f)
                {
                    nextMovementElapsed =
                        Mathf.Lerp(
                            movementElapsed,
                            nextMovementElapsed,
                            allowedMovementFraction);
                    candidatePosition =
                        Vector3.Lerp(
                            visual.localPosition,
                            candidatePosition,
                            allowedMovementFraction);
                    candidateRotation =
                        Quaternion.Slerp(
                            visual.localRotation,
                            candidateRotation,
                            allowedMovementFraction);
                }
            }

            visualBlockedByTraffic =
                allowedMovementFraction <
                1f - 0.0001f;
            movementElapsed = nextMovementElapsed;
            visual.localPosition = candidatePosition;
            visual.localRotation = candidateRotation;
            currentVisualDirection = candidateDirection;
            float deltaTime = Mathf.Max(
                Time.deltaTime,
                0.0001f);
            currentVisualSpeed =
                Vector3.Distance(
                    previousVisualPosition,
                    visual.localPosition) /
                deltaTime;
            previousVisualPosition = visual.localPosition;
            PublishExternalTraffic();
        }

        private void ResolveReferences()
        {
            busRoute ??= GetComponent<BusRoute>();
            cityBusService ??= GetComponent<CityBusService>();
            cityView ??= FindFirstObjectByType<MainCityView>();
        }

        private void Subscribe()
        {
            if (subscribed || busRoute == null)
            {
                return;
            }

            busRoute.TileChanged += HandleTileChanged;
            busRoute.RouteUnavailable += HandleRouteUnavailable;
            busRoute.CanEnterTile = CanEnterTile;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || busRoute == null)
            {
                return;
            }

            busRoute.TileChanged -= HandleTileChanged;
            busRoute.RouteUnavailable -= HandleRouteUnavailable;
            busRoute.CanEnterTile = null;
            cityView?.RemoveExternalTrafficVehicle(this);
            subscribed = false;
        }

        private void EnsureVisual()
        {
            if (visual != null ||
                busVisualPrefab == null ||
                cityView == null)
            {
                return;
            }

            GameObject instance = Instantiate(
                busVisualPrefab,
                cityView.transform);
            instance.name = "CityBusVisual";
            visual = instance.transform;
            visual.localScale = Vector3.one * visualScale;
            ApplyFeatureMaterial(instance);
            instance.SetActive(false);
            previousVisualPosition = visual.localPosition;
        }

        private void ApplyFeatureMaterial(GameObject instance)
        {
            if (busMaterial == null)
            {
                return;
            }

            Renderer[] renderers =
                instance.GetComponentsInChildren<Renderer>(true);

            foreach (Renderer targetRenderer in renderers)
            {
                Material[] materials =
                    targetRenderer.sharedMaterials;

                for (int i = 0; i < materials.Length; i++)
                {
                    materials[i] = busMaterial;
                }

                targetRenderer.sharedMaterials = materials;
            }
        }

        private void HandleTileChanged(Vector2Int tile)
        {
            EnsureVisual();

            if (visual == null || cityView == null || !IsRoad(tile))
            {
                return;
            }

            Vector2 travelDirection = ResolveTravelDirection(tile);
            Vector3 nextPosition =
                CreateLanePosition(
                    tile,
                    travelDirection);
            Quaternion nextRotation =
                CreateRotation(travelDirection);

            if (!hasTarget)
            {
                visual.localPosition = nextPosition;
                visual.localRotation = nextRotation;
                visual.gameObject.SetActive(true);
                movementStartPosition = nextPosition;
                targetLocalPosition = nextPosition;
                movementStartRotation = nextRotation;
                targetLocalRotation = nextRotation;
                movementElapsed = GetMovementDuration();
                hasTarget = true;
                visualBlockedByTraffic = false;
                currentVisualDirection = travelDirection;
                RememberRoadTile(tile, travelDirection);
                previousVisualPosition =
                    visual.localPosition;
                currentVisualSpeed = 0f;
                PublishExternalTraffic();
                return;
            }

            movementStartPosition = visual.localPosition;
            movementStartRotation = visual.localRotation;
            targetLocalPosition = nextPosition;
            targetLocalRotation = nextRotation;
            movementElapsed = 0f;
            visualBlockedByTraffic = false;
            RememberRoadTile(tile, travelDirection);
            visual.gameObject.SetActive(true);
            PublishExternalTraffic();
        }

        private bool CanEnterTile(
            Vector2Int currentTile,
            Vector2Int nextTile)
        {
            if (visualBlockedByTraffic)
            {
                return false;
            }

            if (cityView == null || !IsRoad(nextTile))
            {
                return true;
            }

            Vector2 direction =
                TryGetCardinalDirection(
                    nextTile - currentTile,
                    out Vector2 cardinal)
                    ? cardinal
                    : ResolveTravelDirection(nextTile);
            if (direction.sqrMagnitude < 0.5f)
            {
                direction = Vector2.right;
            }

            Vector3 currentPosition =
                visual != null
                    ? visual.localPosition
                    : CreateLanePosition(
                        currentTile,
                        direction);
            Vector3 nextPosition =
                CreateLanePosition(
                    nextTile,
                    direction);
            Vector3 forward =
                new(
                    direction.x,
                    direction.y,
                    0f);

            return cityView.CanExternalTrafficAdvance(
                this,
                currentPosition,
                nextPosition,
                forward,
                GetMinimumHeadway(),
                nextTile);
        }

        private float GetMovementDuration()
        {
            return Mathf.Max(
                0.01f,
                busRoute != null
                    ? busRoute.SecondsPerTile
                    : movementDuration);
        }

        private Vector3 CreateLanePosition(
            Vector2Int tile,
            Vector2 travelDirection)
        {
            Vector3 position =
                cityView.GridToLocal(
                    tile,
                    visualDepth);
            return position +
                   GetRightLaneOffset(
                       travelDirection,
                       cityView.LaneOffset *
                       cityView.TileSize);
        }

        private void PublishExternalTraffic()
        {
            if (cityView == null ||
                visual == null ||
                !TryGetTrafficFootprint(
                    out float collisionHalfLength,
                    out float collisionHalfWidth))
            {
                return;
            }

            cityView.UpdateExternalTrafficVehicle(
                this,
                visual.localPosition,
                new Vector3(
                    currentVisualDirection.x,
                    currentVisualDirection.y,
                    0f),
                currentVisualSpeed,
                hasLastRoadTile &&
                visual.gameObject.activeInHierarchy,
                lastRoadTile,
                hasLastRoadTile,
                collisionHalfLength,
                collisionHalfWidth);
        }

        private float GetMinimumHeadway()
        {
            return cityView.VehicleMinHeadway *
                   cityView.TileSize;
        }

        private bool TryGetTrafficFootprint(
            out float halfLength,
            out float halfWidth)
        {
            halfLength = 0f;
            halfWidth = 0f;
            BusDefinitionSO definition =
                cityBusService?.Definition;
            if (cityView == null || definition == null)
            {
                return false;
            }

            cityView.GetTrafficFootprint(
                definition.VehicleLengthTiles,
                definition.VehicleWidthTiles,
                out halfLength,
                out halfWidth);
            return true;
        }

        private bool IsRoad(Vector2Int tile)
        {
            return tileData == null ||
                   tileData.GetTileType(tile) == TileType.Road;
        }

        private Vector2 ResolveTravelDirection(Vector2Int tile)
        {
            if (hasLastRoadTile &&
                TryGetCardinalDirection(
                    tile - lastRoadTile,
                    out Vector2 incoming))
            {
                return incoming;
            }

            if (busRoute != null)
            {
                var path = busRoute.CurrentRoadPath;
                int startIndex = busRoute.CurrentRoadPathIndex + 1;

                for (int i = startIndex; i < path.Count; i++)
                {
                    Vector2Int candidate = path[i];

                    if (!IsRoad(candidate))
                    {
                        continue;
                    }

                    if (TryGetCardinalDirection(
                            candidate - tile,
                            out Vector2 outgoing))
                    {
                        return outgoing;
                    }
                }
            }

            return lastTravelDirection.sqrMagnitude > 0.5f
                ? lastTravelDirection
                : Vector2.right;
        }

        private static bool TryGetCardinalDirection(
            Vector2 delta,
            out Vector2 direction)
        {
            if (Mathf.Abs(delta.x) > 0.001f &&
                Mathf.Abs(delta.y) <= 0.001f)
            {
                direction = new Vector2(Mathf.Sign(delta.x), 0f);
                return true;
            }

            if (Mathf.Abs(delta.y) > 0.001f &&
                Mathf.Abs(delta.x) <= 0.001f)
            {
                direction = new Vector2(0f, Mathf.Sign(delta.y));
                return true;
            }

            direction = default;
            return false;
        }

        private static Quaternion CreateRotation(Vector2 direction)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) *
                          Mathf.Rad2Deg;
            return Quaternion.Euler(0f, 0f, angle + 90f) *
                   Quaternion.Euler(90f, 0f, 0f);
        }

        private static Vector3 GetRightLaneOffset(
            Vector2 travelDirection,
            float offset)
        {
            return new Vector3(
                travelDirection.y,
                -travelDirection.x,
                0f) * Mathf.Max(0f, offset);
        }

        private void RememberRoadTile(
            Vector2Int tile,
            Vector2 direction)
        {
            lastRoadTile = tile;
            lastTravelDirection = direction;
            hasLastRoadTile = true;
        }

        private void HandleRouteUnavailable()
        {
            hasTarget = false;
            visualBlockedByTraffic = false;
            currentVisualSpeed = 0f;

            if (visual != null && hasLastRoadTile)
            {
                visual.gameObject.SetActive(true);
                previousVisualPosition =
                    visual.localPosition;
            }

            PublishExternalTraffic();
        }
    }
}
