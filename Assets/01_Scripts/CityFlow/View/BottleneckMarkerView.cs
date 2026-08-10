using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Sim;
using UnityEngine;

namespace CityFlow.View
{
    public sealed class BottleneckMarkerView : MonoBehaviour,
        ICityFlowServiceConsumer
    {
        internal const float DefaultVisibilityThreshold = 0.1f;

        private sealed class Marker
        {
            public Vector2Int Tile;
            public GameObject Object;
            public Renderer[] Renderers;
            public MaterialPropertyBlock PropertyBlock;
            public float Visible01;
        }

        private readonly Dictionary<Vector2Int, Marker> markers = new();
        private FreeFlowStreakVfxProfileSO profile;
        private IFreeFlowStreakLedger ledger;
        private SimEngine simEngine;
        private MainCityView mainView;
        private CityFlowServices services;
        private bool initialized;

        public void Initialize(CityFlowServices services)
        {
            if (initialized)
            {
                return;
            }

            profile = Resources.Load<FreeFlowStreakVfxProfileSO>(
                "CityFlow/FreeFlowStreakVfxProfile");
            this.services = services;
            ledger = services?.FreeFlowStreaks;
            simEngine = services?.Placement as SimEngine;
            mainView = GetComponent<MainCityView>();
            if (profile == null || ledger == null || simEngine == null ||
                mainView == null || profile.BottleneckMarkerPrefab == null)
            {
                return;
            }

            services.Events.Placed += OnPlaced;
            initialized = true;
            RebuildMarkers();
        }

        private void Awake()
        {
            mainView = GetComponent<MainCityView>();
        }

        private void Update()
        {
            if (!initialized)
            {
                return;
            }

            float fadeStep = Time.deltaTime /
                profile.BottleneckMarkerFadeSeconds;
            foreach (Marker marker in markers.Values)
            {
                float intensity = ledger.GetBottleneckIntensity(marker.Tile);
                float target = IsVisibleIntensity(
                    intensity,
                    profile.BottleneckMarkerThreshold)
                    ? intensity
                    : 0f;
                marker.Visible01 = Mathf.MoveTowards(
                    marker.Visible01,
                    target,
                    fadeStep);
                ApplyMarkerVisual(marker);
            }
        }

        private void OnPlaced(PlacedEvent unused)
        {
            RebuildMarkers();
        }

        private void RebuildMarkers()
        {
            if (!initialized && simEngine == null)
            {
                return;
            }

            foreach (Marker marker in markers.Values)
            {
                if (marker.Object != null)
                {
                    Destroy(marker.Object);
                }
            }
            markers.Clear();

            for (int y = 0; y < simEngine.GridHeight; y++)
            {
                for (int x = 0; x < simEngine.GridWidth; x++)
                {
                    Vector2Int tile = new Vector2Int(x, y) + mainView.GridOrigin;
                    if (!simEngine.IsSharedCarIntersection(tile))
                    {
                        continue;
                    }

                    GameObject markerObject = Instantiate(
                        profile.BottleneckMarkerPrefab,
                        transform,
                        false);
                    markerObject.name = $"BottleneckMarker_{tile.x}_{tile.y}";
                    markerObject.transform.localPosition =
                        mainView.GridToLocal(tile, -0.43f);
                    markerObject.transform.localScale =
                        Vector3.one *
                        (mainView.TileSize * profile.BottleneckMarkerScale);
                    markerObject.SetActive(false);
                    markers.Add(tile, new Marker
                    {
                        Tile = tile,
                        Object = markerObject,
                        Renderers = markerObject.GetComponentsInChildren<Renderer>(true),
                        PropertyBlock = new MaterialPropertyBlock(),
                        Visible01 = 0f
                    });
                }
            }
        }

        private void ApplyMarkerVisual(Marker marker)
        {
            bool visible = marker.Visible01 > 0.001f;
            if (marker.Object.activeSelf != visible)
            {
                marker.Object.SetActive(visible);
            }

            if (!visible)
            {
                return;
            }

            Color color = profile.BottleneckMarkerColor;
            color.a *= marker.Visible01;
            for (int index = 0; index < marker.Renderers.Length; index++)
            {
                Renderer renderer = marker.Renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(marker.PropertyBlock);
                Material material = renderer.sharedMaterial;
                if (material != null && material.HasProperty("_BaseColor"))
                {
                    marker.PropertyBlock.SetColor("_BaseColor", color);
                }

                if (material != null && material.HasProperty("_Color"))
                {
                    marker.PropertyBlock.SetColor("_Color", color);
                }

                renderer.SetPropertyBlock(marker.PropertyBlock);
            }
        }

        internal static bool IsVisibleIntensityForTest(float intensity) =>
            IsVisibleIntensity(intensity, DefaultVisibilityThreshold);

        private static bool IsVisibleIntensity(
            float intensity,
            float threshold) =>
            intensity >= Mathf.Clamp01(threshold);

        private void OnDestroy()
        {
            if (initialized && services != null)
            {
                services.Events.Placed -= OnPlaced;
            }

            foreach (Marker marker in markers.Values)
            {
                if (marker.Object != null)
                {
                    Destroy(marker.Object);
                }
            }
            markers.Clear();
        }
    }
}
