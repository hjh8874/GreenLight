using System;

namespace CityFlow.Sim
{
    internal static class CompanyCapacityCalculator
    {
        public static int EffectiveCapacity(
            int totalCapacity,
            double builtAtSimSeconds,
            double currentSimSeconds,
            float slotsPerGameHour,
            float dayLengthSeconds
        )
        {
            int safeTotal = Math.Max(0, totalCapacity);

            if (safeTotal == 0 ||
                currentSimSeconds <= builtAtSimSeconds ||
                slotsPerGameHour <= 0f ||
                dayLengthSeconds <= 0f)
            {
                return 0;
            }

            double elapsedSimSeconds =
                currentSimSeconds - builtAtSimSeconds;
            double elapsedGameHours =
                elapsedSimSeconds * 24d /
                dayLengthSeconds;
            double openedSlots =
                Math.Floor(
                    elapsedGameHours *
                    slotsPerGameHour
                );

            if (openedSlots <= 0d)
            {
                return 0;
            }

            return openedSlots >= safeTotal
                ? safeTotal
                : (int)openedSlots;
        }
    }
}
