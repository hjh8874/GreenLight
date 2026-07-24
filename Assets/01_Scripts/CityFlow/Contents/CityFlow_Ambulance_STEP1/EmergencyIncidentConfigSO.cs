using UnityEngine;

namespace CityFlow.Content
{
    [CreateAssetMenu(fileName = "EmergencyIncidentConfig", menuName = "CityFlow/Content/Emergency Incident Config")]
    public sealed class EmergencyIncidentConfigSO : ScriptableObject
    {
        [Header("발생 간격")]
        [SerializeField, Min(0.1f)] private float minimumSpawnInterval = 12f;
        [SerializeField, Min(0.1f)] private float maximumSpawnInterval = 25f;

        [Header("발생 확률")]
        [SerializeField, Range(0f, 1f)] private float houseSpawnChance = 0.65f;
        [SerializeField, Range(0f, 1f)] private float officeSpawnChance = 0.35f;

        [Header("동시 발생 제한")]
        [SerializeField, Min(1)] private int maximumActiveIncidents = 3;

        public float MinimumSpawnInterval => Mathf.Max(0.1f, minimumSpawnInterval);
        public float MaximumSpawnInterval => Mathf.Max(MinimumSpawnInterval, maximumSpawnInterval);
        public float HouseSpawnChance => Mathf.Clamp01(houseSpawnChance);
        public float OfficeSpawnChance => Mathf.Clamp01(officeSpawnChance);
        public int MaximumActiveIncidents => Mathf.Max(1, maximumActiveIncidents);

#if UNITY_EDITOR
        private void OnValidate()
        {
            minimumSpawnInterval = Mathf.Max(0.1f, minimumSpawnInterval);
            maximumSpawnInterval = Mathf.Max(minimumSpawnInterval, maximumSpawnInterval);
            houseSpawnChance = Mathf.Clamp01(houseSpawnChance);
            officeSpawnChance = Mathf.Clamp01(officeSpawnChance);
            maximumActiveIncidents = Mathf.Max(1, maximumActiveIncidents);
        }
#endif
    }
}
