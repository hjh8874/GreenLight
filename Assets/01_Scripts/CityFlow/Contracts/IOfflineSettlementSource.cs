namespace CityFlow.Contracts
{
    public interface IOfflineSettlementSource
    {
        void SettleOffline(double elapsedSeconds);
    }
}
