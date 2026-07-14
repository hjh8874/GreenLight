namespace CityFlow.Contracts.Save
{
    public interface IWeeklySettlementSaveSource
    {
        WeeklySettlementSaveData CreateSnapshot();

        void RestoreSnapshot(WeeklySettlementSaveData snapshot);
    }
}
