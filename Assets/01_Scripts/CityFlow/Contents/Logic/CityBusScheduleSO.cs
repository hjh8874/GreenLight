using UnityEngine;

namespace CityFlow.Content
{
    [CreateAssetMenu(
        fileName = "CityBusSchedule",
        menuName = "CityFlow/Transit/City Bus Schedule")]
    public sealed class CityBusScheduleSO : ScriptableObject
    {
        [Header("Service Hours")]
        [SerializeField, Range(0, 23)]
        private int serviceStartHour = 6;

        [SerializeField, Range(1, 24)]
        private int serviceEndHour = 22;

        public int ServiceStartHour => serviceStartHour;
        public int ServiceEndHour => serviceEndHour;

        public bool IsOperatingHour(int hour)
        {
            int normalizedHour =
                ((hour % 24) + 24) % 24;
            return normalizedHour >= serviceStartHour &&
                   normalizedHour < serviceEndHour;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            serviceStartHour = Mathf.Clamp(
                serviceStartHour,
                0,
                23);
            serviceEndHour = Mathf.Clamp(
                serviceEndHour,
                serviceStartHour + 1,
                24);
        }
#endif

        // Unity setup: assign this asset to CityBusService in the city bus Prefab.
    }
}
