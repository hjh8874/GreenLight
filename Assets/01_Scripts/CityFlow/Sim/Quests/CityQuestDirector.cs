using System;
using System.Collections.Generic;

namespace CityFlow.Sim.Quests
{
    public enum CityQuestId
    {
        BuildRoad,
        BuildHouse,
        BuildOffice,
        ConnectCommute,
        HarvestFirstIncome,
        BuildHousing,
        AddOfficeCapacity,
        BuildSchool,
        ResolveCongestion,
        HarvestSavings
    }

    public sealed class CityQuestPresentation
    {
        public CityQuestId Id { get; }
        public string Title { get; }
        public string Message { get; }
        public int Priority { get; }

        internal CityQuestPresentation(CityQuestId id, string title, string message, int priority)
        {
            Id = id;
            Title = title;
            Message = message;
            Priority = priority;
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

        public CityQuestSnapshot(
            int roadCount,
            int houseCount,
            int officeCount,
            int schoolCount,
            long totalArrivals,
            long pendingCoins,
            bool hasHarvested,
            int jamTileCount,
            bool hasConnectedCommute = false)
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
        }
    }

    public sealed class CityQuestDirector
    {
        private sealed class QuestDefinition
        {
            public readonly CityQuestPresentation Presentation;
            public readonly float RequiredSeconds;
            public readonly float CooldownSeconds;

            public QuestDefinition(
                CityQuestId id,
                string title,
                string message,
                int priority,
                float requiredSeconds,
                float cooldownSeconds)
            {
                Presentation = new CityQuestPresentation(id, title, message, priority);
                RequiredSeconds = requiredSeconds;
                CooldownSeconds = cooldownSeconds;
            }
        }

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
            new QuestDefinition(CityQuestId.AddOfficeCapacity, "새로운 일자리가 필요해요", "회사와 연결할 수 있는 거주지가 가득 찼어요. 회사를 하나 더 지어 주세요.", 90, 5f, 120f),
            new QuestDefinition(CityQuestId.BuildHousing, "살 곳이 부족해요", "일자리에 비해 거주지가 부족해요. 시민들이 살 집을 더 지어 주세요.", 85, 5f, 120f),
            new QuestDefinition(CityQuestId.BuildSchool, "학교가 필요해요", "도시의 가족들이 늘고 있어요. 아이들이 다닐 학교를 지어 주세요.", 80, 5f, 120f),
            new QuestDefinition(CityQuestId.HarvestSavings, "수익이 쌓였어요", "도시에 수익이 많이 쌓였어요. HARVEST 버튼으로 재화를 수확해 주세요.", 40, 5f, 60f)
        };

        private readonly Dictionary<CityQuestId, float> eligibleSeconds = new();
        private readonly Dictionary<CityQuestId, float> cooldownSeconds = new();

        private QuestDefinition activeDefinition;
        private int tutorialIndex;
        private float nextQuestDelay;
        private bool useResumeMessages;

        public CityQuestPresentation ActiveQuest => activeDefinition?.Presentation;
        public bool IsMinimized { get; private set; }
        public bool IsTutorialComplete => tutorialIndex >= TutorialOrder.Length;
        public int TutorialStage => tutorialIndex;

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

        public bool Tick(in CityQuestSnapshot snapshot, float deltaSeconds)
        {
            float delta = Math.Max(0f, deltaSeconds);
            UpdateCooldowns(delta);
            nextQuestDelay = Math.Max(0f, nextQuestDelay - delta);

            if (activeDefinition != null)
            {
                if (!IsQuestComplete(activeDefinition.Presentation.Id, snapshot))
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

            SkipSatisfiedTutorials(snapshot);

            if (!IsTutorialComplete)
            {
                CityQuestId tutorialId = TutorialOrder[tutorialIndex];

                if (!CanActivateTutorial(tutorialId, snapshot))
                {
                    return false;
                }

                Activate(CreateTutorialDefinition(tutorialId, useResumeMessages));
                return true;
            }

            QuestDefinition candidate = FindDynamicCandidate(snapshot, delta);

            if (candidate == null)
            {
                return false;
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

        private void Activate(QuestDefinition definition)
        {
            activeDefinition = definition;
            IsMinimized = false;
            eligibleSeconds.Clear();
        }

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
        }

        private void SkipSatisfiedTutorials(in CityQuestSnapshot snapshot)
        {
            while (!IsTutorialComplete && IsQuestComplete(TutorialOrder[tutorialIndex], snapshot))
            {
                tutorialIndex++;
            }
        }

        private QuestDefinition FindDynamicCandidate(in CityQuestSnapshot snapshot, float deltaSeconds)
        {
            QuestDefinition best = null;

            foreach (QuestDefinition definition in DynamicDefinitions)
            {
                CityQuestId id = definition.Presentation.Id;
                bool coolingDown = cooldownSeconds.TryGetValue(id, out float cooldown) && cooldown > 0f;

                if (coolingDown || !IsDynamicQuestEligible(id, snapshot))
                {
                    eligibleSeconds[id] = 0f;
                    continue;
                }

                eligibleSeconds.TryGetValue(id, out float elapsed);
                elapsed += deltaSeconds;
                eligibleSeconds[id] = elapsed;

                if (elapsed >= definition.RequiredSeconds
                    && (best == null || definition.Presentation.Priority > best.Presentation.Priority))
                {
                    best = definition;
                }
            }

            return best;
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
                    CityQuestId.HarvestFirstIncome => new QuestDefinition(id, "쌓인 수익을 수확해 주세요", "도착 수익이 대기 중이에요. HARVEST 버튼을 눌러 재화를 받아 주세요.", 200, 0f, 0f),
                    _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
                };
            }

            return id switch
            {
                CityQuestId.BuildRoad => new QuestDefinition(id, "이동할 길이 필요해요", "도시의 시작은 길이에요. 시민들이 이동할 수 있도록 도로를 3칸 이상 지어 주세요.", 200, 0f, 0f),
                CityQuestId.BuildHouse => new QuestDefinition(id, "시민들이 살 집이 필요해요", "도로 옆에 시민들이 머물 수 있는 주거지를 지어 주세요.", 200, 0f, 0f),
                CityQuestId.BuildOffice => new QuestDefinition(id, "일할 곳이 필요해요", "시민들이 일하고 도시가 수익을 얻을 수 있도록 회사를 지어 주세요.", 200, 0f, 0f),
                CityQuestId.ConnectCommute => new QuestDefinition(id, "출근길을 연결해 주세요", "집과 회사 사이에 차량이 이동할 수 있도록 도로를 연결해 주세요.", 200, 0f, 0f),
                CityQuestId.HarvestFirstIncome => new QuestDefinition(id, "첫 수익을 수확해 보세요", "첫 통근 수익이 생겼어요. HARVEST 버튼을 눌러 재화를 받아 보세요.", 200, 0f, 0f),
                _ => throw new ArgumentOutOfRangeException(nameof(id), id, null)
            };
        }

        private static bool CanActivateTutorial(
            CityQuestId id,
            in CityQuestSnapshot snapshot)
        {
            return id != CityQuestId.HarvestFirstIncome
                || snapshot.PendingCoins > 0L
                || snapshot.HasHarvested;
        }

        private static bool IsQuestComplete(CityQuestId id, in CityQuestSnapshot snapshot) => id switch
        {
            CityQuestId.BuildRoad => snapshot.RoadCount >= 3,
            CityQuestId.BuildHouse => snapshot.HouseCount >= 1,
            CityQuestId.BuildOffice => snapshot.OfficeCount >= 1,
            CityQuestId.ConnectCommute => snapshot.HasConnectedCommute,
            CityQuestId.HarvestFirstIncome => snapshot.HasHarvested,
            CityQuestId.BuildHousing => snapshot.HouseCount >= snapshot.OfficeCount,
            CityQuestId.AddOfficeCapacity => snapshot.OfficeCount > 0 && snapshot.HouseCount <= snapshot.OfficeCount * 6,
            CityQuestId.BuildSchool => snapshot.SchoolCount > 0 && snapshot.HouseCount <= snapshot.SchoolCount * 10,
            CityQuestId.ResolveCongestion => snapshot.JamTileCount == 0,
            CityQuestId.HarvestSavings => snapshot.PendingCoins == 0,
            _ => false
        };

        private static bool IsDynamicQuestEligible(CityQuestId id, in CityQuestSnapshot snapshot) => id switch
        {
            CityQuestId.BuildHousing => snapshot.OfficeCount > snapshot.HouseCount,
            CityQuestId.AddOfficeCapacity => snapshot.OfficeCount > 0 && snapshot.HouseCount > snapshot.OfficeCount * 6,
            CityQuestId.BuildSchool => snapshot.HouseCount >= 2 && (snapshot.SchoolCount == 0 || snapshot.HouseCount > snapshot.SchoolCount * 10),
            CityQuestId.ResolveCongestion => snapshot.JamTileCount > 0,
            CityQuestId.HarvestSavings => snapshot.PendingCoins >= 100,
            _ => false
        };


    }
}
