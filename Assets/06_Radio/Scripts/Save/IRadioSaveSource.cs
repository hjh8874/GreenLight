namespace GreenLight.Radio.Save
{
    public interface IRadioSaveSource
    {
        RadioSaveData CreateSnapshot();

        void RestoreSnapshot(RadioSaveData snapshot);
    }
}
