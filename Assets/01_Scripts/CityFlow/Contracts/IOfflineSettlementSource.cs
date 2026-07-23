namespace CityFlow.Contracts
{
    public interface IOfflineSettlementSource
    {
        double MaximumOfflineSeconds { get; }

        long SettleOffline(double elapsedSeconds);
    }
}
