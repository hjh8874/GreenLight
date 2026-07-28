using UnityEngine;

namespace CityFlow.Contracts
{
    public enum SpecialBuildingMenuCategory
    {
        Commercial,
        Public
    }

    public readonly struct SpecialBuildingBuildOption
    {
        public SpecialBuildingBuildOption(
            string buildingId,
            string displayName,
            string categoryName,
            string description,
            Sprite icon,
            Color fallbackColor,
            SpecialBuildingMenuCategory menuCategory,
            int buildCost,
            bool isUnlocked,
            string requiredResearchId,
            bool canReceiveVisitors,
            int visitsPerPeriod,
            int periodDays,
            int visitorCapacity,
            float attractionWeight,
            int coinPerVisit)
        {
            BuildingId = buildingId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            CategoryName = categoryName ?? string.Empty;
            Description = description ?? string.Empty;
            Icon = icon;
            FallbackColor = fallbackColor;
            MenuCategory = menuCategory;
            BuildCost = Mathf.Max(0, buildCost);
            IsUnlocked = isUnlocked;
            RequiredResearchId = requiredResearchId ?? string.Empty;
            CanReceiveVisitors = canReceiveVisitors;
            VisitsPerPeriod = Mathf.Max(0, visitsPerPeriod);
            PeriodDays = Mathf.Max(1, periodDays);
            VisitorCapacity = Mathf.Max(0, visitorCapacity);
            AttractionWeight = Mathf.Max(0f, attractionWeight);
            CoinPerVisit = Mathf.Max(0, coinPerVisit);
        }

        public string BuildingId { get; }
        public string DisplayName { get; }
        public string CategoryName { get; }
        public string Description { get; }
        public Sprite Icon { get; }
        public Color FallbackColor { get; }
        public SpecialBuildingMenuCategory MenuCategory { get; }
        public int BuildCost { get; }
        public bool IsUnlocked { get; }
        public string RequiredResearchId { get; }
        public bool CanReceiveVisitors { get; }
        public int VisitsPerPeriod { get; }
        public int PeriodDays { get; }
        public int VisitorCapacity { get; }
        public float AttractionWeight { get; }
        public int CoinPerVisit { get; }
    }
}
