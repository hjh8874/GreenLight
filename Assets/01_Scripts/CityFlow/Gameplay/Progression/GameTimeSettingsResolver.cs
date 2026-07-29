using UnityEngine;

namespace CityFlow.Gameplay.Progression
{
    internal static class GameTimeSettingsResolver
    {
        private const string DefaultResourcePath =
            "CityFlow/GameTimeSettings";

        public static GameTimeSettingsSO Resolve(
            GameTimeSettingsSO inspectorOverride,
            Object context)
        {
            if (inspectorOverride != null)
            {
                return inspectorOverride;
            }

            GameTimeSettingsSO settings =
                Resources.Load<GameTimeSettingsSO>(
                    DefaultResourcePath);

            if (settings == null)
            {
                Debug.LogWarning(
                    "[GameCalendarService] Default game time settings " +
                    $"were not found at Resources/{DefaultResourcePath}. " +
                    "The built-in 12-minute day fallback will be used.",
                    context);
            }

            return settings;
        }

        // Unity setup: GameCalendarService calls this automatically when no Inspector override is assigned.
    }
}
