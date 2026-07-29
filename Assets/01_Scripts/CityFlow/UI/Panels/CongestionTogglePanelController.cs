using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public class CongestionTogglePanelController : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("UI References")]
        [SerializeField] private Toggle tglCongestionView;

        private CityFlowServices _services;
        private bool _subscribed;

        public void Configure(Toggle congestionToggle)
        {
            tglCongestionView = congestionToggle;
            BindToggle();
        }

        public void Initialize(CityFlowServices services)
        {
            _services = services;
            if (tglCongestionView != null && _services?.Events != null && !_subscribed)
            {
                tglCongestionView.SetIsOnWithoutNotify(_services.Events.IsCongestionViewEnabled);
                
                // Subscribe to external changes
                _services.Events.CongestionViewToggled += OnExternalToggleChanged;
                _subscribed = true;
            }
        }

        private void Start()
        {
            BindToggle();
        }

        private void BindToggle()
        {
            if (tglCongestionView != null)
            {
                tglCongestionView.onValueChanged.RemoveListener(OnCongestionToggleChanged);
                tglCongestionView.onValueChanged.AddListener(OnCongestionToggleChanged);
            }
        }

        private void OnCongestionToggleChanged(bool isOn)
        {
            if (_services != null && _services.Events != null)
            {
                _services.Events.PublishCongestionViewToggled(isOn);
                Debug.Log($"[Settings] 혼잡도 뷰 토글: {isOn}");
            }
        }

        private void OnExternalToggleChanged(bool isOn)
        {
            if (tglCongestionView != null && tglCongestionView.isOn != isOn)
            {
                tglCongestionView.SetIsOnWithoutNotify(isOn);
            }
        }

        private void OnDestroy()
        {
            if (tglCongestionView != null)
            {
                tglCongestionView.onValueChanged.RemoveListener(OnCongestionToggleChanged);
            }
            if (_services != null && _services.Events != null)
            {
                _services.Events.CongestionViewToggled -= OnExternalToggleChanged;
            }
            _subscribed = false;
        }
    }
}
