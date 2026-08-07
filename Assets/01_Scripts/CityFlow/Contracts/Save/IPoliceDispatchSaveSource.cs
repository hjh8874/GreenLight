namespace CityFlow.Contracts.Save
{
    public interface IPoliceDispatchSaveSource
    {
        PoliceDispatchSaveData CreateSnapshot();

        void RestoreSnapshot(PoliceDispatchSaveData snapshot);
    }
}
