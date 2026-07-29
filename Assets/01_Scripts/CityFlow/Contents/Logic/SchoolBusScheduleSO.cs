using UnityEngine;

namespace CityFlow.Content
{
    public enum SchoolBusTripKind
    {
        None = 0,
        MorningCommute = 1,
        AfternoonDismissal = 2
    }

    [CreateAssetMenu(
        fileName = "SchoolBusSchedule",
        menuName = "CityFlow/Transit/School Bus Schedule")]
    public sealed class SchoolBusScheduleSO : ScriptableObject
    {
        [Header("한국형 기본 운행 시간")]
        [SerializeField, Range(0, 23)]
        private int morningStartHour = 7;

        [SerializeField, Range(1, 24)]
        private int morningEndHour = 9;

        [SerializeField, Range(0, 23)]
        private int afternoonStartHour = 15;

        [SerializeField, Range(1, 24)]
        private int afternoonEndHour = 17;

        [Header("운행 요일")]
        [Tooltip("기본값은 월요일부터 금요일까지만 운행합니다.")]
        [SerializeField]
        private bool operateOnWeekends;

        public int MorningStartHour => morningStartHour;
        public int MorningEndHour => morningEndHour;
        public int AfternoonStartHour => afternoonStartHour;
        public int AfternoonEndHour => afternoonEndHour;
        public bool OperateOnWeekends => operateOnWeekends;

        public bool IsOperatingDay(long totalDays)
        {
            if (operateOnWeekends)
            {
                return true;
            }

            int weekdayIndex =
                (int)(((totalDays % 7L) + 7L) % 7L);
            return weekdayIndex < 5;
        }

        public SchoolBusTripKind GetEligibleTrip(
            long totalDays,
            int hour,
            long lastMorningTripDay,
            long lastAfternoonTripDay)
        {
            if (!IsOperatingDay(totalDays))
            {
                return SchoolBusTripKind.None;
            }

            if (lastMorningTripDay != totalDays &&
                IsInsideWindow(
                    hour,
                    morningStartHour,
                    morningEndHour))
            {
                return SchoolBusTripKind.MorningCommute;
            }

            if (lastAfternoonTripDay != totalDays &&
                IsInsideWindow(
                    hour,
                    afternoonStartHour,
                    afternoonEndHour))
            {
                return SchoolBusTripKind.AfternoonDismissal;
            }

            return SchoolBusTripKind.None;
        }

        private static bool IsInsideWindow(
            int hour,
            int startHour,
            int endHour)
        {
            return hour >= startHour && hour < endHour;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            morningStartHour =
                Mathf.Clamp(morningStartHour, 0, 23);
            morningEndHour =
                Mathf.Clamp(
                    morningEndHour,
                    morningStartHour + 1,
                    24);
            afternoonStartHour =
                Mathf.Clamp(afternoonStartHour, 0, 23);
            afternoonEndHour =
                Mathf.Clamp(
                    afternoonEndHour,
                    afternoonStartHour + 1,
                    24);
        }
#endif
    }
}
