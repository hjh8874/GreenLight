using UnityEngine;

namespace CityFlow.Content
{
    [CreateAssetMenu(
        fileName = "EmergencyIncidentConfig",
        menuName =
            "CityFlow/Emergency/Incident Config")]
    public sealed class EmergencyIncidentConfigSO :
        ScriptableObject
    {
        [Header("Spawn")]
        [SerializeField, Min(0.1f)]
        private float minimumSpawnInterval = 10f;
        [SerializeField, Min(0.1f)]
        private float maximumSpawnInterval = 20f;
        [SerializeField, Min(1)]
        private int maximumActiveIncidents = 3;
        [SerializeField, Range(0f, 1f)]
        private float houseWeight = 0.7f;
        [SerializeField, Range(0f, 1f)]
        private float officeWeight = 0.3f;

        [Header("Ambulance Flow")]
        [SerializeField, Min(0.01f)]
        private float travelSecondsPerTile = 0.2f;
        [SerializeField, Min(0.01f)]
        private float treatmentSeconds = 2f;
        [SerializeField, Min(1)]
        private int ambulancesPerHospital = 1;

        public float MinimumSpawnInterval =>
            Mathf.Max(0.1f, minimumSpawnInterval);
        public float MaximumSpawnInterval =>
            Mathf.Max(
                MinimumSpawnInterval,
                maximumSpawnInterval);
        public int MaximumActiveIncidents =>
            Mathf.Max(1, maximumActiveIncidents);
        public float HouseWeight =>
            Mathf.Clamp01(houseWeight);
        public float OfficeWeight =>
            Mathf.Clamp01(officeWeight);
        public float TravelSecondsPerTile =>
            Mathf.Max(0.01f, travelSecondsPerTile);
        public float TreatmentSeconds =>
            Mathf.Max(0.01f, treatmentSeconds);
        public int AmbulancesPerHospital =>
            Mathf.Max(1, ambulancesPerHospital);

#if UNITY_EDITOR
        private void OnValidate()
        {
            minimumSpawnInterval = Mathf.Max(
                0.1f,
                minimumSpawnInterval);
            maximumSpawnInterval = Mathf.Max(
                minimumSpawnInterval,
                maximumSpawnInterval);
            maximumActiveIncidents = Mathf.Max(
                1,
                maximumActiveIncidents);
            houseWeight = Mathf.Clamp01(houseWeight);
            officeWeight = Mathf.Clamp01(officeWeight);
            travelSecondsPerTile = Mathf.Max(
                0.01f,
                travelSecondsPerTile);
            treatmentSeconds = Mathf.Max(
                0.01f,
                treatmentSeconds);
            ambulancesPerHospital = Mathf.Max(
                1,
                ambulancesPerHospital);
        }
#endif
    }
}
