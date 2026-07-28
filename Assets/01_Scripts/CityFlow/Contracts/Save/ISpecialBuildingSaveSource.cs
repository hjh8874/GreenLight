namespace CityFlow.Contracts.Save
{
    public interface ISpecialBuildingSaveSource
    {
        SpecialBuildingSaveData CreateSnapshot();

        void RestoreSnapshot(SpecialBuildingSaveData snapshot);
    }
}
