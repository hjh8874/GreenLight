namespace CityFlow.Contracts.Save
{
    public interface ISimSaveSource
    {
        SimSaveData CreateSnapshot();

        void RestoreSnapshot(SimSaveData snapshot);
    }
}
