using CityFlow.Bootstrap;
using CityFlow.Content.Transit;
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
        [SerializeField] private Material stationMaterial;
        [SerializeField] private float visualDepth = -0.24f;

        private Transform markerRoot;
        private bool subscribed;

        public int VisibleStationCount =>
            markerRoot != null
                ? markerRoot.childCount
                : 0;

        public void Initialize(CityFlowServices _)
        {
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

            for (int i = 0; i < stopRegistry.BusStops.Count; i++)
            {
                CreateMarker(stopRegistry.BusStops[i], i + 1);
            }
        }

        private void EnsureMarkerRoot()
        {
            if (markerRoot != null)
            {
                return;
            }

            var root = new GameObject("PR151_CityBusStops");
            markerRoot = root.transform;
            markerRoot.SetParent(cityView.transform, false);
        }

        private void CreateMarker(Vector2Int tile, int number)
        {
            var station = new GameObject(
                $"BusStop_{number:00}_{tile.x}_{tile.y}");
            Transform stationTransform = station.transform;
            stationTransform.SetParent(markerRoot, false);
            stationTransform.localPosition = new Vector3(
                tile.x + 0.5f,
                tile.y + 0.5f,
                visualDepth);

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
