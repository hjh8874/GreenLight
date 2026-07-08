using System;

namespace CityFlow.Save
{
    public interface ISaveClock
    {
        DateTime UtcNow { get; }
    }
}
