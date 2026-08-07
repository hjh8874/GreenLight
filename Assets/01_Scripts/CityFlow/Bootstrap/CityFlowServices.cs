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
        public ITerrainDecorationSaveSource TerrainDecorations
        {
            get;
            private set;
        }

        public IResearchUnlockService Research { get; private set; }
        public ISpecialBuildingService SpecialBuildings { get; private set; }
        public IReadOnlyPopulationData Population { get; private set; }
        public ISpecialBuildingVisitService SpecialBuildingVisits
        {
            get;
            private set;
        }
        public IVehicleTripService VehicleTrips { get; private set; }
        public IRoadTrafficService RoadTraffic { get; private set; }
        public IRoadRoutePlanningService RoadRoutePlanning { get; private set; }
        public IBusLineService BusLines { get; private set; }
        public IEmergencyIncidentSaveSource EmergencyIncidents
        {
            get;
            private set;
        }
        public IPoliceDispatchService PoliceDispatch
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
        public event Action<ITerrainDecorationSaveSource>
            TerrainDecorationsRegistered;
        public event Action<IResearchUnlockService> ResearchRegistered;
        public event Action<ISpecialBuildingService> SpecialBuildingsRegistered;
        public event Action<IReadOnlyPopulationData> PopulationRegistered;
        public event Action<ISpecialBuildingVisitService>
            SpecialBuildingVisitsRegistered;
        public event Action<IVehicleTripService> VehicleTripsRegistered;
        public event Action<IRoadTrafficService> RoadTrafficRegistered;
        public event Action<IRoadRoutePlanningService>
            RoadRoutePlanningRegistered;
        public event Action<IBusLineService> BusLinesRegistered;
        public event Action<IPoliceDispatchService>
            PoliceDispatchRegistered;

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

        public void RegisterSchoolBusSaveSource(
            ISchoolBusSaveSource schoolBusSaveSource)
        {
            if (schoolBusSaveSource == null)
            {
                return;
            }

            Save?.RegisterSchoolBusSaveSource(schoolBusSaveSource);
        }

        public void RegisterCitizenFeedSaveSource(
            ICitizenFeedSaveSource citizenFeedSaveSource)
        {
            if (citizenFeedSaveSource == null)
            {
                return;
            }

            Save?.RegisterCitizenFeedSaveSource(citizenFeedSaveSource);
        }

        public void RegisterCameraViewSaveSource(
            ICameraViewSaveSource cameraViewSaveSource)
        {
            if (cameraViewSaveSource == null)
            {
                return;
            }

            Save?.RegisterCameraViewSaveSource(cameraViewSaveSource);
        }

        public void RegisterRadioSaveSource(IRadioSaveSource radioSaveSource)
        {
            if (radioSaveSource == null)
            {
                return;
            }

            Save?.RegisterRadioSaveSource(radioSaveSource);
        }

        public bool RegisterEmergencyIncidentSaveSource(
            IEmergencyIncidentSaveSource emergencyIncidentSaveSource)
        {
            if (emergencyIncidentSaveSource == null)
            {
                return false;
            }

            if (EmergencyIncidents != null)
            {
                return ReferenceEquals(
                    EmergencyIncidents,
                    emergencyIncidentSaveSource);
            }

            EmergencyIncidents = emergencyIncidentSaveSource;
            Save?.RegisterEmergencyIncidentSaveSource(
                emergencyIncidentSaveSource);
            return true;
        }

        public bool RegisterPoliceDispatch(
            IPoliceDispatchService policeDispatch)
        {
            if (policeDispatch == null)
            {
                return false;
            }

            if (PoliceDispatch != null)
            {
                return ReferenceEquals(
                    PoliceDispatch,
                    policeDispatch);
            }

            PoliceDispatch = policeDispatch;
            if (policeDispatch is IPoliceDispatchSaveSource saveSource)
            {
                Save?.RegisterPoliceDispatchSaveSource(saveSource);
            }

            PoliceDispatchRegistered?.Invoke(policeDispatch);
            return true;
        }

        public bool RegisterTerrainDecorationSaveSource(
            ITerrainDecorationSaveSource terrainDecorationSaveSource)
        {
            if (terrainDecorationSaveSource == null)
            {
                return false;
            }

            if (TerrainDecorations != null)
            {
                return ReferenceEquals(
                    TerrainDecorations,
                    terrainDecorationSaveSource);
            }

            TerrainDecorations = terrainDecorationSaveSource;
            Save?.RegisterTerrainDecorationSaveSource(
                terrainDecorationSaveSource);
            TerrainDecorationsRegistered?.Invoke(
                terrainDecorationSaveSource);
            return true;
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

        public bool RegisterVehicleTrips(IVehicleTripService vehicleTrips)
        {
            if (vehicleTrips == null)
            {
                return false;
            }

            if (VehicleTrips != null)
            {
                return ReferenceEquals(VehicleTrips, vehicleTrips);
            }

            VehicleTrips = vehicleTrips;
            VehicleTripsRegistered?.Invoke(vehicleTrips);
            return true;
        }

        public bool RegisterRoadTraffic(IRoadTrafficService roadTraffic)
        {
            if (roadTraffic == null)
            {
                return false;
            }

            if (RoadTraffic != null)
            {
                return ReferenceEquals(RoadTraffic, roadTraffic);
            }

            RoadTraffic = roadTraffic;
            RoadTrafficRegistered?.Invoke(roadTraffic);
            return true;
        }

        public bool RegisterRoadRoutePlanning(
            IRoadRoutePlanningService roadRoutePlanning)
        {
            if (roadRoutePlanning == null)
            {
                return false;
            }

            if (RoadRoutePlanning != null)
            {
                return ReferenceEquals(
                    RoadRoutePlanning,
                    roadRoutePlanning);
            }

            RoadRoutePlanning = roadRoutePlanning;
            RoadRoutePlanningRegistered?.Invoke(roadRoutePlanning);
            return true;
        }

        public bool RegisterBusLines(IBusLineService busLines)
        {
            if (busLines == null)
            {
                return false;
            }

            if (BusLines != null)
            {
                return ReferenceEquals(BusLines, busLines);
            }

            BusLines = busLines;
            BusLinesRegistered?.Invoke(busLines);
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
