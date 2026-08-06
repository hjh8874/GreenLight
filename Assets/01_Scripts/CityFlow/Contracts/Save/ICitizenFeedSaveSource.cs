namespace CityFlow.Contracts.Save
{
    public interface ICitizenFeedSaveSource
    {
        CitizenFeedSaveData CreateSnapshot();

        void RestoreSnapshot(CitizenFeedSaveData snapshot);
    }
}
