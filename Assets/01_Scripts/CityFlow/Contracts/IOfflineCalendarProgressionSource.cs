namespace CityFlow.Contracts
{
    public interface IOfflineCalendarProgressionSource
    {
        void AdvanceOffline(double settledRealSeconds);
    }
}
