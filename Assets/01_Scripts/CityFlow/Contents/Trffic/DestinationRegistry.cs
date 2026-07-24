using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Content.Traffic
{
    public static class DestinationRegistry
    {
        private static readonly HashSet<BuildingDestination>
            registeredDestinations = new();

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry()
        {
            registeredDestinations.Clear();
        }

        public static void Register(
            BuildingDestination destination)
        {
            if (destination == null)
            {
                return;
            }

            registeredDestinations.Add(destination);
        }

        public static void Unregister(
            BuildingDestination destination)
        {
            if (destination == null)
            {
                return;
            }

            registeredDestinations.Remove(destination);
        }

        public static void GetAvailableDestinations(
            CityDestinationType destinationType,
            BuildingDestination currentDestination,
            BuildingDestination homeDestination,
            List<BuildingDestination> results,
            bool allowHomeDestination,
            float minimumTravelDistance)
        {
            results.Clear();

            float minimumDistanceSqr =
                minimumTravelDistance * minimumTravelDistance;

            Vector3 originPosition =
                currentDestination != null
                    ? currentDestination.VehicleStopPoint.position
                    : Vector3.zero;

            foreach (BuildingDestination destination
                     in registeredDestinations)
            {
                if (destination == null)
                {
                    continue;
                }

                if (!destination.isActiveAndEnabled)
                {
                    continue;
                }

                if (!destination.HasCapacity)
                {
                    continue;
                }

                if (destination.DestinationType != destinationType)
                {
                    continue;
                }

                if (destination == currentDestination)
                {
                    continue;
                }

                if (!allowHomeDestination &&
                    destination == homeDestination)
                {
                    continue;
                }

                if (currentDestination != null)
                {
                    float distanceSqr =
                        (
                            destination.VehicleStopPoint.position -
                            originPosition
                        ).sqrMagnitude;

                    if (distanceSqr < minimumDistanceSqr)
                    {
                        continue;
                    }
                }

                results.Add(destination);
            }
        }

        public static BuildingDestination GetRandomHome()
        {
            List<BuildingDestination> homes = new();

            foreach (BuildingDestination destination
                     in registeredDestinations)
            {
                if (destination == null)
                {
                    continue;
                }

                if (!destination.isActiveAndEnabled)
                {
                    continue;
                }

                if (!destination.CanBeUsedAsHome)
                {
                    continue;
                }

                homes.Add(destination);
            }

            if (homes.Count == 0)
            {
                return null;
            }

            int index = Random.Range(0, homes.Count);
            return homes[index];
        }
    }
}