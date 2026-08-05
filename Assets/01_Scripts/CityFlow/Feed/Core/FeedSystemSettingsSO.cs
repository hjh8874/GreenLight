using UnityEngine;

namespace CityFlow.Feed
{
    [CreateAssetMenu(
        fileName = "FeedSystemSettings",
        menuName = "GreenLight/Feed/System Settings")]
    public sealed class FeedSystemSettingsSO : ScriptableObject
    {
        [Header("Post Limits")]
        [SerializeField, Min(0f)] private float minimumRealSecondsBetweenPosts = 0.75f;
        [SerializeField, Min(1)] private int maximumPostsPerGameHour = 3;
        [SerializeField, Min(1)] private int maximumPostsPerGameDay = 12;
        [SerializeField, Min(1)] private int maximumVisiblePosts = 50;

        [Header("Duplicate Prevention")]
        [SerializeField, Min(0f)] private float sameLocationCooldownHours = 3f;
        [SerializeField, Min(0f)] private float sameAuthorCooldownHours = 2f;
        [SerializeField, Min(0f)] private float sameTemplateCooldownHours = 6f;

        [Header("Selection")]
        [SerializeField, Range(0f, 100f)] private float minimumFeedScore = 30f;
        [SerializeField] private bool logDiagnostics = true;

        [Header("World Event Thresholds")]
        [SerializeField, Min(0.1f)] private float sustainedCongestionGameHours = 2f;
        [SerializeField, Min(1f)] private float notableArrivalDistanceTiles = 12f;
        [SerializeField, Min(1)] private int vehicleSurgeCount = 20;
        [SerializeField, Range(0.1f, 0.95f)] private float vehicleSurgeResetRatio = 0.65f;

        [Header("Writing Style")]
        [SerializeField, Range(0f, 1f)] private float decorationChance = 0.35f;

        public float MinimumRealSecondsBetweenPosts => minimumRealSecondsBetweenPosts;
        public int MaximumPostsPerGameHour => maximumPostsPerGameHour;
        public int MaximumPostsPerGameDay => maximumPostsPerGameDay;
        public int MaximumVisiblePosts => maximumVisiblePosts;
        public float SameLocationCooldownHours => sameLocationCooldownHours;
        public float SameAuthorCooldownHours => sameAuthorCooldownHours;
        public float SameTemplateCooldownHours => sameTemplateCooldownHours;
        public float MinimumFeedScore => minimumFeedScore;
        public bool LogDiagnostics => logDiagnostics;
        public float SustainedCongestionGameHours => sustainedCongestionGameHours;
        public float NotableArrivalDistanceTiles => notableArrivalDistanceTiles;
        public int VehicleSurgeCount => vehicleSurgeCount;
        public float VehicleSurgeResetRatio => vehicleSurgeResetRatio;
        public float DecorationChance => decorationChance;

#if UNITY_EDITOR
        public void ConfigureDefaults()
        {
            minimumRealSecondsBetweenPosts = 0.75f;
            maximumPostsPerGameHour = 3;
            maximumPostsPerGameDay = 12;
            maximumVisiblePosts = 50;
            sameLocationCooldownHours = 3f;
            sameAuthorCooldownHours = 2f;
            sameTemplateCooldownHours = 6f;
            minimumFeedScore = 30f;
            logDiagnostics = true;
            sustainedCongestionGameHours = 2f;
            notableArrivalDistanceTiles = 12f;
            vehicleSurgeCount = 20;
            vehicleSurgeResetRatio = 0.65f;
            decorationChance = 0.35f;
        }
#endif

        // Unity setup: Create through GreenLight/Feed/System Settings or the V1 data generator.
    }
}
