namespace CityFlow.Contracts
{
    public interface IOfflineSettlementSource
    {
        double SettleOffline(double elapsedSeconds);
    }
}
