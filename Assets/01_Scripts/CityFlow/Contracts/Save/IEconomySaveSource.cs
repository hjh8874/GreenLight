namespace CityFlow.Contracts.Save
{
    public interface IEconomySaveSource
    {
        EconomySaveData CreateSnapshot();

        void RestoreSnapshot(EconomySaveData snapshot);
    }
}
