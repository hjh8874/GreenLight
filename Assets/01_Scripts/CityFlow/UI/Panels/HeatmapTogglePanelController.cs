using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public sealed class HeatmapTogglePanelController : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private Toggle tglHeatmapView;
        private CityFlowServices _services;
        private bool _subscribed;

        public void Configure(Toggle heatmapToggle)
        {
            tglHeatmapView = heatmapToggle;
            BindToggle();
        }

        public void Initialize(CityFlowServices services)
        {
            _services = services;
            if (tglHeatmapView == null || _services?.Events == null || _subscribed)
            {
                return;
            }

            tglHeatmapView.SetIsOnWithoutNotify(_services.Events.IsHeatmapViewEnabled);
            _services.Events.HeatmapViewToggled += OnExternalToggleChanged;
            _subscribed = true;
            BindToggle();
        }

        private void Awake() => BindToggle();

        private void BindToggle()
        {
            if (tglHeatmapView == null) return;
            tglHeatmapView.onValueChanged.RemoveListener(OnHeatmapToggleChanged);
            tglHeatmapView.onValueChanged.AddListener(OnHeatmapToggleChanged);
        }

        private void OnHeatmapToggleChanged(bool isOn)
        {
            _services?.Events?.PublishHeatmapViewToggled(isOn);
        }

        private void OnExternalToggleChanged(bool isOn)
        {
            if (tglHeatmapView != null && tglHeatmapView.isOn != isOn)
            {
                tglHeatmapView.SetIsOnWithoutNotify(isOn);
            }
        }

        private void OnDestroy()
        {
            if (tglHeatmapView != null)
            {
                tglHeatmapView.onValueChanged.RemoveListener(OnHeatmapToggleChanged);
            }
            if (_services?.Events != null)
            {
                _services.Events.HeatmapViewToggled -= OnExternalToggleChanged;
            }
            _subscribed = false;
        }
    }
}
