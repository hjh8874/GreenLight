using CityFlow.Bootstrap;
using CityFlow.Content.Transit;
using CityFlow.Contracts;
using CityFlow.View;
using UnityEngine;

namespace CityFlow.DebugTools
{
    /// <summary>
    /// Prototype-only station markers driven by the shared stop registry.
    /// Final art can replace the generated pole and sign without changing
    /// route or registration code.
    /// </summary>
    public sealed class CityBusStopWorldView :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        [SerializeField] private BusStopRegistry stopRegistry;
        [SerializeField] private MainCityView cityView;
        [SerializeField] private GameObject stationPrefab;
        [SerializeField] private Material stationMaterial;
        [SerializeField] private float visualDepth = -0.24f;
        [SerializeField] private Vector2 stationFootprint =
            new(0.65f, 0.32f);

        private Transform markerRoot;
        private IReadOnlyTileData tileData;
        private bool subscribed;
        private int visibleStationCount;

        public int VisibleStationCount => visibleStationCount;
        public int VisiblePlatformCount =>
            markerRoot != null
                ? markerRoot.childCount
                : 0;

        public void Initialize(CityFlowServices services)
        {
            tileData = services?.TileData;
            ResolveReferences();
            Subscribe();
            RebuildMarkers();
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
            RebuildMarkers();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void ResolveReferences()
        {
            stopRegistry ??= GetComponent<BusStopRegistry>();
            cityView ??= FindFirstObjectByType<MainCityView>();
        }

        private void Subscribe()
        {
            if (subscribed || stopRegistry == null)
            {
                return;
            }

            stopRegistry.RegistryChanged += RebuildMarkers;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || stopRegistry == null)
            {
                return;
            }

            stopRegistry.RegistryChanged -= RebuildMarkers;
            subscribed = false;
        }

        private void RebuildMarkers()
        {
            if (stopRegistry == null || cityView == null)
            {
                return;
            }

            EnsureMarkerRoot();

            for (int i = markerRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(markerRoot.GetChild(i).gameObject);
            }

            visibleStationCount = stopRegistry.BusStops.Count;
            for (int i = 0; i < stopRegistry.BusStops.Count; i++)
            {
                CreateMarkerPair(stopRegistry.BusStops[i], i + 1);
            }
        }

        private void EnsureMarkerRoot()
        {
            if (markerRoot != null)
            {
                return;
            }

            var root = new GameObject("CityBusStops");
            markerRoot = root.transform;
            markerRoot.SetParent(cityView.transform, false);
        }

        private void CreateMarkerPair(Vector2Int tile, int number)
        {
            if (tileData != null &&
                BusStopInfrastructurePolicy.TryGetPlatformPair(
                    tile,
                    IsRoad,
                    out Vector2Int accessRoad,
                    out Vector2Int oppositePlatform))
            {
                CreateMarker(
                    tile,
                    accessRoad,
                    $"BusStop_{number:00}_A_{tile.x}_{tile.y}");

                CreateMarker(
                    oppositePlatform,
                    accessRoad,
                    $"BusStop_{number:00}_B_" +
                    $"{oppositePlatform.x}_{oppositePlatform.y}");
                return;
            }

            CreateMarker(
                tile,
                tile + Vector2Int.up,
                $"BusStop_{number:00}_A_{tile.x}_{tile.y}");
        }

        private void CreateMarker(
            Vector2Int tile,
            Vector2Int accessRoad,
            string objectName)
        {
            GameObject station = CreateStationVisual(
                markerRoot,
                objectName);
            Transform stationTransform = station.transform;
            Vector2Int localTile = tile - cityView.GridOrigin;
            stationTransform.localPosition = new Vector3(
                localTile.x + 0.5f,
                localTile.y + 0.5f,
                cityView != null
                    ? cityView.RoadSurfaceZ
                    : visualDepth);
            stationTransform.localRotation =
                GetStationFacingRotation(
                    tile,
                    accessRoad);
        }

        public bool TryCreatePlacementPreview(
            out GameObject preview)
        {
            preview = new GameObject(
                "PlacementPreview_BusStop");
            GameObject primaryVisual =
                CreateStationVisual(
                    preview.transform,
                    "PrimaryPlatform");
            primaryVisual.transform.localPosition =
                Vector3.zero;
            primaryVisual.transform.localRotation =
                Quaternion.identity;
            return true;
        }

        public bool TryCreatePlacementPreview(
            Vector2Int tile,
            out GameObject preview)
        {
            if (!TryCreatePlacementPreview(out preview) ||
                tileData == null ||
                !BusStopInfrastructurePolicy.TryGetPlatformPair(
                    tile,
                    IsRoad,
                    out Vector2Int accessRoad,
                    out Vector2Int oppositePlatform))
            {
                return preview != null;
            }

            Quaternion previewRotation =
                GetStationFacingRotation(
                    tile,
                    accessRoad);
            Transform primaryVisual =
                preview.transform.Find("PrimaryPlatform");
            if (primaryVisual != null)
            {
                primaryVisual.localRotation = previewRotation;
            }

            GameObject oppositeVisual = CreateStationVisual(
                preview.transform,
                "OppositePlatform");
            Vector2Int offset = oppositePlatform - tile;
            float tileSize = cityView != null
                ? cityView.TileSize
                : 1f;
            oppositeVisual.transform.localPosition = new Vector3(
                offset.x * tileSize,
                offset.y * tileSize,
                0f);
            oppositeVisual.transform.localRotation =
                GetStationFacingRotation(
                    oppositePlatform,
                    accessRoad);
            return true;
        }

        private static Quaternion GetStationFacingRotation(
            Vector2Int platform,
            Vector2Int accessRoad)
        {
            Vector2Int towardRoad = accessRoad - platform;
            if (towardRoad == Vector2Int.zero)
            {
                return Quaternion.identity;
            }

            float angle = Mathf.Atan2(
                              towardRoad.y,
                              towardRoad.x) *
                          Mathf.Rad2Deg - 90f;
            return Quaternion.Euler(0f, 0f, angle);
        }

        private bool IsRoad(Vector2Int tile) =>
            tileData != null &&
            tileData.GetTileType(tile) == TileType.Road;

        private GameObject CreateStationVisual(
            Transform parent,
            string objectName)
        {
            if (stationPrefab != null)
            {
                var authoredStation = new GameObject(objectName);
                authoredStation.transform.SetParent(parent, false);

                GameObject model =
                    Instantiate(
                        stationPrefab,
                        authoredStation.transform);
                model.name = "BusStopModel";
                FitStationPrefab(
                    model.transform,
                    authoredStation.transform);
                return authoredStation;
            }

            var station = new GameObject(objectName);
            Transform stationTransform =
                station.transform;
            stationTransform.SetParent(parent, false);

            CreatePart(
                stationTransform,
                "Pole",
                new Vector3(0f, 0f, -0.18f),
                new Vector3(0.08f, 0.08f, 0.42f));
            CreatePart(
                stationTransform,
                "StopSign",
                new Vector3(0f, 0f, -0.42f),
                new Vector3(0.28f, 0.16f, 0.08f));
            CreatePart(
                stationTransform,
                "WaitingPad",
                new Vector3(0f, 0f, 0.02f),
                new Vector3(0.52f, 0.34f, 0.04f));
            return station;
        }

        private void FitStationPrefab(
            Transform model,
            Transform relativeTo)
        {
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            model.localScale = Vector3.one;

            if (!TryGetRendererBounds(
                    model.gameObject,
                    relativeTo,
                    out Bounds sourceBounds))
            {
                return;
            }

            float scale = Mathf.Min(
                stationFootprint.x /
                    Mathf.Max(0.0001f, sourceBounds.size.x),
                stationFootprint.y /
                    Mathf.Max(0.0001f, sourceBounds.size.y));
            model.localScale = Vector3.one * scale;

            if (!TryGetRendererBounds(
                    model.gameObject,
                    relativeTo,
                    out Bounds fittedBounds))
            {
                return;
            }

            model.localPosition = new Vector3(
                -fittedBounds.center.x,
                -fittedBounds.center.y,
                -fittedBounds.max.z);
        }

        private static bool TryGetRendererBounds(
            GameObject root,
            Transform relativeTo,
            out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                Bounds localBounds = renderer.localBounds;
                Vector3 min = localBounds.min;
                Vector3 max = localBounds.max;

                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = new Vector3(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    point = relativeTo.InverseTransformPoint(
                        renderer.transform.TransformPoint(point));

                    if (!hasBounds)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }

            return hasBounds;
        }

        private void CreatePart(
            Transform parent,
            string partName,
            Vector3 localPosition,
            Vector3 localScale)
        {
            GameObject part =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = partName;
            Transform partTransform = part.transform;
            partTransform.SetParent(parent, false);
            partTransform.localPosition = localPosition;
            partTransform.localScale = localScale;

            if (part.TryGetComponent(out Collider targetCollider))
            {
                Destroy(targetCollider);
            }

            if (stationMaterial != null &&
                part.TryGetComponent(out Renderer targetRenderer))
            {
                targetRenderer.sharedMaterial = stationMaterial;
            }
        }
    }
}
