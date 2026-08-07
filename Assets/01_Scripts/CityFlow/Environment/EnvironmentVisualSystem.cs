using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.View;
using UnityEngine;

namespace CityFlow.Environment
{
    [DisallowMultipleComponent]
    public sealed class EnvironmentVisualSystem :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        private const float ViewResolveInterval = 1f;

        private readonly List<EnvironmentVisualModule> modules = new();

        private CityFlowServices services;
        private IGameCalendarService calendar;
        private MainCityView cityView;
        private float nextViewResolveTime;
        private bool missingCameraWarningLogged;

        public Camera ActiveCamera { get; private set; }
        public IWorldCoordinateSpace WorldCoordinates { get; private set; }
        public float NormalizedZoom01 => cityView != null
            ? cityView.NormalizedZoom01
            : 0.5f;
        public bool IsDriveViewActive =>
            cityView != null && cityView.IsDriveViewActive;
        public int CurrentHour => calendar?.Hour ?? 12;
        public long CurrentGameHourIndex => calendar != null
            ? calendar.TotalDays * calendar.HoursPerDay + calendar.Hour
            : CurrentHour;

        public void Initialize(CityFlowServices cityFlowServices)
        {
            if (ReferenceEquals(services, cityFlowServices))
            {
                return;
            }

            UnbindServices();
            services = cityFlowServices;
            if (services == null)
            {
                return;
            }

            services.WorldCoordinatesRegistered += OnWorldCoordinatesRegistered;
            services.GameCalendarRegistered += BindCalendar;
            OnWorldCoordinatesRegistered(services.WorldCoordinates);
            BindCalendar(services.GameCalendar);
        }

        public T GetModule<T>() where T : EnvironmentVisualModule
        {
            for (int index = 0; index < modules.Count; index++)
            {
                if (modules[index] is T module)
                {
                    return module;
                }
            }

            return null;
        }

        public void SetAllVisualsEnabled(bool isEnabled)
        {
            for (int index = 0; index < modules.Count; index++)
            {
                modules[index]?.SetVisualEnabled(isEnabled);
            }
        }

        public bool TryGetViewAnchor(
            out Vector3 groundPosition,
            out Quaternion coordinateRotation)
        {
            groundPosition = default;
            coordinateRotation = WorldCoordinates?.CoordinateRotation
                ?? Quaternion.identity;
            if (ActiveCamera == null)
            {
                return false;
            }

            Vector3 normal = WorldCoordinates?.GroundNormal
                ?? Vector3.back;
            Vector3 origin = WorldCoordinates?.Origin
                ?? Vector3.zero;
            var groundPlane = new Plane(normal, origin);
            Ray centerRay = ActiveCamera.ViewportPointToRay(
                new Vector3(0.5f, 0.5f, 0f));
            if (!groundPlane.Raycast(centerRay, out float distance))
            {
                return false;
            }

            groundPosition = centerRay.GetPoint(distance);
            return true;
        }

        private void Awake()
        {
            EnvironmentVisualModule[] discoveredModules =
                GetComponentsInChildren<EnvironmentVisualModule>(true);
            for (int index = 0; index < discoveredModules.Length; index++)
            {
                EnvironmentVisualModule module = discoveredModules[index];
                if (module == null || modules.Contains(module))
                {
                    continue;
                }

                modules.Add(module);
                module.InitializeModule(this);
            }
        }

        private void Update()
        {
            ResolveViewContext();
            if (ActiveCamera == null)
            {
                if (!missingCameraWarningLogged)
                {
                    missingCameraWarningLogged = true;
                    Debug.LogWarning(
                        "[EnvironmentVisualSystem] No active camera was found. " +
                        "Environment visuals are waiting for a camera.",
                        this);
                }

                return;
            }

            missingCameraWarningLogged = false;
            float deltaTime = Time.unscaledDeltaTime;
            for (int index = 0; index < modules.Count; index++)
            {
                modules[index]?.TickModule(deltaTime);
            }
        }

        private void OnDestroy()
        {
            for (int index = 0; index < modules.Count; index++)
            {
                modules[index]?.ShutdownModule();
            }

            modules.Clear();
            UnbindServices();
        }

        private void ResolveViewContext()
        {
            if (cityView == null && Time.unscaledTime >= nextViewResolveTime)
            {
                cityView = FindAnyObjectByType<MainCityView>();
                nextViewResolveTime = Time.unscaledTime + ViewResolveInterval;
            }

            Camera nextCamera = cityView != null
                ? cityView.ActiveViewCamera
                : null;
            if (nextCamera == null || !nextCamera.isActiveAndEnabled)
            {
                nextCamera = Camera.main;
            }

            ActiveCamera = nextCamera;
        }

        private void OnWorldCoordinatesRegistered(
            IWorldCoordinateSpace worldCoordinates)
        {
            WorldCoordinates = worldCoordinates;
        }

        private void BindCalendar(IGameCalendarService gameCalendar)
        {
            calendar = gameCalendar;
        }

        private void UnbindServices()
        {
            if (services != null)
            {
                services.WorldCoordinatesRegistered -=
                    OnWorldCoordinatesRegistered;
                services.GameCalendarRegistered -= BindCalendar;
            }

            services = null;
            calendar = null;
            WorldCoordinates = null;
        }

        // Unity setup:
        // Place EnvironmentVisualSystem.prefab beside CityBootstrap.
    }
}
