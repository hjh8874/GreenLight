using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.View
{
    public sealed class FlowBurstView : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private Transform burstMarker;
        [SerializeField] private float visibleSeconds = 0.6f;
        [SerializeField] private float maxScale = 1.5f;

        private float hideAtTime;
        private CityFlowServices services;

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            this.services = services;
            services.Events.FlowBurst += OnFlowBurst;
            EnsureBurstMarker();

            if (burstMarker != null)
            {
                burstMarker.gameObject.SetActive(false);
            }
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
            burstMarker.localScale = Vector3.one * Mathf.Lerp(maxScale, 0.2f, remaining01);

            if (Time.time >= hideAtTime)
            {
                burstMarker.gameObject.SetActive(false);
            }
        }

        private void OnFlowBurst(FlowBurstEvent e)
        {
            EnsureBurstMarker();

            if (burstMarker == null)
            {
                return;
            }

            burstMarker.position = GridUtil.GridToWorld(e.Tile);
            burstMarker.localScale = Vector3.one * maxScale;
            burstMarker.gameObject.SetActive(true);
            hideAtTime = Time.time + visibleSeconds;
        }

        private void EnsureBurstMarker()
        {
            if (burstMarker != null)
            {
                return;
            }

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "FlowBurstMarker";
            marker.transform.SetParent(transform, false);

            Collider markerCollider = marker.GetComponent<Collider>();
            if (markerCollider != null)
            {
                Destroy(markerCollider);
            }

            Renderer markerRenderer = marker.GetComponent<Renderer>();
            if (markerRenderer != null)
            {
                markerRenderer.material.color = new Color(1f, 0.9f, 0.15f, 0.85f);
            }

            burstMarker = marker.transform;
        }
    }
}
