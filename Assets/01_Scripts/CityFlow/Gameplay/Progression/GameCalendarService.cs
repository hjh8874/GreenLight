using System;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Gameplay.Progression
{
    public sealed class GameCalendarService : MonoBehaviour, ICityFlowServiceConsumer, IGameCalendarService
    {
        [Header("Prototype Time Scale")]
        [Tooltip("Prototype default: 1 real second equals 1 game hour.")]
        [SerializeField] private float realSecondsPerGameHour = 1f;
        [SerializeField] private int hoursPerDay = 24;
        [SerializeField] private int daysPerMonth = 30;
        [SerializeField] private int monthsPerYear = 12;

        [Header("Initial Date")]
        [SerializeField] private int startYear = 1;
        [SerializeField] private int startMonth = 1;
        [SerializeField] private int startDay = 1;
        [SerializeField] private int startHour;

        private bool initialized;
        private float accumulatedRealSeconds;

        public int Year { get; private set; }
        public int Month { get; private set; }
        public int Day { get; private set; }
        public int Hour { get; private set; }
        public int TotalMonths { get; private set; }
        public float RealSecondsPerGameHour => realSecondsPerGameHour;

        public event Action<int> HourChanged;
        public event Action<int> DayChanged;
        public event Action<int> MonthChanged;

        public void Initialize(CityFlowServices services)
        {
            ApplyInitialDate();
            services.RegisterGameCalendar(this);
            initialized = true;

            Debug.Log($"[GameCalendarService] Calendar started at Y{Year} M{Month} D{Day} {Hour:00}:00. 1 game hour = {realSecondsPerGameHour:0.##} real seconds.");
        }

        private void Update()
        {
            if (!initialized)
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
            Hour = Mathf.Clamp(startHour, 0, Mathf.Max(1, hoursPerDay) - 1);
            TotalMonths = ((Year - 1) * Mathf.Max(1, monthsPerYear)) + Month;
        }

        private void AdvanceHour()
        {
            Hour++;
            if (Hour >= Mathf.Max(1, hoursPerDay))
            {
                Hour = 0;
                AdvanceDay();
            }

            HourChanged?.Invoke(Hour);
        }

        private void AdvanceDay()
        {
            Day++;
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

        // Attach this component to a scene object. CityBootstrap initializes it through ICityFlowServiceConsumer.
    }
}
