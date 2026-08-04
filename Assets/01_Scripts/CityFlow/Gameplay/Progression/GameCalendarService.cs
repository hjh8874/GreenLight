using System;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Gameplay.Progression
{
    public sealed class GameCalendarService :
        MonoBehaviour,
        ICityFlowServiceConsumer,
        IGameCalendarService,
        IGameCalendarSaveSource
    {
        [Header("Time Balance")]
        [Tooltip("Optional scene override. When empty, the default Resources game time settings are used.")]
        [SerializeField] private GameTimeSettingsSO timeSettings;

        [Header("Calendar Structure")]
        [SerializeField] private int daysPerMonth = 30;
        [SerializeField] private int monthsPerYear = 12;

        [Header("Initial Date")]
        [SerializeField] private int startYear = 1;
        [SerializeField] private int startMonth = 1;
        [SerializeField] private int startDay = 1;
        [SerializeField] private int startHour;

        private bool initialized;
        private float accumulatedRealSeconds;
        private float realSecondsPerGameHour =
            GameTimeSettingsSO.DefaultRealMinutesPerGameDay * 60f /
            GameTimeSettingsSO.HoursPerDay;

        public int Year { get; private set; }
        public int Month { get; private set; }
        public int Day { get; private set; }
        public int Hour { get; private set; }
        public int TotalMonths { get; private set; }
        public long TotalDays { get; private set; }
        public float RealSecondsPerGameHour => realSecondsPerGameHour;
        public float RealSecondsPerGameDay =>
            realSecondsPerGameHour * HoursPerDay;
        public int HoursPerDay => GameTimeSettingsSO.HoursPerDay;
        public float TimeOfDay01
        {
            get
            {
                float secondsPerHour =
                    Mathf.Max(0.01f, realSecondsPerGameHour);
                float currentHourProgress = Mathf.Clamp01(
                    accumulatedRealSeconds / secondsPerHour);

                return Mathf.Repeat(
                    Hour + currentHourProgress,
                    HoursPerDay) / HoursPerDay;
            }
        }

        public event Action<int> HourChanged;
        public event Action<int> DayChanged;
        public event Action<int> MonthChanged;

        public void Initialize(CityFlowServices services)
        {
            ApplyTimeSettings();
            ApplyInitialDate();
            services.RegisterGameCalendar(this);
            initialized = true;

            Debug.Log(
                $"[GameCalendarService] Calendar started at Y{Year} " +
                $"M{Month} D{Day} {Hour:00}:00. One game day = " +
                $"{RealSecondsPerGameDay / 60f:0.##} real minutes " +
                $"({realSecondsPerGameHour:0.##} seconds per hour).");
        }

        private void ApplyTimeSettings()
        {
            GameTimeSettingsSO resolved =
                GameTimeSettingsResolver.Resolve(timeSettings, this);

            realSecondsPerGameHour = resolved != null
                ? resolved.RealSecondsPerGameHour
                : GameTimeSettingsSO.DefaultRealMinutesPerGameDay *
                  60f / GameTimeSettingsSO.HoursPerDay;
        }

        private void Update()
        {
            if (!initialized || CityBootstrap.IsTitlePreviewMode)
            {
                return;
            }

            float secondsPerHour = Mathf.Max(0.01f, realSecondsPerGameHour);
            accumulatedRealSeconds += Time.deltaTime;

            while (accumulatedRealSeconds >= secondsPerHour)
            {
                accumulatedRealSeconds -= secondsPerHour;
                AdvanceHour();
            }
        }

        private void ApplyInitialDate()
        {
            Year = Mathf.Max(1, startYear);
            Month = Mathf.Clamp(startMonth, 1, Mathf.Max(1, monthsPerYear));
            Day = Mathf.Clamp(startDay, 1, Mathf.Max(1, daysPerMonth));
            Hour = Mathf.Clamp(startHour, 0, HoursPerDay - 1);
            TotalMonths = ((Year - 1) * Mathf.Max(1, monthsPerYear)) + Month;
            TotalDays = CalculateTotalDays(Year, Month, Day);
        }

        public GameCalendarSaveData CreateSnapshot()
        {
            return new GameCalendarSaveData
            {
                Year = Year,
                Month = Month,
                Day = Day,
                Hour = Hour,
                TotalMonths = TotalMonths,
                TotalDays = TotalDays,
                AccumulatedRealSeconds = accumulatedRealSeconds
            };
        }

        public void RestoreSnapshot(GameCalendarSaveData snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            int validHoursPerDay = HoursPerDay;
            int validDaysPerMonth = Mathf.Max(1, daysPerMonth);
            int validMonthsPerYear = Mathf.Max(1, monthsPerYear);
            float secondsPerHour = Mathf.Max(0.01f, realSecondsPerGameHour);

            Year = Mathf.Max(1, snapshot.Year);
            Month = Mathf.Clamp(snapshot.Month, 1, validMonthsPerYear);
            Day = Mathf.Clamp(snapshot.Day, 1, validDaysPerMonth);
            Hour = Mathf.Clamp(snapshot.Hour, 0, validHoursPerDay - 1);
            TotalMonths = Mathf.Max(1, snapshot.TotalMonths);
            long calculatedTotalDays = CalculateTotalDays(Year, Month, Day);
            TotalDays = snapshot.TotalDays > 0L
                ? snapshot.TotalDays
                : calculatedTotalDays;
            accumulatedRealSeconds = Mathf.Clamp(snapshot.AccumulatedRealSeconds, 0f, secondsPerHour);

            PublishRestoredDate();
            Debug.Log($"[GameCalendarService] Calendar restored to Y{Year} M{Month} D{Day} {Hour:00}:00.");
        }

        private void PublishRestoredDate()
        {
            HourChanged?.Invoke(Hour);
            DayChanged?.Invoke(Day);
            MonthChanged?.Invoke(TotalMonths);
        }

        private static int ClampToPositiveInt(long value)
        {
            return (int)Math.Max(1L, Math.Min(int.MaxValue, value));
        }

        private void AdvanceHour()
        {
            Hour++;
            if (Hour >= HoursPerDay)
            {
                Hour = 0;
                AdvanceDay();
            }

            HourChanged?.Invoke(Hour);
        }

        private void AdvanceDay()
        {
            Day++;
            TotalDays++;
            if (Day > Mathf.Max(1, daysPerMonth))
            {
                Day = 1;
                AdvanceMonth();
            }

            DayChanged?.Invoke(Day);
        }

        private void AdvanceMonth()
        {
            Month++;
            TotalMonths++;

            if (Month > Mathf.Max(1, monthsPerYear))
            {
                Month = 1;
                Year++;
            }

            MonthChanged?.Invoke(TotalMonths);
            Debug.Log($"[GameCalendarService] Month changed: Y{Year} M{Month} D{Day} {Hour:00}:00.");
        }

        private long CalculateTotalDays(int year, int month, int day)
        {
            long validYear = Math.Max(1, year) - 1L;
            long validMonth = Math.Max(1, month) - 1L;
            long validDay = Math.Max(1, day) - 1L;
            long validMonthsPerYear = Math.Max(1, monthsPerYear);
            long validDaysPerMonth = Math.Max(1, daysPerMonth);

            return ((validYear * validMonthsPerYear) + validMonth) *
                   validDaysPerMonth + validDay;
        }

        // Unity setup: Attach this component once. It loads the default Resources GameTimeSettings asset automatically.
    }
}
