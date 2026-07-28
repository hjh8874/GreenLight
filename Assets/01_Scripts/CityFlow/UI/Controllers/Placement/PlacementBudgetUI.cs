using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CityFlow.Contracts;
using CityFlow.Bootstrap;

namespace CityFlow.UI.Controllers.Placement
{
    internal class PlacementBudgetUI
    {
        private readonly TextMeshProUGUI _roadBudgetText;
        private readonly Button _roadExpandButton;
        private readonly TextMeshProUGUI _roadExpandCostText;
        private readonly Color _expandAffordableColor;
        private readonly Color _expandUnaffordableColor;

        public PlacementBudgetUI(
            TextMeshProUGUI roadBudgetText,
            Button roadExpandButton,
            TextMeshProUGUI roadExpandCostText,
            Color expandAffordableColor,
            Color expandUnaffordableColor)
        {
            _roadBudgetText = roadBudgetText;
            _roadExpandButton = roadExpandButton;
            _roadExpandCostText = roadExpandCostText;
            _expandAffordableColor = expandAffordableColor;
            _expandUnaffordableColor = expandUnaffordableColor;
        }

        private UnityEngine.Events.UnityAction _expandAction;

        public void Initialize(System.Action onExpandClicked)
        {
            if (_roadExpandButton != null)
            {
                if (_expandAction != null)
                {
                    _roadExpandButton.onClick.RemoveListener(_expandAction);
                }
                _expandAction = () => onExpandClicked?.Invoke();
                _roadExpandButton.onClick.AddListener(_expandAction);
            }
        }

        public void UpdateUI(bool isBuildingMode, TileType currentType, CityFlowServices services)
        {
            if (_roadBudgetText != null) _roadBudgetText.gameObject.SetActive(false);
            if (_roadExpandButton != null) _roadExpandButton.gameObject.SetActive(false);
            if (_roadExpandCostText != null) _roadExpandCostText.gameObject.SetActive(false);
        }
    }
}
