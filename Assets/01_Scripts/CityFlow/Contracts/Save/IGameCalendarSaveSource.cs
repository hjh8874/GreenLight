namespace CityFlow.Contracts.Save
{
    public interface IGameCalendarSaveSource
    {
        GameCalendarSaveData CreateSnapshot();

        void RestoreSnapshot(GameCalendarSaveData snapshot);
    }
}
