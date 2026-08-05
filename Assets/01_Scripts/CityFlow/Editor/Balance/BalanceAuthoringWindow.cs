#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using CityFlow.Configs;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.EditorTools.Save;
using CityFlow.Gameplay.Progression;
using CityFlow.Gameplay.Research;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityFlow.EditorTools.Balance
{
    public sealed class BalanceAuthoringWindow : EditorWindow
    {
        internal enum ResearchBalanceSection
        {
            BuildingUnlock,
            Expansion
        }

        internal const string SourceScenePath =
            "Assets/00_Scenes/CityFlowIntegrated_cmt.unity";
        internal const string BalanceScenePath =
            "Assets/00_Scenes/Debug/CityFlowBalance_Lee.unity";
        internal const string ResearchCatalogPath =
            "Assets/05_ScriptableObjects/Resources/CityFlow/ResearchCatalog.asset";
        internal const string WorkingResearchCatalogPath =
            "Assets/05_ScriptableObjects/Balance/Editor/ResearchCatalog_Balance.asset";
        internal const string WorkingBuildingCatalogPath =
            "Assets/05_ScriptableObjects/Balance/Editor/SpecialBuildingCatalog_Balance.asset";
        private const string BuildingNameEntryLabel = "건설 UI 정보";
        private const string WorkingRoot =
            "Assets/05_ScriptableObjects/Balance/Editor";

        private sealed class BalanceEntry
        {
            public readonly string Group;
            public readonly string Label;
            public readonly string SourcePath;
            public readonly string WorkingPath;
            public readonly string[] VisiblePropertyPaths;
            public readonly string[] PublishPropertyPaths;
            public readonly bool ShowInNavigation;
            public readonly bool PublishToSource;
            public readonly string LinkedResearchId;

            public BalanceEntry(
                string group,
                string label,
                string sourcePath,
                string workingName,
                string[] visiblePropertyPaths = null,
                bool showInNavigation = true,
                bool publishToSource = true,
                string linkedResearchId = null,
                string[] publishPropertyPaths = null)
            {
                Group = group;
                Label = label;
                SourcePath = sourcePath;
                WorkingPath = $"{WorkingRoot}/{workingName}.asset";
                VisiblePropertyPaths = visiblePropertyPaths ??
                                       Array.Empty<string>();
                PublishPropertyPaths = publishPropertyPaths ??
                                       VisiblePropertyPaths;
                ShowInNavigation = showInNavigation;
                PublishToSource = publishToSource;
                LinkedResearchId = linkedResearchId?.Trim() ?? string.Empty;
            }
        }

        private static readonly string[] GeneralVehiclePropertyPaths =
        {
            "Value.CarsPerHouse",
            "Value.MaxSimCars",
            "Value.MaxPendingVehicleTrips",
            "Value.MaxConcurrentSpecialTrips",
            "Value.LeisureTripRatio",
            "Value.TruckCommuterRatio",
            "Value.MorningStartHour",
            "Value.MorningEndHour",
            "Value.EveningStartHour",
            "Value.EveningEndHour",
            "Value.DemandChoicePool",
            "Value.RushAmplitude",
            "Value.CoinPerTrip"
        };

        private static readonly string[] ResidentialPropertyPaths =
        {
            "Value.CarsPerHouse",
            "Value.ConstructionHoursHouse"
        };

        private static readonly string[] CompanyPropertyPaths =
        {
            "Value.OfficeCapacity",
            "Value.CompanyHiringSlotsPerGameHour",
            "Value.ConstructionHoursOffice"
        };

        private static readonly string[] SimConfigPublishPropertyPaths =
            GeneralVehiclePropertyPaths
                .Concat(ResidentialPropertyPaths)
                .Concat(CompanyPropertyPaths)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        private static readonly string[] EmergencyIncidentPropertyPaths =
        {
            "minimumSpawnInterval",
            "maximumSpawnInterval",
            "minimumDispatchIntervalDays",
            "maximumDispatchIntervalDays",
            "maximumActiveIncidents",
            "maximumAutomaticIncidentsPerDay",
            "houseWeight",
            "officeWeight",
            "schoolWeight",
            "specialBuildingWeight",
            "recentTargetHistorySize",
            "travelSecondsPerTile",
            "treatmentSeconds",
            "ambulancesPerHospital",
            "routeRetrySeconds",
            "maximumOutboundRouteRetries",
            "maximumReturnRouteRetries",
            "visualScale",
            "visualDepth",
            "vehicleLengthTiles",
            "vehicleWidthTiles"
        };

        private static readonly string[] TileBuildingPropertyPaths =
        {
            "buildingName",
            "buildingDescription",
            "buildCost",
            "dailyCoinValue",
            "prosperityValue"
        };

        private static readonly string[] SpecialBuildingPropertyPaths =
        {
            "buildingName",
            "description",
            "canGenerateTraffic",
            "canReceiveTraffic",
            "trafficGenerationAmount",
            "destinationRewardMultiplier",
            "buildCost",
            "dailyCoinValue",
            "prosperityValue",
            "footprint",
            "canReceiveVisitors",
            "visitCadence",
            "visitorCapacity",
            "attractionWeight",
            "coinPerVisit",
            "visitTimeProfile",
            "schoolCoverageCapacity",
            "coveredPopulationCapBonus",
            "hospitalCoverageRadius",
            "hospitalPatientCapacity"
        };

        private static readonly BalanceEntry[] Entries =
        {
            new(
                "핵심",
                "시뮬레이션",
                "Assets/05_ScriptableObjects/SimConfig_Integrated.asset",
                "SimConfig_Integrated_Balance",
                SimConfigPublishPropertyPaths,
                publishPropertyPaths: SimConfigPublishPropertyPaths),
            new(
                "핵심",
                "경제",
                "Assets/05_ScriptableObjects/EconomyConfig.asset",
                "EconomyConfig_Balance"),
            new(
                "핵심",
                "거리 보상",
                "Assets/05_ScriptableObjects/DistanceRewardConfig.asset",
                "DistanceRewardConfig_Balance"),
            new(
                "핵심",
                "인구",
                "Assets/05_ScriptableObjects/CityFlow/PopulationConfig.asset",
                "PopulationConfig_Balance"),
            new(
                "시간",
                "게임 시간",
                "Assets/05_ScriptableObjects/Resources/CityFlow/GameTimeSettings.asset",
                "GameTimeSettings_Balance"),
            new(
                "연구",
                "건물 해금 연구",
                ResearchCatalogPath,
                "ResearchCatalog_Balance"),
            new(
                "교통",
                "일반 차량",
                "Assets/05_ScriptableObjects/SimConfig_Integrated.asset",
                "SimConfig_Integrated_Balance",
                GeneralVehiclePropertyPaths),
            new(
                "교통",
                "시내버스",
                "Assets/05_ScriptableObjects/CityFlow/Transit/CityBusDefinition.asset",
                "CityBusDefinition_Balance"),
            new(
                "교통",
                "스쿨버스",
                "Assets/05_ScriptableObjects/CityFlow/Transit/SchoolBusDefinition.asset",
                "SchoolBusDefinition_Balance"),
            new(
                "교통",
                "시내버스 운행 시간",
                "Assets/05_ScriptableObjects/CityFlow/Transit/DefaultCityBusSchedule.asset",
                "DefaultCityBusSchedule_Balance"),
            new(
                "교통",
                "스쿨버스 운행 시간",
                "Assets/05_ScriptableObjects/CityFlow/Transit/KoreanSchoolBusSchedule.asset",
                "KoreanSchoolBusSchedule_Balance"),
            new(
                "건물",
                "주거 지역",
                "Assets/05_ScriptableObjects/SimConfig_Integrated.asset",
                "SimConfig_Integrated_Balance",
                ResidentialPropertyPaths),
            new(
                "건물",
                "회사",
                "Assets/05_ScriptableObjects/SimConfig_Integrated.asset",
                "SimConfig_Integrated_Balance",
                CompanyPropertyPaths),
            new(
                "건물",
                BuildingNameEntryLabel,
                "Assets/05_ScriptableObjects/Buildings/SpecialBuildingCatalog.asset",
                "SpecialBuildingCatalog_Balance",
                showInNavigation: true,
                publishToSource: false),
            new(
                "내부",
                "특수 건물 카탈로그",
                "Assets/05_ScriptableObjects/Buildings/SpecialBuildingCatalog.asset",
                "SpecialBuildingCatalog_Balance",
                showInNavigation: false,
                publishToSource: false),
            new(
                "내부",
                "주거 지역 실제 건물",
                "Assets/05_ScriptableObjects/CityFlow/TileData/HouseData.asset",
                "HouseData_Balance",
                TileBuildingPropertyPaths,
                showInNavigation: false),
            new(
                "내부",
                "회사 실제 건물",
                "Assets/05_ScriptableObjects/CityFlow/TileData/OfficeData.asset",
                "OfficeData_Balance",
                TileBuildingPropertyPaths,
                showInNavigation: false),
            new(
                "내부",
                "학교 실제 건물",
                "Assets/05_ScriptableObjects/CityFlow/TileData/SchoolData.asset",
                "SchoolData_Balance",
                TileBuildingPropertyPaths,
                showInNavigation: false,
                linkedResearchId: "research_building_school"),
            new(
                "내부",
                "병원 실제 건물",
                "Assets/05_ScriptableObjects/CityFlow/TileData/HospitalTileData.asset",
                "HospitalTileData_Balance",
                TileBuildingPropertyPaths,
                showInNavigation: false,
                linkedResearchId: "research_building_hospital"),
            new(
                "내부", "커피숍 실제 건물",
                "Assets/05_ScriptableObjects/Buildings/Building_CoffeeShop.asset",
                "Building_CoffeeShop_Balance",
                SpecialBuildingPropertyPaths,
                showInNavigation: false,
                linkedResearchId: "research_building_coffee_shop"),
            new(
                "내부", "헬스장 실제 건물",
                "Assets/05_ScriptableObjects/Buildings/Building_StoreCorner_Video.asset",
                "Building_StoreCorner_Video_Balance",
                SpecialBuildingPropertyPaths,
                showInNavigation: false,
                linkedResearchId: "research_building_video_store"),
            new(
                "내부", "약국 실제 건물",
                "Assets/05_ScriptableObjects/Buildings/Building_StoreCorner_Drug.asset",
                "Building_StoreCorner_Drug_Balance",
                SpecialBuildingPropertyPaths,
                showInNavigation: false,
                linkedResearchId: "research_building_pharmacy"),
            new(
                "내부", "주유소 실제 건물",
                "Assets/05_ScriptableObjects/Buildings/Building_PetrolStation.asset",
                "Building_PetrolStation_Balance",
                SpecialBuildingPropertyPaths,
                showInNavigation: false,
                linkedResearchId: "research_building_petrol_station"),
            new(
                "내부", "정비소 실제 건물",
                "Assets/05_ScriptableObjects/Buildings/Building_AutoRepair.asset",
                "Building_AutoRepair_Balance",
                SpecialBuildingPropertyPaths,
                showInNavigation: false,
                linkedResearchId: "research_building_auto_repair"),
            new(
                "내부", "영화관 실제 건물",
                "Assets/05_ScriptableObjects/Buildings/Building_Cinema.asset",
                "Building_Cinema_Balance",
                SpecialBuildingPropertyPaths,
                showInNavigation: false,
                linkedResearchId: "research_building_cinema"),
            new(
                "내부", "경찰서 실제 건물",
                "Assets/05_ScriptableObjects/Buildings/Building_PoliceStation.asset",
                "Building_PoliceStation_Balance",
                SpecialBuildingPropertyPaths,
                showInNavigation: false,
                linkedResearchId: "research_building_police_station"),
            new(
                "내부", "쇼핑몰 실제 건물",
                "Assets/05_ScriptableObjects/Buildings/Building_Mall.asset",
                "Building_Mall_Balance",
                SpecialBuildingPropertyPaths,
                showInNavigation: false,
                linkedResearchId: "research_building_mall"),
            new(
                "응급",
                "응급 신고와 구급차",
                "Assets/05_ScriptableObjects/CityFlow/Emergency/EmergencyIncidentConfig.asset",
                "EmergencyIncidentConfig_Balance",
                EmergencyIncidentPropertyPaths,
                publishPropertyPaths: EmergencyIncidentPropertyPaths),
            new(
                "인프라",
                "신호등",
                "Assets/05_ScriptableObjects/CityFlow/InfrastructureData/SignalData.asset",
                "SignalData_Balance"),
            new(
                "인프라",
                "회전교차로",
                "Assets/05_ScriptableObjects/CityFlow/InfrastructureData/RoundaboutData.asset",
                "RoundaboutData_Balance"),
            new(
                "인프라",
                "고가도로",
                "Assets/05_ScriptableObjects/CityFlow/InfrastructureData/OverpassData.asset",
                "OverpassData_Balance"),
            new(
                "인프라",
                "일방통행",
                "Assets/05_ScriptableObjects/CityFlow/InfrastructureData/OnewayData.asset",
                "OnewayData_Balance"),
            new(
                "인프라",
                "회전 제한",
                "Assets/05_ScriptableObjects/CityFlow/InfrastructureData/TurnRestrictionData.asset",
                "TurnRestrictionData_Balance"),
            new(
                "인프라",
                "우선 도로",
                "Assets/05_ScriptableObjects/CityFlow/InfrastructureData/PriorityRoadData.asset",
                "PriorityRoadData_Balance"),
            new(
                "인프라",
                "고속도로",
                "Assets/05_ScriptableObjects/CityFlow/InfrastructureData/HighwayData.asset",
                "HighwayData_Balance")
        };

        private static IEnumerable<BalanceEntry> UniqueAssetEntries =>
            Entries
                .GroupBy(
                    entry => entry.WorkingPath,
                    StringComparer.Ordinal)
                .Select(group => group.First());

        // 저장 필드명은 세이브·에셋 호환성을 위해 그대로 두고,
        // 밸런스 편집기에서 보이는 이름만 한국어로 바꾼다.
        private static readonly IReadOnlyDictionary<string, string>
            PropertyLabels = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["x"] = "가로축",
                ["y"] = "세로축",
                ["z"] = "높이축",
                ["w"] = "네 번째 축",
                ["Value"] = "시뮬레이션 설정값",
                ["standardVehicleFootprint"] = "일반 차량 차체 규격",
                ["TickInterval"] = "시뮬레이션 틱 간격(초)",
                ["MaxStepsPerFrame"] = "프레임당 최대 처리 단계",
                ["GridWidth"] = "도시 격자 너비",
                ["GridHeight"] = "도시 격자 높이",
                ["RoadCapacity"] = "도로 처리 용량",
                ["QueueCapacityPerTile"] = "타일당 차량 대기열 용량",
                ["QueueServicePerTick"] = "틱당 대기열 처리 차량 수",
                ["GridlockValveTicks"] = "교착 해제 대기 틱",
                ["VehicleRerouteBlockedTicks"] = "차량 경로 재탐색 대기 틱",
                ["VehicleRestartBlockedTicks"] = "차량 재출발 대기 틱",
                ["UnsignaledIntersectionRoundCap"] = "무신호 교차로 회차당 통과 차량 수",
                ["CoinPerTrip"] = "운행 1회 보상",
                ["CarsPerHouse"] = "주거지당 차량 수",
                ["BusCoverageRadius"] = "버스 정류장 유효 반경",
                ["MorningStartHour"] = "출근 시작 시간",
                ["MorningEndHour"] = "출근 종료 시간",
                ["EveningStartHour"] = "퇴근 시작 시간",
                ["EveningEndHour"] = "퇴근 종료 시간",
                ["SchoolMorningStartHour"] = "등교 시작 시간",
                ["SchoolMorningEndHour"] = "등교 종료 시간",
                ["SchoolReturnStartHour"] = "하교 시작 시간",
                ["SchoolReturnEndHour"] = "하교 종료 시간",
                ["MaxSimCars"] = "최대 시뮬레이션 차량 수",
                ["MaxPendingVehicleTrips"] = "최대 대기 운행 수",
                ["MaxConcurrentSpecialTrips"] = "최대 동시 특수 운행 수",
                ["LeisureTripRatio"] = "여가 외출 비율",
                ["TruckCommuterRatio"] = "통근 차량 중 트럭 비율",
                ["QueueSlowRatio"] = "대기열 서행 판정 비율",
                ["QueueJamRatio"] = "대기열 정체 판정 비율",
                ["SlowRatio"] = "서행 판정 비율",
                ["JamRatio"] = "정체 판정 비율",
                ["EfficiencyMin"] = "최저 도로 효율",
                ["EfficiencyMinRatio"] = "최저 효율 도달 비율",
                ["GreenWaveFloor"] = "그린웨이브 최저 효율",
                ["GreenWaveScanInterval"] = "그린웨이브 검사 주기",
                ["GreenWaveThreshold"] = "그린웨이브 달성 기준",
                ["GreenWaveMagnitudeOffset"] = "그린웨이브 강도 기준값",
                ["GreenWaveMagnitudeScale"] = "그린웨이브 강도 배율",
                ["OfficeCapacity"] = "회사 수용 인원",
                ["CompanyHiringSlotsPerGameHour"] = "게임 시간당 회사 채용 수",
                ["ConstructionHoursHouse"] = "주거지 건설 시간",
                ["ConstructionHoursOffice"] = "회사 건설 시간",
                ["ConstructionHoursSchool"] = "학교 건설 시간",
                ["ConstructionHoursHospital"] = "병원 건설 시간",
                ["ConstructionHoursSpecial"] = "특수 건물 건설 시간",
                ["SchoolCapacity"] = "학교 수용 인원",
                ["DemandChoicePool"] = "목적지 선택 후보 수",
                ["RushAmplitude"] = "출퇴근 혼잡 증가폭",
                ["DayLengthSeconds"] = "게임 하루 길이(초)",
                ["OverrideDurationSeconds"] = "신호 강제 제어 지속 시간",
                ["OverrideCooldownSeconds"] = "신호 강제 제어 재사용 대기 시간",
                ["OverrideCorridorSignals"] = "강제 제어 최대 신호 수",
                ["UnsignaledInterference"] = "무신호 교차로 간섭 계수",
                ["RoundaboutInterference"] = "회전교차로 간섭 계수",
                ["RoundaboutCapacityFactor"] = "회전교차로 용량 배율",
                ["PriorityMainInterference"] = "우선도로 주축 간섭 계수",
                ["PriorityYieldInterference"] = "우선도로 양보축 간섭 계수",
                ["RoutingCongestionWeight"] = "경로 탐색 혼잡 가중치",
                ["AutoDetectSignals"] = "교차로 신호 자동 생성",
                ["CoinBase"] = "도착 기본 보상",
                ["MaxRoadTiles"] = "최대 도로 타일 수",
                ["RoadExpandBaseCost"] = "도로 확장 기본 비용",
                ["RoadExpandCostGrowth"] = "도로 확장 비용 증가량",
                ["StabilityJamWeight"] = "정체 안정도 감점 가중치",
                ["BurstJamEnterRatio"] = "버스트 정체 진입 기준",
                ["BurstFreeReturnRatio"] = "버스트 정상 복귀 기준",
                ["BurstCooldownSeconds"] = "버스트 재사용 대기 시간",
                ["BurstRewardThreshold"] = "버스트 보상 발생 기준",

                ["coinBase"] = "도착 기본 보상",
                ["defaultDestinationRewardPercent"] = "목적지 기본 보상 비율(%)",
                ["settlementDays"] = "정산 주기(게임 일)",
                ["offlineMaximumRealHours"] = "오프라인 보상 최대 현실 시간",
                ["offlineIncomePercent"] = "오프라인 수익 비율(%)",
                ["weeklyCoinPerBuilding"] = "건물당 주간 수익",
                ["cityUnlockCosts"] = "도시 단계별 해금 비용",
                ["landCosts"] = "토지 매입 단계별 비용",
                ["upgradeCosts"] = "업그레이드 단계별 비용",
                ["initialRefundPercent"] = "최초 철거 환급률(%)",
                ["refundDecreasePercent"] = "환급 감소량(%)",
                ["minimumRefundPercent"] = "최저 환급률(%)",
                ["refundDecreaseIntervalDays"] = "환급 감소 주기(게임 일)",
                ["realMinutesPerGameDay"] = "게임 하루당 현실 시간(분)",
                ["flowBurstRewardPercent"] = "플로우 버스트 보상 비율(%)",
                ["flowBurstDurationSeconds"] = "플로우 버스트 지속 시간(초)",
                ["rewardTiers"] = "거리별 보상 단계",
                ["minimumDistanceTiles"] = "최소 이동 거리(타일)",
                ["rewardMultiplier"] = "보상 배율",
                ["populationEntries"] = "건물별 인구 설정",
                ["tileType"] = "건물 종류",
                ["populationValue"] = "인구 증가량",
                ["schoolCoverageRadius"] = "학교 영향 반경",
                ["schoolCoveragePopulationBonus"] = "학교 영향권 인구 보너스",

                ["busId"] = "버스 ID",
                ["displayName"] = "표시 이름",
                ["buildingName"] = "실제 건물 이름",
                ["buildingDescription"] = "건설 메뉴 설명",
                ["description"] = "건물 설명",
                ["buildCost"] = "건설 비용",
                ["dailyCoinValue"] = "일일 기본 코인",
                ["prosperityValue"] = "번성도 증가량",
                ["canGenerateTraffic"] = "교통 수요 생성",
                ["canReceiveTraffic"] = "차량 목적지 허용",
                ["trafficGenerationAmount"] = "기본 이동 수요",
                ["destinationRewardMultiplier"] = "도착 보상 배율",
                ["footprint"] = "건물 점유 크기",
                ["canReceiveVisitors"] = "방문객 허용",
                ["visitCadence"] = "방문 주기",
                ["visitsPerPeriod"] = "주기당 방문 횟수",
                ["periodDays"] = "방문 주기 일수",
                ["visitorCapacity"] = "방문객 수용 인원",
                ["attractionWeight"] = "방문 목적지 가중치",
                ["coinPerVisit"] = "방문 1회 수익",
                ["visitTimeProfile"] = "방문 가능 시간대",
                ["schoolCoverageCapacity"] = "학교 영향 수용량",
                ["coveredPopulationCapBonus"] = "영향권 인구 상한 보너스",
                ["hospitalCoverageRadius"] = "병원 영향 반경",
                ["hospitalPatientCapacity"] = "병원 환자 수용량",
                ["busType"] = "버스 종류",
                ["secondsPerTile"] = "타일당 이동 시간(초)",
                ["stopWaitSeconds"] = "정류장 대기 시간(초)",
                ["initialStops"] = "초기 정류장 좌표",
                ["passengerCapacity"] = "승객 정원",
                ["boardingDemandPerStop"] = "정류장당 탑승 수요",
                ["leavingDemandPerStop"] = "정류장당 하차 수요",
                ["vehicleFootprintProfile"] = "차체 점유 규격",
                ["vehicleLengthTiles"] = "차량 길이(타일)",
                ["vehicleWidthTiles"] = "차량 너비(타일)",
                ["stopRevenueCoins"] = "정류장 도착 수익",
                ["routeColor"] = "노선 표시 색상",
                ["vehicleVisualPrefab"] = "차량 외형 프리팹",
                ["serviceStartHour"] = "운행 시작 시간",
                ["serviceEndHour"] = "운행 종료 시간",
                ["morningStartHour"] = "등교 운행 시작 시간",
                ["morningEndHour"] = "등교 운행 종료 시간",
                ["afternoonStartHour"] = "하교 운행 시작 시간",
                ["afternoonEndHour"] = "하교 운행 종료 시간",
                ["operateOnWeekends"] = "주말 운행",

                ["minimumSpawnInterval"] = "최소 신고 발생 간격(초)",
                ["maximumSpawnInterval"] = "최대 신고 발생 간격(초)",
                ["minimumDispatchIntervalDays"] = "최소 자동 출동 간격(게임 일)",
                ["maximumDispatchIntervalDays"] = "최대 자동 출동 간격(게임 일)",
                ["maximumActiveIncidents"] = "최대 동시 응급 신고 수",
                ["maximumAutomaticIncidentsPerDay"] = "하루 최대 자동 신고 수",
                ["incidentDefinitions"] = "응급 신고 종류",
                ["houseWeight"] = "주거지 출동 가중치",
                ["officeWeight"] = "회사 출동 가중치",
                ["schoolWeight"] = "학교 출동 가중치",
                ["specialBuildingWeight"] = "특수 건물 출동 가중치",
                ["recentTargetHistorySize"] = "최근 출동지 중복 방지 개수",
                ["travelSecondsPerTile"] = "구급차 타일당 이동 시간(초)",
                ["treatmentSeconds"] = "현장 정차 시간(초)",
                ["ambulancesPerHospital"] = "병원당 구급차 수",
                ["routeRetrySeconds"] = "경로 재탐색 간격(초)",
                ["maximumOutboundRouteRetries"] = "출동 경로 최대 재시도 횟수",
                ["maximumReturnRouteRetries"] = "복귀 경로 최대 재시도 횟수",
                ["visualScale"] = "구급차 외형 크기",
                ["visualDepth"] = "구급차 화면 깊이",

                ["Kind"] = "인프라 종류",
                ["InfrastructureName"] = "인프라 이름",
                ["Icon"] = "아이콘",
                ["Description"] = "설명",
                ["Cost"] = "건설 비용",
                ["GreenSlots"] = "신호등 초록불 슬롯 길이",
                ["OnewayDir"] = "일방통행 방향",
                ["TurnMode"] = "허용 회전 방식",
                ["PriorityAxis"] = "우선도로 주축"
            };

        private static readonly IReadOnlyDictionary<string, string>
            EnumLabels = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["None"] = "없음",
                ["CityBus"] = "시내버스",
                ["SchoolBus"] = "스쿨버스",
                ["Empty"] = "빈 공간",
                ["Road"] = "도로",
                ["House"] = "주거 지역",
                ["Office"] = "회사",
                ["School"] = "학교",
                ["Hospital"] = "병원",
                ["SpecialBuilding"] = "특수 건물",
                ["UnderConstruction"] = "건설 중",
                ["Signal"] = "신호등",
                ["Roundabout"] = "회전교차로",
                ["Overpass"] = "고가도로",
                ["Oneway"] = "일방통행",
                ["OneWay"] = "일방통행",
                ["TurnRestriction"] = "회전 제한",
                ["PriorityRoad"] = "우선 도로",
                ["Highway"] = "고속도로",
                ["BusStop"] = "버스 정류장",
                ["LeftOnly"] = "좌회전만 허용",
                ["RightOnly"] = "우회전만 허용",
                ["StraightOnly"] = "직진만 허용",
                ["NoLeft"] = "좌회전 금지",
                ["NoRight"] = "우회전 금지",
                ["Horizontal"] = "가로축",
                ["Vertical"] = "세로축"
            };

        private readonly List<string> validationMessages = new();
        private Vector2 scroll;
        private string selectedGroup = "핵심";
        private int selectedEntryIndex;
        private int selectedResearchIndex;
        private ResearchBalanceSection selectedResearchSection;
        private bool showResearchAdvanced;
        private Dictionary<string, string> researchUnlockLabels;
        private UnityEditor.Editor cachedAssetEditor;
        private UnityEngine.Object cachedTarget;

        internal static bool IsResearchInSection(
            ResearchCategory category,
            ResearchBalanceSection section)
        {
            bool isExpansion = category == ResearchCategory.Expansion;
            return isExpansion ==
                   (section == ResearchBalanceSection.Expansion);
        }

        [MenuItem("CityFlow/Balance/밸런스 편집기 열기")]
        public static void OpenWindow()
        {
            BalanceAuthoringWindow window =
                GetWindow<BalanceAuthoringWindow>("게임 밸런스");
            window.minSize = new Vector2(760f, 520f);
            window.Show();
        }

        [MenuItem("CityFlow/Balance/작업 공간 생성 및 열기")]
        public static void CreateAndOpenWorkspace()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EnsureWorkingAssets();
            EnsureBalanceScene();

            Scene activeScene = SceneManager.GetActiveScene();
            Scene scene = IsSupportedBalanceScenePath(activeScene.path)
                ? activeScene
                : EditorSceneManager.OpenScene(
                    BalanceScenePath,
                    OpenSceneMode.Single);
            int changedReferences = RewireSceneToWorkingAssets(scene);

            if (changedReferences > 0)
            {
                EditorSceneManager.SaveScene(scene);
            }

            EditorSceneManager.playModeStartScene =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path);
            OpenWindow();
            Debug.Log(
                $"[Balance] 전용 Scene 준비 완료: {BalanceScenePath} " +
                $"(작업용 설정 연결 {changedReferences}개)");
        }

        private void OnDisable()
        {
            EditorApplication.delayCall -=
                RewireLoadedBalanceSceneToWorkingAssets;
            DestroyCachedEditor();
        }

        private void OnEnable()
        {
            EnsureWorkingAssets();
            EditorApplication.delayCall -=
                RewireLoadedBalanceSceneToWorkingAssets;
            EditorApplication.delayCall +=
                RewireLoadedBalanceSceneToWorkingAssets;
        }

        private static void RewireLoadedBalanceSceneToWorkingAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling ||
                EditorApplication.isUpdating)
            {
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!IsSupportedBalanceScenePath(scene.path))
            {
                return;
            }

            bool wasDirty = scene.isDirty;
            int changedReferences = RewireSceneToWorkingAssets(scene);
            if (changedReferences <= 0)
            {
                return;
            }

            // Do not silently save unrelated user edits. A clean debug scene can
            // be saved safely; a dirty one keeps the new references in memory and
            // remains visibly dirty for the user to review.
            if (!wasDirty)
            {
                EditorSceneManager.SaveScene(scene);
            }

            Debug.Log(
                $"[Balance] Connected {changedReferences} working balance " +
                $"references to the debug scene: {scene.path}");
        }

        internal static bool IsSupportedBalanceScenePath(string scenePath)
        {
            const string debugPrefix = "Assets/00_Scenes/Debug/";
            const string balanceScenePrefix = "CityFlowBalance_Lee";
            if (string.IsNullOrWhiteSpace(scenePath) ||
                !scenePath.StartsWith(debugPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string fileName = scenePath.Substring(debugPrefix.Length);
            return fileName.StartsWith(
                       balanceScenePrefix,
                       StringComparison.Ordinal) &&
                   fileName.EndsWith(".unity", StringComparison.Ordinal);
        }

        private void OnProjectChange()
        {
            researchUnlockLabels = null;
            Repaint();
        }

        private void OnGUI()
        {
            DrawHeader();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawNavigation();
                DrawSelectedAsset();
            }
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(
                "게임 밸런스 작업 공간",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "통합 Scene과 실제 설정 에셋은 직접 수정하지 않습니다. " +
                "작업용 복사본에서 먼저 플레이 테스트한 뒤, 확정 버튼으로만 실제 수치에 반영하세요.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("작업 공간 생성 / 열기", GUILayout.Height(28f)))
                {
                    CreateAndOpenWorkspace();
                    GUIUtility.ExitGUI();
                }

                if (GUILayout.Button("저장", GUILayout.Height(28f)))
                {
                    AssetDatabase.SaveAssets();
                }

                if (GUILayout.Button("검증", GUILayout.Height(28f)))
                {
                    RunValidation();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(1f, 0.75f, 0.35f);
                if (GUILayout.Button(
                        "테스트 완료 후 확정값을 실제 에셋에 반영",
                        GUILayout.Height(28f)))
                {
                    PublishWorkingValues();
                }

                GUI.backgroundColor = Color.white;
            }

            using (new EditorGUI.DisabledScope(
                       EditorApplication.isPlayingOrWillChangePlaymode ||
                       EditorApplication.isCompiling ||
                       EditorApplication.isUpdating))
            {
                GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);
                if (GUILayout.Button(
                        "현재 게임 진행 데이터만 초기화",
                        GUILayout.Height(24f)))
                {
                    GameProgressResetTool.ConfirmAndReset();
                    GUIUtility.ExitGUI();
                }

                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.LabelField(
                $"테스트 Scene: {BalanceScenePath}",
                EditorStyles.miniLabel);

            foreach (string message in validationMessages)
            {
                EditorGUILayout.HelpBox(message, MessageType.Warning);
            }

            EditorGUILayout.Space(4f);
        }

        private void DrawNavigation()
        {
            string[] groups = Entries
                .Where(entry => entry.ShowInNavigation)
                .Select(entry => entry.Group)
                .Distinct()
                .ToArray();

            using (new EditorGUILayout.VerticalScope(
                       EditorStyles.helpBox,
                       GUILayout.Width(190f)))
            {
                EditorGUILayout.LabelField("분류", EditorStyles.boldLabel);

                foreach (string group in groups)
                {
                    bool selected = group == selectedGroup;
                    if (GUILayout.Toggle(
                            selected,
                            group,
                            EditorStyles.miniButton) &&
                        !selected)
                    {
                        selectedGroup = group;
                        selectedEntryIndex = 0;
                        scroll = Vector2.zero;
                        DestroyCachedEditor();
                    }
                }

                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("설정", EditorStyles.boldLabel);

                BalanceEntry[] groupEntries = Entries
                    .Where(entry => entry.ShowInNavigation &&
                                    entry.Group == selectedGroup)
                    .ToArray();

                for (int i = 0; i < groupEntries.Length; i++)
                {
                    if (groupEntries[i].Label == "건물 해금 연구")
                    {
                        DrawResearchNavigationButton(
                            i,
                            ResearchBalanceSection.BuildingUnlock,
                            "건물 해금 연구");
                        DrawResearchNavigationButton(
                            i,
                            ResearchBalanceSection.Expansion,
                            "개척 시스템");
                        continue;
                    }

                    if (GUILayout.Toggle(
                            i == selectedEntryIndex,
                            groupEntries[i].Label,
                            EditorStyles.miniButtonLeft) &&
                        selectedEntryIndex != i)
                    {
                        selectedEntryIndex = i;
                        scroll = Vector2.zero;
                        DestroyCachedEditor();
                    }
                }
            }
        }

        private void DrawResearchNavigationButton(
            int entryIndex,
            ResearchBalanceSection section,
            string label)
        {
            bool selected = selectedEntryIndex == entryIndex &&
                            selectedResearchSection == section;
            if (!GUILayout.Toggle(
                    selected,
                    label,
                    EditorStyles.miniButtonLeft) ||
                selected)
            {
                return;
            }

            selectedEntryIndex = entryIndex;
            selectedResearchSection = section;
            selectedResearchIndex = -1;
            scroll = Vector2.zero;
            DestroyCachedEditor();
        }

        private void DrawSelectedAsset()
        {
            BalanceEntry[] groupEntries = Entries
                .Where(entry => entry.ShowInNavigation &&
                                entry.Group == selectedGroup)
                .ToArray();

            if (groupEntries.Length == 0)
            {
                return;
            }

            selectedEntryIndex = Mathf.Clamp(
                selectedEntryIndex,
                0,
                groupEntries.Length - 1);
            BalanceEntry entry = groupEntries[selectedEntryIndex];
            bool isResearchEntry = entry.Label == "건물 해금 연구";
            bool isBuildingNameEntry =
                entry.Label == BuildingNameEntryLabel;
            string selectedLabel = isResearchEntry &&
                                   selectedResearchSection ==
                                   ResearchBalanceSection.Expansion
                ? "개척 시스템"
                : entry.Label;
            UnityEngine.Object target =
                AssetDatabase.LoadMainAssetAtPath(entry.WorkingPath);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(selectedLabel, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    $"작업용: {entry.WorkingPath}",
                    EditorStyles.miniLabel);

                if (entry.Label == "경제")
                {
                    EditorGUILayout.HelpBox(
                        "경제 설정의 realMinutesPerGameDay는 현재 런타임에서 사용하지 않는 이전 필드입니다. " +
                        "실제 하루 길이는 '시간 > 게임 시간'에서 조정하세요.",
                        MessageType.Info);
                }
                else if (entry.Label == "시뮬레이션")
                {
                    EditorGUILayout.HelpBox(
                        "플레이 시작 시 DayLengthSeconds는 게임 시간 설정과 자동 동기화됩니다. " +
                        "하루 길이는 '시간 > 게임 시간'에서 조정하세요.",
                        MessageType.Info);
                }
                else if (entry.Label == "일반 차량")
                {
                    EditorGUILayout.HelpBox(
                        "일반 차량의 생성량, 동시 운행 한도, 출퇴근 시간, " +
                        "차량 구성과 운행 보상을 조정합니다. 같은 값은 주거 지역과 " +
                        "회사 설정에도 연결되어 즉시 일관되게 반영됩니다.",
                        MessageType.Info);
                }
                else if (entry.Label == "주거 지역")
                {
                    EditorGUILayout.HelpBox(
                        "주거 지역 한 곳에서 보유할 차량 수와 건설 완료까지 필요한 " +
                        "게임 시간을 조정합니다. 주거 인구는 '핵심 > 인구'에서 조정합니다.",
                        MessageType.Info);
                }
                else if (entry.Label == "회사")
                {
                    EditorGUILayout.HelpBox(
                        "회사 한 곳의 수용 인원, 게임 시간당 채용 인원과 건설 완료까지 " +
                        "필요한 게임 시간을 조정합니다.",
                        MessageType.Info);
                }
                else if (isResearchEntry &&
                         selectedResearchSection ==
                         ResearchBalanceSection.BuildingUnlock)
                {
                    EditorGUILayout.HelpBox(
                        "연구별 선행 연구, 해금 조건, 비용, 게임 내 연구 시간을 조정합니다. " +
                        "조건 목록이 비어 있으면 기존 단일 조건을 사용하고, 조건을 여러 개 넣으면 모두 만족해야 합니다. " +
                        "연구 ID는 건물 해금 연결에 사용되므로 변경할 때 주의하세요.",
                        MessageType.Info);
                }
                else if (isResearchEntry)
                {
                    EditorGUILayout.HelpBox(
                        "개척 단계별 조건, 비용, 게임 내 연구 시간과 확장 단계 연결을 조정합니다. " +
                        "완료한 연구의 확장 단계 ID가 월드 확장 설정과 연결됩니다.",
                        MessageType.Info);
                }

                if (target == null)
                {
                    EditorGUILayout.HelpBox(
                        "작업용 에셋이 없습니다. '작업 공간 생성 / 열기'를 눌러 주세요.",
                        MessageType.Warning);
                    return;
                }

                if (isResearchEntry)
                {
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    DrawResearchCatalog(target, selectedResearchSection);
                    EditorGUILayout.EndScrollView();
                    return;
                }

                if (isBuildingNameEntry)
                {
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    DrawBuildingNameEditor();
                    EditorGUILayout.EndScrollView();
                    return;
                }

                scroll = EditorGUILayout.BeginScrollView(scroll);
                if (entry.VisiblePropertyPaths.Length > 0)
                {
                    DrawFilteredAssetInspector(
                        target,
                        entry.VisiblePropertyPaths);
                    if (entry.Label == "주거 지역")
                    {
                        DrawResidentialPopulationSetting();
                        DrawSupplementalBuildingSettings(
                            "HouseData_Balance.asset",
                            "주거 건설 UI와 수익");
                    }
                    else if (entry.Label == "회사")
                    {
                        DrawSupplementalBuildingSettings(
                            "OfficeData_Balance.asset",
                            "회사 건설 UI와 수익");
                    }
                }
                else
                {
                    DrawLocalizedAssetInspector(target);
                }
                EditorGUILayout.EndScrollView();
            }
        }

        internal static IReadOnlyList<string> GetVisiblePropertyPaths(
            string group,
            string label)
        {
            BalanceEntry entry = Entries.FirstOrDefault(
                candidate => candidate.Group == group &&
                             candidate.Label == label);
            return entry?.VisiblePropertyPaths ?? Array.Empty<string>();
        }

        internal static IReadOnlyList<string> GetPublishPropertyPaths(
            string sourcePath)
        {
            BalanceEntry entry = UniqueAssetEntries.FirstOrDefault(
                candidate => string.Equals(
                    candidate.SourcePath,
                    sourcePath,
                    StringComparison.Ordinal));
            return entry?.PublishPropertyPaths ?? Array.Empty<string>();
        }

        internal static bool CopyPublishedProperties(
            UnityEngine.Object working,
            UnityEngine.Object source,
            IReadOnlyList<string> propertyPaths)
        {
            if (working == null || source == null ||
                propertyPaths == null || propertyPaths.Count == 0)
            {
                return false;
            }

            var workingObject = new SerializedObject(working);
            var sourceObject = new SerializedObject(source);
            workingObject.UpdateIfRequiredOrScript();
            sourceObject.UpdateIfRequiredOrScript();

            bool copiedAny = false;
            foreach (string propertyPath in propertyPaths
                         .Where(path => !string.IsNullOrWhiteSpace(path))
                         .Distinct(StringComparer.Ordinal))
            {
                SerializedProperty workingProperty =
                    workingObject.FindProperty(propertyPath);
                SerializedProperty sourceProperty =
                    sourceObject.FindProperty(propertyPath);
                if (workingProperty == null || sourceProperty == null)
                {
                    Debug.LogWarning(
                        $"[Balance] 게시 필드를 찾을 수 없습니다: " +
                        $"{propertyPath} ({working.name} -> {source.name})");
                    continue;
                }

                sourceObject.CopyFromSerializedProperty(workingProperty);
                copiedAny = true;
            }

            if (copiedAny)
            {
                sourceObject.ApplyModifiedPropertiesWithoutUndo();
            }

            return copiedAny;
        }

        internal static bool HasLocalizedPropertyLabel(string propertyName) =>
            PropertyLabels.ContainsKey(propertyName ?? string.Empty);

        internal static string GetLocalizedPropertyLabel(
            string propertyName,
            string fallback = null)
        {
            return PropertyLabels.TryGetValue(
                propertyName ?? string.Empty,
                out string label)
                ? label
                : fallback ?? ObjectNames.NicifyVariableName(propertyName);
        }

        internal static string GetLocalizedEnumLabel(string enumName)
        {
            return EnumLabels.TryGetValue(
                enumName ?? string.Empty,
                out string label)
                ? label
                : ObjectNames.NicifyVariableName(enumName);
        }

        private void DrawBuildingNameEditor()
        {
            EditorGUILayout.HelpBox(
                "여기서 바꾼 이름, 비용, 수입, 안정도, 설명은 건설 메뉴 툴팁과 " +
                "건물 정보창에 표시됩니다. " +
                "연구 카드 이름은 '연구 > 건물 해금 연구'에서 별도로 설정합니다.",
                MessageType.Info);

            foreach (string workingPath in GetBuildingNameWorkingPaths())
            {
                UnityEngine.Object target =
                    AssetDatabase.LoadMainAssetAtPath(workingPath);
                if (target == null)
                {
                    continue;
                }

                var serialized = new SerializedObject(target);
                serialized.UpdateIfRequiredOrScript();
                SerializedProperty buildingName =
                    serialized.FindProperty("buildingName");
                if (buildingName == null)
                {
                    continue;
                }

                using (new EditorGUILayout.VerticalScope(
                           EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(
                        string.IsNullOrWhiteSpace(buildingName.stringValue)
                            ? target.name
                            : buildingName.stringValue,
                        EditorStyles.boldLabel);

                    EditorGUILayout.PropertyField(
                        buildingName,
                        new GUIContent(
                            "건설 UI 표시 이름",
                            "건설 메뉴 툴팁과 건물 정보창에 표시할 이름입니다."));

                    string categoryLabel = target switch
                    {
                        TileDataSO tileData => tileData.Category.ToString(),
                        BuildingDefinitionSO definition =>
                            definition.category.ToString(),
                        _ => "-"
                    };
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.TextField(
                            new GUIContent(
                                "표시 분류",
                                "건물 동작과 연결된 값이므로 이 화면에서는 변경하지 않습니다."),
                            categoryLabel);
                    }

                    DrawTooltipProperty(
                        serialized,
                        "buildCost",
                        "건설 비용",
                        "툴팁의 '비용'에 표시되는 코인입니다.");

                    if (target is TileDataSO)
                    {
                        DrawTooltipProperty(
                            serialized,
                            "dailyCoinValue",
                            "툴팁 수입",
                            "툴팁의 '수입'에 표시되는 값입니다.");
                        DrawTooltipProperty(
                            serialized,
                            "prosperityValue",
                            "툴팁 안정도",
                            "툴팁의 '안정도'에 표시되는 값입니다.");
                    }
                    else
                    {
                        DrawTooltipProperty(
                            serialized,
                            "visitCadence.visitsPerPeriod",
                            "기간 내 방문 횟수",
                            "특수 건물 툴팁의 방문 횟수입니다.");
                        DrawTooltipProperty(
                            serialized,
                            "visitCadence.periodDays",
                            "방문 기간(일)",
                            "특수 건물 툴팁의 방문 주기입니다.");
                    }

                    if (string.IsNullOrWhiteSpace(buildingName.stringValue))
                    {
                        EditorGUILayout.HelpBox(
                            "건물 이름은 비워둘 수 없습니다.",
                            MessageType.Error);
                    }

                    string descriptionPropertyName = target is TileDataSO
                        ? "buildingDescription"
                        : "description";
                    SerializedProperty description =
                        serialized.FindProperty(descriptionPropertyName);
                    if (description != null)
                    {
                        EditorGUILayout.PropertyField(
                            description,
                            new GUIContent(
                                "건설 UI 설명",
                                "툴팁 맨 아래에 표시되는 설명입니다."));
                    }

                    if (serialized.ApplyModifiedProperties())
                    {
                        EditorUtility.SetDirty(target);
                        researchUnlockLabels = null;
                        RefreshOpenBuildPanels();
                    }
                }
            }
        }

        private static void DrawTooltipProperty(
            SerializedObject serialized,
            string propertyPath,
            string label,
            string tooltip)
        {
            SerializedProperty property =
                serialized.FindProperty(propertyPath);
            if (property != null)
            {
                EditorGUILayout.PropertyField(
                    property,
                    new GUIContent(label, tooltip));
            }
        }

        private static void RefreshOpenBuildPanels()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            CityFlow.UI.BuildPanelController[] panels =
                UnityEngine.Object.FindObjectsByType<
                    CityFlow.UI.BuildPanelController>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            foreach (CityFlow.UI.BuildPanelController panel in panels)
            {
                panel?.RefreshBalancePresentation();
            }
        }

        internal static IReadOnlyList<string> GetBuildingNameWorkingPaths() =>
            Entries
                .Where(entry => entry.VisiblePropertyPaths.Contains(
                    "buildingName",
                    StringComparer.Ordinal))
                .GroupBy(entry => entry.WorkingPath, StringComparer.Ordinal)
                .Select(group => group.Key)
                .ToArray();

        private static void DrawLocalizedAssetInspector(
            UnityEngine.Object target)
        {
            var serialized = new SerializedObject(target);
            serialized.UpdateIfRequiredOrScript();

            SerializedProperty property = serialized.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (property.propertyPath == "m_Script")
                {
                    continue;
                }

                DrawLocalizedProperty(
                    property.Copy(),
                    CreateLocalizedContent(property));
            }

            if (serialized.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(target);
            }

            if (target is GameTimeSettingsSO timeSettings)
            {
                DrawGameTimeSummary(timeSettings);
            }
        }

        private static void DrawFilteredAssetInspector(
            UnityEngine.Object target,
            IReadOnlyList<string> propertyPaths)
        {
            var serialized = new SerializedObject(target);
            serialized.UpdateIfRequiredOrScript();

            foreach (string propertyPath in propertyPaths)
            {
                SerializedProperty property =
                    serialized.FindProperty(propertyPath);
                if (property == null)
                {
                    EditorGUILayout.HelpBox(
                        $"설정 항목을 찾을 수 없습니다: {propertyPath}",
                        MessageType.Error);
                    continue;
                }

                DrawLocalizedProperty(
                    property.Copy(),
                    CreateLocalizedContent(property));
            }

            if (serialized.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(target);
            }
        }

        private static void DrawResidentialPopulationSetting()
        {
            const string populationWorkingPath =
                "Assets/05_ScriptableObjects/Balance/Editor/PopulationConfig_Balance.asset";
            UnityEngine.Object target =
                AssetDatabase.LoadMainAssetAtPath(populationWorkingPath);
            if (target == null)
            {
                EditorGUILayout.HelpBox(
                    "주거 인구 작업용 설정을 찾을 수 없습니다.",
                    MessageType.Warning);
                return;
            }

            var serialized = new SerializedObject(target);
            serialized.UpdateIfRequiredOrScript();
            SerializedProperty entries =
                serialized.FindProperty("populationEntries");
            SerializedProperty housePopulation = null;

            if (entries != null && entries.isArray)
            {
                for (int index = 0; index < entries.arraySize; index++)
                {
                    SerializedProperty entry =
                        entries.GetArrayElementAtIndex(index);
                    SerializedProperty tileType =
                        entry.FindPropertyRelative("tileType");
                    if (tileType != null &&
                        tileType.enumValueIndex == (int)TileType.House)
                    {
                        housePopulation =
                            entry.FindPropertyRelative("populationValue");
                        break;
                    }
                }
            }

            if (housePopulation == null)
            {
                EditorGUILayout.HelpBox(
                    "인구 설정에 주거 지역 항목이 없습니다. '핵심 > 인구'에서 추가해 주세요.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "주거 인구",
                EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                housePopulation,
                new GUIContent(
                    "주거지당 인구 증가량",
                    "주거 지역 한 곳이 기본으로 제공하는 인구입니다."));

            if (serialized.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(target);
            }
        }

        private static void DrawSupplementalBuildingSettings(
            string workingFileName,
            string heading)
        {
            UnityEngine.Object target = AssetDatabase.LoadMainAssetAtPath(
                $"{WorkingRoot}/{workingFileName}");
            if (target == null)
            {
                EditorGUILayout.HelpBox(
                    "실제 건물 작업용 설정을 찾을 수 없습니다. " +
                    "'작업 공간 생성 / 열기'를 다시 눌러 주세요.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(heading, EditorStyles.boldLabel);
            DrawFilteredAssetInspector(
                target,
                TileBuildingPropertyPaths);
        }

        private static void DrawGameTimeSummary(
            GameTimeSettingsSO settings)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "1배속 기준 환산 시간",
                EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.FloatField(
                    "게임 1시간당 현실 초",
                    settings.RealSecondsPerGameHour);
                EditorGUILayout.FloatField(
                    "게임 1일당 현실 분",
                    settings.RealMinutesPerGameDay);
                EditorGUILayout.FloatField(
                    "게임 1주당 현실 분",
                    settings.RealMinutesPerGameDay * 7f);
                EditorGUILayout.FloatField(
                    "게임 1개월당 현실 시간",
                    settings.RealMinutesPerGameDay * 30f / 60f);
                EditorGUILayout.FloatField(
                    "게임 1년당 현실 시간",
                    settings.RealMinutesPerGameDay * 360f / 60f);
            }

            EditorGUILayout.HelpBox(
                "시간 값을 바꾼 뒤에는 플레이 모드를 다시 시작해야 " +
                "모든 시간 시스템에 같은 속도가 적용됩니다.",
                MessageType.Info);
        }

        private static void DrawLocalizedProperty(
            SerializedProperty property,
            GUIContent label)
        {
            if (property.isArray &&
                property.propertyType != SerializedPropertyType.String)
            {
                DrawLocalizedArray(property, label);
                return;
            }

            if (property.propertyType == SerializedPropertyType.Generic)
            {
                DrawLocalizedGroup(property, label);
                return;
            }

            if (DrawLocalizedVector(property, label))
            {
                return;
            }

            if (property.propertyType == SerializedPropertyType.Enum)
            {
                string[] labels = property.enumNames
                    .Select(GetLocalizedEnumLabel)
                    .ToArray();
                property.enumValueIndex = EditorGUILayout.Popup(
                    label,
                    property.enumValueIndex,
                    labels);
                return;
            }

            EditorGUILayout.PropertyField(property, label, false);
        }

        private static bool DrawLocalizedVector(
            SerializedProperty property,
            GUIContent label)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Vector2Int:
                {
                    Vector2Int value = property.vector2IntValue;
                    EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        value.x = EditorGUILayout.IntField("가로축", value.x);
                        value.y = EditorGUILayout.IntField("세로축", value.y);
                    }

                    property.vector2IntValue = value;
                    return true;
                }
                case SerializedPropertyType.Vector2:
                {
                    Vector2 value = property.vector2Value;
                    EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        value.x = EditorGUILayout.FloatField("가로축", value.x);
                        value.y = EditorGUILayout.FloatField("세로축", value.y);
                    }

                    property.vector2Value = value;
                    return true;
                }
                case SerializedPropertyType.Vector3Int:
                {
                    Vector3Int value = property.vector3IntValue;
                    EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        value.x = EditorGUILayout.IntField("가로축", value.x);
                        value.y = EditorGUILayout.IntField("세로축", value.y);
                        value.z = EditorGUILayout.IntField("높이축", value.z);
                    }

                    property.vector3IntValue = value;
                    return true;
                }
                case SerializedPropertyType.Vector3:
                {
                    Vector3 value = property.vector3Value;
                    EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        value.x = EditorGUILayout.FloatField("가로축", value.x);
                        value.y = EditorGUILayout.FloatField("세로축", value.y);
                        value.z = EditorGUILayout.FloatField("높이축", value.z);
                    }

                    property.vector3Value = value;
                    return true;
                }
                case SerializedPropertyType.Vector4:
                {
                    Vector4 value = property.vector4Value;
                    EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        value.x = EditorGUILayout.FloatField("가로축", value.x);
                        value.y = EditorGUILayout.FloatField("세로축", value.y);
                        value.z = EditorGUILayout.FloatField("높이축", value.z);
                        value.w = EditorGUILayout.FloatField("네 번째 축", value.w);
                    }

                    property.vector4Value = value;
                    return true;
                }
                default:
                    return false;
            }
        }

        private static void DrawLocalizedArray(
            SerializedProperty property,
            GUIContent label)
        {
            property.isExpanded = EditorGUILayout.Foldout(
                property.isExpanded,
                $"{label.text} ({property.arraySize}개)",
                true);
            if (!property.isExpanded)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                int size = Mathf.Max(
                    0,
                    EditorGUILayout.DelayedIntField(
                        new GUIContent("항목 수"),
                        property.arraySize));
                if (size != property.arraySize)
                {
                    property.arraySize = size;
                }

                for (int index = 0; index < property.arraySize; index++)
                {
                    SerializedProperty element =
                        property.GetArrayElementAtIndex(index);
                    DrawLocalizedProperty(
                        element,
                        new GUIContent($"항목 {index + 1}"));
                }
            }
        }

        private static void DrawLocalizedGroup(
            SerializedProperty property,
            GUIContent label)
        {
            property.isExpanded = EditorGUILayout.Foldout(
                property.isExpanded,
                label,
                true);
            if (!property.isExpanded)
            {
                return;
            }

            SerializedProperty child = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            if (!child.NextVisible(true))
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                while (!SerializedProperty.EqualContents(child, end))
                {
                    DrawLocalizedProperty(
                        child.Copy(),
                        CreateLocalizedContent(child));
                    if (!child.NextVisible(false))
                    {
                        break;
                    }
                }
            }
        }

        private static GUIContent CreateLocalizedContent(
            SerializedProperty property)
        {
            return new GUIContent(
                GetLocalizedPropertyLabel(
                    property.name,
                    property.displayName),
                property.tooltip);
        }

        private static void EnsureWorkingAssets()
        {
            EnsureFolder("Assets/05_ScriptableObjects/Balance");
            EnsureFolder(WorkingRoot);

            foreach (BalanceEntry entry in UniqueAssetEntries)
            {
                if (AssetDatabase.LoadMainAssetAtPath(entry.SourcePath) == null)
                {
                    Debug.LogWarning(
                        $"[Balance] 원본 설정 에셋을 찾지 못했습니다: {entry.SourcePath}");
                    continue;
                }

                if (AssetDatabase.LoadMainAssetAtPath(entry.WorkingPath) != null)
                {
                    continue;
                }

                if (!AssetDatabase.CopyAsset(entry.SourcePath, entry.WorkingPath))
                {
                    Debug.LogError(
                        $"[Balance] 작업용 에셋 복사 실패: {entry.WorkingPath}");
                }
            }

            RewireWorkingBuildingCatalog();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void RewireWorkingBuildingCatalog()
        {
            UnityEngine.Object catalog =
                AssetDatabase.LoadMainAssetAtPath(
                    WorkingBuildingCatalogPath);
            if (catalog == null)
            {
                return;
            }

            var serialized = new SerializedObject(catalog);
            serialized.UpdateIfRequiredOrScript();
            SerializedProperty buildings =
                serialized.FindProperty("buildings");
            if (buildings == null || !buildings.isArray)
            {
                return;
            }

            bool changed = false;
            for (int index = 0; index < buildings.arraySize; index++)
            {
                SerializedProperty element =
                    buildings.GetArrayElementAtIndex(index);
                UnityEngine.Object current =
                    element.objectReferenceValue;
                if (current == null)
                {
                    continue;
                }

                string currentPath = AssetDatabase.GetAssetPath(current);
                BalanceEntry entry = Entries.FirstOrDefault(candidate =>
                    candidate.LinkedResearchId.Length > 0 &&
                    (candidate.SourcePath == currentPath ||
                     candidate.WorkingPath == currentPath));
                if (entry == null)
                {
                    continue;
                }

                UnityEngine.Object working =
                    AssetDatabase.LoadMainAssetAtPath(
                        entry.WorkingPath);
                if (working == null || current == working)
                {
                    continue;
                }

                element.objectReferenceValue = working;
                changed = true;
            }

            if (changed && serialized.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(catalog);
            }
        }

        private static void EnsureBalanceScene()
        {
            EnsureFolder("Assets/00_Scenes/Debug");

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
            {
                throw new InvalidOperationException(
                    $"통합 Scene을 찾을 수 없습니다: {SourceScenePath}");
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BalanceScenePath) != null)
            {
                return;
            }

            if (!AssetDatabase.CopyAsset(SourceScenePath, BalanceScenePath))
            {
                throw new InvalidOperationException(
                    $"밸런스 Scene 복제에 실패했습니다: {BalanceScenePath}");
            }

            AssetDatabase.Refresh();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int separatorIndex = path.LastIndexOf('/');
            string parent = path.Substring(0, separatorIndex);
            string folderName = path.Substring(separatorIndex + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        private static int RewireSceneToWorkingAssets(Scene scene)
        {
            Dictionary<UnityEngine.Object, UnityEngine.Object> replacements =
                new();
            UnityEngine.Object workingResearchCatalog =
                AssetDatabase.LoadMainAssetAtPath(
                    WorkingResearchCatalogPath);

            foreach (BalanceEntry entry in UniqueAssetEntries)
            {
                UnityEngine.Object source =
                    AssetDatabase.LoadMainAssetAtPath(entry.SourcePath);
                UnityEngine.Object working =
                    AssetDatabase.LoadMainAssetAtPath(entry.WorkingPath);

                if (source != null && working != null)
                {
                    replacements[source] = working;
                }
            }

            int changeCount = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Component component in
                         root.GetComponentsInChildren<Component>(true))
                {
                    if (component == null)
                    {
                        continue;
                    }

                    SerializedObject serialized = new(component);
                    // Research catalog assignment is deferred until after the
                    // generic iterator has finished (see below).
                    SerializedProperty researchCatalogProperty = null;
                    SerializedProperty property = serialized.GetIterator();
                    bool enterChildren = true;
                    bool recorded = false;

                    while (property.Next(enterChildren))
                    {
                        enterChildren = true;
                        if (property.propertyType !=
                            SerializedPropertyType.ObjectReference)
                        {
                            continue;
                        }

                        UnityEngine.Object current = property.objectReferenceValue;
                        if (current == null ||
                            !replacements.TryGetValue(
                                current,
                                out UnityEngine.Object replacement))
                        {
                            continue;
                        }

                        if (!recorded)
                        {
                            Undo.RecordObject(component, "밸런스 작업용 설정 연결");
                            recorded = true;
                        }

                        property.objectReferenceValue = replacement;
                        changeCount++;
                    }

                    researchCatalogProperty = UsesResearchCatalog(component)
                        ? serialized.FindProperty("catalog")
                        : null;
                    if (researchCatalogProperty != null &&
                        workingResearchCatalog != null &&
                        researchCatalogProperty.objectReferenceValue !=
                        workingResearchCatalog)
                    {
                        if (!recorded)
                        {
                            Undo.RecordObject(
                                component,
                                "Connect working research catalog");
                            recorded = true;
                        }

                        researchCatalogProperty.objectReferenceValue =
                            workingResearchCatalog;
                        changeCount++;
                    }

                    if (recorded)
                    {
                        serialized.ApplyModifiedProperties();
                        if (PrefabUtility.IsPartOfPrefabInstance(component))
                        {
                            PrefabUtility.RecordPrefabInstancePropertyModifications(
                                component);
                        }
                        EditorUtility.SetDirty(component);
                    }
                }
            }

            if (changeCount > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }

            return changeCount;
        }

        private static bool UsesResearchCatalog(Component component)
        {
            string typeName = component?.GetType().FullName;
            return typeName ==
                       "CityFlow.Gameplay.Research.ResearchUnlockService" ||
                   typeName ==
                       "CityFlow.UI.ResearchPanelController";
        }

        private void RunValidation()
        {
            validationMessages.Clear();

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
            {
                validationMessages.Add("통합 Scene 원본이 없습니다.");
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BalanceScenePath) == null)
            {
                validationMessages.Add("밸런스 전용 Scene이 아직 생성되지 않았습니다.");
            }

            if (EditorBuildSettings.scenes.Any(
                    scene => scene.path == BalanceScenePath))
            {
                validationMessages.Add(
                    "밸런스 전용 Scene이 Build Settings에 포함되어 있습니다. 제거해 주세요.");
            }

            foreach (BalanceEntry entry in UniqueAssetEntries)
            {
                if (AssetDatabase.LoadMainAssetAtPath(entry.SourcePath) == null)
                {
                    validationMessages.Add($"원본 누락: {entry.Label}");
                }

                if (AssetDatabase.LoadMainAssetAtPath(entry.WorkingPath) == null)
                {
                    validationMessages.Add($"작업용 에셋 누락: {entry.Label}");
                }
            }

            ValidateTimeConsistency();
            ValidateResearchCatalog();
            ValidateLoadedSceneReferences();

            if (validationMessages.Count == 0)
            {
                ShowNotification(new GUIContent("밸런스 작업 공간 검증 완료"));
            }
        }

        private void ValidateTimeConsistency()
        {
            BalanceEntry simEntry = Entries.First(
                entry => entry.Label == "시뮬레이션");
            BalanceEntry timeEntry = Entries.First(
                entry => entry.Label == "게임 시간");

            UnityEngine.Object sim =
                AssetDatabase.LoadMainAssetAtPath(simEntry.WorkingPath);
            UnityEngine.Object time =
                AssetDatabase.LoadMainAssetAtPath(timeEntry.WorkingPath);

            if (sim == null || time == null)
            {
                return;
            }

            SerializedProperty daySeconds =
                new SerializedObject(sim).FindProperty("Value.DayLengthSeconds");
            SerializedProperty realMinutes =
                new SerializedObject(time).FindProperty("realMinutesPerGameDay");

            if (daySeconds == null || realMinutes == null)
            {
                return;
            }

            float timeAssetSeconds = realMinutes.floatValue * 60f;
            if (!Mathf.Approximately(daySeconds.floatValue, timeAssetSeconds))
            {
                validationMessages.Add(
                    $"하루 길이 불일치: 시뮬레이션 {daySeconds.floatValue:0.##}초 / " +
                    $"게임 시간 {timeAssetSeconds:0.##}초. 두 값을 같은 기준으로 맞춰 주세요.");
            }
        }

        private void DrawResearchCatalog(
            UnityEngine.Object target,
            ResearchBalanceSection section)
        {
            var serialized = new SerializedObject(target);
            serialized.Update();
            SerializedProperty entries =
                serialized.FindProperty("entries");
            if (entries == null || entries.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "편집할 연구가 없습니다.",
                    MessageType.Warning);
                return;
            }

            var matchingIndices = new List<int>();
            for (int index = 0; index < entries.arraySize; index++)
            {
                SerializedProperty entry =
                    entries.GetArrayElementAtIndex(index);
                SerializedProperty category =
                    entry.FindPropertyRelative("category");
                ResearchCategory researchCategory = category != null
                    ? (ResearchCategory)category.enumValueIndex
                    : ResearchCategory.Commercial;
                if (IsResearchInSection(researchCategory, section))
                {
                    matchingIndices.Add(index);
                }
            }

            if (matchingIndices.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    section == ResearchBalanceSection.Expansion
                        ? "설정할 개척 단계가 없습니다."
                        : "설정할 건물 해금 연구가 없습니다.",
                    MessageType.Warning);
                return;
            }

            int popupIndex = matchingIndices.IndexOf(selectedResearchIndex);
            popupIndex = popupIndex >= 0 ? popupIndex : 0;
            string[] researchLabels = matchingIndices
                .Select(index => GetResearchLabel(
                    entries.GetArrayElementAtIndex(index),
                    index))
                .ToArray();

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(
                "1. 수정할 연구 선택",
                EditorStyles.boldLabel);
            popupIndex = EditorGUILayout.Popup(popupIndex, researchLabels);
            selectedResearchIndex = matchingIndices[popupIndex];

            SerializedProperty selectedEntry =
                entries.GetArrayElementAtIndex(
                    selectedResearchIndex);
            string selectedResearchId = (
                selectedEntry.FindPropertyRelative("researchId")
                    ?.stringValue ?? string.Empty).Trim();

            if (section == ResearchBalanceSection.BuildingUnlock)
            {
                DrawUnlockedBuildingSummary(selectedResearchId);
            }
            else
            {
                DrawExpansionStageSummary(selectedEntry);
            }

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.VerticalScope(
                       EditorStyles.helpBox))
            {
                DrawResearchIdentity(entries, selectedEntry, section);
            }

            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.VerticalScope(
                       EditorStyles.helpBox))
            {
                DrawResearchRequirements(selectedEntry);
            }

            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.VerticalScope(
                       EditorStyles.helpBox))
            {
                DrawResearchCostAndDuration(selectedEntry);
            }

            if (section == ResearchBalanceSection.BuildingUnlock)
            {
                EditorGUILayout.Space(6f);
                DrawLinkedBuildingBalance(selectedResearchId);
            }

            EditorGUILayout.Space(6f);
            showResearchAdvanced = EditorGUILayout.Foldout(
                showResearchAdvanced,
                "고급 설정",
                true);
            if (showResearchAdvanced)
            {
                using (new EditorGUILayout.VerticalScope(
                           EditorStyles.helpBox))
                {
                    EditorGUILayout.HelpBox(
                        "연구 ID는 건물 데이터와 저장 데이터가 사용합니다. " +
                        "이미 연결된 연구 ID는 특별한 이유가 없다면 변경하지 마세요.",
                        MessageType.Warning);
                    EditorGUILayout.PropertyField(
                        selectedEntry.FindPropertyRelative(
                            "researchId"),
                        new GUIContent("연구 ID"));
                }
            }

            if (serialized.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(target);
            }
        }

        private static void DrawLinkedBuildingBalance(
            string researchId)
        {
            BalanceEntry[] linkedEntries = Entries
                .Where(entry =>
                    string.Equals(
                        entry.LinkedResearchId,
                        researchId,
                        StringComparison.Ordinal))
                .ToArray();

            using (new EditorGUILayout.VerticalScope(
                       EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "5. 실제 건설 메뉴와 건물 밸런스",
                    EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "연구 화면 이름·비용과 실제 건설 메뉴의 건물 이름·건설 비용은 " +
                    "서로 다른 값입니다. 아래 값은 건설 버튼, 툴팁, 배치된 건물에 적용됩니다.",
                    MessageType.Info);

                if (linkedEntries.Length == 0)
                {
                    EditorGUILayout.HelpBox(
                        "이 연구에 연결된 실제 건물 설정을 찾을 수 없습니다.",
                        MessageType.Warning);
                    return;
                }

                foreach (BalanceEntry entry in linkedEntries)
                {
                    UnityEngine.Object target =
                        AssetDatabase.LoadMainAssetAtPath(
                            entry.WorkingPath);
                    if (target == null)
                    {
                        EditorGUILayout.HelpBox(
                            $"{entry.Label} 작업용 에셋이 없습니다. " +
                            "'작업 공간 생성 / 열기'를 다시 눌러 주세요.",
                            MessageType.Warning);
                        continue;
                    }

                    EditorGUILayout.LabelField(
                        entry.Label,
                        EditorStyles.miniBoldLabel);
                    DrawFilteredAssetInspector(
                        target,
                        entry.VisiblePropertyPaths);
                }
            }
        }

        internal static IReadOnlyList<string>
            GetLinkedBuildingWorkingPaths(string researchId) =>
            Entries
                .Where(entry => string.Equals(
                    entry.LinkedResearchId,
                    researchId?.Trim(),
                    StringComparison.Ordinal))
                .Select(entry => entry.WorkingPath)
                .ToArray();

        private static void DrawExpansionStageSummary(
            SerializedProperty selectedEntry)
        {
            string stageId = (
                selectedEntry.FindPropertyRelative("worldGridStageId")
                    ?.stringValue ?? string.Empty).Trim();
            EditorGUILayout.HelpBox(
                stageId.Length > 0
                    ? $"연구 완료 후 적용할 개척 단계: {stageId}"
                    : "연결된 개척 단계가 없습니다. 확장 단계 ID를 설정해 주세요.",
                stageId.Length > 0
                    ? MessageType.Info
                    : MessageType.Warning);
        }

        private void DrawUnlockedBuildingSummary(
            string researchId)
        {
            researchUnlockLabels = BuildResearchUnlockLabels();

            if (researchId.Length > 0 &&
                researchUnlockLabels.TryGetValue(
                    researchId,
                    out string buildingNames))
            {
                EditorGUILayout.HelpBox(
                    $"이 연구가 해금하는 건물: {buildingNames}",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(
                "이 연구 ID에 연결된 건물이 없습니다.",
                MessageType.Warning);
        }

        private static Dictionary<string, string>
            BuildResearchUnlockLabels()
        {
            var namesByResearch =
                new Dictionary<string, List<string>>(
                    StringComparer.Ordinal);

            foreach (BalanceEntry entry in Entries.Where(candidate =>
                         candidate.LinkedResearchId.Length > 0))
            {
                UnityEngine.Object asset =
                    AssetDatabase.LoadMainAssetAtPath(
                        entry.WorkingPath) ??
                    AssetDatabase.LoadMainAssetAtPath(
                        entry.SourcePath);
                string buildingName = asset switch
                {
                    BuildingDefinitionSO definition =>
                        definition.buildingName,
                    TileDataSO tileData =>
                        tileData.BuildingName,
                    _ => string.Empty
                };
                AddResearchBuildingLabel(
                    namesByResearch,
                    entry.LinkedResearchId,
                    buildingName);
            }

            return namesByResearch.ToDictionary(
                pair => pair.Key,
                pair => string.Join(
                    ", ",
                    pair.Value
                        .Where(name =>
                            !string.IsNullOrWhiteSpace(name))
                        .Distinct()));
        }

        private static void AddResearchBuildingLabel(
            Dictionary<string, List<string>> namesByResearch,
            string researchId,
            string buildingName)
        {
            string normalizedId =
                researchId?.Trim() ?? string.Empty;
            string normalizedName =
                buildingName?.Trim() ?? string.Empty;
            if (normalizedId.Length == 0 ||
                normalizedName.Length == 0)
            {
                return;
            }

            if (!namesByResearch.TryGetValue(
                    normalizedId,
                    out List<string> names))
            {
                names = new List<string>();
                namesByResearch.Add(normalizedId, names);
            }

            names.Add(normalizedName);
        }

        private static string GetResearchLabel(
            SerializedProperty entry,
            int index)
        {
            string displayName = (
                entry.FindPropertyRelative("displayName")
                    ?.stringValue ?? string.Empty).Trim();
            string researchId = (
                entry.FindPropertyRelative("researchId")
                    ?.stringValue ?? string.Empty).Trim();

            if (displayName.Length > 0)
            {
                return displayName;
            }

            return researchId.Length > 0
                ? researchId
                : $"이름 없는 연구 {index + 1}";
        }

        private static void DrawResearchIdentity(
            SerializedProperty entries,
            SerializedProperty selectedEntry,
            ResearchBalanceSection section)
        {
            EditorGUILayout.LabelField(
                "2. 이름과 선행 연구",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                selectedEntry.FindPropertyRelative("displayName"),
                new GUIContent(
                    "화면 표시 이름",
                    "게임 연구 화면에 보이는 이름입니다."));

            SerializedProperty category =
                selectedEntry.FindPropertyRelative("category");
            if (section == ResearchBalanceSection.BuildingUnlock)
            {
                EditorGUILayout.PropertyField(
                    category,
                    new GUIContent(
                        "해금 카테고리",
                        "게임 연구 화면에서 이 건물이 표시될 카테고리입니다."));
            }
            else
            {
                EditorGUILayout.LabelField("설정 종류", "개척");
            }

            if (category != null &&
                category.enumValueIndex ==
                (int)ResearchCategory.Expansion)
            {
                EditorGUILayout.PropertyField(
                    selectedEntry.FindPropertyRelative(
                        "worldGridStageId"),
                    new GUIContent(
                        "확장 단계 ID",
                        "연구 완료 시 해금할 WorldGridUnlockProfile 단계 ID입니다."));
            }

            DrawPrerequisitePopup(entries, selectedEntry, section);
        }

        private static void DrawPrerequisitePopup(
            SerializedProperty entries,
            SerializedProperty selectedEntry,
            ResearchBalanceSection section)
        {
            SerializedProperty selectedIdProperty =
                selectedEntry.FindPropertyRelative("researchId");
            SerializedProperty prerequisiteProperty =
                selectedEntry.FindPropertyRelative(
                    "prerequisiteId");
            string selectedId =
                selectedIdProperty?.stringValue?.Trim() ??
                string.Empty;
            string currentPrerequisite =
                prerequisiteProperty?.stringValue?.Trim() ??
                string.Empty;

            var values = new List<string> { string.Empty };
            var labels = new List<string>
            {
                "없음 — 바로 연구 가능"
            };

            for (int index = 0;
                 index < entries.arraySize;
                 index++)
            {
                SerializedProperty candidate =
                    entries.GetArrayElementAtIndex(index);
                SerializedProperty candidateCategory =
                    candidate.FindPropertyRelative("category");
                ResearchCategory candidateResearchCategory =
                    candidateCategory != null
                        ? (ResearchCategory)candidateCategory.enumValueIndex
                        : ResearchCategory.Commercial;
                if (!IsResearchInSection(
                        candidateResearchCategory,
                        section))
                {
                    continue;
                }

                string candidateId =
                    candidate.FindPropertyRelative("researchId")
                        ?.stringValue?.Trim() ?? string.Empty;
                if (candidateId.Length == 0 ||
                    candidateId == selectedId)
                {
                    continue;
                }

                values.Add(candidateId);
                labels.Add(
                    GetResearchLabel(candidate, index));
            }

            int selectedIndex = values.IndexOf(
                currentPrerequisite);
            if (selectedIndex < 0 &&
                currentPrerequisite.Length > 0)
            {
                values.Add(currentPrerequisite);
                labels.Add(
                    $"연결 오류 — {currentPrerequisite}");
                selectedIndex = values.Count - 1;
            }

            selectedIndex = EditorGUILayout.Popup(
                new GUIContent(
                    "먼저 완료할 연구",
                    "이 연구를 시작하기 전에 완료해야 하는 연구입니다."),
                Mathf.Max(0, selectedIndex),
                labels.ToArray());
            prerequisiteProperty.stringValue =
                values[selectedIndex];
        }

        private static void DrawResearchRequirements(
            SerializedProperty selectedEntry)
        {
            EditorGUILayout.LabelField(
                "3. 해금 조건",
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "표시된 조건을 모두 만족하면 연구를 시작할 수 있습니다.",
                MessageType.None);

            SerializedProperty requirements =
                selectedEntry.FindPropertyRelative("requirements");
            if (requirements == null)
            {
                EditorGUILayout.HelpBox(
                    "조건 목록을 찾을 수 없습니다.",
                    MessageType.Error);
                return;
            }

            if (requirements.arraySize == 0)
            {
                EditorGUILayout.LabelField(
                    "조건 1",
                    EditorStyles.miniBoldLabel);
                DrawResearchCondition(
                    selectedEntry.FindPropertyRelative(
                        "conditionKind"),
                    selectedEntry.FindPropertyRelative(
                        "threshold"),
                    selectedEntry.FindPropertyRelative(
                        "targetTileType"));

                if (GUILayout.Button(
                        "+ 조건 하나 더 추가",
                        GUILayout.Height(24f)))
                {
                    ConvertLegacyConditionToRequirements(
                        selectedEntry,
                        requirements);
                }

                return;
            }

            int removeIndex = -1;
            for (int index = 0;
                 index < requirements.arraySize;
                 index++)
            {
                SerializedProperty requirement =
                    requirements.GetArrayElementAtIndex(index);
                using (new EditorGUILayout.VerticalScope(
                           EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            $"조건 {index + 1}",
                            EditorStyles.miniBoldLabel);
                        if (GUILayout.Button(
                                "삭제",
                                GUILayout.Width(48f)))
                        {
                            removeIndex = index;
                        }
                    }

                    DrawResearchCondition(
                        requirement.FindPropertyRelative(
                            "conditionKind"),
                        requirement.FindPropertyRelative(
                            "threshold"),
                        requirement.FindPropertyRelative(
                            "targetTileType"));
                }
            }

            if (removeIndex >= 0)
            {
                if (requirements.arraySize == 1)
                {
                    CopyCondition(
                        requirements.GetArrayElementAtIndex(0),
                        selectedEntry);
                }
                requirements.DeleteArrayElementAtIndex(
                    removeIndex);
            }

            if (GUILayout.Button(
                    "+ 조건 추가",
                    GUILayout.Height(24f)))
            {
                AddDefaultRequirement(requirements);
            }
        }

        private static void DrawResearchCondition(
            SerializedProperty conditionKind,
            SerializedProperty threshold,
            SerializedProperty targetTileType)
        {
            string[] conditionLabels =
            {
                "전날 도착 차량 수",
                "도시 인구",
                "건물 개수"
            };
            conditionKind.enumValueIndex =
                EditorGUILayout.Popup(
                    "조건 종류",
                    conditionKind.enumValueIndex,
                    conditionLabels);

            if (conditionKind.enumValueIndex ==
                (int)ResearchConditionKind.BuildingCount)
            {
                targetTileType.enumValueIndex =
                    EditorGUILayout.Popup(
                        "대상 건물",
                        targetTileType.enumValueIndex,
                        GetTileTypeLabels());
            }

            threshold.intValue = Mathf.Max(
                0,
                EditorGUILayout.IntField(
                    conditionKind.enumValueIndex ==
                    (int)ResearchConditionKind.BuildingCount
                        ? "필요 개수"
                        : "필요 수치",
                    threshold.intValue));
        }

        private static string[] GetTileTypeLabels()
        {
            return Enum.GetValues(typeof(TileType))
                .Cast<TileType>()
                .Select(type => type switch
                {
                    TileType.Empty => "빈 공간",
                    TileType.Road => "도로",
                    TileType.House => "주거 지역",
                    TileType.Office => "회사",
                    TileType.School => "학교",
                    TileType.Hospital => "병원",
                    TileType.SpecialBuilding => "특수 건물",
                    TileType.UnderConstruction => "건설 중",
                    _ => type.ToString()
                })
                .ToArray();
        }

        private static void ConvertLegacyConditionToRequirements(
            SerializedProperty selectedEntry,
            SerializedProperty requirements)
        {
            requirements.arraySize = 2;
            CopyCondition(
                selectedEntry,
                requirements.GetArrayElementAtIndex(0));
            InitializeRequirement(
                requirements.GetArrayElementAtIndex(1));
        }

        private static void AddDefaultRequirement(
            SerializedProperty requirements)
        {
            int index = requirements.arraySize;
            requirements.InsertArrayElementAtIndex(index);
            InitializeRequirement(
                requirements.GetArrayElementAtIndex(index));
        }

        private static void InitializeRequirement(
            SerializedProperty requirement)
        {
            requirement.FindPropertyRelative(
                    "conditionKind")
                .enumValueIndex =
                (int)ResearchConditionKind.BuildingCount;
            requirement.FindPropertyRelative(
                    "threshold")
                .intValue = 1;
            requirement.FindPropertyRelative(
                    "targetTileType")
                .enumValueIndex = (int)TileType.House;
        }

        private static void CopyCondition(
            SerializedProperty source,
            SerializedProperty destination)
        {
            destination.FindPropertyRelative(
                    "conditionKind")
                .enumValueIndex =
                source.FindPropertyRelative(
                        "conditionKind")
                    .enumValueIndex;
            destination.FindPropertyRelative(
                    "threshold")
                .intValue =
                source.FindPropertyRelative(
                        "threshold")
                    .intValue;
            destination.FindPropertyRelative(
                    "targetTileType")
                .enumValueIndex =
                source.FindPropertyRelative(
                        "targetTileType")
                    .enumValueIndex;
        }

        private static void DrawResearchCostAndDuration(
            SerializedProperty selectedEntry)
        {
            EditorGUILayout.LabelField(
                "4. 비용과 시간",
                EditorStyles.boldLabel);

            SerializedProperty cost =
                selectedEntry.FindPropertyRelative(
                    "researchCost");
            SerializedProperty duration =
                selectedEntry.FindPropertyRelative(
                    "researchDurationHours");

            cost.intValue = Mathf.Max(
                0,
                EditorGUILayout.IntField(
                    new GUIContent(
                        "연구 비용",
                        "연구 시작 시 한 번 지불하는 재화입니다."),
                    cost.intValue));
            duration.intValue = Mathf.Max(
                0,
                EditorGUILayout.IntField(
                    new GUIContent(
                        "연구 시간",
                        "게임 안에서 흐르는 시간 기준입니다. 0이면 즉시 완료됩니다."),
                    duration.intValue));

            EditorGUILayout.HelpBox(
                duration.intValue == 0
                    ? "연구 시작 즉시 완료됩니다."
                    : $"게임 시간으로 {duration.intValue}시간 뒤 완료됩니다.",
                MessageType.Info);
        }

        private void ValidateResearchCatalog()
        {
            UnityEngine.Object catalog =
                AssetDatabase.LoadMainAssetAtPath(
                    WorkingResearchCatalogPath);
            if (catalog == null)
            {
                return;
            }

            SerializedProperty entries =
                new SerializedObject(catalog).FindProperty("entries");
            if (entries == null)
            {
                validationMessages.Add(
                    "연구 카탈로그에서 연구 목록을 찾을 수 없습니다.");
                return;
            }

            var researchIds =
                new HashSet<string>(StringComparer.Ordinal);
            var prerequisiteIds = new List<(string ResearchId, string PrerequisiteId)>();

            for (int index = 0; index < entries.arraySize; index++)
            {
                SerializedProperty entry =
                    entries.GetArrayElementAtIndex(index);
                string researchId = (
                    entry.FindPropertyRelative("researchId")
                        ?.stringValue ?? string.Empty).Trim();
                string prerequisiteId = (
                    entry.FindPropertyRelative("prerequisiteId")
                        ?.stringValue ?? string.Empty).Trim();

                if (researchId.Length == 0)
                {
                    validationMessages.Add(
                        $"연구 {index + 1}번의 연구 ID가 비어 있습니다.");
                }
                else if (!researchIds.Add(researchId))
                {
                    validationMessages.Add(
                        $"연구 ID가 중복되었습니다: {researchId}");
                }

                if (prerequisiteId.Length > 0)
                {
                    prerequisiteIds.Add((researchId, prerequisiteId));
                }

                SerializedProperty category =
                    entry.FindPropertyRelative("category");
                string worldGridStageId = (
                    entry.FindPropertyRelative("worldGridStageId")
                        ?.stringValue ?? string.Empty).Trim();
                if (category != null &&
                    category.enumValueIndex ==
                    (int)ResearchCategory.Expansion &&
                    worldGridStageId.Length == 0)
                {
                    validationMessages.Add(
                        $"개척 연구 {researchId}의 확장 단계 ID가 비어 있습니다.");
                }

                ValidateNonNegative(
                    entry,
                    "researchCost",
                    researchId,
                    "연구 비용");
                ValidateNonNegative(
                    entry,
                    "researchDurationHours",
                    researchId,
                    "연구 시간");
                ValidateNonNegative(
                    entry,
                    "threshold",
                    researchId,
                    "단일 조건 목표치");

                SerializedProperty requirements =
                    entry.FindPropertyRelative("requirements");
                if (requirements == null)
                {
                    continue;
                }

                for (int requirementIndex = 0;
                     requirementIndex < requirements.arraySize;
                     requirementIndex++)
                {
                    ValidateNonNegative(
                        requirements.GetArrayElementAtIndex(
                            requirementIndex),
                        "threshold",
                        researchId,
                        $"조건 {requirementIndex + 1} 목표치");
                }
            }

            for (int index = 0;
                 index < prerequisiteIds.Count;
                 index++)
            {
                (string researchId, string prerequisiteId) =
                    prerequisiteIds[index];
                if (!researchIds.Contains(prerequisiteId))
                {
                    validationMessages.Add(
                        $"연구 {researchId}의 선행 연구 ID가 존재하지 않습니다: " +
                        prerequisiteId);
                }
            }
        }

        private void ValidateNonNegative(
            SerializedProperty owner,
            string propertyName,
            string researchId,
            string label)
        {
            SerializedProperty value =
                owner?.FindPropertyRelative(propertyName);
            if (value != null && value.intValue < 0)
            {
                validationMessages.Add(
                    $"연구 {researchId}: {label}는 0 이상이어야 합니다.");
            }
        }

        private void ValidateLoadedSceneReferences()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!IsSupportedBalanceScenePath(scene.path))
            {
                validationMessages.Add(
                    "밸런스 전용 Scene이 열려 있지 않아 Scene 연결 상태는 검사하지 못했습니다.");
                return;
            }

            HashSet<UnityEngine.Object> productionAssets = Entries
                .Select(entry =>
                    AssetDatabase.LoadMainAssetAtPath(entry.SourcePath))
                .Where(asset => asset != null)
                .ToHashSet();
            UnityEngine.Object workingResearchCatalog =
                AssetDatabase.LoadMainAssetAtPath(
                    WorkingResearchCatalogPath);
            int productionReferenceCount = 0;
            int invalidResearchCatalogCount = 0;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Component component in
                         root.GetComponentsInChildren<Component>(true))
                {
                    if (component == null)
                    {
                        continue;
                    }

                    SerializedProperty property =
                        new SerializedObject(component).GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType ==
                                SerializedPropertyType.ObjectReference &&
                            property.objectReferenceValue != null &&
                            productionAssets.Contains(
                                property.objectReferenceValue))
                        {
                            productionReferenceCount++;
                        }
                    }

                    if (UsesResearchCatalog(component))
                    {
                        SerializedProperty catalogProperty =
                            new SerializedObject(component)
                                .FindProperty("catalog");
                        if (catalogProperty == null ||
                            catalogProperty.objectReferenceValue !=
                            workingResearchCatalog)
                        {
                            invalidResearchCatalogCount++;
                        }
                    }
                }
            }

            if (productionReferenceCount > 0)
            {
                validationMessages.Add(
                    $"Scene에 실제 설정 참조가 {productionReferenceCount}개 남아 있습니다. " +
                    "'작업 공간 생성 / 열기'를 다시 눌러 작업용 설정으로 연결하세요.");
            }

            if (invalidResearchCatalogCount > 0)
            {
                validationMessages.Add(
                    $"연구 서비스 또는 UI {invalidResearchCatalogCount}개가 " +
                    "작업용 연구 카탈로그에 연결되지 않았습니다. " +
                    "'작업 공간 생성 / 열기'를 다시 눌러 주세요.");
            }
        }

        private void PublishWorkingValues()
        {
            RunValidation();
            if (validationMessages.Any(
                    message => message.Contains("누락") ||
                               message.Contains("Build Settings")))
            {
                EditorUtility.DisplayDialog(
                    "반영 중단",
                    "필수 에셋 또는 Scene 상태를 먼저 해결해 주세요.",
                    "확인");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "밸런스 수치 확정",
                "작업용 수치를 실제 게임 설정 에셋에 반영합니다.\n" +
                "통합 Scene은 수정하지 않지만 여러 공용 설정 에셋이 변경됩니다.\n\n" +
                "계속할까요?",
                "반영",
                "취소");

            if (!confirmed)
            {
                return;
            }

            foreach (BalanceEntry entry in UniqueAssetEntries)
            {
                if (!entry.PublishToSource)
                {
                    continue;
                }

                UnityEngine.Object source =
                    AssetDatabase.LoadMainAssetAtPath(entry.SourcePath);
                UnityEngine.Object working =
                    AssetDatabase.LoadMainAssetAtPath(entry.WorkingPath);

                if (source == null || working == null)
                {
                    continue;
                }

                string originalName = source.name;
                Undo.RecordObject(source, "밸런스 수치 확정");
                if (entry.PublishPropertyPaths.Length > 0)
                {
                    CopyPublishedProperties(
                        working,
                        source,
                        entry.PublishPropertyPaths);
                }
                else
                {
                    EditorUtility.CopySerialized(working, source);
                }
                source.name = originalName;
                EditorUtility.SetDirty(source);
            }

            AssetDatabase.SaveAssets();
            ShowNotification(new GUIContent("확정 수치를 실제 에셋에 반영했습니다."));
            Debug.Log(
                "[Balance] 작업용 밸런스 수치를 실제 설정 에셋에 반영했습니다. " +
                "통합 Scene은 변경하지 않았습니다.");
        }

        private void DestroyCachedEditor()
        {
            if (cachedAssetEditor != null)
            {
                DestroyImmediate(cachedAssetEditor);
            }

            cachedAssetEditor = null;
            cachedTarget = null;
        }
    }
}
#endif
