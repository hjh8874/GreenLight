using System;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.Save;

namespace CityFlow.Bootstrap
{
    public sealed class CityFlowServices
    {
        public SimEventHub Events { get; }
        public IReadOnlyTileData TileData { get; }
        public IPlacementService Placement { get; }
        public IReadOnlyCityStats Stats { get; }
        public SaveService Save { get; }
        public IEconomyService Economy { get; private set; }
        public IGameCalendarService GameCalendar { get; private set; }
        public IWeeklyEconomyService WeeklyEconomy { get; private set; }
        public IWorldGridService WorldGrid { get; private set; }
        public IWorldGridExpansionService WorldGridExpansion { get; private set; }
        public IWorldCoordinateSpace WorldCoordinates { get; private set; }
        public IWorldCoordinateRoot WorldCoordinateRoot { get; private set; }
        public IResearchUnlockService Research { get; private set; }
        public ISpecialBuildingService SpecialBuildings { get; private set; }
        public IReadOnlyPopulationData Population { get; private set; }
        public ISpecialBuildingVisitService SpecialBuildingVisits
        {
            get;
            private set;
        }

        public event Action<IEconomyService> EconomyRegistered;
        public event Action<IGameCalendarService> GameCalendarRegistered;
        public event Action<IWeeklyEconomyService> WeeklyEconomyRegistered;
        public event Action<IWorldGridService> WorldGridRegistered;
        public event Action<IWorldGridExpansionService>
            WorldGridExpansionRegistered;
        public event Action<IWorldCoordinateSpace> WorldCoordinatesRegistered;
        public event Action<IWorldCoordinateRoot> WorldCoordinateRootRegistered;
        public event Action<IResearchUnlockService> ResearchRegistered;
        public event Action<ISpecialBuildingService> SpecialBuildingsRegistered;
        public event Action<IReadOnlyPopulationData> PopulationRegistered;
        public event Action<ISpecialBuildingVisitService>
            SpecialBuildingVisitsRegistered;

        public CityFlowServices(
            SimEventHub events,
            IReadOnlyTileData tileData,
            IPlacementService placement,
            SaveService save = null,
            IEconomyService economy = null,
            IReadOnlyCityStats stats = null)
        {
            Events = events;
            TileData = tileData;
            Placement = placement;
            Stats = stats;
            Save = save;
            RegisterEconomy(economy);
        }

        public void RegisterEconomy(IEconomyService economy)
        {
            if (economy == null)
            {
                return;
            }

            Economy = economy;

            if (economy is IEconomySaveSource economySaveSource)
            {
                Save?.RegisterEconomySaveSource(economySaveSource);
            }

            EconomyRegistered?.Invoke(economy);
        }

        public void RegisterGameCalendar(IGameCalendarService gameCalendar)
        {
            if (gameCalendar == null)
            {
                return;
            }

            GameCalendar = gameCalendar;

            if (gameCalendar is IGameCalendarSaveSource gameCalendarSaveSource)
            {
                Save?.RegisterGameCalendarSaveSource(gameCalendarSaveSource);
            }

            GameCalendarRegistered?.Invoke(gameCalendar);
        }

        public void RegisterWeeklySettlementSaveSource(
            IWeeklySettlementSaveSource weeklySettlementSaveSource)
        {
            if (weeklySettlementSaveSource == null)
            {
                return;
            }

            Save?.RegisterWeeklySettlementSaveSource(weeklySettlementSaveSource);
        }

        public void RegisterWeeklyEconomy(IWeeklyEconomyService weeklyEconomy)
        {
            if (weeklyEconomy == null)
            {
                return;
            }

            WeeklyEconomy = weeklyEconomy;

            if (weeklyEconomy is IWeeklySettlementSaveSource saveSource)
            {
                Save?.RegisterWeeklySettlementSaveSource(saveSource);
            }

            WeeklyEconomyRegistered?.Invoke(weeklyEconomy);
        }

        public void RegisterResearchSaveSource(IResearchSaveSource researchSaveSource)
        {
            if (researchSaveSource == null)
            {
                return;
            }

            Save?.RegisterResearchSaveSource(researchSaveSource);
        }

        public bool RegisterResearch(IResearchUnlockService research)
        {
            if (research == null)
            {
                return false;
            }

            if (Research != null)
            {
                return ReferenceEquals(Research, research);
            }

            Research = research;

            if (research is IResearchSaveSource saveSource)
            {
                Save?.RegisterResearchSaveSource(saveSource);
            }

            ResearchRegistered?.Invoke(research);
            return true;
        }

        public void RegisterProgressionSaveSource(IProgressionSaveSource progressionSaveSource)
        {
            if (progressionSaveSource == null)
            {
                return;
            }

            Save?.RegisterProgressionSaveSource(progressionSaveSource);
        }

        public void RegisterRadioSaveSource(IRadioSaveSource radioSaveSource)
        {
            if (radioSaveSource == null)
            {
                return;
            }

            Save?.RegisterRadioSaveSource(radioSaveSource);
        }

        public void RegisterTerrainDecorationSaveSource(
            ITerrainDecorationSaveSource terrainDecorationSaveSource)
        {
            if (terrainDecorationSaveSource == null)
            {
                return;
            }

            Save?.RegisterTerrainDecorationSaveSource(
                terrainDecorationSaveSource);
        }

        public bool RegisterWorldGrid(IWorldGridService worldGrid)
        {
            if (worldGrid == null)
            {
                return false;
            }

            if (WorldGrid != null)
            {
                return ReferenceEquals(WorldGrid, worldGrid);
            }

            WorldGrid = worldGrid;

            if (worldGrid is IWorldGridSaveSource saveSource)
            {
                Save?.RegisterWorldGridSaveSource(saveSource);
            }

            WorldGridRegistered?.Invoke(worldGrid);
            return true;
        }

        public bool RegisterWorldGridExpansion(
            IWorldGridExpansionService worldGridExpansion)
        {
            if (worldGridExpansion == null)
            {
                return false;
            }

            if (WorldGridExpansion != null)
            {
                return ReferenceEquals(
                    WorldGridExpansion,
                    worldGridExpansion);
            }

            WorldGridExpansion = worldGridExpansion;
            WorldGridExpansionRegistered?.Invoke(worldGridExpansion);
            return true;
        }

        public bool RegisterWorldCoordinates(
            IWorldCoordinateSpace worldCoordinates)
        {
            if (worldCoordinates == null)
            {
                return false;
            }

            if (WorldCoordinates != null)
            {
                return ReferenceEquals(WorldCoordinates, worldCoordinates);
            }

            WorldCoordinates = worldCoordinates;
            WorldCoordinatesRegistered?.Invoke(worldCoordinates);
            return true;
        }

        public bool RegisterSpecialBuildings(
            ISpecialBuildingService specialBuildings)
        {
            if (specialBuildings == null)
            {
                return false;
            }

            if (SpecialBuildings != null)
            {
                return ReferenceEquals(SpecialBuildings, specialBuildings);
            }

            SpecialBuildings = specialBuildings;

            if (specialBuildings is ISpecialBuildingSaveSource saveSource)
            {
                Save?.RegisterSpecialBuildingSaveSource(saveSource);
            }

            SpecialBuildingsRegistered?.Invoke(specialBuildings);
            return true;
        }

        public bool RegisterPopulation(IReadOnlyPopulationData population)
        {
            if (population == null)
            {
                return false;
            }

            if (Population != null)
            {
                return ReferenceEquals(Population, population);
            }

            Population = population;
            PopulationRegistered?.Invoke(population);
            return true;
        }

        public bool RegisterSpecialBuildingVisits(
            ISpecialBuildingVisitService specialBuildingVisits)
        {
            if (specialBuildingVisits == null)
            {
                return false;
            }

            if (SpecialBuildingVisits != null)
            {
                return ReferenceEquals(
                    SpecialBuildingVisits,
                    specialBuildingVisits);
            }

            SpecialBuildingVisits = specialBuildingVisits;
            if (specialBuildingVisits is ISpecialBuildingVisitSaveSource
                saveSource)
            {
                Save?.RegisterSpecialBuildingVisitSaveSource(saveSource);
            }

            SpecialBuildingVisitsRegistered?.Invoke(specialBuildingVisits);
            return true;
        }

        public bool RegisterWorldCoordinateRoot(
            IWorldCoordinateRoot worldCoordinateRoot)
        {
            if (worldCoordinateRoot == null)
            {
                return false;
            }

            if (WorldCoordinateRoot != null)
            {
                return ReferenceEquals(
                    WorldCoordinateRoot,
                    worldCoordinateRoot);
            }

            WorldCoordinateRoot = worldCoordinateRoot;
            WorldCoordinateRootRegistered?.Invoke(worldCoordinateRoot);
            return true;
        }
    }
}
