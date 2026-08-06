using CityFlow.Contracts;

namespace CityFlow.UI.Data
{
    public static class BuildingHoverCommentResolver
    {
        public static string Resolve(
            BuildingHoverCommentCatalogSO catalog,
            BuildingHoverCommentContext context)
        {
            if (catalog == null)
            {
                return DefaultComment(context);
            }

            BuildingHoverCommentProfile profile = ResolveProfile(
                catalog,
                context);
            string comment = profile?.PickComment(
                context.Congestion == CongestionLevel.Jam,
                StableTileSeed(context.Tile.x, context.Tile.y));

            if (string.IsNullOrWhiteSpace(comment))
            {
                comment = DefaultComment(context);
            }

            int progressPercent = UnityEngine.Mathf.RoundToInt(
                context.ConstructionProgress01 * 100f);
            return comment
                .Replace("{building}", context.BuildingName)
                .Replace("{progress}", progressPercent.ToString());
        }

        private static BuildingHoverCommentProfile ResolveProfile(
            BuildingHoverCommentCatalogSO catalog,
            BuildingHoverCommentContext context)
        {
            if (context.TileType == TileType.UnderConstruction)
            {
                return catalog.ConstructionProfile;
            }

            if (context.TileType == TileType.SpecialBuilding &&
                catalog.TryGetSpecialBuildingProfile(
                    context.SpecialBuildingId,
                    out BuildingHoverCommentProfile specialProfile))
            {
                return specialProfile;
            }

            if (context.TileType == TileType.Office &&
                catalog.TryGetCompanyProfile(
                    context.CompanyTypeId,
                    out BuildingHoverCommentProfile companyProfile))
            {
                return companyProfile;
            }

            return catalog.TryGetTileProfile(
                context.TileType,
                out BuildingHoverCommentProfile tileProfile)
                ? tileProfile
                : catalog.FallbackProfile;
        }

        private static int StableTileSeed(int x, int y)
        {
            unchecked
            {
                return (x * 73856093) ^ (y * 19349663);
            }
        }

        private static string DefaultComment(
            BuildingHoverCommentContext context)
        {
            if (context.TileType == TileType.UnderConstruction)
            {
                return $"{context.BuildingName} 공사가 진행 중입니다. " +
                       $"현재 {UnityEngine.Mathf.RoundToInt(context.ConstructionProgress01 * 100f)}% 완료되었습니다.";
            }

            return context.Congestion == CongestionLevel.Jam
                ? "주변 도로가 혼잡해 건물 이용에 불편이 생기고 있습니다."
                : "건물이 정상적으로 운영되고 있습니다.";
        }
    }
}
