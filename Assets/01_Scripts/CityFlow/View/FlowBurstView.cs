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

        public void Initialize(CityFlowServices services)
        {
            services.Events.FlowBurst += OnFlowBurst;

            if (burstMarker != null)
            {
                burstMarker.gameObject.SetActive(false);
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
            if (burstMarker == null)
            {
                return;
            }

            burstMarker.position = GridUtil.GridToWorld(e.Tile);
            burstMarker.localScale = Vector3.one * maxScale;
            burstMarker.gameObject.SetActive(true);
            hideAtTime = Time.time + visibleSeconds;
        }
    }
}
