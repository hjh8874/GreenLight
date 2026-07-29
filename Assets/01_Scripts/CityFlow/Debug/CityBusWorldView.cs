using CityFlow.Bootstrap;
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
        [SerializeField] private MainCityView cityView;
        [SerializeField] private GameObject busVisualPrefab;
        [SerializeField] private Material busMaterial;

        [Header("Presentation")]
        [SerializeField, Min(0.01f)]
        private float visualScale = 0.085f;
        [SerializeField]
        private float visualDepth = -0.38f;
        [SerializeField, Min(0.01f)]
        private float movementDuration = 0.22f;
        [SerializeField, Range(0f, 0.5f)]
        private float laneOffset = 0.18f;

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
        private bool hasLastRoadTile;
        private bool subscribed;

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

            movementElapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(
                movementElapsed /
                Mathf.Max(0.01f, movementDuration));

            visual.localPosition = Vector3.Lerp(
                movementStartPosition,
                targetLocalPosition,
                progress);
            visual.localRotation = Quaternion.Slerp(
                movementStartRotation,
                targetLocalRotation,
                progress);
        }

        private void ResolveReferences()
        {
            busRoute ??= GetComponent<BusRoute>();
            cityView ??= FindAnyObjectByType<MainCityView>();
        }

        private void Subscribe()
        {
            if (subscribed || busRoute == null)
            {
                return;
            }

            busRoute.TileChanged += HandleTileChanged;
            busRoute.RouteUnavailable += HandleRouteUnavailable;
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
            Vector3 laneRight = new(
                travelDirection.y,
                -travelDirection.x,
                0f);
            Vector3 nextPosition = new(
                tile.x + 0.5f,
                tile.y + 0.5f,
                visualDepth);
            nextPosition += laneRight * laneOffset;

            Quaternion nextRotation = CreateRotation(travelDirection);

            if (!hasTarget)
            {
                visual.localPosition = nextPosition;
                visual.localRotation = nextRotation;
                visual.gameObject.SetActive(true);
                movementStartPosition = nextPosition;
                targetLocalPosition = nextPosition;
                movementStartRotation = nextRotation;
                targetLocalRotation = nextRotation;
                movementElapsed = movementDuration;
                hasTarget = true;
                RememberRoadTile(tile, travelDirection);
                return;
            }

            movementStartPosition = visual.localPosition;
            movementStartRotation = visual.localRotation;
            targetLocalPosition = nextPosition;
            targetLocalRotation = nextRotation;
            movementElapsed = 0f;
            RememberRoadTile(tile, travelDirection);
            visual.gameObject.SetActive(true);
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
            hasLastRoadTile = false;
            lastTravelDirection = default;

            if (visual != null)
            {
                visual.gameObject.SetActive(false);
            }
        }
    }
}
