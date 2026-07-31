using CityFlow.Bootstrap;
using CityFlow.Configs;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.Gameplay.Research;
using CityFlow.UI.Controllers.Placement;
using NUnit.Framework;
using UnityEngine;

public class ResearchUnlockTreeTests
{
    [Test]
    public void PrerequisiteLocked_MakesSatisfiedResearchNotReadyOrUnlockable()
    {
        var owner = new GameObject("research");
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        try
        {
            ConfigureCatalog(catalog,
                ("root", "", ResearchConditionKind.Population, 1),
                ("child", "root", ResearchConditionKind.Population, 1));
            var service = owner.AddComponent<ResearchUnlockService>();
            SetPrivate(service, "catalog", catalog);
            service.Initialize(new CityFlowServices(new SimEventHub(), null, null));
            service.inputsOverrideForTest = () => new ResearchConditionInputs(0, 10, null);
            service.EvaluatePendingResearch();

            Assert.IsFalse(service.IsReady("child"));
            Assert.IsFalse(service.TryUnlock("child"));
            Assert.IsFalse(service.IsUnlocked("child"));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void PrerequisiteUnlockedAndSatisfied_CanUnlockOnce()
    {
        var owner = new GameObject("research");
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        try
        {
            ConfigureCatalog(catalog,
                ("root", "", ResearchConditionKind.Population, 1),
                ("child", "root", ResearchConditionKind.Population, 1));
            var service = owner.AddComponent<ResearchUnlockService>();
            SetPrivate(service, "catalog", catalog);
            service.Initialize(new CityFlowServices(new SimEventHub(), null, null));
            service.inputsOverrideForTest = () => new ResearchConditionInputs(0, 10, null);
            service.EvaluatePendingResearch();
            Assert.IsTrue(service.TryUnlock("root"));
            service.EvaluatePendingResearch();

            Assert.IsTrue(service.IsReady("child"));
            Assert.IsTrue(service.TryUnlock("child"));
            Assert.IsFalse(service.TryUnlock("child"));
            Assert.IsTrue(service.IsUnlocked("child"));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void RootResearch_IsReadyFromItsConditionAlone()
    {
        var owner = new GameObject("research");
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        try
        {
            ConfigureCatalog(catalog,
                ("root", "", ResearchConditionKind.Population, 10));
            var service = owner.AddComponent<ResearchUnlockService>();
            SetPrivate(service, "catalog", catalog);
            service.Initialize(new CityFlowServices(new SimEventHub(), null, null));
            service.inputsOverrideForTest = () => new ResearchConditionInputs(0, 10, null);
            service.EvaluatePendingResearch();

            Assert.IsTrue(service.IsReady("root"));
            Assert.IsFalse(service.IsUnlocked("root"));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void EvaluatePendingResearch_DoesNotAutomaticallyUnlockReadyResearch()
    {
        var owner = new GameObject("research");
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        try
        {
            ConfigureCatalog(catalog,
                ("root", "", ResearchConditionKind.Population, 1));
            var service = owner.AddComponent<ResearchUnlockService>();
            SetPrivate(service, "catalog", catalog);
            service.Initialize(new CityFlowServices(new SimEventHub(), null, null));
            service.inputsOverrideForTest = () => new ResearchConditionInputs(0, 10, null);
            service.EvaluatePendingResearch();

            Assert.IsTrue(service.IsReady("root"));
            Assert.IsFalse(service.IsUnlocked("root"));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void PaidTimedResearch_DeductsOnce_AndUnlocksAfterGameHours()
    {
        var owner = new GameObject("research");
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        try
        {
            ConfigureCatalog(catalog,
                ("school", "", ResearchConditionKind.Population, 1));
            ConfigureCostAndDuration(
                catalog,
                index: 0,
                cost: 300,
                durationHours: 6);

            var economy = new TestEconomy(1000);
            var calendar = new TestCalendar();
            var services = new CityFlowServices(
                new SimEventHub(),
                null,
                null,
                economy: economy);
            services.RegisterGameCalendar(calendar);

            var service = owner.AddComponent<ResearchUnlockService>();
            SetPrivate(service, "catalog", catalog);
            service.Initialize(services);
            service.inputsOverrideForTest = () =>
                new ResearchConditionInputs(0, 10, null);
            service.EvaluatePendingResearch();

            Assert.IsTrue(service.TryStartResearch("school"));
            Assert.AreEqual(700, economy.Coins);
            Assert.IsTrue(service.IsResearching("school"));
            Assert.IsFalse(service.IsUnlocked("school"));
            Assert.IsFalse(
                service.TryStartResearch("school"),
                "같은 연구 중복 결제 금지");
            Assert.AreEqual(700, economy.Coins);

            calendar.AdvanceHours(5);
            Assert.IsFalse(service.IsUnlocked("school"));
            Assert.AreEqual(
                1,
                service.GetRemainingResearchHours("school"));

            calendar.AdvanceHours(1);
            Assert.IsTrue(service.IsUnlocked("school"));
            Assert.IsFalse(service.IsResearching("school"));
            Assert.AreEqual(string.Empty, service.ActiveResearchId);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void InsufficientCoins_DoesNotStartOrChangeSaveState()
    {
        var owner = new GameObject("research");
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        try
        {
            ConfigureCatalog(catalog,
                ("hospital", "", ResearchConditionKind.Population, 1));
            ConfigureCostAndDuration(
                catalog,
                index: 0,
                cost: 500,
                durationHours: 12);

            var economy = new TestEconomy(499);
            var calendar = new TestCalendar();
            var services = new CityFlowServices(
                new SimEventHub(),
                null,
                null,
                economy: economy);
            services.RegisterGameCalendar(calendar);

            var service = owner.AddComponent<ResearchUnlockService>();
            SetPrivate(service, "catalog", catalog);
            service.Initialize(services);
            service.inputsOverrideForTest = () =>
                new ResearchConditionInputs(0, 10, null);
            service.EvaluatePendingResearch();

            Assert.IsFalse(service.TryStartResearch("hospital"));
            Assert.AreEqual(499, economy.Coins);
            Assert.AreEqual(string.Empty, service.ActiveResearchId);
            Assert.IsFalse(service.IsUnlocked("hospital"));

            ResearchSaveData snapshot = service.CreateSnapshot();
            Assert.AreEqual(string.Empty, snapshot.ActiveResearchId);
            Assert.AreEqual(0L, snapshot.ResearchCompletionGameHour);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void ActiveResearch_RestoresWithRemainingTime_AndCompletes()
    {
        var owner = new GameObject("research");
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        try
        {
            ConfigureCatalog(catalog,
                ("hospital", "", ResearchConditionKind.Population, 1));
            ConfigureCostAndDuration(
                catalog,
                index: 0,
                cost: 500,
                durationHours: 6);

            var economy = new TestEconomy(1000);
            var calendar = new TestCalendar();
            var services = new CityFlowServices(
                new SimEventHub(),
                null,
                null,
                economy: economy);
            services.RegisterGameCalendar(calendar);

            var service = owner.AddComponent<ResearchUnlockService>();
            SetPrivate(service, "catalog", catalog);
            service.Initialize(services);
            service.inputsOverrideForTest = () =>
                new ResearchConditionInputs(0, 10, null);
            service.EvaluatePendingResearch();

            Assert.IsTrue(service.TryStartResearch("hospital"));
            calendar.AdvanceHours(2);
            ResearchSaveData snapshot = service.CreateSnapshot();

            service.RestoreSnapshot(snapshot);

            Assert.IsTrue(service.IsResearching("hospital"));
            Assert.AreEqual(
                4,
                service.GetRemainingResearchHours("hospital"));

            calendar.AdvanceHours(4);

            Assert.IsTrue(service.IsUnlocked("hospital"));
            Assert.IsFalse(service.IsResearching("hospital"));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(catalog);
        }
    }

    [Test]
    public void RequiredResearch_BlocksBaseBuildingUntilUnlocked()
    {
        var owner = new GameObject("research");
        var catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        var schoolData = ScriptableObject.CreateInstance<TileDataSO>();
        try
        {
            ConfigureCatalog(catalog,
                ("school", "", ResearchConditionKind.Population, 1));
            ConfigureTileResearch(
                schoolData,
                TileType.School,
                "school");

            var services = new CityFlowServices(
                new SimEventHub(),
                null,
                null);
            var service = owner.AddComponent<ResearchUnlockService>();
            SetPrivate(service, "catalog", catalog);
            service.Initialize(services);
            service.inputsOverrideForTest = () =>
                new ResearchConditionInputs(0, 10, null);
            service.EvaluatePendingResearch();

            var dispatcher = new PlacementActionDispatcher(
                new[] { schoolData },
                useFakeMode: false);

            Assert.IsFalse(
                dispatcher.IsTileTypeUnlocked(
                    TileType.School,
                    services));

            Assert.IsTrue(service.TryUnlock("school"));

            Assert.IsTrue(
                dispatcher.IsTileTypeUnlocked(
                    TileType.School,
                    services));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(schoolData);
        }
    }

    static void ConfigureCatalog(ResearchCatalogSO catalog,
        params (string id, string prerequisite, ResearchConditionKind kind, int threshold)[] rows)
    {
        var so = new UnityEditor.SerializedObject(catalog);
        var list = so.FindProperty("entries");
        list.arraySize = rows.Length;
        for (int i = 0; i < rows.Length; i++)
        {
            var p = list.GetArrayElementAtIndex(i);
            p.FindPropertyRelative("researchId").stringValue = rows[i].id;
            p.FindPropertyRelative("prerequisiteId").stringValue = rows[i].prerequisite;
            p.FindPropertyRelative("displayName").stringValue = rows[i].id;
            p.FindPropertyRelative("conditionKind").enumValueIndex = (int)rows[i].kind;
            p.FindPropertyRelative("threshold").intValue = rows[i].threshold;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ConfigureCostAndDuration(
        ResearchCatalogSO catalog,
        int index,
        int cost,
        int durationHours)
    {
        var so = new UnityEditor.SerializedObject(catalog);
        var entry = so.FindProperty("entries")
            .GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("researchCost").intValue = cost;
        entry.FindPropertyRelative("researchDurationHours").intValue =
            durationHours;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ConfigureTileResearch(
        TileDataSO tileData,
        TileType category,
        string requiredResearchId)
    {
        var so = new UnityEditor.SerializedObject(tileData);
        so.FindProperty("category").enumValueIndex = (int)category;
        so.FindProperty("requiredResearchId").stringValue =
            requiredResearchId;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetPrivate(object target, string field, object value) =>
        target.GetType().GetField(field,
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance).SetValue(target, value);

    sealed class TestEconomy : IEconomyService
    {
        public long Coins { get; private set; }
        public event System.Action<long> CoinsChanged;

        public TestEconomy(long coins)
        {
            Coins = coins;
        }

        public bool TrySpend(long amount)
        {
            if (amount < 0 || Coins < amount)
            {
                return false;
            }

            Coins -= amount;
            CoinsChanged?.Invoke(Coins);
            return true;
        }

        public void AddCoins(long amount, string reason)
        {
            Coins += amount;
            CoinsChanged?.Invoke(Coins);
        }
    }

    sealed class TestCalendar : IGameCalendarService
    {
        public int Year => 1;
        public int Month => 1;
        public int Day => (int)TotalDays + 1;
        public int Hour { get; private set; }
        public int TotalMonths => 1;
        public long TotalDays { get; private set; }
        public float RealSecondsPerGameHour => 1f;
        public float RealSecondsPerGameDay => 24f;
        public int HoursPerDay => 24;
        public float TimeOfDay01 => Hour / 24f;

        public event System.Action<int> HourChanged;
        public event System.Action<int> DayChanged;
        public event System.Action<int> MonthChanged;

        public void AdvanceHours(int hours)
        {
            long absoluteHour =
                TotalDays * HoursPerDay + Hour + hours;
            TotalDays = absoluteHour / HoursPerDay;
            Hour = (int)(absoluteHour % HoursPerDay);
            HourChanged?.Invoke(Hour);
            DayChanged?.Invoke(Day);
        }
    }
}
