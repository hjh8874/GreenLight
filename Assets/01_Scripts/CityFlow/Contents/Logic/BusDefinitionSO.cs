using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Content
{
    [CreateAssetMenu(
        fileName = "BusDefinition",
        menuName = "CityFlow/Transit/Bus Definition")]
    public sealed class BusDefinitionSO : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string busId = "city_bus";
        [SerializeField] private string displayName = "City Bus";
        [SerializeField] private BusType busType = BusType.CityBus;

        [Header("Route")]
        [SerializeField, Min(0.01f)]
        private float secondsPerTile = 0.35f;
        [SerializeField, Min(0f)]
        private float stopWaitSeconds = 1.5f;
        [SerializeField]
        private Vector2Int[] initialStops =
            System.Array.Empty<Vector2Int>();

        [Header("Passengers")]
        [SerializeField, Min(1)]
        private int passengerCapacity = 20;
        [SerializeField, Min(0)]
        private int boardingDemandPerStop = 3;
        [SerializeField, Min(0)]
        private int leavingDemandPerStop = 2;

        [Header("Presentation")]
        [SerializeField] private Color routeColor =
            new(0.1f, 0.75f, 0.85f, 1f);
        [SerializeField] private GameObject vehicleVisualPrefab;

        public string BusId => busId;
        public string DisplayName => displayName;
        public BusType BusType => busType;
        public float SecondsPerTile =>
            Mathf.Max(0.01f, secondsPerTile);
        public float StopWaitSeconds =>
            Mathf.Max(0f, stopWaitSeconds);
        public IReadOnlyList<Vector2Int> InitialStops =>
            initialStops;
        public int PassengerCapacity =>
            Mathf.Max(1, passengerCapacity);
        public int BoardingDemandPerStop =>
            Mathf.Max(0, boardingDemandPerStop);
        public int LeavingDemandPerStop =>
            Mathf.Max(0, leavingDemandPerStop);
        public Color RouteColor => routeColor;
        public GameObject VehicleVisualPrefab =>
            vehicleVisualPrefab;

#if UNITY_EDITOR
        private void OnValidate()
        {
            busId = busId?.Trim();
            displayName = displayName?.Trim();
            secondsPerTile = Mathf.Max(
                0.01f,
                secondsPerTile);
            stopWaitSeconds = Mathf.Max(
                0f,
                stopWaitSeconds);
            passengerCapacity = Mathf.Max(
                1,
                passengerCapacity);
            boardingDemandPerStop = Mathf.Max(
                0,
                boardingDemandPerStop);
            leavingDemandPerStop = Mathf.Max(
                0,
                leavingDemandPerStop);
            initialStops ??=
                System.Array.Empty<Vector2Int>();
        }
#endif
    }
}
