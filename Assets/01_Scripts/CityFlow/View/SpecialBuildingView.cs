using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.View
{
    [DisallowMultipleComponent]
    public sealed class SpecialBuildingView :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        [SerializeField]
        private BuildingCatalogSO catalog;

        [SerializeField]
        private GameObject fallbackPrefab;

        [SerializeField, Min(0f)]
        private float surfaceOffset = 0.02f;

        private readonly Dictionary<Vector2Int, GameObject> visuals = new();
        private CityFlowServices services;
        private ISpecialBuildingService buildingService;
        private Transform visualRoot;
        private bool initialized;

        public int VisualCount => visuals.Count;

        public void Initialize(CityFlowServices cityServices)
        {
            if (!isActiveAndEnabled || initialized)
            {
                return;
            }

            if (cityServices == null || catalog == null ||
                fallbackPrefab == null)
            {
                Debug.LogWarning(
                    "[SpecialBuildingView] Services, catalog, or fallback " +
                    "Prefab is missing.",
                    this);
                return;
            }

            services = cityServices;
            services.SpecialBuildingsRegistered += OnServiceRegistered;
            services.WorldCoordinatesRegistered += OnCoordinatesRegistered;
            initialized = true;
            EnsureVisualRoot();
            BindService(services.SpecialBuildings);
            RebuildAll();
        }

        private void OnDestroy()
        {
            if (services != null)
            {
                services.SpecialBuildingsRegistered -= OnServiceRegistered;
                services.WorldCoordinatesRegistered -= OnCoordinatesRegistered;
            }

            BindService(null);
            ClearVisuals();
        }

        private void OnServiceRegistered(
            ISpecialBuildingService registeredService)
        {
            BindService(registeredService);
            RebuildAll();
        }

        private void OnCoordinatesRegistered(IWorldCoordinateSpace _)
        {
            RebuildAll();
        }

        private void BindService(ISpecialBuildingService nextService)
        {
            if (ReferenceEquals(buildingService, nextService))
            {
                return;
            }

            if (buildingService != null)
            {
                buildingService.BuildingChanged -= OnBuildingChanged;
                buildingService.BuildingsRestored -= OnBuildingsRestored;
            }

            buildingService = nextService;
            if (buildingService != null)
            {
                buildingService.BuildingChanged += OnBuildingChanged;
                buildingService.BuildingsRestored += OnBuildingsRestored;
            }
        }

        private void OnBuildingChanged(SpecialBuildingChangedEvent changed)
        {
            if (changed.IsRemove)
            {
                RemoveVisual(changed.Building.Anchor);
                return;
            }

            CreateOrReplaceVisual(changed.Building);
        }

        private void OnBuildingsRestored()
        {
            RebuildAll();
        }

        private void RebuildAll()
        {
            ClearVisuals();
            if (!initialized || buildingService == null ||
                services?.WorldCoordinates == null)
            {
                return;
            }

            SpecialBuildingInstance[] buildings =
                buildingService.CreateBuildingSnapshot();
            for (int index = 0; index < buildings.Length; index++)
            {
                CreateOrReplaceVisual(buildings[index]);
            }
        }

        private void CreateOrReplaceVisual(SpecialBuildingInstance building)
        {
            if (services?.WorldCoordinates == null ||
                !catalog.TryGet(
                    building.BuildingId,
                    out BuildingDefinitionSO definition))
            {
                return;
            }

            RemoveVisual(building.Anchor);
            EnsureVisualRoot();

            bool usesFallback = definition.VisualPrefab == null;
            GameObject sourcePrefab = usesFallback
                ? fallbackPrefab
                : definition.VisualPrefab;
            GameObject instance = Instantiate(sourcePrefab, visualRoot);
            instance.name = $"{building.BuildingId}_" +
                            $"{building.Anchor.x}_{building.Anchor.y}";

            IWorldCoordinateSpace coordinates = services.WorldCoordinates;
            Vector2Int footprint = definition.Footprint;
            Vector2 center = new Vector2(
                building.Anchor.x + footprint.x * 0.5f,
                building.Anchor.y + footprint.y * 0.5f);
            Quaternion directionRotation = Quaternion.Euler(
                0f,
                0f,
                TileFootprint.ToAngle(building.Direction));
            Quaternion worldRotation =
                coordinates.CoordinateRotation *
                directionRotation *
                Quaternion.Euler(definition.VisualEulerAngles);

            instance.transform.SetPositionAndRotation(
                coordinates.GridPointToWorld(center, surfaceOffset) +
                worldRotation * definition.VisualOffset,
                worldRotation);
            instance.transform.localScale = definition.VisualScale;
            DisableColliders(instance);

            if (usesFallback &&
                instance.TryGetComponent(
                    out SpecialBuildingFallbackPresenter presenter))
            {
                presenter.Configure(definition, coordinates.TileSize);
            }

            visuals.Add(building.Anchor, instance);
        }

        private void EnsureVisualRoot()
        {
            if (visualRoot != null)
            {
                return;
            }

            var root = new GameObject("SpecialBuildingVisuals");
            visualRoot = root.transform;
            visualRoot.SetParent(transform, false);
        }

        private void RemoveVisual(Vector2Int anchor)
        {
            if (!visuals.TryGetValue(anchor, out GameObject instance))
            {
                return;
            }

            visuals.Remove(anchor);
            DestroyUnityObject(instance);
        }

        private void ClearVisuals()
        {
            foreach (GameObject instance in visuals.Values)
            {
                DestroyUnityObject(instance);
            }

            visuals.Clear();
        }

        private static void DisableColliders(GameObject root)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = false;
            }
        }

        private static void DestroyUnityObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}

// Unity setup: This component is prewired in SpecialBuildingSystem.prefab.
