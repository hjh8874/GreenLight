namespace CityFlow.Contracts.Save
{
    public interface ISimSaveSource
    {
        int GridWidth { get; }

        int GridHeight { get; }

        SimSaveData CreateSnapshot();

        void RestoreSnapshot(SimSaveData snapshot);
    }
}
