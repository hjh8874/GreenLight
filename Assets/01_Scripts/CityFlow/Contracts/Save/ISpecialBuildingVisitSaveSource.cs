namespace CityFlow.Contracts.Save
{
    public interface ISpecialBuildingVisitSaveSource
    {
        SpecialBuildingVisitSaveData CreateSnapshot();
        void RestoreSnapshot(SpecialBuildingVisitSaveData snapshot);
    }
}
