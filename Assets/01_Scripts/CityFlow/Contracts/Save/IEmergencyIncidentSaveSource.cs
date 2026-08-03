namespace CityFlow.Contracts.Save
{
    public interface IEmergencyIncidentSaveSource
    {
        EmergencyIncidentSaveData CreateSnapshot();

        void RestoreSnapshot(EmergencyIncidentSaveData snapshot);
    }
}
