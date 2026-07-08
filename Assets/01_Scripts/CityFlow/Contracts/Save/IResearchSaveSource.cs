namespace CityFlow.Contracts.Save
{
    public interface IResearchSaveSource
    {
        ResearchSaveData CreateSnapshot();

        void RestoreSnapshot(ResearchSaveData snapshot);
    }
}
