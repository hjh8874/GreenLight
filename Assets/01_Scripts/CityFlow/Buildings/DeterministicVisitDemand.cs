using System;

namespace CityFlow.Buildings
{
    public static class DeterministicVisitDemand
    {
        public static int CalculateDailyDemand(
            int population,
            int visitsPerPeriod,
            int periodDays,
            long totalDay,
            string demandKey)
        {
            long safePopulation = Math.Max(0, population);
            long safeVisits = Math.Max(0, visitsPerPeriod);
            int safePeriod = Math.Max(1, periodDays);
            long numerator = SaturatingMultiply(
                safePopulation,
                safeVisits);
            long dailyBase = numerator / safePeriod;
            int remainder = (int)(numerator % safePeriod);
            int phase = (int)(StableHash(demandKey) % (uint)safePeriod);
            int dayIndex = PositiveModulo(totalDay + phase, safePeriod);
            long result = dailyBase + (dayIndex < remainder ? 1L : 0L);
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }

        public static int StableRotation(
            string buildingId,
            long totalDay,
            int count)
        {
            if (count <= 1)
            {
                return 0;
            }

            uint hash = StableHash(
                $"{buildingId ?? string.Empty}:{totalDay}");
            return (int)(hash % (uint)count);
        }

        private static int PositiveModulo(long value, int divisor)
        {
            long remainder = value % divisor;
            return (int)(remainder < 0L ? remainder + divisor : remainder);
        }

        private static long SaturatingMultiply(long left, long right)
        {
            if (left <= 0L || right <= 0L)
            {
                return 0L;
            }

            return left > long.MaxValue / right
                ? long.MaxValue
                : left * right;
        }

        private static uint StableHash(string value)
        {
            const uint offset = 2166136261u;
            const uint prime = 16777619u;
            uint hash = offset;
            string safeValue = value ?? string.Empty;

            for (int index = 0; index < safeValue.Length; index++)
            {
                hash ^= safeValue[index];
                hash *= prime;
            }

            return hash;
        }
    }
}
