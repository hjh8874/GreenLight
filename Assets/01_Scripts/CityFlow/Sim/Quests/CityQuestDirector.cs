using System;
using System.Collections.Generic;

namespace CityFlow.Sim.Quests
{
    public enum CityQuestId
    {
        ShortcutCamera,
        ShortcutPlacement,
        ShortcutVehicle,
        ShortcutWindow,
        BuildRoad,
        BuildHouse,
        BuildOffice,
        ConnectCommute,
        HarvestFirstIncome,
        PrepareSchoolResearch,
        BuildHousing,
        AddOfficeCapacity,
        BuildSignal,
        BuildRoundabout,
        BuildBusStop,
        BuildSchool,
        ResolveCongestion,
        HarvestSavings,
        StartResearch,
        CompleteResearch,
        BuildUnlockedFacility,
        BuildHospital,
        ExpandCity
    }

    public sealed class CityQuestPresentation
    {
        public CityQuestId Id { get; }
        public string Title { get; }
        public string Message { get; }
        public int Priority { get; }
        public bool CanAcknowledge { get; }
        public string ActionLabel { get; }

        internal CityQuestPresentation(
            CityQuestId id,
            string title,
            string message,
            int priority,
            bool canAcknowledge = false,
            string actionLabel = "")
        {
            Id = id;
            Title = title;
            Message = message;
            Priority = priority;
            CanAcknowledge = canAcknowledge;
            ActionLabel = actionLabel ?? string.Empty;
        }
    }

    public readonly struct CityQuestSnapshot
    {
        public readonly int RoadCount;
        public readonly int HouseCount;
        public readonly int OfficeCount;
        public readonly int SchoolCount;
        public readonly bool HasConnectedCommute;
        public readonly long TotalArrivals;
        public readonly long PendingCoins;
        public readonly bool HasHarvested;
        public readonly int JamTileCount;
        public readonly int HospitalCount;
        public readonly string ReadyResearchId;
        public readonly string ActiveResearchId;
        public readonly string UnbuiltSpecialBuildingId;
        public readonly bool IsSchoolResearchUnlocked;
        public readonly bool IsHospitalResearchUnlocked;
        public readonly int SignalCount;
        public readonly int RoundaboutCount;
        public readonly int BusStopCount;
        public readonly bool HasIntersectionFacilityService;
        public readonly bool HasBusStopInfrastructureService;
        public readonly bool IsBusOperating;

        public CityQuestSnapshot(
            int roadCount,
            int houseCount,
            int officeCount,
            int schoolCount,
            long totalArrivals,
            long pendingCoins,
            bool hasHarvested,
            int jamTileCount,
            bool hasConnectedCommute = false,
            int hospitalCount = 0,
            string readyResearchId = "",
            string activeResearchId = "",
            string unbuiltSpecialBuildingId = "",
            bool isSchoolResearchUnlocked = false,
            bool isHospitalResearchUnlocked = false,
            int signalCount = 0,
            int roundaboutCount = 0,
            int busStopCount = 0,
            bool hasIntersectionFacilityService = false,
            bool hasBusStopInfrastructureService = false,
            bool isBusOperating = false)
        {
            RoadCount = Math.Max(0, roadCount);
            HouseCount = Math.Max(0, houseCount);
            OfficeCount = Math.Max(0, officeCount);
            SchoolCount = Math.Max(0, schoolCount);
            HasConnectedCommute = hasConnectedCommute;
            TotalArrivals = Math.Max(0L, totalArrivals);
            PendingCoins = Math.Max(0L, pendingCoins);
            HasHarvested = hasHarvested;
            JamTileCount = Math.Max(0, jamTileCount);
            HospitalCount = Math.Max(0, hospitalCount);
            ReadyResearchId = readyResearchId ?? string.Empty;
            ActiveResearchId = activeResearchId ?? string.Empty;
            UnbuiltSpecialBuildingId = unbuiltSpecialBuildingId ?? string.Empty;
            IsSchoolResearchUnlocked = isSchoolResearchUnlocked;
            IsHospitalResearchUnlocked = isHospitalResearchUnlocked;
            SignalCount = Math.Max(0, signalCount);
            RoundaboutCount = Math.Max(0, roundaboutCount);
            BusStopCount = Math.Max(0, busStopCount);
            HasIntersectionFacilityService = hasIntersectionFacilityService;
            HasBusStopInfrastructureService = hasBusStopInfrastructureService;
            IsBusOperating = isBusOperating;
        }
    }

    public sealed class CityQuestDirector
    {
        private sealed class QuestDefinition
        {
            public readonly CityQuestPresentation Presentation;
            public readonly float RequiredSeconds;
            public readonly float CooldownSeconds;
            public readonly string TargetKey;
            public readonly int TargetValue;

            public QuestDefinition(
                CityQuestId id,
                string title,
                string message,
                int priority,
                float requiredSeconds,
                float cooldownSeconds,
                string targetKey = "",
                int targetValue = 0,
                bool canAcknowledge = false,
                string actionLabel = "")
            {
                Presentation = new CityQuestPresentation(
                    id,
                    title,
                    message,
                    priority,
                    canAcknowledge,
                    actionLabel);
                RequiredSeconds = requiredSeconds;
                CooldownSeconds = cooldownSeconds;
                TargetKey = targetKey ?? string.Empty;
                TargetValue = Math.Max(0, targetValue);
            }
        }

        public const int ShortcutGuideCount = 4;

        private static readonly QuestDefinition[] ShortcutGuideDefinitions =
        {
            new QuestDefinition(
                CityQuestId.ShortcutCamera,
                "조작 안내 1/4 · 카메라",
                "Tab: 평면/입체 전환  |  휠: 확대·축소\n가운데 버튼 드래그: 이동\n마우스 뒤/앞 버튼: 좌우 회전",
                300,
                0f,
                0f,
                targetValue: 0,
                canAcknowledge: true,
                actionLabel: "다음"),
            new QuestDefinition(
                CityQuestId.ShortcutPlacement,
                "조작 안내 2/4 · 건설",
                "좌클릭/드래그: 배치  |  우클릭: 철거\nESC: 선택·패널 닫기",
                300,
                0f,
                0f,
                targetValue: 0,
                canAcknowledge: true,
                actionLabel: "다음"),
            new QuestDefinition(
                CityQuestId.ShortcutVehicle,
                "조작 안내 3/4 · 차량 뷰",
                "도로를 주행 중인 차량 좌클릭: 차량 뷰 진입\nESC: 차량 뷰 종료",
                300,
                0f,
                0f,
                targetValue: 0,
                canAcknowledge: true,
                actionLabel: "다음"),
            new QuestDefinition(
                CityQuestId.ShortcutWindow,
                "조작 안내 4/4 · 창 모드",
                "1 / 2 / 3: 플로팅 창 크기 프리셋\n저장·불러오기 창도 ESC로 닫기",
                300,
                0f,
                0f,
                targetValue: 0,
                canAcknowledge: true,
                actionLabel: "시작")
        };

        private static readonly CityQuestId[] TutorialOrder =
        {
            CityQuestId.BuildRoad,
            CityQuestId.BuildHouse,
            CityQuestId.BuildOffice,
            CityQuestId.ConnectCommute,
            CityQuestId.HarvestFirstIncome
        };

        private static readonly QuestDefinition[] DynamicDefinitions =
        {
            new QuestDefinition(CityQuestId.ResolveCongestion, "도로가 너무 막혀요", "정체가 계속되고 있어요. 우회 도로를 만들거나 교통 흐름을 개선해 주세요.", 100, 10f, 120f),
            new QuestDefinition(CityQuestId.PrepareSchoolResearch, "학교 연구 조건을 준비하세요", "학교 연구를 열려면 집 3채와 회사 2곳이 필요해요. 주거지와 일자리를 더 늘려 주세요.", 95, 1f, 0f),
            new QuestDefinition(CityQuestId.AddOfficeCapacity, "새로운 일자리가 필요해요", "현재 거주지 수가 회사들의 통근 수용량을 초과했어요. 회사를 하나 더 지어 주세요.", 90, 5f, 120f),
            new QuestDefinition(CityQuestId.BuildSignal, "첫 신호등을 설치해 보세요", "교통 메뉴에서 도로가 3방향 이상 연결된 교차로에 신호등을 설치해 보세요.", 88, 2f, 0f),
            new QuestDefinition(CityQuestId.BuildRoundabout, "회전교차로를 만들어 보세요", "교통 메뉴에서 신호등이 없는 교차로에 회전교차로를 설치해 보세요.", 87, 2f, 0f),
            new QuestDefinition(CityQuestId.BuildBusStop, "버스 운행을 시작해 보세요", "버스는 도로로 연결된 정류장이 2개 이상 있어야 운행해요. 교통 메뉴에서 서로 연결된 직선 도로 옆에 버스정류장 2개를 설치해 보세요.", 86, 2f, 0f),
            new QuestDefinition(CityQuestId.BuildHousing, "살 곳이 부족해요", "일자리에 비해 거주지가 부족해요. 시민들이 살 집을 더 지어 주세요.", 85, 5f, 120f),
            new QuestDefinition(CityQuestId.BuildSchool, "학교가 필요해요", "도시의 가족들이 늘고 있어요. 아이들이 다닐 학교를 지어 주세요.", 80, 5f, 120f),
            new QuestDefinition(CityQuestId.BuildHospital, "병원이 필요해요", "병원 연구가 완료됐어요. 시민을 치료할 병원을 지어 주세요.", 79, 5f, 120f),
            new QuestDefinition(CityQuestId.BuildUnlockedFacility, "새 시설을 지어 보세요", "연구로 열린 새 건물을 건설 메뉴에서 배치해 보세요.", 75, 3f, 30f),
            new QuestDefinition(CityQuestId.StartResearch, "새 연구를 시작할 수 있어요", "연구 메뉴에서 건물·공공시설·지역 개척 연구를 선택해 시작해 보세요.", 70, 2f, 15f),
            new QuestDefinition(CityQuestId.CompleteResearch, "연구가 진행 중이에요", "게임 시간이 지나면 연구가 완료되고 새 건물이나 지역이 열려요.", 65, 2f, 15f),
            new QuestDefinition(CityQuestId.HarvestSavings, "수익이 쌓였어요", "도시에 수익이 많이 쌓였어요. HARVEST 버튼으로 재화를 수확해 주세요.", 40, 5f, 60f)
        };

        private readonly Dictionary<CityQuestId, float> eligibleSeconds = new();
        private readonly Dictionary<CityQuestId, float> cooldownSeconds = new();

        private QuestDefinition activeDefinition;
        private int tutorialIndex;
        private float nextQuestDelay;
        private bool useResumeMessages;
        private readonly bool showShortcutGuide;
        private int shortcutGuideIndex;

        public CityQuestPresentation ActiveQuest => activeDefinition?.Presentation;
        public bool IsMinimized { get; private set; }
        public bool IsTutorialComplete => tutorialIndex >= TutorialOrder.Length;
        public int TutorialStage => tutorialIndex;
        public int ShortcutGuideStage => shortcutGuideIndex;
        public bool IsShortcutGuideComplete =>
            !showShortcutGuide || shortcutGuideIndex >= ShortcutGuideCount;

        public CityQuestDirector(bool showShortcutGuide = false)
        {
            this.showShortcutGuide = showShortcutGuide;
            shortcutGuideIndex = showShortcutGuide ? 0 : ShortcutGuideCount;
        }

        public void SetResumeMode(bool isResumedSession)
        {
            useResumeMessages = isResumedSession;
        }

        public void RestoreTutorialStage(int stage)
        {
            tutorialIndex = Math.Max(0, Math.Min(TutorialOrder.Length, stage));
            activeDefinition = null;
            IsMinimized = false;
            nextQuestDelay = 0f;
            eligibleSeconds.Clear();
        }

        public void RestoreShortcutGuideStage(int stage)
        {
            shortcutGuideIndex = Math.Max(
                0,
                Math.Min(ShortcutGuideCount, stage));
            activeDefinition = null;
            IsMinimized = false;
            nextQuestDelay = 0f;
            eligibleSeconds.Clear();
        }

        public bool Tick(in CityQuestSnapshot snapshot, float deltaSeconds)
        {
            float delta = Math.Max(0f, deltaSeconds);
            UpdateCooldowns(delta);
            nextQuestDelay = Math.Max(0f, nextQuestDelay - delta);

            if (activeDefinition != null)
            {
                if (activeDefinition.Presentation.Id == CityQuestId.ExpandCity)
                {
                    QuestDefinition specificCandidate =
                        FindDynamicCandidate(snapshot, delta, out _);
                    if (specificCandidate != null)
                    {
                        Activate(specificCandidate);
                        return true;
                    }
                }

                if (!IsQuestComplete(activeDefinition, snapshot))
                {
                    return false;
                }

                CompleteActiveQuest();
                return true;
            }

            if (nextQuestDelay > 0f)
            {
                return false;
            }

            if (!IsShortcutGuideComplete)
            {
                Activate(ShortcutGuideDefinitions[shortcutGuideIndex]);
                return true;
            }

            SkipSatisfiedTutorials(snapshot);

            if (!IsTutorialComplete)
            {
                CityQuestId tutorialId = TutorialOrder[tutorialIndex];

                Activate(CreateTutorialDefinition(tutorialId, useResumeMessages));
                return true;
            }

            QuestDefinition candidate = FindDynamicCandidate(
                snapshot,
                delta,
                out bool hasEligibleSpecificQuest);

            if (candidate == null)
            {
                if (hasEligibleSpecificQuest)
                {
                    return false;
                }

                Activate(CreateGrowthMilestoneDefinition(snapshot));
                return true;
            }

            Activate(candidate);
            return true;
        }

        public bool Minimize()
        {
            if (activeDefinition == null || IsMinimized) return false;
            IsMinimized = true;
            return true;
        }

        public bool Restore()
        {
            if (activeDefinition == null || !IsMinimized) return false;
            IsMinimized = false;
            return true;
        }

        public bool Acknowledge()
        {
            if (activeDefinition == null ||
                !activeDefinition.Presentation.CanAcknowledge ||
                shortcutGuideIndex >= ShortcutGuideCount ||
                activeDefinition.Presentation.Id !=
                ShortcutGuideDefinitions[shortcutGuideIndex].Presentation.Id)
            {
                return false;
            }

            shortcutGuideIndex++;
            activeDefinition = null;
            IsMinimized = false;
            nextQuestDelay = 0f;
            return true;
        }

        private void Activate(QuestDefinition definition)
        {
            activeDefinition = definition;
            IsMinimized = false;
            eligibleSeconds.Clear();
        }

        // 퀘스트가 실제로 "달성되어" 끝난 순간만 알린다.
        // ViewStateChanged 로는 이걸 알 수 없다 — 그건 "지금 보여줄 퀘스트"가 바뀔 때마다
        // 울리므로 우선순위 끼어들기·세이브 복원에서도 발생한다.
        // 축하 연출을 그 신호에 걸면 엉뚱한 순간에 터진다.
        public event Action<CityQuestId> QuestCompleted;

        private void CompleteActiveQuest()
        {
            CityQuestId completedId = activeDefinition.Presentation.Id;

            if (!IsTutorialComplete && completedId == TutorialOrder[tutorialIndex])
            {
                tutorialIndex++;
            }
            else
            {
                cooldownSeconds[completedId] = activeDefinition.CooldownSeconds;
            }

            activeDefinition = null;
            IsMinimized = false;
            nextQuestDelay = 3f;
            QuestCompleted?.Invoke(completedId);
        }

        private void SkipSatisfiedTutorials(in CityQuestSnapshot snapshot)
        {
            while (!IsTutorialComplete && IsQuestComplete(TutorialOrder[tutorialIndex], snapshot))
            {
                tutorialIndex++;
            }
        }

        private QuestDefinition FindDynamicCandidate(
            in CityQuestSnapshot snapshot,
            float deltaSeconds,
            out bool hasEligibleSpecificQuest)
        {
            QuestDefinition best = null;
            hasEligibleSpecificQuest = false;

            foreach (QuestDefinition template in DynamicDefinitions)
            {
                CityQuestId id = template.Presentation.Id;
                bool coolingDown = cooldownSeconds.TryGetValue(id, out float cooldown) && cooldown > 0f;

                if (coolingDown || !IsDynamicQuestEligible(id, snapshot))
                {
                    eligibleSeconds[id] = 0f;
                    continue;
                }

                hasEligibleSpecificQuest = true;

                eligibleSeconds.TryGetValue(id, out float elapsed);
                elapsed += deltaSeconds;
                eligibleSeconds[id] = elapsed;

                if (elapsed >= template.RequiredSeconds
                    && (best == null || template.Presentation.Priority > best.Presentation.Priority))
                {
                    best = CreateDynamicDefinition(template, snapshot);
                }
            }

            return best;
        }

        private static QuestDefinition CreateGrowthMilestoneDefinition(
            in CityQuestSnapshot snapshot)
        {
            int currentBuildingCount = GetCoreBuildingCount(snapshot);
            int targetBuildingCount = Math.Max(
                5,
                ((currentBuildingCount / 5) + 1) * 5);

            return new QuestDefinition(
                CityQuestId.ExpandCity,
                "도시를 계속 성장시켜 보세요",
                $"집·회사·학교·병원을 모두 합쳐 {targetBuildingCount}곳까지 늘려 보세요.",
                10,
                0f,
                0f,
                targetValue: targetBuildingCount);
        }

        private static int GetCoreBuildingCount(
            in CityQuestSnapshot snapshot) =>
            snapshot.HouseCount +
            snapshot.OfficeCount +
            snapshot.SchoolCount +
            snapshot.HospitalCount;

        private static QuestDefinition CreateDynamicDefinition(
            QuestDefinition template,
            in CityQuestSnapshot snapshot)
        {
            string targetKey = template.Presentation.Id switch
            {
                CityQuestId.StartResearch => snapshot.ReadyResearchId,
                CityQuestId.CompleteResearch => snapshot.ActiveResearchId,
                CityQuestId.BuildUnlockedFacility =>
                    snapshot.UnbuiltSpecialBuildingId,
                _ => string.Empty
            };

            return new QuestDefinition(
                template.Presentation.Id,
                template.Presentation.Title,
                template.Presentation.Message,
                template.Presentation.Priority,
                template.RequiredSeconds,
                template.CooldownSeconds,
                targetKey);
        }

        private void UpdateCooldowns(float deltaSeconds)
        {
            if (cooldownSeconds.Count == 0 || deltaSeconds <= 0f) return;

            CityQuestId[] ids = new CityQuestId[cooldownSeconds.Count];
            cooldownSeconds.Keys.CopyTo(ids, 0);

            foreach (CityQuestId id in ids)
            {
                cooldownSeconds[id] = Math.Max(0f, cooldownSeconds[id] - deltaSeconds);
            }
        }

        private static QuestDefinition CreateTutorialDefinition(
            CityQuestId id,
            bool useResumeMessages)
        {
            if (useResumeMessages)
            {
                return id switch
                {
                    CityQuestId.BuildRoad => new QuestDefinition(id, "도로 건설을 계속해 주세요", "불러온 도시에는 아직 도로가 부족해요. 시민들이 이동할 수 있도록 도로를 3칸 이상 연결해 주세요.", 200, 0f, 0f),
                    CityQuestId.BuildHouse => new QuestDefinition(id, "주거지를 준비해 주세요", "불러온 도시에는 시민이 살 주거지가 아직 없어요. 도로 옆에 집을 지어 주세요.", 200, 0f, 0f),
                    CityQuestId.BuildOffice => new QuestDefinition(id, "일자리를 준비해 주세요", "불러온 도시에는 시민이 일할 회사가 아직 없어요. 도로 옆에 회사를 지어 주세요.", 200, 0f, 0f),
                    CityQuestId.ConnectCommute => new QuestDefinition(id, "출근길을 다시 확인해 주세요", "집과 회사 사이에 차량이 이동할 수 있도록 도로를 연결해 주세요.", 200, 0f, 0f),
                    CityQuestId.HarvestFirstIncome => new QuestDefinition(id, "첫 통근 수익을 받아 보세요", "집과 회사가 연결됐어요. 차량이 회사에 도착하면 수익이 쌓여요. HARVEST 버튼으로 첫 수익을 받아 주세요.", 200, 0f, 0f),
                    _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
                };
            }

            return id switch
            {
                CityQuestId.BuildRoad => new QuestDefinition(id, "이동할 길이 필요해요", "도시의 시작은 길이에요. 시민들이 이동할 수 있도록 도로를 3칸 이상 지어 주세요.", 200, 0f, 0f),
                CityQuestId.BuildHouse => new QuestDefinition(id, "시민들이 살 집이 필요해요", "도로 옆에 시민들이 머물 수 있는 주거지를 지어 주세요.", 200, 0f, 0f),
                CityQuestId.BuildOffice => new QuestDefinition(id, "일할 곳이 필요해요", "시민들이 일하고 도시가 수익을 얻을 수 있도록 회사를 지어 주세요.", 200, 0f, 0f),
                CityQuestId.ConnectCommute => new QuestDefinition(id, "출근길을 연결해 주세요", "집과 회사 사이에 차량이 이동할 수 있도록 도로를 연결해 주세요.", 200, 0f, 0f),
                CityQuestId.HarvestFirstIncome => new QuestDefinition(id, "첫 통근 수익을 받아 보세요", "집과 회사가 연결됐어요. 차량이 회사에 도착하면 수익이 쌓여요. HARVEST 버튼으로 첫 수익을 받아 주세요.", 200, 0f, 0f),
                _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
            };
        }

        private static bool IsQuestComplete(
            QuestDefinition definition,
            in CityQuestSnapshot snapshot)
        {
            CityQuestId id = definition.Presentation.Id;
            return id switch
            {
            CityQuestId.BuildRoad => snapshot.RoadCount >= 3,
            CityQuestId.BuildHouse => snapshot.HouseCount >= 1,
            CityQuestId.BuildOffice => snapshot.OfficeCount >= 1,
            CityQuestId.ConnectCommute => snapshot.HasConnectedCommute,
            CityQuestId.HarvestFirstIncome => snapshot.HasHarvested,
            CityQuestId.PrepareSchoolResearch =>
                snapshot.SchoolCount > 0 ||
                snapshot.IsSchoolResearchUnlocked ||
                (snapshot.HouseCount >= 3 && snapshot.OfficeCount >= 2) ||
                snapshot.ReadyResearchId.Length > 0 ||
                snapshot.ActiveResearchId.Length > 0,
            CityQuestId.BuildHousing => snapshot.HouseCount >= snapshot.OfficeCount,
            CityQuestId.AddOfficeCapacity => snapshot.OfficeCount > 0 && snapshot.HouseCount <= snapshot.OfficeCount * 6,
            CityQuestId.BuildSignal => snapshot.SignalCount > 0,
            CityQuestId.BuildRoundabout => snapshot.RoundaboutCount > 0,
            CityQuestId.BuildBusStop => snapshot.IsBusOperating,
            CityQuestId.BuildSchool => snapshot.SchoolCount > 0 && snapshot.HouseCount <= snapshot.SchoolCount * 10,
            CityQuestId.BuildHospital => snapshot.HospitalCount > 0,
            CityQuestId.StartResearch =>
                !string.Equals(
                    snapshot.ReadyResearchId,
                    definition.TargetKey,
                    StringComparison.Ordinal),
            CityQuestId.CompleteResearch =>
                !string.Equals(
                    snapshot.ActiveResearchId,
                    definition.TargetKey,
                    StringComparison.Ordinal),
            CityQuestId.BuildUnlockedFacility =>
                !string.Equals(
                    snapshot.UnbuiltSpecialBuildingId,
                    definition.TargetKey,
                    StringComparison.Ordinal),
            CityQuestId.ResolveCongestion => snapshot.JamTileCount == 0,
            CityQuestId.HarvestSavings => snapshot.PendingCoins == 0,
            CityQuestId.ExpandCity =>
                GetCoreBuildingCount(snapshot) >= definition.TargetValue,
            _ => false
            };
        }

        private static bool IsQuestComplete(
            CityQuestId id,
            in CityQuestSnapshot snapshot) =>
            IsQuestComplete(
                new QuestDefinition(
                    id,
                    string.Empty,
                    string.Empty,
                    0,
                    0f,
                    0f),
                snapshot);

        private static bool IsDynamicQuestEligible(CityQuestId id, in CityQuestSnapshot snapshot) => id switch
        {
            CityQuestId.PrepareSchoolResearch =>
                snapshot.SchoolCount == 0 &&
                !snapshot.IsSchoolResearchUnlocked &&
                !snapshot.IsHospitalResearchUnlocked &&
                snapshot.UnbuiltSpecialBuildingId.Length == 0 &&
                snapshot.ReadyResearchId.Length == 0 &&
                snapshot.ActiveResearchId.Length == 0 &&
                snapshot.JamTileCount == 0 &&
                (snapshot.HouseCount < 3 || snapshot.OfficeCount < 2),
            CityQuestId.BuildHousing => snapshot.OfficeCount > snapshot.HouseCount,
            CityQuestId.AddOfficeCapacity => snapshot.OfficeCount > 0 && snapshot.HouseCount > snapshot.OfficeCount * 6,
            CityQuestId.BuildSignal =>
                snapshot.HasIntersectionFacilityService &&
                snapshot.HouseCount >= 3 &&
                snapshot.OfficeCount >= 2 &&
                snapshot.SignalCount == 0,
            CityQuestId.BuildRoundabout =>
                snapshot.HasIntersectionFacilityService &&
                snapshot.HouseCount >= 3 &&
                snapshot.OfficeCount >= 2 &&
                snapshot.SignalCount > 0 &&
                snapshot.RoundaboutCount == 0,
            CityQuestId.BuildBusStop =>
                snapshot.HasBusStopInfrastructureService &&
                snapshot.HouseCount >= 3 &&
                snapshot.OfficeCount >= 2 &&
                snapshot.RoundaboutCount > 0 &&
                !snapshot.IsBusOperating,
            CityQuestId.BuildSchool =>
                snapshot.IsSchoolResearchUnlocked &&
                snapshot.HouseCount >= 2 &&
                (snapshot.SchoolCount == 0 ||
                 snapshot.HouseCount > snapshot.SchoolCount * 10),
            CityQuestId.BuildHospital =>
                snapshot.IsHospitalResearchUnlocked &&
                snapshot.HospitalCount == 0,
            CityQuestId.StartResearch =>
                snapshot.ReadyResearchId.Length > 0 &&
                snapshot.ActiveResearchId.Length == 0,
            CityQuestId.CompleteResearch =>
                snapshot.ActiveResearchId.Length > 0,
            CityQuestId.BuildUnlockedFacility =>
                snapshot.UnbuiltSpecialBuildingId.Length > 0,
            CityQuestId.ResolveCongestion => snapshot.JamTileCount > 0,
            CityQuestId.HarvestSavings => snapshot.PendingCoins >= 100,
            _ => false
        };


    }
}
