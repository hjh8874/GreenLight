using System;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class CameraViewSaveData
    {
        public bool HasZoom;
        public float NormalizedZoom01;
    }

    public interface ICameraViewSaveSource
    {
        CameraViewSaveData CreateSnapshot();

        void RestoreSnapshot(CameraViewSaveData snapshot);
    }

    [Serializable]
    public sealed class GameSaveData
    {
        public int SaveVersion;
        public long SavedAtUtcTicks;
        public int GridWidth;
        public int GridHeight;
        public SimSaveData Simulation;
        public EconomySaveData Economy;
        public WeeklySettlementSaveData WeeklySettlement;
        public ResearchSaveData Research;
        public ProgressionSaveData Progression;
        public GameCalendarSaveData Calendar;
        public SchoolBusSaveData SchoolBus;
        public RadioSaveData Radio;
        public TerrainDecorationSaveData TerrainDecorations;
        public WorldGridSaveData WorldGrid;
        public SpecialBuildingSaveData SpecialBuildings;
        public SpecialBuildingVisitSaveData SpecialBuildingVisits;
        public EmergencyIncidentSaveData EmergencyIncidents;
        public PoliceDispatchSaveData PoliceDispatch;
        public CameraViewSaveData CameraView;
        // 옛 세이브엔 이 필드가 없어 null로 온다. 복원 쪽이 ?? new로 받아
        // 빈 장부로 시작하므로 SaveVersion을 올리지 않는다(기존 섹션과 동일한 관용).
        public CitizenFeedSaveData CitizenFeed;
    }
}
