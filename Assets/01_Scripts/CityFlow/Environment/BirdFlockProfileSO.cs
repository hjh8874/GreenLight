using UnityEngine;

namespace CityFlow.Environment
{
    [CreateAssetMenu(
        fileName = "BirdFlockProfile",
        menuName = "CityFlow/Environment/Bird Flock Profile")]
    public sealed class BirdFlockProfileSO : ScriptableObject
    {
        [Header("Population")]
        [SerializeField, Min(1)] private int maximumBirds = 14;
        [SerializeField, Min(0)] private int minimumVisibleBirds = 7;
        [SerializeField] private AnimationCurve densityByZoom = new(
            new Keyframe(0f, 1f),
            new Keyframe(0.5f, 0.85f),
            new Keyframe(1f, 0.65f));
        [SerializeField] private bool hideInDriveView = true;
        [SerializeField, Range(0, 23)] private int visibleStartHour = 0;
        [SerializeField, Range(1, 24)] private int visibleEndHour = 24;

        [Header("Flock Crossing")]
        [SerializeField, Range(1, 24)] private int flockIntervalGameHours = 2;
        [SerializeField, Min(0.5f)] private float minimumCrossingSeconds = 4.5f;
        [SerializeField, Min(0.5f)] private float maximumCrossingSeconds = 6.5f;
        [SerializeField, Min(0f)] private float minimumAltitude = 2.4f;
        [SerializeField, Min(0f)] private float maximumAltitude = 4.2f;
        [SerializeField, Min(0.05f)] private float formationSpacing = 0.22f;
        [SerializeField, Min(0f)] private float formationJitter = 0.08f;
        [SerializeField, Min(0f)] private float edgePadding = 1.5f;

        [Header("Village Resident Birds")]
        [SerializeField, Range(3, 12)] private int villageBirdCount = 4;
        [SerializeField, Min(0f)] private float villageBirdArrivalStaggerSeconds = 0.75f;
        [SerializeField, Min(0.5f)] private float villageBirdMinimumArrivalSeconds = 3.5f;
        [SerializeField, Min(0.5f)] private float villageBirdMaximumArrivalSeconds = 5.5f;
        [SerializeField, Min(0f)] private float villageBirdMinimumPerchedSeconds = 8f;
        [SerializeField, Min(0f)] private float villageBirdMaximumPerchedSeconds = 16f;
        [SerializeField, Min(0.1f)] private float villageBirdTakeoffSeconds = 1.25f;
        [SerializeField, Min(0.5f)] private float villageBirdMinimumDepartureSeconds = 3.5f;
        [SerializeField, Min(0.5f)] private float villageBirdMaximumDepartureSeconds = 5.5f;
        [SerializeField, Min(0f)] private float villageBirdMinimumReturnDelaySeconds = 1.5f;
        [SerializeField, Min(0f)] private float villageBirdMaximumReturnDelaySeconds = 4f;
        [SerializeField, Min(0f)] private float villageBirdPerchedAltitude = 0.12f;
        [SerializeField, Range(0.05f, 0.8f)] private float villageBirdSpawnAreaRatio = 0.28f;

        [Header("View Tracking")]
        [SerializeField, Min(1f)] private float viewAreaMultiplier = 1.25f;
        [SerializeField, Min(0.01f)] private float followSharpness = 5f;

        [Header("Appearance")]
        [SerializeField] private Color birdColor = new(0.12f, 0.15f, 0.17f, 1f);
        [SerializeField, Min(0.01f)] private float minimumSize = 0.21f;
        [SerializeField, Min(0.01f)] private float maximumSize = 0.315f;
        [SerializeField, Min(0.01f)] private float minimumFlapFrequency = 3.2f;
        [SerializeField, Min(0.01f)] private float maximumFlapFrequency = 5.2f;
        [SerializeField, Range(0.2f, 1f)] private float minimumWingScale = 0.62f;
        [SerializeField, Min(0f)] private float bobHeight = 0.025f;
        [SerializeField] private int deterministicSeed = 7319;

        public int MaximumBirds => maximumBirds;
        public int MinimumVisibleBirds => minimumVisibleBirds;
        public AnimationCurve DensityByZoom => densityByZoom;
        public bool HideInDriveView => hideInDriveView;
        public int VisibleStartHour => visibleStartHour;
        public int VisibleEndHour => visibleEndHour;
        public int FlockIntervalGameHours => flockIntervalGameHours;
        public float MinimumCrossingSeconds => minimumCrossingSeconds;
        public float MaximumCrossingSeconds => maximumCrossingSeconds;
        public float MinimumAltitude => minimumAltitude;
        public float MaximumAltitude => maximumAltitude;
        public float FormationSpacing => formationSpacing;
        public float FormationJitter => formationJitter;
        public float EdgePadding => edgePadding;
        public int VillageBirdCount => villageBirdCount;
        public float VillageBirdArrivalStaggerSeconds =>
            villageBirdArrivalStaggerSeconds;
        public float VillageBirdMinimumArrivalSeconds =>
            villageBirdMinimumArrivalSeconds;
        public float VillageBirdMaximumArrivalSeconds =>
            villageBirdMaximumArrivalSeconds;
        public float VillageBirdMinimumPerchedSeconds =>
            villageBirdMinimumPerchedSeconds;
        public float VillageBirdMaximumPerchedSeconds =>
            villageBirdMaximumPerchedSeconds;
        public float VillageBirdTakeoffSeconds => villageBirdTakeoffSeconds;
        public float VillageBirdMinimumDepartureSeconds =>
            villageBirdMinimumDepartureSeconds;
        public float VillageBirdMaximumDepartureSeconds =>
            villageBirdMaximumDepartureSeconds;
        public float VillageBirdMinimumReturnDelaySeconds =>
            villageBirdMinimumReturnDelaySeconds;
        public float VillageBirdMaximumReturnDelaySeconds =>
            villageBirdMaximumReturnDelaySeconds;
        public float VillageBirdPerchedAltitude => villageBirdPerchedAltitude;
        public float VillageBirdSpawnAreaRatio => villageBirdSpawnAreaRatio;
        public float ViewAreaMultiplier => viewAreaMultiplier;
        public float FollowSharpness => followSharpness;
        public Color BirdColor => birdColor;
        public float MinimumSize => minimumSize;
        public float MaximumSize => maximumSize;
        public float MinimumFlapFrequency => minimumFlapFrequency;
        public float MaximumFlapFrequency => maximumFlapFrequency;
        public float MinimumWingScale => minimumWingScale;
        public float BobHeight => bobHeight;
        public int DeterministicSeed => deterministicSeed;

        public int EvaluateVisibleBirdCount(float normalizedZoom01)
        {
            float density = densityByZoom != null
                ? densityByZoom.Evaluate(Mathf.Clamp01(normalizedZoom01))
                : 1f;
            int count = Mathf.RoundToInt(maximumBirds * Mathf.Clamp01(density));
            return Mathf.Clamp(count, minimumVisibleBirds, maximumBirds);
        }

        public bool IsVisibleAtHour(int hour)
        {
            int normalizedHour = ((hour % 24) + 24) % 24;
            return normalizedHour >= visibleStartHour &&
                   normalizedHour < visibleEndHour;
        }

        private void OnValidate()
        {
            maximumBirds = Mathf.Max(1, maximumBirds);
            minimumVisibleBirds = Mathf.Clamp(
                minimumVisibleBirds,
                0,
                maximumBirds);
            flockIntervalGameHours = Mathf.Clamp(
                flockIntervalGameHours,
                1,
                24);
            maximumCrossingSeconds = Mathf.Max(
                minimumCrossingSeconds,
                maximumCrossingSeconds);
            maximumAltitude = Mathf.Max(minimumAltitude, maximumAltitude);
            visibleStartHour = Mathf.Clamp(visibleStartHour, 0, 23);
            visibleEndHour = Mathf.Clamp(visibleEndHour, 1, 24);
            if (visibleEndHour <= visibleStartHour)
            {
                visibleEndHour = Mathf.Min(24, visibleStartHour + 1);
            }

            formationSpacing = Mathf.Max(0.05f, formationSpacing);
            formationJitter = Mathf.Max(0f, formationJitter);
            edgePadding = Mathf.Max(0f, edgePadding);
            villageBirdCount = Mathf.Clamp(villageBirdCount, 3, 12);
            villageBirdArrivalStaggerSeconds = Mathf.Max(
                0f,
                villageBirdArrivalStaggerSeconds);
            villageBirdMaximumArrivalSeconds = Mathf.Max(
                villageBirdMinimumArrivalSeconds,
                villageBirdMaximumArrivalSeconds);
            villageBirdMinimumPerchedSeconds = Mathf.Max(
                0f,
                villageBirdMinimumPerchedSeconds);
            villageBirdMaximumPerchedSeconds = Mathf.Max(
                villageBirdMinimumPerchedSeconds,
                villageBirdMaximumPerchedSeconds);
            villageBirdTakeoffSeconds = Mathf.Max(
                0.1f,
                villageBirdTakeoffSeconds);
            villageBirdMaximumDepartureSeconds = Mathf.Max(
                villageBirdMinimumDepartureSeconds,
                villageBirdMaximumDepartureSeconds);
            villageBirdMinimumReturnDelaySeconds = Mathf.Max(
                0f,
                villageBirdMinimumReturnDelaySeconds);
            villageBirdMaximumReturnDelaySeconds = Mathf.Max(
                villageBirdMinimumReturnDelaySeconds,
                villageBirdMaximumReturnDelaySeconds);
            villageBirdPerchedAltitude = Mathf.Max(
                0f,
                villageBirdPerchedAltitude);
            villageBirdSpawnAreaRatio = Mathf.Clamp(
                villageBirdSpawnAreaRatio,
                0.05f,
                0.8f);
            maximumSize = Mathf.Max(minimumSize, maximumSize);
            maximumFlapFrequency = Mathf.Max(
                minimumFlapFrequency,
                maximumFlapFrequency);
            viewAreaMultiplier = Mathf.Max(1f, viewAreaMultiplier);
            followSharpness = Mathf.Max(0.01f, followSharpness);
        }

        // Unity setup:
        // The environment baker creates and assigns this profile automatically.
    }
}
