namespace CityFlow.Contracts
{
    public interface IWorldGridService : IWorldGridAccess
    {
        bool TryUnlockChunk(GridChunkId chunk);
    }
}
