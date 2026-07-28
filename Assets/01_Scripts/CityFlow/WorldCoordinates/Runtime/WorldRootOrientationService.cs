using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.WorldCoordinates
{
    [DisallowMultipleComponent]
    public sealed class WorldRootOrientationService :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        [SerializeField] private WorldCoordinateService coordinateService;

        private CityFlowServices services;
        private bool initialized;
        private bool applied;

        public WorldCoordinateService CoordinateService => coordinateService;
        public bool IsApplied => applied;

        public void Initialize(CityFlowServices cityFlowServices)
        {
            if (!isActiveAndEnabled || initialized)
            {
                return;
            }

            coordinateService ??= GetComponent<WorldCoordinateService>();
            if (cityFlowServices == null || coordinateService == null)
            {
                Debug.LogWarning(
                    "[WorldRootOrientationService] Services or coordinate " +
                    "service is missing.",
                    this);
                return;
            }

            services = cityFlowServices;
            services.WorldCoordinatesRegistered += OnCoordinatesRegistered;
            services.WorldCoordinateRootRegistered += OnRootRegistered;
            initialized = true;
            TryApply();
        }

        private void OnDestroy()
        {
            if (services == null)
            {
                return;
            }

            services.WorldCoordinatesRegistered -= OnCoordinatesRegistered;
            services.WorldCoordinateRootRegistered -= OnRootRegistered;
        }

        private void OnCoordinatesRegistered(IWorldCoordinateSpace _)
        {
            TryApply();
        }

        private void OnRootRegistered(IWorldCoordinateRoot _)
        {
            TryApply();
        }

        private void TryApply()
        {
            if (services?.WorldCoordinateRoot == null ||
                !ReferenceEquals(
                    services.WorldCoordinates,
                    coordinateService))
            {
                return;
            }

            services.WorldCoordinateRoot.ApplyCoordinateSpace(
                services.WorldCoordinates);
            if (!applied)
            {
                applied = true;
                Debug.Log(
                    "[WorldRootOrientationService] Applied the coordinate " +
                    "profile to the registered world root.",
                    this);
            }
        }
    }
}

// Unity setup: This component is prewired in WorldCoordinateSystem.prefab.
