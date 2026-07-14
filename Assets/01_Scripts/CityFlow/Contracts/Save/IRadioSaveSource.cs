namespace CityFlow.Contracts.Save
{
    public interface IRadioSaveSource
    {
        RadioSaveData CreateSnapshot();

        void RestoreSnapshot(RadioSaveData snapshot);
    }
}
