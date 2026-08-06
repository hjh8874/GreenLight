using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.UI.Data
{
    public readonly struct BuildingHoverCommentContext
    {
        public BuildingHoverCommentContext(
            Vector2Int tile,
            TileType tileType,
            CongestionLevel congestion,
            string buildingName,
            string companyTypeId = "",
            string specialBuildingId = "",
            float constructionProgress01 = 0f)
        {
            Tile = tile;
            TileType = tileType;
            Congestion = congestion;
            BuildingName = buildingName ?? string.Empty;
            CompanyTypeId = companyTypeId ?? string.Empty;
            SpecialBuildingId = specialBuildingId ?? string.Empty;
            ConstructionProgress01 = Mathf.Clamp01(
                constructionProgress01);
        }

        public Vector2Int Tile { get; }
        public TileType TileType { get; }
        public CongestionLevel Congestion { get; }
        public string BuildingName { get; }
        public string CompanyTypeId { get; }
        public string SpecialBuildingId { get; }
        public float ConstructionProgress01 { get; }
    }
}
