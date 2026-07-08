namespace CityFlow.Contracts.Save
{
    public interface IProgressionSaveSource
    {
        ProgressionSaveData CreateSnapshot();

        void RestoreSnapshot(ProgressionSaveData snapshot);
    }
}
