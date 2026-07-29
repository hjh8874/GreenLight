namespace CityFlow.Contracts.Save
{
    public interface ISchoolBusSaveSource
    {
        SchoolBusSaveData CreateSnapshot();

        void RestoreSnapshot(SchoolBusSaveData snapshot);
    }
}
