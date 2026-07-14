using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.View
{
    public sealed class FlowBurstView : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private Transform burstMarker;
        [SerializeField] private float visibleSeconds = 0.6f;
        [SerializeField] private float maxScale = 0.4f;

        private float hideAtTime;
        private CityFlowServices services;
        private MainCityView cityView;
        private Vector3 markerBaseScale = Vector3.one;
        private Transform activeVehicle;

        public void Configure(MainCityView mainCityView)
        {
            cityView = mainCityView;
            visibleSeconds = cityView.FlowBurstSeconds;
            EnsureBurstMarker();
        }

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled || this.services == services)
            {
                return;
            }

            if (this.services != null)
            {
                this.services.Events.FlowBurst -= OnFlowBurst;
            }

            this.services = services;
            services.Events.FlowBurst += OnFlowBurst;
            cityView ??= GetComponent<MainCityView>();
            EnsureBurstMarker();

            if (burstMarker != null)
            {
                burstMarker.gameObject.SetActive(false);
            }
        }

        private void Awake()
        {
            cityView = GetComponent<MainCityView>();
        }

        private void OnDestroy()
        {
            // 구독했으면 파괴 시 반드시 해제 — 좀비 호출·메모리 누수 방지.
            if (services != null)
            {
                services.Events.FlowBurst -= OnFlowBurst;
            }
        }

        private void Update()
        {
            if (burstMarker == null || !burstMarker.gameObject.activeSelf)
            {
                return;
            }

            float remaining01 = Mathf.Clamp01((hideAtTime - Time.time) / visibleSeconds);
            float elapsed01 = 1f - remaining01;
            float pulse = Mathf.Sin(elapsed01 * Mathf.PI);
            burstMarker.localScale = markerBaseScale * Mathf.Lerp(0.15f, maxScale, pulse);

            if (activeVehicle != null && activeVehicle.gameObject.activeInHierarchy && cityView != null)
            {
                burstMarker.position = activeVehicle.position
                    - cityView.transform.forward * (cityView.TileSize * 0.35f);
            }

            if (Time.time >= hideAtTime)
            {
                burstMarker.gameObject.SetActive(false);
                activeVehicle = null;
            }
        }

        private void OnFlowBurst(FlowBurstEvent e)
        {
            if (burstMarker == null)
            {
                return;
            }

            activeVehicle = null;
            burstMarker.position = cityView != null
                ? cityView.GetFlowBurstAnchor(e.Tile, out activeVehicle)
                : GridUtil.GridToWorld(e.Tile);
            burstMarker.localScale = markerBaseScale * 0.15f;
            burstMarker.gameObject.SetActive(true);
            hideAtTime = Time.time + visibleSeconds;
        }

        private void EnsureBurstMarker()
        {
            if (burstMarker != null)
            {
                if (cityView != null && cityView.EffectRoot != null
                    && burstMarker.parent != cityView.EffectRoot)
                {
                    burstMarker.SetParent(cityView.EffectRoot, false);
                }
                markerBaseScale = burstMarker.localScale;
                return;
            }

            GameObject marker = cityView != null && cityView.FlowBurstPrefab != null
                ? Instantiate(cityView.FlowBurstPrefab)
                : GameObject.CreatePrimitive(PrimitiveType.Sphere);

            marker.name = "FlowBurstMarker";
            marker.transform.SetParent(cityView != null && cityView.EffectRoot != null
                ? cityView.EffectRoot
                : transform, false);
            markerBaseScale = Vector3.one * (cityView != null ? cityView.TileSize : GridUtil.TileSize);
            marker.transform.localScale = markerBaseScale;

            if (cityView == null || cityView.FlowBurstPrefab == null)
            {
                Collider markerCollider = marker.GetComponent<Collider>();
                if (markerCollider != null)
                {
                    Destroy(markerCollider);
                }

                Renderer markerRenderer = marker.GetComponent<Renderer>();
                if (markerRenderer != null)
                {
                    Color color = cityView != null ? cityView.FlowBurstColor : new Color(1f, 0.78f, 0.12f);
                    markerRenderer.sharedMaterial = CreateMarkerMaterial(color);
                }
            }

            burstMarker = marker.transform;
            burstMarker.gameObject.SetActive(false);
        }

        private static Material CreateMarkerMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            shader ??= Shader.Find("Unlit/Color");
            shader ??= Shader.Find("Sprites/Default");
            shader ??= Shader.Find("Standard");

            Material material = new Material(shader);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            return material;
        }
    }
}
