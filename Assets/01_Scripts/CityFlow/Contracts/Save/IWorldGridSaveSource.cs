namespace CityFlow.Contracts.Save
{
    public interface IWorldGridSaveSource
    {
        WorldGridSaveData CreateSnapshot();

        void RestoreSnapshot(WorldGridSaveData snapshot);
    }
}
