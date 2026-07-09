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
        float RealSecondsPerGameHour { get; }

        event Action<int> HourChanged;
        event Action<int> DayChanged;
        event Action<int> MonthChanged;
    }
}
