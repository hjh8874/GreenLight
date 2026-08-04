using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    /// <summary>Translates the Sim's city-wide jam ratio into the mini HUD dot.</summary>
    public sealed class FloatingCongestionDot : MonoBehaviour, ICityFlowServiceConsumer
    {
        private const float SlowThreshold01 = 0.25f;
        private const float JamThreshold01 = 0.60f;

        [SerializeField] private Image dotImage;

        private ICongestionHistory _history;

        public void Configure(Image image)
        {
            dotImage = image;
        }

        public void Initialize(CityFlowServices services)
        {
            _history = services?.Placement as ICongestionHistory;
            Refresh();
        }

        private void Awake()
        {
            if (dotImage == null)
            {
                dotImage = GetComponent<Image>();
            }
        }

        private void Update()
        {
            Refresh();
        }

        private void Refresh()
        {
            if (dotImage == null || _history == null)
            {
                return;
            }

            float jamRatio = _history.CityJamRatio01;
            dotImage.color = jamRatio >= JamThreshold01
                ? Color.red
                : jamRatio >= SlowThreshold01
                    ? Color.yellow
                    : Color.green;
        }
    }
}
