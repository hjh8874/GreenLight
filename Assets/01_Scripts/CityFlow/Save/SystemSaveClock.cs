using System;

namespace CityFlow.Save
{
    public sealed class SystemSaveClock : ISaveClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
