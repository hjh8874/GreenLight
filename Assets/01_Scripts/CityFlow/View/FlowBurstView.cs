using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.View
{
    public sealed class FlowBurstView : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private Transform burstMarker;
        [SerializeField] private float visibleSeconds = 0.6f;
        [SerializeField, Min(0.1f)] private float maxScale = 1.5f;

        private const float MinimumVisibleScale = 1f;

        private sealed class BurstInstance
        {
            public Transform Marker;
            public Vector3 BaseScale;
            public float HideAtTime;
            public Transform TargetVehicle;
        }

        private readonly List<BurstInstance> burstInstances = new();
        private CityFlowServices services;
        private MainCityView cityView;

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

            for (int i = 0; i < burstInstances.Count; i++)
            {
                BurstInstance instance = burstInstances[i];
                if (instance.Marker != null)
                {
                    instance.Marker.gameObject.SetActive(false);
                }

                instance.TargetVehicle = null;
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
            float duration = Mathf.Max(0.01f, visibleSeconds);
            float visibleMaxScale = Mathf.Max(MinimumVisibleScale, maxScale);
            for (int i = 0; i < burstInstances.Count; i++)
            {
                BurstInstance instance = burstInstances[i];
                if (instance.Marker == null || !instance.Marker.gameObject.activeSelf)
                {
                    continue;
                }

                float remaining01 = Mathf.Clamp01((instance.HideAtTime - Time.time) / duration);
                float elapsed01 = 1f - remaining01;
                float pulse = Mathf.Sin(elapsed01 * Mathf.PI);
                instance.Marker.localScale = instance.BaseScale
                    * Mathf.Lerp(0.15f, visibleMaxScale, pulse);

                if (instance.TargetVehicle != null
                    && instance.TargetVehicle.gameObject.activeInHierarchy
                    && cityView != null)
                {
                    instance.Marker.position = instance.TargetVehicle.position
                        - cityView.transform.forward * (cityView.TileSize * 0.35f);
                }

                if (Time.time >= instance.HideAtTime)
                {
                    instance.Marker.gameObject.SetActive(false);
                    instance.TargetVehicle = null;
                }
            }
        }

        private void OnFlowBurst(FlowBurstEvent e)
        {
            BurstInstance instance = GetOrCreateBurstInstance();
            instance.TargetVehicle = null;
            instance.Marker.position = cityView != null
                ? cityView.GetFlowBurstAnchor(e.Tile, out instance.TargetVehicle)
                : GridUtil.GridToWorld(e.Tile);
            instance.Marker.localScale = instance.BaseScale * 0.15f;
            instance.Marker.gameObject.SetActive(true);
            instance.HideAtTime = Time.time + Mathf.Max(0.01f, visibleSeconds);
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
                RegisterBurstMarker(burstMarker);
                return;
            }

            burstMarker = CreateBurstMarker();
            RegisterBurstMarker(burstMarker);
        }

        private BurstInstance GetOrCreateBurstInstance()
        {
            EnsureBurstMarker();
            for (int i = 0; i < burstInstances.Count; i++)
            {
                BurstInstance instance = burstInstances[i];
                if (instance.Marker != null && !instance.Marker.gameObject.activeSelf)
                {
                    return instance;
                }
            }

            return RegisterBurstMarker(CreateBurstMarker());
        }

        private BurstInstance RegisterBurstMarker(Transform marker)
        {
            for (int i = 0; i < burstInstances.Count; i++)
            {
                if (burstInstances[i].Marker == marker)
                {
                    return burstInstances[i];
                }
            }

            var instance = new BurstInstance
            {
                Marker = marker,
                BaseScale = marker.localScale
            };
            burstInstances.Add(instance);
            marker.gameObject.SetActive(false);
            return instance;
        }

        private Transform CreateBurstMarker()
        {
            GameObject marker = cityView != null && cityView.FlowBurstPrefab != null
                ? Instantiate(cityView.FlowBurstPrefab)
                : GameObject.CreatePrimitive(PrimitiveType.Sphere);

            marker.name = "FlowBurstMarker";
            marker.transform.SetParent(cityView != null && cityView.EffectRoot != null
                ? cityView.EffectRoot
                : transform, false);
            marker.transform.localScale = Vector3.one
                * (cityView != null ? cityView.TileSize : GridUtil.TileSize);

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

            marker.SetActive(false);
            return marker.transform;
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
