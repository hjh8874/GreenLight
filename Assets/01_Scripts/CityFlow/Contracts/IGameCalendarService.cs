using System;

namespace CityFlow.Contracts
{
    public interface IGameCalendarService
    {
        int Year { get; }
        int Month { get; }
        int Day { get; }
        int Hour { get; }
        int TotalMonths { get; }
        long TotalDays { get; }
        float RealSecondsPerGameHour { get; }
        float RealSecondsPerGameDay { get; }
        int HoursPerDay { get; }
        float TimeOfDay01 { get; }

        event Action<int> HourChanged;
        event Action<int> DayChanged;
        event Action<int> MonthChanged;
    }
}
