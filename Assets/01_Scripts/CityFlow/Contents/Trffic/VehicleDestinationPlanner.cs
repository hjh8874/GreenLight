using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Content.Traffic
{
    [DisallowMultipleComponent]
    public sealed class VehicleDestinationPlanner : MonoBehaviour
    {
        [Header("Vehicle Life Profile")]
        [SerializeField]
        private VehicleVisitProfileSO visitProfile;

        [Header("Vehicle Home")]
        [SerializeField]
        private BuildingDestination homeDestination;

        [SerializeField]
        private bool assignRandomHomeOnStart = true;

        [Header("Initial Position")]
        [SerializeField]
        private BuildingDestination initialDestination;

        [Header("Destination Selection")]
        [Min(0f)]
        [SerializeField]
        private float minimumTravelDistance = 2f;

        [Tooltip("가까운 건물을 선호하도록 설정합니다.")]
        [SerializeField]
        private bool preferNearbyDestination = true;

        [Min(1)]
        [SerializeField]
        private int nearbyCandidateCount = 3;

        private readonly List<BuildingDestination>
            candidateBuffer = new();

        private BuildingDestination occupiedDestination;
        private BuildingDestination reservedDestination;
        private BuildingDestination currentTargetDestination;

        private bool returnHomeOnNextTrip;
        private bool currentVisitRequiresReturnHome;

        public VehicleVisitProfileSO VisitProfile =>
            visitProfile;

        public BuildingDestination HomeDestination =>
            homeDestination;

        public BuildingDestination OccupiedDestination =>
            occupiedDestination;

        public BuildingDestination CurrentTargetDestination =>
            currentTargetDestination;

        public event Action<BuildingDestination>
            DestinationSelected;

        public event Action<BuildingDestination>
            DestinationReached;

        private void Awake()
        {
            occupiedDestination = initialDestination;

            if (homeDestination == null &&
                assignRandomHomeOnStart)
            {
                homeDestination =
                    DestinationRegistry.GetRandomHome();
            }

            if (occupiedDestination == null)
            {
                occupiedDestination = homeDestination;
            }
        }

        private void OnDisable()
        {
            ReleaseReservedDestination();
            ReleaseOccupiedDestination();
        }

        public BuildingDestination SelectNextDestination(
            int currentHour)
        {
            ReleaseReservedDestination();

            BuildingDestination selectedDestination = null;
            bool requiresReturnHome = false;

            if (returnHomeOnNextTrip)
            {
                selectedDestination = SelectHomeDestination();
                returnHomeOnNextTrip = false;
            }
            else
            {
                selectedDestination =
                    SelectProfileDestination(
                        currentHour,
                        out requiresReturnHome);
            }

            if (selectedDestination == null &&
                visitProfile != null &&
                visitProfile.ReturnHomeWhenNoRule)
            {
                selectedDestination =
                    SelectHomeDestination();

                requiresReturnHome = false;
            }

            if (selectedDestination == null)
            {
                return null;
            }

            if (!selectedDestination.TryReserve())
            {
                return null;
            }

            reservedDestination = selectedDestination;
            currentTargetDestination = selectedDestination;

            currentVisitRequiresReturnHome =
                requiresReturnHome;

            DestinationSelected?.Invoke(selectedDestination);

            return selectedDestination;
        }

        public void NotifyDestinationReached()
        {
            if (currentTargetDestination == null)
            {
                return;
            }

            ReleaseOccupiedDestination();

            occupiedDestination = reservedDestination;
            reservedDestination = null;

            BuildingDestination reachedDestination =
                currentTargetDestination;

            currentTargetDestination = null;

            if (currentVisitRequiresReturnHome &&
                reachedDestination != homeDestination)
            {
                returnHomeOnNextTrip = true;
            }

            currentVisitRequiresReturnHome = false;

            DestinationReached?.Invoke(reachedDestination);
        }

        public void CancelCurrentTrip()
        {
            ReleaseReservedDestination();

            currentTargetDestination = null;
            currentVisitRequiresReturnHome = false;
        }

        private BuildingDestination SelectProfileDestination(
            int currentHour,
            out bool requiresReturnHome)
        {
            requiresReturnHome = false;

            if (visitProfile == null)
            {
                return null;
            }

            bool hasRule =
                visitProfile.TrySelectDestinationType(
                    currentHour,
                    out CityDestinationType destinationType,
                    out requiresReturnHome);

            if (!hasRule)
            {
                return null;
            }

            if (destinationType ==
                CityDestinationType.Residential)
            {
                return SelectHomeDestination();
            }

            DestinationRegistry.GetAvailableDestinations(
                destinationType,
                occupiedDestination,
                homeDestination,
                candidateBuffer,
                allowHomeDestination: false,
                minimumTravelDistance);

            if (candidateBuffer.Count == 0)
            {
                return null;
            }

            if (preferNearbyDestination)
            {
                SortCandidatesByDistance();
            }

            int selectableCount = preferNearbyDestination
                ? Mathf.Min(
                    nearbyCandidateCount,
                    candidateBuffer.Count)
                : candidateBuffer.Count;

            int randomIndex =
                UnityEngine.Random.Range(
                    0,
                    selectableCount);

            return candidateBuffer[randomIndex];
        }

        private BuildingDestination SelectHomeDestination()
        {
            if (homeDestination == null)
            {
                homeDestination =
                    DestinationRegistry.GetRandomHome();
            }

            if (homeDestination == null)
            {
                return null;
            }

            if (homeDestination == occupiedDestination)
            {
                return null;
            }

            if (!homeDestination.HasCapacity)
            {
                return null;
            }

            return homeDestination;
        }

        private void SortCandidatesByDistance()
        {
            Vector3 originPosition =
                occupiedDestination != null
                    ? occupiedDestination.VehicleStopPoint.position
                    : transform.position;

            candidateBuffer.Sort(
                (left, right) =>
                {
                    float leftDistance =
                        (
                            left.VehicleStopPoint.position -
                            originPosition
                        ).sqrMagnitude;

                    float rightDistance =
                        (
                            right.VehicleStopPoint.position -
                            originPosition
                        ).sqrMagnitude;

                    return leftDistance.CompareTo(rightDistance);
                });
        }

        private void ReleaseReservedDestination()
        {
            if (reservedDestination == null)
            {
                return;
            }

            reservedDestination.Release();
            reservedDestination = null;
        }

        private void ReleaseOccupiedDestination()
        {
            if (occupiedDestination == null)
            {
                return;
            }

            occupiedDestination.Release();
            occupiedDestination = null;
        }
    }
}