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

        public bool TryGetVisualRenderers(
            Vector2Int anchor,
            out Renderer[] renderers)
        {
            renderers = System.Array.Empty<Renderer>();
            if (!visuals.TryGetValue(anchor, out GameObject visual) ||
                visual == null)
            {
                return false;
            }

            renderers = visual.GetComponentsInChildren<Renderer>(true);
            return renderers.Length > 0;
        }

        public bool TryGetParkingPose(
            Vector2Int anchor,
            int slotIndex,
            out BuildingParkingPose pose)
        {
            pose = default;
            if ((!visuals.TryGetValue(
                     anchor,
                     out GameObject visual) ||
                 visual == null) &&
                buildingService != null &&
                services?.WorldCoordinates != null &&
                buildingService.TryGetBuilding(
                    anchor,
                    out SpecialBuildingInstance building))
            {
                CreateOrReplaceVisual(building);
                visuals.TryGetValue(
                    building.Anchor,
                    out visual);
            }

            if (visual == null)
            {
                return false;
            }

            BuildingParkingLayout layout =
                visual.GetComponentInChildren<BuildingParkingLayout>(
                    true);
            return layout != null &&
                   layout.TryGetParkingPose(slotIndex, out pose);
        }

        public bool TryCreatePlacementPreview(
            string buildingId,
            out GameObject preview)
        {
            preview = null;
            if (catalog == null ||
                !catalog.TryGet(
                    buildingId,
                    out BuildingDefinitionSO definition))
            {
                return false;
            }

            if (definition.VisualPrefab == null &&
                fallbackPrefab == null)
            {
                return false;
            }

            var root =
                new GameObject(
                    $"PlacementPreview_{definition.buildingId}");
            CreateConfiguredVisual(
                definition,
                root.transform,
                "BuildingVisual");

            preview = root;
            return true;
        }

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
            services.Events.Placed += OnPlaced;
            services.SpecialBuildingsRegistered += OnServiceRegistered;
            services.WorldCoordinatesRegistered += OnCoordinatesRegistered;
            services.WorldCoordinateRootRegistered +=
                OnWorldCoordinateRootRegistered;
            initialized = true;
            EnsureVisualRoot();
            BindService(services.SpecialBuildings);
            RebuildAll();
        }

        private void OnDestroy()
        {
            if (services != null)
            {
                services.Events.Placed -= OnPlaced;
                services.SpecialBuildingsRegistered -= OnServiceRegistered;
                services.WorldCoordinatesRegistered -= OnCoordinatesRegistered;
                services.WorldCoordinateRootRegistered -=
                    OnWorldCoordinateRootRegistered;
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

        private void OnWorldCoordinateRootRegistered(IWorldCoordinateRoot _)
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

        private void OnPlaced(PlacedEvent placed)
        {
            if (placed.IsRemove ||
                !TileFootprint.IsSpecialBuilding(placed.Type) ||
                buildingService == null ||
                !buildingService.TryGetBuilding(
                    placed.Tile,
                    out SpecialBuildingInstance building))
            {
                return;
            }

            CreateOrReplaceVisual(building);
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

            TileType placedType = services.TileData != null
                ? services.TileData.GetTileType(building.Anchor)
                : TileType.Empty;
            if (placedType == TileType.UnderConstruction)
            {
                return;
            }

            EnsureVisualRoot();

            IWorldCoordinateSpace coordinates = services.WorldCoordinates;
            Vector2Int footprint = definition.Footprint;
            if (TileFootprint.IsSpecialBuilding(placedType))
            {
                // 구 저장의 2x2 약국·커피숍은 기존 점유 중심을 유지한다.
                footprint = TileFootprint.GetSize(placedType);
            }
            footprint = TileFootprint.GetRotatedSize(
                footprint,
                building.Direction);
            Vector2 center = new Vector2(
                building.Anchor.x + footprint.x * 0.5f,
                building.Anchor.y + footprint.y * 0.5f);
            Quaternion directionRotation = Quaternion.Euler(
                0f,
                0f,
                TileFootprint.ToAngle(building.Direction));
            Quaternion rootRotation =
                coordinates.CoordinateRotation *
                directionRotation;

            var root = new GameObject(
                $"{building.BuildingId}_" +
                $"{building.Anchor.x}_{building.Anchor.y}");
            root.transform.SetParent(visualRoot, false);
            root.transform.SetPositionAndRotation(
                coordinates.GridPointToWorld(
                    center,
                    ResolveSurfaceOffset()),
                rootRotation);
            CreateConfiguredVisual(
                definition,
                root.transform,
                "BuildingVisual");

            visuals.Add(building.Anchor, root);
        }

        private float ResolveSurfaceOffset()
        {
            // Coordinate offsets follow GroundNormal, opposite MainCityView +Z.
            return services?.WorldCoordinateRoot is MainCityView cityView
                ? -cityView.RoadSurfaceZ
                : surfaceOffset;
        }

        private GameObject CreateConfiguredVisual(
            BuildingDefinitionSO definition,
            Transform parent,
            string visualName)
        {
            bool usesFallback =
                definition.VisualPrefab == null;
            GameObject sourcePrefab = usesFallback
                ? fallbackPrefab
                : definition.VisualPrefab;
            GameObject instance =
                Instantiate(sourcePrefab, parent);
            instance.name = visualName;

            Quaternion visualRotation =
                Quaternion.Euler(
                    definition.VisualEulerAngles);
            instance.transform.localPosition =
                visualRotation *
                definition.VisualOffset;
            instance.transform.localRotation =
                visualRotation;
            instance.transform.localScale =
                definition.VisualScale;
            DisableColliders(instance);

            if (usesFallback &&
                instance.TryGetComponent(
                    out SpecialBuildingFallbackPresenter presenter))
            {
                float tileSize =
                    services?.WorldCoordinates?.TileSize ??
                    1f;
                presenter.Configure(
                    definition,
                    tileSize);
            }

            if (!usesFallback &&
                (definition.category == BuildingCategory.Medical ||
                 definition.category == BuildingCategory.Civic))
            {
                Transform authoredModel =
                    instance.transform.Find("Model");
                BuildingNightLighting.Attach(
                    authoredModel != null
                        ? authoredModel.gameObject
                        : instance,
                    services,
                    BuildingNightLightProfile.StudioHorizonCivic);
            }

            return instance;
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
