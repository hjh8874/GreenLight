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

        public void Initialize(System.Action onExpandClicked)
        {
            if (_roadExpandButton != null)
            {
                _roadExpandButton.onClick.RemoveAllListeners();
                _roadExpandButton.onClick.AddListener(() => onExpandClicked?.Invoke());
            }
        }

        public void UpdateUI(bool isBuildingMode, TileType currentType, CityFlowServices services)
        {
            bool showRoad = isBuildingMode && currentType == TileType.Road && services != null && services.Stats != null;

            if (_roadBudgetText != null)
            {
                if (_roadBudgetText.gameObject.activeSelf != showRoad)
                    _roadBudgetText.gameObject.SetActive(showRoad);

                if (showRoad)
                    _roadBudgetText.text = $"도로 {services.Stats.RoadTileCount}/{services.Stats.MaxRoadTiles}";
            }

            var expansion = showRoad ? services?.Placement as CityFlow.Contracts.IRoadExpansionService : null;
            bool showExpand = expansion != null;

            if (_roadExpandButton != null)
            {
                if (_roadExpandButton.gameObject.activeSelf != showExpand)
                    _roadExpandButton.gameObject.SetActive(showExpand);

                if (showExpand)
                {
                    bool affordable = services.Economy != null && services.Economy.Coins >= expansion.NextRoadExpandCost;
                    _roadExpandButton.interactable = affordable;
                }
            }

            if (_roadExpandCostText != null)
            {
                if (_roadExpandCostText.gameObject.activeSelf != showExpand)
                    _roadExpandCostText.gameObject.SetActive(showExpand);

                if (showExpand)
                {
                    bool affordable = services.Economy != null && services.Economy.Coins >= expansion.NextRoadExpandCost;
                    _roadExpandCostText.text = $"+10칸 {expansion.NextRoadExpandCost:N0}";
                    _roadExpandCostText.color = affordable ? _expandAffordableColor : _expandUnaffordableColor;
                }
            }
        }
    }
}
