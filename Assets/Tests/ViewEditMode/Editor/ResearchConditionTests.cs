using System;
using System.Collections.Generic;
using System.IO;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.Gameplay.Research;
using CityFlow.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

// 기본 에디터 어셈블리. 실행: run_tests(group_names=[".*ResearchConditionTests.*"])
public class ResearchConditionTests
{
    static ResearchEntry Entry(string id, ResearchConditionKind kind, int threshold,
        TileType target = TileType.Empty) =>
        new ResearchEntry { researchId = id, displayName = id,
            conditionKind = kind, threshold = threshold, targetTileType = target };

    static ResearchConditionInputs Inputs(int arrivals = 0, int population = 0,
        int schools = 0, int hospitals = 0) =>
        new ResearchConditionInputs(arrivals, population,
            t => t == TileType.School ? schools : t == TileType.Hospital ? hospitals : 0);

    [Test]
    public void DailyArrivals_IsSatisfiedAtThreshold_HalfOpenBelow()
    {
        var e = Entry("a", ResearchConditionKind.DailyArrivals, 60);
        Assert.IsFalse(ResearchConditionEvaluator.IsSatisfied(e, Inputs(arrivals: 59)));
        Assert.IsTrue(ResearchConditionEvaluator.IsSatisfied(e, Inputs(arrivals: 60)), "경계 = 충족");
        Assert.IsTrue(ResearchConditionEvaluator.IsSatisfied(e, Inputs(arrivals: 200)));
    }

    [Test]
    public void Population_And_BuildingCount_ReadTheirOwnInputs()
    {
        var pop = Entry("p", ResearchConditionKind.Population, 20);
        Assert.IsFalse(ResearchConditionEvaluator.IsSatisfied(pop, Inputs(population: 19)));
        Assert.IsTrue(ResearchConditionEvaluator.IsSatisfied(pop, Inputs(population: 20)));

        var school = Entry("s", ResearchConditionKind.BuildingCount, 1, TileType.School);
        Assert.IsFalse(ResearchConditionEvaluator.IsSatisfied(school, Inputs(schools: 0)));
        Assert.IsTrue(ResearchConditionEvaluator.IsSatisfied(school, Inputs(schools: 1)));
        Assert.IsFalse(ResearchConditionEvaluator.IsSatisfied(school, Inputs(hospitals: 3)),
            "다른 타일 개수는 안 센다");
    }

    [Test]
    public void MultipleRequirements_MustAllBeSatisfied()
    {
        var entry = new ResearchEntry
        {
            researchId = "school",
            requirements = new List<ResearchRequirement>
            {
                new()
                {
                    conditionKind = ResearchConditionKind.BuildingCount,
                    threshold = 3,
                    targetTileType = TileType.House
                },
                new()
                {
                    conditionKind = ResearchConditionKind.BuildingCount,
                    threshold = 2,
                    targetTileType = TileType.Office
                }
            }
        };

        ResearchConditionInputs missingOffice =
            new(0, 0, type =>
                type == TileType.House ? 3 :
                type == TileType.Office ? 1 : 0);
        ResearchConditionInputs allSatisfied =
            new(0, 0, type =>
                type == TileType.House ? 3 :
                type == TileType.Office ? 2 : 0);

        Assert.IsFalse(
            ResearchConditionEvaluator.IsSatisfied(
                entry,
                missingOffice));
        Assert.IsTrue(
            ResearchConditionEvaluator.IsSatisfied(
                entry,
                allSatisfied));
    }

    [Test]
    public void CurrentValue_ReturnsTheConditionSourceValue()
    {
        Assert.AreEqual(131, ResearchConditionEvaluator.CurrentValue(
            Entry("a", ResearchConditionKind.DailyArrivals, 150), Inputs(arrivals: 131)));
        Assert.AreEqual(84, ResearchConditionEvaluator.CurrentValue(
            Entry("p", ResearchConditionKind.Population, 80), Inputs(population: 84)));
    }

    [Test]
    public void Catalog_ValidEntries_WarnsAndSkips_EmptyAndDuplicateIds()
    {
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        var so = new UnityEditor.SerializedObject(catalog);
        var list = so.FindProperty("entries");
        list.arraySize = 3;
        // [0] 정상, [1] 빈 id, [2] 중복 id — managed reference 가 아니라 plain serializable 이므로
        // 자식 프로퍼티를 직접 채운다
        SetEntry(list.GetArrayElementAtIndex(0), "research_a");
        SetEntry(list.GetArrayElementAtIndex(1), "  ");
        SetEntry(list.GetArrayElementAtIndex(2), "research_a");
        so.ApplyModifiedPropertiesWithoutUndo();

        UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
            new System.Text.RegularExpressions.Regex("id"));
        UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
            new System.Text.RegularExpressions.Regex("중복"));
        List<ResearchEntry> valid = catalog.ValidEntries();
        Assert.AreEqual(1, valid.Count);
        Assert.AreEqual("research_a", valid[0].researchId);
    }

    static void SetEntry(UnityEditor.SerializedProperty p, string id)
    {
        p.FindPropertyRelative("researchId").stringValue = id;
        p.FindPropertyRelative("displayName").stringValue = id;
        p.FindPropertyRelative("threshold").intValue = 1;
    }

    [Test]
    public void EvaluatePendingResearch_MarksSatisfiedReady_SkipsLocked_NeverRelocks()
    {
        var owner = new GameObject("research");
        try
        {
            var service = owner.AddComponent<ResearchUnlockService>();
            var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
            ConfigureCatalog(catalog,
                ("research_pop20", ResearchConditionKind.Population, 20),
                ("research_arr60", ResearchConditionKind.DailyArrivals, 60));
            SetPrivateField(service, "catalog", catalog);

            var services = new CityFlowServices(new SimEventHub(), null, null);
            service.Initialize(services);

            var unlocked = new List<string>();
            service.ResearchUnlocked += id => unlocked.Add(id);

            // 인구만 충족하는 입력을 주입해 평가
            SetTestInputs(service, population: 25, arrivals: 10);
            service.EvaluatePendingResearch();
            CollectionAssert.IsEmpty(unlocked);
            Assert.IsTrue(service.IsReady("research_pop20"));
            Assert.IsFalse(service.IsUnlocked("research_pop20"));
            Assert.IsFalse(service.IsUnlocked("research_arr60"), "미달 조건은 잠긴 채");

            // 재평가 — ready 상태만 다시 계산하고 이벤트는 내지 않는다
            service.EvaluatePendingResearch();
            Assert.AreEqual(0, unlocked.Count, "자동 해금 금지");

            Assert.IsTrue(service.TryUnlock("research_pop20"));
            Assert.AreEqual(1, unlocked.Count, "수동 해금 이벤트 1회");

            // 통행량 충족 → 남은 것 해금
            SetTestInputs(service, population: 25, arrivals: 60);
            service.EvaluatePendingResearch();
            Assert.IsTrue(service.IsReady("research_arr60"));
            Assert.IsFalse(service.IsUnlocked("research_arr60"));
        }
        finally { Object.DestroyImmediate(owner); }
    }

    [Test]
    public void FinalizedArrivals_UpdateUnlocksOnlyAfterStatsValueChanges()
    {
        var owner = new GameObject("research");
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        try
        {
            var service = owner.AddComponent<ResearchUnlockService>();
            ConfigureCatalog(catalog,
                ("research_arr60", ResearchConditionKind.DailyArrivals, 60));
            SetPrivateField(service, "catalog", catalog);

            var stats = new FakeStats { LastDayArrivalCount = 59 };
            var services = new CityFlowServices(
                new SimEventHub(), null, null, stats: stats);
            service.Initialize(services);

            InvokeUpdate(service);
            Assert.IsFalse(service.IsUnlocked("research_arr60"),
                "어제 확정 통행량이 임계값 미달이면 잠긴 채여야 한다");

            stats.LastDayArrivalCount = 60;
            InvokeUpdate(service);
            Assert.IsTrue(service.IsReady("research_arr60"),
                "통계 확정값이 바뀐 뒤 Update에서 해금 가능해야 한다");
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void CompletedExpansionResearch_UnlocksConfiguredWorldStage()
    {
        var owner = new GameObject("expansion_research");
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        try
        {
            var service = owner.AddComponent<ResearchUnlockService>();
            ConfigureExpansionCatalog(
                catalog,
                "research_expansion_center_040",
                "center_040");
            SetPrivateField(service, "catalog", catalog);

            var services = new CityFlowServices(
                new SimEventHub(), null, null);
            var expansion = new FakeWorldGridExpansion();
            Assert.IsTrue(services.RegisterWorldGridExpansion(expansion));
            service.Initialize(services);
            SetTestInputs(service, population: 10, arrivals: 0);
            service.EvaluatePendingResearch();

            Assert.IsTrue(service.TryStartResearch(
                "research_expansion_center_040"));
            CollectionAssert.AreEqual(
                new[] { "center_040" },
                expansion.RequestedStages);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void ExpansionServiceRegisteredLater_AppliesCompletedResearchReward()
    {
        var owner = new GameObject("deferred_expansion_research");
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        try
        {
            var service = owner.AddComponent<ResearchUnlockService>();
            ConfigureExpansionCatalog(
                catalog,
                "research_expansion_center_040",
                "center_040");
            SetPrivateField(service, "catalog", catalog);

            var services = new CityFlowServices(
                new SimEventHub(), null, null);
            service.Initialize(services);
            SetTestInputs(service, population: 10, arrivals: 0);
            service.EvaluatePendingResearch();
            Assert.IsTrue(service.TryStartResearch(
                "research_expansion_center_040"));

            var expansion = new FakeWorldGridExpansion();
            Assert.IsTrue(services.RegisterWorldGridExpansion(expansion));
            CollectionAssert.AreEqual(
                new[] { "center_040" },
                expansion.RequestedStages);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void FailedExpansionReward_LogsResearchAndStage()
    {
        var owner = new GameObject("failed_expansion_research");
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        try
        {
            var service = owner.AddComponent<ResearchUnlockService>();
            ConfigureExpansionCatalog(
                catalog,
                "research_expansion_center_040",
                "center_040");
            SetPrivateField(service, "catalog", catalog);

            var services = new CityFlowServices(
                new SimEventHub(), null, null);
            var expansion = new FakeWorldGridExpansion
            {
                RejectStageRequests = true
            };
            Assert.IsTrue(services.RegisterWorldGridExpansion(expansion));
            service.Initialize(services);
            SetTestInputs(service, population: 10, arrivals: 0);
            service.EvaluatePendingResearch();

            LogAssert.Expect(
                LogType.Warning,
                "[ResearchUnlockService] Failed to apply expansion reward. " +
                "Research=research_expansion_center_040, Stage=center_040.");
            Assert.IsTrue(service.TryStartResearch(
                "research_expansion_center_040"));
            CollectionAssert.AreEqual(
                new[] { "center_040" },
                expansion.RequestedStages);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void AlreadyUnlockedExpansionReward_DoesNotRequestStageAgain()
    {
        var owner = new GameObject("existing_expansion_research");
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        try
        {
            var service = owner.AddComponent<ResearchUnlockService>();
            ConfigureExpansionCatalog(
                catalog,
                "research_expansion_center_040",
                "center_040");
            SetPrivateField(service, "catalog", catalog);

            var services = new CityFlowServices(
                new SimEventHub(), null, null);
            var expansion = new FakeWorldGridExpansion();
            expansion.MarkStageUnlocked("center_040", 1);
            Assert.IsTrue(services.RegisterWorldGridExpansion(expansion));
            service.Initialize(services);
            SetTestInputs(service, population: 10, arrivals: 0);
            service.EvaluatePendingResearch();

            Assert.IsTrue(service.TryStartResearch(
                "research_expansion_center_040"));
            Assert.IsEmpty(expansion.RequestedStages);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void RestoredExpansionResearch_AppliesConfiguredWorldStage()
    {
        var owner = new GameObject("restored_expansion_research");
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        try
        {
            var service = owner.AddComponent<ResearchUnlockService>();
            ConfigureExpansionCatalog(
                catalog,
                "research_expansion_center_040",
                "center_040");
            SetPrivateField(service, "catalog", catalog);

            var services = new CityFlowServices(
                new SimEventHub(), null, null);
            var expansion = new FakeWorldGridExpansion();
            Assert.IsTrue(services.RegisterWorldGridExpansion(expansion));
            service.Initialize(services);

            service.RestoreSnapshot(new ResearchSaveData
            {
                UnlockedResearchIds = new[]
                {
                    "research_expansion_center_040"
                }
            });

            Assert.IsTrue(service.IsUnlocked(
                "research_expansion_center_040"));
            CollectionAssert.AreEqual(
                new[] { "center_040" },
                expansion.RequestedStages);
            Assert.IsTrue(expansion.IsStageUnlocked("center_040"));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void LegacyResearchSnapshot_WithMissingFields_UsesSafeDefaults()
    {
        var owner = new GameObject("legacy_research_snapshot");
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        try
        {
            var service = owner.AddComponent<ResearchUnlockService>();
            ConfigureExpansionCatalog(
                catalog,
                "research_expansion_center_040",
                "center_040");
            SetPrivateField(service, "catalog", catalog);
            service.Initialize(new CityFlowServices(
                new SimEventHub(), null, null));

            Assert.DoesNotThrow(() =>
                service.RestoreSnapshot(new ResearchSaveData()));
            Assert.AreEqual(0, service.UnlockedCount);
            Assert.AreEqual(string.Empty, service.ActiveResearchId);
            Assert.AreEqual(
                0L,
                service.CreateSnapshot().ResearchCompletionGameHour);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void SaveServiceReload_RestoresExpansionAfterResearchRegisters()
    {
        string id = Guid.NewGuid().ToString("N");
        string savePath = Path.Combine(
            Path.GetTempPath(), $"research_expansion_{id}.json");
        string backupPath = Path.Combine(
            Path.GetTempPath(), $"research_expansion_{id}.backup");
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        var sourceOwner = new GameObject("source_research");
        var restoredOwner = new GameObject("restored_research");
        try
        {
            ConfigureExpansionCatalog(
                catalog,
                "research_expansion_center_040",
                "center_040");
            var repository = new JsonSaveRepository(savePath, backupPath);

            var sourceSave = new SaveService(
                new FakeSimSaveSource(),
                repository,
                new FakeSaveClock());
            var sourceServices = new CityFlowServices(
                new SimEventHub(), null, null, sourceSave);
            var sourceExpansion = new FakeWorldGridExpansion();
            Assert.IsTrue(
                sourceServices.RegisterWorldGridExpansion(sourceExpansion));
            var sourceResearch =
                sourceOwner.AddComponent<ResearchUnlockService>();
            SetPrivateField(sourceResearch, "catalog", catalog);
            sourceResearch.Initialize(sourceServices);
            SetTestInputs(sourceResearch, population: 10, arrivals: 0);
            sourceResearch.EvaluatePendingResearch();
            Assert.IsTrue(sourceResearch.TryStartResearch(
                "research_expansion_center_040"));
            Assert.IsTrue(sourceSave.Save());

            var restoredSave = new SaveService(
                new FakeSimSaveSource(),
                repository,
                new FakeSaveClock());
            var restoredServices = new CityFlowServices(
                new SimEventHub(), null, null, restoredSave);
            var restoredExpansion = new FakeWorldGridExpansion();
            Assert.IsTrue(restoredServices.RegisterWorldGridExpansion(
                restoredExpansion));

            Assert.IsTrue(restoredSave.TryLoadAndRestore());
            var restoredResearch =
                restoredOwner.AddComponent<ResearchUnlockService>();
            SetPrivateField(restoredResearch, "catalog", catalog);
            restoredResearch.Initialize(restoredServices);

            Assert.IsTrue(restoredResearch.IsUnlocked(
                "research_expansion_center_040"));
            Assert.IsTrue(restoredExpansion.IsStageUnlocked("center_040"));
            CollectionAssert.AreEqual(
                new[] { "center_040" },
                restoredExpansion.RequestedStages);
        }
        finally
        {
            Object.DestroyImmediate(sourceOwner);
            Object.DestroyImmediate(restoredOwner);
            Object.DestroyImmediate(catalog);
            DeleteTemporarySave(savePath);
            DeleteTemporarySave(backupPath);
            DeleteTemporarySave($"{savePath}.tmp");
            DeleteTemporarySave($"{backupPath}.fallback");
        }
    }

    [Test]
    public void SaveServiceReload_LegacyMissingResearchFieldsUseSafeDefaults()
    {
        string id = Guid.NewGuid().ToString("N");
        string savePath = Path.Combine(
            Path.GetTempPath(), $"legacy_research_{id}.json");
        string backupPath = Path.Combine(
            Path.GetTempPath(), $"legacy_research_{id}.backup");
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        var owner = new GameObject("legacy_restored_research");
        try
        {
            ConfigureExpansionCatalog(
                catalog,
                "research_expansion_center_040",
                "center_040");
            var repository = new JsonSaveRepository(savePath, backupPath);
            Assert.IsTrue(repository.TrySave(new GameSaveData
            {
                SaveVersion = SaveConstants.CurrentSaveVersion,
                Simulation = new SimSaveData(),
                Research = new ResearchSaveData()
            }));

            var save = new SaveService(
                new FakeSimSaveSource(),
                repository,
                new FakeSaveClock());
            var services = new CityFlowServices(
                new SimEventHub(), null, null, save);
            Assert.IsTrue(save.TryLoadAndRestore());

            var research = owner.AddComponent<ResearchUnlockService>();
            SetPrivateField(research, "catalog", catalog);
            Assert.DoesNotThrow(() => research.Initialize(services));
            Assert.AreEqual(0, research.UnlockedCount);
            Assert.AreEqual(string.Empty, research.ActiveResearchId);
            Assert.AreEqual(
                0L,
                research.CreateSnapshot().ResearchCompletionGameHour);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(catalog);
            DeleteTemporarySave(savePath);
            DeleteTemporarySave(backupPath);
            DeleteTemporarySave($"{savePath}.tmp");
            DeleteTemporarySave($"{backupPath}.fallback");
        }
    }

    static void DeleteTemporarySave(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    static void ConfigureExpansionCatalog(
        ResearchCatalogSO catalog,
        string researchId,
        string stageId)
    {
        var serialized = new UnityEditor.SerializedObject(catalog);
        UnityEditor.SerializedProperty list =
            serialized.FindProperty("entries");
        list.arraySize = 1;
        UnityEditor.SerializedProperty entry =
            list.GetArrayElementAtIndex(0);
        entry.FindPropertyRelative("researchId").stringValue = researchId;
        entry.FindPropertyRelative("displayName").stringValue = researchId;
        entry.FindPropertyRelative("category").enumValueIndex =
            (int)ResearchCategory.Expansion;
        entry.FindPropertyRelative("conditionKind").enumValueIndex =
            (int)ResearchConditionKind.Population;
        entry.FindPropertyRelative("threshold").intValue = 0;
        entry.FindPropertyRelative("researchCost").intValue = 0;
        entry.FindPropertyRelative("researchDurationHours").intValue = 0;
        entry.FindPropertyRelative("worldGridStageId").stringValue = stageId;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ConfigureCatalog(ResearchCatalogSO catalog,
        params (string id, ResearchConditionKind kind, int threshold)[] rows)
    {
        var so = new UnityEditor.SerializedObject(catalog);
        var list = so.FindProperty("entries");
        list.arraySize = rows.Length;
        for (int i = 0; i < rows.Length; i++)
        {
            var p = list.GetArrayElementAtIndex(i);
            p.FindPropertyRelative("researchId").stringValue = rows[i].id;
            p.FindPropertyRelative("displayName").stringValue = rows[i].id;
            p.FindPropertyRelative("conditionKind").enumValueIndex = (int)rows[i].kind;
            p.FindPropertyRelative("threshold").intValue = rows[i].threshold;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetPrivateField(object target, string field, object value) =>
        target.GetType().GetField(field,
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance)
            .SetValue(target, value);

    static void SetTestInputs(ResearchUnlockService service, int population, int arrivals) =>
        service.inputsOverrideForTest = () =>
            new ResearchConditionInputs(arrivals, population, null);

    static void InvokeUpdate(ResearchUnlockService service)
    {
        var method = typeof(ResearchUnlockService).GetMethod(
            "Update",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);
        method?.Invoke(service, null);
    }

    sealed class FakeStats : IReadOnlyCityStats
    {
        public int ActiveVehicleCount => 0;
        public int LastDayArrivalCount { get; set; }

        public bool TryGetCompanyStaffing(
            Vector2Int tile,
            out CompanyStaffing staffing)
        {
            staffing = default;
            return false;
        }

        public bool TryGetCompanyTypeId(Vector2Int tile, out string companyTypeId)
        {
            companyTypeId = null;
            return false;
        }

        public System.Collections.Generic.IReadOnlyList<CommuterHomeCount>
            GetCompanyCommuterHomes(Vector2Int tile) =>
            System.Array.Empty<CommuterHomeCount>();
    }

    sealed class FakeWorldGridExpansion : IWorldGridExpansionService
    {
        readonly HashSet<string> unlockedStages = new()
        {
            "center_020"
        };

        public int CurrentStageIndex { get; private set; }
        public string CurrentStageId { get; private set; } = "center_020";
        public bool CanUnlockNextStage => true;
        public List<string> RequestedStages { get; } = new();
        public bool RejectStageRequests { get; set; }

        public event System.Action<WorldGridStageChangedEvent> StageChanged;

        public bool IsStageUnlocked(string stageId) =>
            unlockedStages.Contains(stageId);

        public bool TryUnlockNextStage() => false;

        public bool TryUnlockStage(string stageId)
        {
            RequestedStages.Add(stageId);
            if (RejectStageRequests)
            {
                return false;
            }

            unlockedStages.Add(stageId);
            CurrentStageId = stageId;
            CurrentStageIndex++;
            StageChanged?.Invoke(new WorldGridStageChangedEvent(
                stageId,
                CurrentStageIndex,
                default,
                WorldGridStageChangeReason.Unlocked));
            return true;
        }

        public void MarkStageUnlocked(string stageId, int stageIndex)
        {
            unlockedStages.Add(stageId);
            CurrentStageId = stageId;
            CurrentStageIndex = stageIndex;
        }

        public bool TryResetToInitialStage() => false;
    }

    sealed class FakeSimSaveSource : ISimSaveSource
    {
        public int GridWidth => 20;
        public int GridHeight => 20;
        public SimSaveData CreateSnapshot() => new();
        public void RestoreSnapshot(SimSaveData snapshot) { }
    }

    sealed class FakeSaveClock : ISaveClock
    {
        public DateTime UtcNow =>
            new(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc);
    }
}
