namespace CityFlow.Contracts.Save
{
    public readonly struct RestoreCompletedEvent
    {
        public double SettledOfflineSeconds { get; }
        public bool IncludesOfflineProgression { get; }

        public RestoreCompletedEvent(
            double settledOfflineSeconds,
            bool includesOfflineProgression)
        {
            SettledOfflineSeconds = settledOfflineSeconds;
            IncludesOfflineProgression = includesOfflineProgression;
        }
    }

}
