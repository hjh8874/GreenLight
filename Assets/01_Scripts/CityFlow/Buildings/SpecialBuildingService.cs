using System;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Buildings
{
    [DisallowMultipleComponent]
    public sealed class SpecialBuildingService :
        MonoBehaviour,
        ICityFlowServiceConsumer,
        ISpecialBuildingService,
        ISpecialBuildingSaveSource
    {
        [SerializeField]
        private BuildingCatalogSO catalog;

        [SerializeField]
        private string playModeTestBuildingId = "mall";

        [SerializeField]
        private Vector2Int playModeTestAnchor = new Vector2Int(100, 100);

        private readonly SpecialBuildingState state = new();
        private CityFlowServices services;
        private IResearchUnlockService research;
        private bool initialized;

        public BuildingCatalogSO Catalog => catalog;
        public int BuildingCount => state.Count;

        public event Action<SpecialBuildingChangedEvent> BuildingChanged;
        public event Action BuildingsRestored;
        public event Action BuildOptionsChanged;
        public event Action<HappinessEffectChangedEvent>
            HappinessEffectChanged;

        public void Initialize(CityFlowServices cityServices)
        {
            if (!isActiveAndEnabled || initialized)
            {
                return;
            }

            if (cityServices?.Placement == null ||
                cityServices.TileData == null ||
                catalog == null)
            {
                Debug.LogWarning(
                    "[SpecialBuildingService] Placement, tile data, or catalog is missing.",
                    this);
                return;
            }

            services = cityServices;
            initialized = true;

            if (!services.RegisterSpecialBuildings(this))
            {
                initialized = false;
                services = null;
                Debug.LogWarning(
                    "[SpecialBuildingService] Another special building service is registered.",
                    this);
                return;
            }

            services.ResearchRegistered += OnResearchRegistered;
            BindResearch(services.Research);
            services.Events.Placed += OnPlaced;
            Debug.Log(
                $"[SpecialBuildingService] Registered catalog with {catalog.Count} definitions.",
                this);
        }

        private void OnDestroy()
        {
            if (initialized && services != null)
            {
                services.Events.Placed -= OnPlaced;
                services.ResearchRegistered -= OnResearchRegistered;
            }

            BindResearch(null);
        }

        public bool CanPlace(
            string buildingId,
            Vector2Int anchor,
            PlacementDirection direction = PlacementDirection.North)
        {
            return initialized &&
                   TryResolveDefinition(
                       buildingId,
                       out BuildingDefinitionSO definition) &&
                   IsDefinitionUnlocked(definition) &&
                   services.Placement.CanPlace(
                       anchor,
                       TileType.SpecialBuilding,
                       direction);
        }

        public bool TryPlace(
            string buildingId,
            Vector2Int anchor,
            PlacementDirection direction = PlacementDirection.North)
        {
            if (!CanPlace(buildingId, anchor, direction) ||
                !state.TryAdd(buildingId, anchor, direction))
            {
                return false;
            }

            if (!services.Placement.Place(
                    anchor,
                    TileType.SpecialBuilding,
                    direction))
            {
                state.TryRemove(anchor, out _);
                return false;
            }

            state.TryGet(anchor, out SpecialBuildingInstance building);
            BuildingChanged?.Invoke(
                new SpecialBuildingChangedEvent(building, isRemove: false));
            PublishHappinessEffect(building, isActive: true);
            return true;
        }

        public bool TryRemove(Vector2Int tile)
        {
            if (!initialized ||
                !TryResolveAnchor(tile, out Vector2Int anchor) ||
                !state.TryRemove(anchor, out SpecialBuildingInstance building))
            {
                return false;
            }

            if (!services.Placement.Remove(anchor))
            {
                state.TryAdd(
                    building.BuildingId,
                    building.Anchor,
                    building.Direction);
                return false;
            }

            BuildingChanged?.Invoke(
                new SpecialBuildingChangedEvent(building, isRemove: true));
            PublishHappinessEffect(building, isActive: false);
            return true;
        }

        public bool TryGetBuilding(
            Vector2Int tile,
            out SpecialBuildingInstance building)
        {
            building = default;
            return initialized &&
                   TryResolveAnchor(tile, out Vector2Int anchor) &&
                   state.TryGet(anchor, out building);
        }

        public bool IsBuildingUnlocked(string buildingId)
        {
            return initialized &&
                   TryResolveDefinition(
                       buildingId,
                       out BuildingDefinitionSO definition) &&
                   IsDefinitionUnlocked(definition);
        }

        public bool TryGetBuildOption(
            string buildingId,
            out SpecialBuildingBuildOption option)
        {
            option = default;
            return TryResolveDefinition(
                       buildingId,
                       out BuildingDefinitionSO definition) &&
                   TryCreateBuildOption(definition, out option);
        }

        public SpecialBuildingInstance[] CreateBuildingSnapshot() =>
            state.CreateInstanceSnapshot();

        public SpecialBuildingBuildOption[] CreateBuildOptionSnapshot()
        {
            if (catalog == null || catalog.Buildings == null)
            {
                return Array.Empty<SpecialBuildingBuildOption>();
            }

            var options = new System.Collections.Generic.List<
                SpecialBuildingBuildOption>(catalog.Count);
            for (int index = 0; index < catalog.Buildings.Count; index++)
            {
                if (TryCreateBuildOption(
                        catalog.Buildings[index],
                        out SpecialBuildingBuildOption option))
                {
                    options.Add(option);
                }
            }

            return options.ToArray();
        }

        public HappinessEffectDescriptor[]
            CreateActiveHappinessEffectSnapshot()
        {
            SpecialBuildingInstance[] buildings =
                state.CreateInstanceSnapshot();
            var effects = new System.Collections.Generic.List<
                HappinessEffectDescriptor>(buildings.Length);

            for (int index = 0; index < buildings.Length; index++)
            {
                if (TryCreateHappinessEffect(
                        buildings[index],
                        out HappinessEffectDescriptor effect))
                {
                    effects.Add(effect);
                }
            }

            return effects.ToArray();
        }

#if UNITY_EDITOR
        [ContextMenu("Play Mode Test/Place Selected Building")]
        private void PlaceSelectedBuildingForPlayModeTest()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning(
                    "[SpecialBuildingService] Enter Play Mode first.",
                    this);
                return;
            }

            if (!TryPlace(playModeTestBuildingId, playModeTestAnchor))
            {
                Debug.LogWarning(
                    "[SpecialBuildingService] Test placement failed. " +
                    "Unlock its research and check the target tiles.",
                    this);
            }
        }

        [ContextMenu("Play Mode Test/Remove Selected Building")]
        private void RemoveSelectedBuildingForPlayModeTest()
        {
            if (!Application.isPlaying ||
                !TryRemove(playModeTestAnchor))
            {
                Debug.LogWarning(
                    "[SpecialBuildingService] Test removal failed.",
                    this);
            }
        }

        private void OnValidate()
        {
            playModeTestBuildingId =
                playModeTestBuildingId?.Trim() ?? string.Empty;
        }
#endif

        public SpecialBuildingSaveData CreateSnapshot() =>
            state.CreateSnapshot();

        public void RestoreSnapshot(SpecialBuildingSaveData snapshot)
        {
            SpecialBuildingInstance[] previousBuildings =
                state.CreateInstanceSnapshot();
            for (int index = 0; index < previousBuildings.Length; index++)
            {
                PublishHappinessEffect(
                    previousBuildings[index],
                    isActive: false);
            }

            state.Clear();
            SpecialBuildingInstanceSaveData[] entries = snapshot?.Buildings;

            if (entries != null)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    SpecialBuildingInstanceSaveData saved = entries[i];
                    if (saved == null ||
                        !TryResolveDefinition(saved.BuildingId, out _))
                    {
                        continue;
                    }

                    Vector2Int anchor = new Vector2Int(saved.X, saved.Y);
                    if (services?.TileData == null ||
                        services.TileData.GetTileType(anchor) !=
                        TileType.SpecialBuilding ||
                        !services.TileData.IsFootprintAnchor(anchor))
                    {
                        continue;
                    }

                    state.TryAdd(
                        saved.BuildingId,
                        anchor,
                        services.TileData.GetDirection(anchor));
                }
            }

            BuildingsRestored?.Invoke();
            SpecialBuildingInstance[] restoredBuildings =
                state.CreateInstanceSnapshot();
            for (int index = 0; index < restoredBuildings.Length; index++)
            {
                PublishHappinessEffect(
                    restoredBuildings[index],
                    isActive: true);
            }
        }

        private void OnPlaced(PlacedEvent placed)
        {
            if (placed.Type != TileType.SpecialBuilding)
            {
                return;
            }

            if (placed.IsRemove &&
                state.TryRemove(
                    placed.Tile,
                    out SpecialBuildingInstance removed))
            {
                BuildingChanged?.Invoke(
                    new SpecialBuildingChangedEvent(
                        removed,
                        isRemove: true));
                PublishHappinessEffect(removed, isActive: false);
                return;
            }

            if (!placed.IsRemove && !state.TryGet(placed.Tile, out _))
            {
                Debug.LogWarning(
                    "[SpecialBuildingService] A SpecialBuilding tile was placed " +
                    "without a building ID. Use ISpecialBuildingService.TryPlace().",
                    this);
            }
        }

        private bool TryResolveDefinition(
            string buildingId,
            out BuildingDefinitionSO definition)
        {
            definition = null;
            if (catalog == null || !catalog.TryGet(buildingId, out definition))
            {
                return false;
            }

            Vector2Int supported =
                TileFootprint.GetSize(TileType.SpecialBuilding);
            if (definition.Footprint != supported)
            {
                Debug.LogWarning(
                    $"[SpecialBuildingService] {definition.buildingId} uses " +
                    $"unsupported footprint {definition.Footprint}. " +
                    $"Current special buildings require {supported}.",
                    definition);
                definition = null;
                return false;
            }

            return true;
        }

        private bool IsDefinitionUnlocked(BuildingDefinitionSO definition)
        {
            if (definition == null)
            {
                return false;
            }

            return definition.unlockedByDefault ||
                   string.IsNullOrEmpty(definition.RequiredResearchId) ||
                   research?.IsUnlocked(
                       definition.RequiredResearchId) == true;
        }

        private bool TryCreateBuildOption(
            BuildingDefinitionSO definition,
            out SpecialBuildingBuildOption option)
        {
            option = default;
            if (definition == null ||
                string.IsNullOrWhiteSpace(definition.buildingId))
            {
                return false;
            }

            VisitCadence cadence = definition.VisitCadence;
            option = new SpecialBuildingBuildOption(
                definition.buildingId,
                definition.buildingName,
                definition.category.ToString(),
                definition.description,
                definition.BuildingIcon,
                definition.FallbackColor,
                definition.MenuCategory,
                definition.buildCost,
                IsDefinitionUnlocked(definition),
                definition.RequiredResearchId,
                definition.CanReceiveVisitors,
                cadence.VisitsPerPeriod,
                cadence.PeriodDays,
                definition.VisitorCapacity,
                definition.AttractionWeight,
                definition.CoinPerVisit);
            return true;
        }

        private void OnResearchRegistered(
            IResearchUnlockService registeredResearch)
        {
            BindResearch(registeredResearch);
            BuildOptionsChanged?.Invoke();
        }

        private void BindResearch(IResearchUnlockService nextResearch)
        {
            if (ReferenceEquals(research, nextResearch))
            {
                return;
            }

            if (research != null)
            {
                research.ResearchUnlocked -= OnResearchUnlocked;
                research.ResearchStateRestored -= OnResearchStateRestored;
            }

            research = nextResearch;
            if (research != null)
            {
                research.ResearchUnlocked += OnResearchUnlocked;
                research.ResearchStateRestored += OnResearchStateRestored;
            }
        }

        private void OnResearchUnlocked(string _)
        {
            BuildOptionsChanged?.Invoke();
        }

        private void OnResearchStateRestored()
        {
            BuildOptionsChanged?.Invoke();
        }

        private void PublishHappinessEffect(
            SpecialBuildingInstance building,
            bool isActive)
        {
            if (!TryCreateHappinessEffect(building, out var effect))
            {
                return;
            }

            HappinessEffectChanged?.Invoke(
                new HappinessEffectChangedEvent(effect, isActive));
        }

        private bool TryCreateHappinessEffect(
            SpecialBuildingInstance building,
            out HappinessEffectDescriptor effect)
        {
            effect = default;
            if (!TryResolveDefinition(
                    building.BuildingId,
                    out BuildingDefinitionSO definition) ||
                string.IsNullOrEmpty(definition.HappinessEffectKey))
            {
                return false;
            }

            effect = new HappinessEffectDescriptor(
                $"special-building:{building.BuildingId}:" +
                $"{building.Anchor.x}:{building.Anchor.y}",
                definition.HappinessEffectKey,
                building.BuildingId,
                building.Anchor);
            return true;
        }

        private bool TryResolveAnchor(
            Vector2Int tile,
            out Vector2Int anchor)
        {
            anchor = tile;
            if (state.TryGet(anchor, out _))
            {
                return true;
            }

            return services?.TileData != null &&
                   services.TileData.TryGetFootprintAnchor(tile, out anchor) &&
                   state.TryGet(anchor, out _);
        }
    }
}
