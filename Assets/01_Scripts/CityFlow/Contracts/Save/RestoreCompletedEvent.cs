namespace CityFlow.Contracts.Save
{
    public readonly struct RestoreCompletedEvent
    {
        public double SettledOfflineSeconds { get; }

        public RestoreCompletedEvent(double settledOfflineSeconds)
        {
            SettledOfflineSeconds = settledOfflineSeconds;
        }
    }
}
