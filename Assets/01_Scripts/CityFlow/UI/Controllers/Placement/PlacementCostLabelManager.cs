using TMPro;
using UnityEngine;

namespace CityFlow.UI.Controllers.Placement
{
    internal class PlacementCostLabelManager
    {
        private const int GhostSortingOrder = 100;
        private const float CostUpdateInterval = 0.2f;

        private GameObject _costLabelObj;
        private TextMeshPro _costLabelTMP;
        private long _lastDisplayedCost = -1;
        private bool _lastDisplayedAffordable = true;
        private float _costUpdateTimer;

        private readonly Color _affordableColor;
        private readonly Color _unaffordableColor;
        private readonly bool _showCostLabel;

        public PlacementCostLabelManager(bool showCostLabel, Color affordableColor, Color unaffordableColor)
        {
            _showCostLabel = showCostLabel;
            _affordableColor = affordableColor;
            _unaffordableColor = unaffordableColor;
        }

        public void Initialize()
        {
            if (!_showCostLabel || _costLabelObj != null) return;

            _costLabelObj = new GameObject("GhostCostLabel");
            _costLabelObj.hideFlags = HideFlags.HideAndDontSave;

            _costLabelTMP = _costLabelObj.AddComponent<TextMeshPro>();
            _costLabelTMP.alignment = TextAlignmentOptions.Center;
            _costLabelTMP.fontSize = 3f;
            _costLabelTMP.fontStyle = FontStyles.Bold;
            _costLabelTMP.enableWordWrapping = false;
            _costLabelTMP.overflowMode = TextOverflowModes.Overflow;
            _costLabelTMP.sortingOrder = GhostSortingOrder + 10;

            _costLabelObj.SetActive(false);
        }

        public void Cleanup()
        {
            if (_costLabelObj != null)
            {
                UnityEngine.Object.Destroy(_costLabelObj);
            }
        }

        public void ResetState()
        {
            _lastDisplayedCost = -1;
            SetCostLabelActive(false);
        }

        public void SetCostLabelActive(bool active)
        {
            if (_costLabelObj != null && _costLabelObj.activeSelf != active)
            {
                _costLabelObj.SetActive(active);
            }
        }

        public void SyncPosition(Vector3 ghostPos, float surfaceZ, bool useXYPlane)
        {
            if (_costLabelObj == null) return;

            if (useXYPlane)
            {
                _costLabelObj.transform.position = new Vector3(
                    ghostPos.x,
                    ghostPos.y,
                    ghostPos.z - 0.1f
                );
                _costLabelObj.transform.rotation = Quaternion.identity;
            }
            else
            {
                _costLabelObj.transform.position = new Vector3(
                    ghostPos.x,
                    surfaceZ + 0.05f,
                    ghostPos.z
                );
                _costLabelObj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
        }

        public void UpdateCost(long currentCost, bool affordable, bool canPlace, float deltaTime)
        {
            if (_costLabelTMP == null) return;

            _costUpdateTimer += deltaTime;

            if (currentCost == _lastDisplayedCost && affordable == _lastDisplayedAffordable
                && _costUpdateTimer < CostUpdateInterval)
            {
                return;
            }

            _costUpdateTimer = 0f;
            _lastDisplayedCost = currentCost;
            _lastDisplayedAffordable = affordable;

            if (currentCost <= 0)
            {
                SetCostLabelActive(false);
                return;
            }

            SetCostLabelActive(true);

            _costLabelTMP.text = $"$ {currentCost:N0}";
            _costLabelTMP.color = (affordable && canPlace) ? _affordableColor : _unaffordableColor;
        }
    }
}
