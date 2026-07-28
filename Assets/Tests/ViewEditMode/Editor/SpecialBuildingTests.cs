using System;
using System.Reflection;
using CityFlow.Bootstrap;
using CityFlow.Buildings;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.Gameplay.Research;
using CityFlow.Save;
using CityFlow.Sim;
using CityFlow.View;
using CityFlow.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CityFlow.Tests
{
    public sealed class SpecialBuildingTests
    {
        private const string CatalogPath =
            "Assets/05_ScriptableObjects/Buildings/SpecialBuildingCatalog.asset";
        private const string SystemPrefabPath =
            "Assets/02_Prefabs/Buildings/SpecialBuildingSystem.prefab";
        private const string FallbackPrefabPath =
            "Assets/02_Prefabs/Buildings/SpecialBuildingFallback.prefab";

        [Test]
        public void Catalog_ContainsEightDefinitionsWithExpectedCadences()
        {
            BuildingCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<BuildingCatalogSO>(CatalogPath);

            Assert.NotNull(catalog);
            Assert.AreEqual(8, catalog.Count);
            AssertCadence(catalog, "mall", 1, 1);
            AssertCadence(catalog, "petrol_station", 2, 1);
            AssertCadence(catalog, "police_station", 1, 10);
            AssertCadence(catalog, "video_store", 1, 1);
            AssertCadence(catalog, "pharmacy", 1, 2);
            AssertCadence(catalog, "coffee_shop", 1, 1);
            AssertCadence(catalog, "cinema", 1, 1);
            AssertCadence(catalog, "auto_repair", 1, 5);
        }

        [Test]
        public void Prefabs_ContainPrewiredRuntimeComponents()
        {
            GameObject systemPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(SystemPrefabPath);
            GameObject fallbackPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(FallbackPrefabPath);

            Assert.NotNull(systemPrefab);
            Assert.NotNull(systemPrefab.GetComponent<SpecialBuildingService>());
            Assert.NotNull(systemPrefab.GetComponent<ResearchUnlockService>());
            Assert.NotNull(systemPrefab.GetComponent<SpecialBuildingView>());
            Assert.NotNull(
                systemPrefab.GetComponent<SpecialBuildingVisitService>());
            Assert.NotNull(fallbackPrefab);
            Assert.NotNull(
                fallbackPrefab.GetComponent<
                    SpecialBuildingFallbackPresenter>());
        }

        [Test]
        public void ExistingBuildSlotPrefab_SupportsLockedSpecialBuilding()
        {
            const string buildSlotPath =
                "Assets/02_Prefabs/UI_BuildSlot.prefab";
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(buildSlotPath);
            GameObject instance = null;

            try
            {
                Assert.NotNull(prefab);
                instance = UnityEngine.Object.Instantiate(prefab);
                BuildSlotController slot =
                    instance.GetComponent<BuildSlotController>();
                var locked = new SpecialBuildingBuildOption(
                    "mall",
                    "큰 상점",
                    "Commercial",
                    "설명",
                    null,
                    Color.green,
                    SpecialBuildingMenuCategory.Commercial,
                    100,
                    false,
                    "research_building_mall",
                    true,
                    1,
                    1,
                    0,
                    1f,
                    1);

                slot.ConfigureSpecialBuilding(locked, null, null);

                Button button = instance.transform
                    .Find("Btn_Buy")
                    .GetComponent<Button>();
                TMP_Text cost = instance.transform
                    .Find("CostText")
                    .GetComponent<TMP_Text>();
                Assert.IsFalse(button.interactable);
                Assert.AreEqual("잠김", cost.text);

                var unlocked = new SpecialBuildingBuildOption(
                    "mall",
                    "큰 상점",
                    "Commercial",
                    "설명",
                    null,
                    Color.green,
                    SpecialBuildingMenuCategory.Commercial,
                    100,
                    true,
                    "research_building_mall",
                    true,
                    1,
                    1,
                    0,
                    1f,
                    1);
                slot.RefreshSpecialBuilding(unlocked);

                Assert.IsTrue(button.interactable);
                Assert.AreEqual("100", cost.text);
            }
            finally
            {
                if (instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void DeterministicDemand_LowFrequencyTotalsMatchPeriod()
        {
            const int population = 17;
            int total = 0;

            for (long day = 0; day < 5; day++)
            {
                int first = DeterministicVisitDemand.CalculateDailyDemand(
                    population,
                    1,
                    5,
                    day,
                    "auto_repair");
                int second = DeterministicVisitDemand.CalculateDailyDemand(
                    population,
                    1,
                    5,
                    day,
                    "auto_repair");
                Assert.AreEqual(first, second);
                total += first;
            }

            Assert.AreEqual(population, total);
            Assert.AreEqual(
                population * 2,
                DeterministicVisitDemand.CalculateDailyDemand(
                    population,
                    2,
                    1,
                    12,
                    "petrol_station"));
        }

        [Test]
        public void VisitService_ProcessesDayAndPersistsStatistics()
        {
            BuildingCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<BuildingCatalogSO>(CatalogPath);
            Assert.IsTrue(catalog.TryGet(
                "cinema",
                out BuildingDefinitionSO cinema));

            GameObject serviceObject = null;

            try
            {
                SimConfig config = SimConfig.Default();
                config.GridWidth = 20;
                config.GridHeight = 20;
                var events = new SimEventHub();
                var engine = new SimEngine(config, events);
                var save = new SaveService(
                    engine,
                    new JsonSaveRepository(),
                    new SystemSaveClock());
                var economy = new TestEconomy();
                var services = new CityFlowServices(
                    events,
                    engine,
                    engine,
                    save,
                    economy,
                    engine);

                serviceObject = new GameObject("SpecialBuildingVisitTest");
                ResearchUnlockService research =
                    serviceObject.AddComponent<ResearchUnlockService>();
                research.Initialize(services);
                research.TryUnlock("research_building_cinema");

                SpecialBuildingService buildingService =
                    serviceObject.AddComponent<SpecialBuildingService>();
                SetPrivateField(buildingService, "catalog", catalog);
                buildingService.Initialize(services);
                Assert.IsTrue(buildingService.TryPlace(
                    "cinema",
                    new Vector2Int(2, 3)));

                var population = new TestPopulation(10);
                var calendar = new TestCalendar();
                services.RegisterPopulation(population);
                services.RegisterGameCalendar(calendar);

                SpecialBuildingVisitService visitService =
                    serviceObject.AddComponent<SpecialBuildingVisitService>();
                visitService.Initialize(services);
                calendar.AdvanceDay();

                Assert.IsTrue(visitService.TryGetStatistics(
                    new Vector2Int(3, 4),
                    out SpecialBuildingVisitStatistics statistics));
                Assert.AreEqual(10, statistics.PlannedToday);
                Assert.AreEqual(10L, statistics.TotalPlannedVisits);
                Assert.AreEqual(0L, economy.Coins);

                GameSaveData snapshot = save.CreateSnapshot();
                Assert.NotNull(snapshot.SpecialBuildingVisits);
                Assert.IsTrue(snapshot.SpecialBuildingVisits.HasState);
                Assert.AreEqual(
                    calendar.TotalDays,
                    snapshot.SpecialBuildingVisits.LastProcessedTotalDay);
                Assert.AreEqual(
                    1,
                    snapshot.SpecialBuildingVisits.Statistics.Length);
            }
            finally
            {
                if (serviceObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(serviceObject);
                }
            }
        }

        [Test]
        public void FallbackPresenter_CreatesColliderFreeVisualParts()
        {
            BuildingCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<BuildingCatalogSO>(CatalogPath);
            GameObject fallbackPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(FallbackPrefabPath);
            GameObject instance = null;

            try
            {
                Assert.IsTrue(catalog.TryGet(
                    "mall",
                    out BuildingDefinitionSO definition));
                instance = UnityEngine.Object.Instantiate(fallbackPrefab);
                instance.GetComponent<SpecialBuildingFallbackPresenter>()
                    .Configure(definition, 1f);

                Assert.NotNull(instance.transform.Find("Body"));
                Assert.NotNull(instance.transform.Find("Roof"));
                Assert.NotNull(instance.transform.Find("FrontMarker"));
                Collider[] colliders =
                    instance.GetComponentsInChildren<Collider>(true);
                for (int index = 0; index < colliders.Length; index++)
                {
                    Assert.IsFalse(colliders[index].enabled);
                }
            }
            finally
            {
                if (instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void Service_RejectsLockedBuildingUntilResearchUnlocks()
        {
            BuildingCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<BuildingCatalogSO>(CatalogPath);
            GameObject serviceObject = null;

            try
            {
                RuntimeContext runtime = CreateRuntime(
                    catalog,
                    out serviceObject,
                    unlockCinema: false);

                Assert.IsFalse(runtime.Service.IsBuildingUnlocked("cinema"));
                Assert.IsFalse(runtime.Service.TryPlace(
                    "cinema",
                    new Vector2Int(2, 3)));

                Assert.IsTrue(runtime.Research.TryUnlock(
                    "research_building_cinema"));
                Assert.IsTrue(runtime.Service.IsBuildingUnlocked("cinema"));
                Assert.IsTrue(runtime.Service.TryPlace(
                    "cinema",
                    new Vector2Int(2, 3)));
            }
            finally
            {
                if (serviceObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(serviceObject);
                }
            }
        }

        [Test]
        public void Service_PlaceAndRestore_PreservesIdentityAndDirection()
        {
            BuildingCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<BuildingCatalogSO>(CatalogPath);
            Assert.NotNull(catalog);

            GameObject firstObject = null;
            GameObject restoredObject = null;

            try
            {
                var first = CreateRuntime(catalog, out firstObject);
                Assert.IsTrue(first.Service.TryPlace(
                    "cinema",
                    new Vector2Int(2, 3),
                    PlacementDirection.East));
                Assert.AreEqual(
                    TileType.SpecialBuilding,
                    first.Engine.GetTileType(new Vector2Int(3, 4)));
                Assert.IsTrue(first.Service.TryGetBuilding(
                    new Vector2Int(3, 4),
                    out SpecialBuildingInstance placed));
                Assert.AreEqual("cinema", placed.BuildingId);

                HappinessEffectDescriptor[] effects =
                    first.Service.CreateActiveHappinessEffectSnapshot();
                Assert.AreEqual(1, effects.Length);
                Assert.AreEqual(
                    "happiness_building_cinema",
                    effects[0].EffectKey);

                GameSaveData snapshot = first.Save.CreateSnapshot();
                var restored = CreateRuntime(catalog, out restoredObject);
                restored.Save.RestoreSnapshot(snapshot);

                Assert.IsTrue(restored.Service.TryGetBuilding(
                    new Vector2Int(3, 4),
                    out SpecialBuildingInstance loaded));
                Assert.AreEqual("cinema", loaded.BuildingId);
                Assert.AreEqual(
                    PlacementDirection.East,
                    loaded.Direction);
                Assert.AreEqual(1, restored.Service.BuildingCount);
                Assert.IsTrue(restored.Research.IsUnlocked(
                    "research_building_cinema"));
            }
            finally
            {
                if (firstObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(firstObject);
                }

                if (restoredObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(restoredObject);
                }
            }
        }

        private static RuntimeContext CreateRuntime(
            BuildingCatalogSO catalog,
            out GameObject serviceObject,
            bool unlockCinema = true)
        {
            SimConfig config = SimConfig.Default();
            config.GridWidth = 20;
            config.GridHeight = 20;
            var events = new SimEventHub();
            var engine = new SimEngine(config, events);
            var save = new SaveService(
                engine,
                new JsonSaveRepository(),
                new SystemSaveClock());
            var services = new CityFlowServices(
                events,
                engine,
                engine,
                save,
                stats: engine);

            serviceObject = new GameObject("SpecialBuildingServiceTest");
            ResearchUnlockService research =
                serviceObject.AddComponent<ResearchUnlockService>();
            research.Initialize(services);
            if (unlockCinema)
            {
                research.TryUnlock("research_building_cinema");
            }

            SpecialBuildingService service =
                serviceObject.AddComponent<SpecialBuildingService>();
            SetPrivateField(service, "catalog", catalog);
            service.Initialize(services);

            return new RuntimeContext(engine, save, service, research);
        }

        private static void AssertCadence(
            BuildingCatalogSO catalog,
            string buildingId,
            int visits,
            int days)
        {
            Assert.IsTrue(catalog.TryGet(buildingId, out BuildingDefinitionSO definition));
            Assert.AreEqual(visits, definition.VisitCadence.VisitsPerPeriod);
            Assert.AreEqual(days, definition.VisitCadence.PeriodDays);
            Assert.AreEqual(Vector2Int.one * 2, definition.Footprint);
            Assert.IsFalse(string.IsNullOrEmpty(
                definition.HappinessEffectKey));
            Assert.Greater(definition.FallbackHeight, 0f);
        }

        private static void SetPrivateField<T>(
            object target,
            string fieldName,
            T value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, fieldName);
            field.SetValue(target, value);
        }

        private readonly struct RuntimeContext
        {
            public RuntimeContext(
                SimEngine engine,
                SaveService save,
                SpecialBuildingService service,
                ResearchUnlockService research)
            {
                Engine = engine;
                Save = save;
                Service = service;
                Research = research;
            }

            public SimEngine Engine { get; }
            public SaveService Save { get; }
            public SpecialBuildingService Service { get; }
            public ResearchUnlockService Research { get; }
        }

        private sealed class TestPopulation : IReadOnlyPopulationData
        {
            public TestPopulation(int population)
            {
                CurrentPopulation = population;
            }

            public int CurrentPopulation { get; private set; }

            public event Action<int> PopulationChanged;

            public void SetPopulation(int population)
            {
                CurrentPopulation = Math.Max(0, population);
                PopulationChanged?.Invoke(CurrentPopulation);
            }
        }

        private sealed class TestCalendar : IGameCalendarService
        {
            public int Year { get; private set; } = 1;
            public int Month { get; private set; } = 1;
            public int Day { get; private set; } = 1;
            public int Hour { get; private set; }
            public int TotalMonths { get; private set; } = 1;
            public long TotalDays { get; private set; }
            public float RealSecondsPerGameHour => 1f;

            public event Action<int> HourChanged;
            public event Action<int> DayChanged;
            public event Action<int> MonthChanged;

            public void AdvanceDay()
            {
                Day++;
                TotalDays++;
                DayChanged?.Invoke(Day);
            }
        }

        private sealed class TestEconomy : IEconomyService
        {
            public long Coins { get; private set; }

            public event Action<long> CoinsChanged;

            public bool TrySpend(long amount)
            {
                if (amount < 0L || amount > Coins)
                {
                    return false;
                }

                Coins -= amount;
                CoinsChanged?.Invoke(Coins);
                return true;
            }

            public void AddCoins(long amount, string reason)
            {
                if (amount <= 0L)
                {
                    return;
                }

                Coins += amount;
                CoinsChanged?.Invoke(Coins);
            }
        }
    }
}
