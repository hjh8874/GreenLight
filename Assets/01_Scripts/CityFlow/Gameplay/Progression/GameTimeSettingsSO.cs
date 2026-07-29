using UnityEngine;

namespace CityFlow.Gameplay.Progression
{
    [CreateAssetMenu(
        fileName = "GameTimeSettings",
        menuName = "CityFlow/Progression/Game Time Settings")]
    public sealed class GameTimeSettingsSO : ScriptableObject
    {
        public const int HoursPerDay = 24;
        public const float DefaultRealMinutesPerGameDay = 12f;
        public const float MinimumRealMinutesPerGameDay = 0.01f;

        [Header("Calendar Pace")]
        [Tooltip("Real minutes required for one 24-hour game day at Time.timeScale 1.")]
        [SerializeField, Min(MinimumRealMinutesPerGameDay)]
        private float realMinutesPerGameDay =
            DefaultRealMinutesPerGameDay;

        public float RealMinutesPerGameDay => Mathf.Max(
            MinimumRealMinutesPerGameDay,
            realMinutesPerGameDay);

        public float RealSecondsPerGameDay =>
            RealMinutesPerGameDay * 60f;

        public float RealSecondsPerGameHour =>
            RealSecondsPerGameDay / HoursPerDay;

        private void OnValidate()
        {
            realMinutesPerGameDay = Mathf.Max(
                MinimumRealMinutesPerGameDay,
                realMinutesPerGameDay);
        }

        // Unity setup: Edit the default Resources GameTimeSettings asset to change calendar pace.
    }
}
