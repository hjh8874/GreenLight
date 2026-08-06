using System;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.UI.Data
{
    [Serializable]
    public sealed class BuildingHoverCommentProfile
    {
        [SerializeField]
        private string[] normalComments = Array.Empty<string>();

        [SerializeField]
        private string[] congestedComments = Array.Empty<string>();

        public string PickComment(bool isCongested, int seed)
        {
            string comment = PickFrom(
                isCongested ? congestedComments : normalComments,
                seed);
            if (!string.IsNullOrEmpty(comment))
            {
                return comment;
            }

            return PickFrom(normalComments, seed);
        }

        private static string PickFrom(string[] comments, int seed)
        {
            if (comments == null || comments.Length == 0)
            {
                return string.Empty;
            }

            int start = (seed & int.MaxValue) % comments.Length;
            for (int offset = 0; offset < comments.Length; offset++)
            {
                string comment = comments[(start + offset) % comments.Length];
                if (!string.IsNullOrWhiteSpace(comment))
                {
                    return comment;
                }
            }

            return string.Empty;
        }
    }

    [Serializable]
    public sealed class TileBuildingHoverCommentEntry
    {
        [SerializeField]
        private TileType tileType;

        [SerializeField]
        private BuildingHoverCommentProfile comments = new();

        public TileType TileType => tileType;
        public BuildingHoverCommentProfile Comments => comments;
    }

    [Serializable]
    public sealed class IdBuildingHoverCommentEntry
    {
        [SerializeField]
        private string id;

        [SerializeField]
        private BuildingHoverCommentProfile comments = new();

        public string Id => id ?? string.Empty;
        public BuildingHoverCommentProfile Comments => comments;
    }

    [CreateAssetMenu(
        fileName = "BuildingHoverCommentCatalog",
        menuName = "CityFlow/UI/Building Hover Comment Catalog")]
    public sealed class BuildingHoverCommentCatalogSO : ScriptableObject
    {
        public const string DefaultResourcePath =
            "CityFlow/BuildingHoverCommentCatalog";

        [SerializeField]
        private TileBuildingHoverCommentEntry[] tileProfiles =
            Array.Empty<TileBuildingHoverCommentEntry>();

        [SerializeField]
        private IdBuildingHoverCommentEntry[] companyProfiles =
            Array.Empty<IdBuildingHoverCommentEntry>();

        [SerializeField]
        private IdBuildingHoverCommentEntry[] specialBuildingProfiles =
            Array.Empty<IdBuildingHoverCommentEntry>();

        [SerializeField]
        private BuildingHoverCommentProfile constructionProfile = new();

        [SerializeField]
        private BuildingHoverCommentProfile fallbackProfile = new();

        public BuildingHoverCommentProfile ConstructionProfile =>
            constructionProfile;

        public BuildingHoverCommentProfile FallbackProfile =>
            fallbackProfile;

        public static BuildingHoverCommentCatalogSO LoadDefault() =>
            Resources.Load<BuildingHoverCommentCatalogSO>(
                DefaultResourcePath);

        public bool TryGetTileProfile(
            TileType tileType,
            out BuildingHoverCommentProfile profile)
        {
            for (int index = 0; index < tileProfiles.Length; index++)
            {
                TileBuildingHoverCommentEntry entry = tileProfiles[index];
                if (entry != null && entry.TileType == tileType)
                {
                    profile = entry.Comments;
                    return profile != null;
                }
            }

            profile = null;
            return false;
        }

        public bool TryGetCompanyProfile(
            string companyTypeId,
            out BuildingHoverCommentProfile profile) =>
            TryGetIdProfile(
                companyProfiles,
                companyTypeId,
                out profile);

        public bool TryGetSpecialBuildingProfile(
            string buildingId,
            out BuildingHoverCommentProfile profile) =>
            TryGetIdProfile(
                specialBuildingProfiles,
                buildingId,
                out profile);

        private static bool TryGetIdProfile(
            IdBuildingHoverCommentEntry[] entries,
            string id,
            out BuildingHoverCommentProfile profile)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                profile = null;
                return false;
            }

            for (int index = 0; index < entries.Length; index++)
            {
                IdBuildingHoverCommentEntry entry = entries[index];
                if (entry != null &&
                    string.Equals(
                        entry.Id,
                        id.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    profile = entry.Comments;
                    return profile != null;
                }
            }

            profile = null;
            return false;
        }
    }
}
