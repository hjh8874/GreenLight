namespace CityFlow.Contracts.Save
{
    public interface ITerrainDecorationSaveSource
    {
        TerrainDecorationSaveData CreateSnapshot();

        void RestoreSnapshot(TerrainDecorationSaveData snapshot);
    }
}
