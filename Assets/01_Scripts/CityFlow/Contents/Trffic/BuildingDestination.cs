using UnityEngine;

namespace CityFlow.Content.Traffic
{
    [DisallowMultipleComponent]
    public sealed class BuildingDestination : MonoBehaviour
    {
        [Header("Destination Identity")]
        [SerializeField]
        private string destinationId;

        [SerializeField]
        private string displayName;

        [SerializeField]
        private CityDestinationType destinationType;

        [Header("Vehicle Access")]
        [Tooltip("차량이 실제로 도착해야 하는 도로 위 위치입니다.")]
        [SerializeField]
        private Transform vehicleStopPoint;

        [SerializeField]
        private bool acceptsVehicleVisits = true;

        [Min(1)]
        [SerializeField]
        private int maxConcurrentVehicles = 10;

        [Header("Residential Setting")]
        [Tooltip("차량의 집으로 지정할 수 있는 주거 건물인지 설정합니다.")]
        [SerializeField]
        private bool canBeUsedAsHome;

        private int currentReservedVehicles;

        public string DestinationId => destinationId;

        public string DisplayName =>
            string.IsNullOrWhiteSpace(displayName)
                ? gameObject.name
                : displayName;

        public CityDestinationType DestinationType => destinationType;

        public Transform VehicleStopPoint =>
            vehicleStopPoint != null
                ? vehicleStopPoint
                : transform;

        public bool AcceptsVehicleVisits => acceptsVehicleVisits;

        public bool CanBeUsedAsHome => canBeUsedAsHome;

        public int MaxConcurrentVehicles => maxConcurrentVehicles;

        public int CurrentReservedVehicles => currentReservedVehicles;

        public bool HasCapacity =>
            acceptsVehicleVisits &&
            currentReservedVehicles < maxConcurrentVehicles;

        private void OnEnable()
        {
            DestinationRegistry.Register(this);
        }

        private void OnDisable()
        {
            DestinationRegistry.Unregister(this);
            currentReservedVehicles = 0;
        }

        public bool TryReserve()
        {
            if (!HasCapacity)
            {
                return false;
            }

            currentReservedVehicles++;
            return true;
        }

        public void Release()
        {
            currentReservedVehicles =
                Mathf.Max(0, currentReservedVehicles - 1);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            maxConcurrentVehicles =
                Mathf.Max(1, maxConcurrentVehicles);

            if (destinationType == CityDestinationType.Residential)
            {
                canBeUsedAsHome = true;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Transform stopPoint = VehicleStopPoint;

            Gizmos.DrawWireSphere(
                stopPoint.position,
                0.5f);

            Gizmos.DrawLine(
                transform.position,
                stopPoint.position);
        }
#endif
    }
}