using System;
using System.IO;
using System.Reflection;
using CityFlow.Bootstrap;
using CityFlow.Buildings;
using CityFlow.Configs;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.Gameplay.Research;
using CityFlow.Save;
using CityFlow.Sim;
using CityFlow.View;
using CityFlow.UI;
using CityFlow.UI.Controllers.Placement;
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
            Assert.NotNull(
                systemPrefab.GetComponent<SpecialBuildingVisitTripSource>());
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
            string savePath = Path.Combine(
                Path.GetTempPath(),
                $"greenlight-special-trip-{Guid.NewGuid():N}.json");
            string backupPath = savePath + ".bak";

            try
            {
                SimConfig config = SimConfig.Default();
                config.GridWidth = 20;
                config.GridHeight = 20;
                var events = new SimEventHub();
                var engine = new SimEngine(config, events);
                var save = new SaveService(
                    engine,
                    new JsonSaveRepository(savePath, backupPath),
                    new SystemSaveClock());
                var economy = new TestEconomy();
                var services = new CityFlowServices(
                    events,
                    engine,
                    engine,
                    save,
                    economy,
                    engine);
                services.RegisterVehicleTrips(engine);

                serviceObject = new GameObject("SpecialBuildingVisitTest");
                ResearchUnlockService research =
                    serviceObject.AddComponent<ResearchUnlockService>();
                research.Initialize(services);
                UnlockCinemaForTest(research);

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
                SpecialBuildingVisitTripSource tripSource =
                    serviceObject.AddComponent<SpecialBuildingVisitTripSource>();
                tripSource.Initialize(services);
                calendar.AdvanceDay();

                Assert.IsTrue(visitService.TryGetStatistics(
                    new Vector2Int(3, 4),
                    out SpecialBuildingVisitStatistics statistics));
                Assert.AreEqual(10, statistics.PlannedToday);
                Assert.AreEqual(10L, statistics.TotalPlannedVisits);
                Assert.AreEqual(0L, economy.Coins);
                Assert.AreEqual(10, engine.PendingTripCount);

                GameSaveData snapshot = save.CreateSnapshot();
                Assert.NotNull(snapshot.SpecialBuildingVisits);
                Assert.IsTrue(snapshot.SpecialBuildingVisits.HasState);
                Assert.AreEqual(
                    calendar.TotalDays,
                    snapshot.SpecialBuildingVisits.LastProcessedTotalDay);
                Assert.AreEqual(
                    1,
                    snapshot.SpecialBuildingVisits.Statistics.Length);

                calendar.SetHour(12);
                Assert.IsTrue(save.Repository.TrySave(snapshot));
                Assert.IsTrue(save.TryLoadAndRestore());

                Assert.AreEqual(
                    5,
                    engine.PendingTripCount,
                    "Only visits scheduled after the restored hour should be rebuilt.");
            }
            finally
            {
                if (serviceObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(serviceObject);
                }

                if (File.Exists(savePath))
                {
                    File.Delete(savePath);
                }

                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
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

                UnlockCinemaForTest(runtime.Research);
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

        [Test]
        public void Service_PendingConstruction_RestoresAndActivatesHappinessOnCompletion()
        {
            BuildingCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<BuildingCatalogSO>(CatalogPath);
            GameObject firstObject = null;
            GameObject restoredObject = null;

            try
            {
                RuntimeContext first = CreateRuntime(
                    catalog,
                    out firstObject,
                    constructionHoursSpecial: 4f);
                int firstActivations = 0;
                first.Service.HappinessEffectChanged += changed =>
                {
                    if (changed.IsActive)
                    {
                        firstActivations++;
                    }
                };

                Vector2Int anchor = new Vector2Int(2, 3);
                Assert.IsTrue(first.Service.TryPlace("cinema", anchor));
                Assert.AreEqual(
                    TileType.UnderConstruction,
                    first.Engine.GetTileType(anchor));
                Assert.AreEqual(1, first.Service.BuildingCount);
                Assert.AreEqual(
                    0,
                    first.Service.CreateActiveHappinessEffectSnapshot().Length);
                Assert.AreEqual(0, firstActivations);

                GameSaveData snapshot = first.Save.CreateSnapshot();
                RuntimeContext restored = CreateRuntime(
                    catalog,
                    out restoredObject,
                    constructionHoursSpecial: 4f);
                int restoredActivations = 0;
                restored.Service.HappinessEffectChanged += changed =>
                {
                    if (changed.IsActive)
                    {
                        restoredActivations++;
                    }
                };

                restored.Save.RestoreSnapshot(snapshot);

                Assert.AreEqual(1, restored.Service.BuildingCount);
                Assert.IsTrue(restored.Service.TryGetBuilding(
                    new Vector2Int(3, 4),
                    out SpecialBuildingInstance loaded));
                Assert.AreEqual("cinema", loaded.BuildingId);
                Assert.AreEqual(
                    0,
                    restored.Service.CreateActiveHappinessEffectSnapshot().Length);
                Assert.AreEqual(0, restoredActivations);

                for (int i = 0; i < 16; i++)
                {
                    restored.Engine.Tick(0.25f);
                }

                Assert.AreEqual(
                    TileType.SpecialBuilding,
                    restored.Engine.GetTileType(anchor));
                Assert.AreEqual(
                    1,
                    restored.Service.CreateActiveHappinessEffectSnapshot().Length);
                Assert.AreEqual(1, restoredActivations);
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

        [Test]
        public void Dispatcher_DemolishesPendingSpecialBuilding_AndAllowsReplacement()
        {
            BuildingCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<BuildingCatalogSO>(CatalogPath);
            GameObject serviceObject = null;

            try
            {
                RuntimeContext runtime = CreateRuntime(
                    catalog,
                    out serviceObject,
                    constructionHoursSpecial: 4f);
                Vector2Int anchor = new Vector2Int(2, 3);
                Assert.IsTrue(runtime.Service.TryPlace("cinema", anchor));
                Assert.AreEqual(
                    TileType.UnderConstruction,
                    runtime.Engine.GetTileType(anchor));
                Assert.AreEqual(
                    0,
                    runtime.Service.CreateActiveHappinessEffectSnapshot().Length);

                var dispatcher = new PlacementActionDispatcher(
                    availableTiles: null,
                    useFakeMode: false);
                dispatcher.PlaceInfrastructure(
                    new Vector2Int(3, 4),
                    TileType.Empty,
                    PlacementDirection.North,
                    runtime.Services);

                Assert.AreEqual(0, runtime.Service.BuildingCount);
                Assert.AreEqual(
                    TileType.Empty,
                    runtime.Engine.GetTileType(anchor));
                Assert.AreEqual(
                    0,
                    runtime.Service.CreateActiveHappinessEffectSnapshot().Length);
                Assert.AreEqual(
                    0,
                    runtime.Service.CreateSnapshot().Buildings.Length);
                Assert.IsTrue(
                    runtime.Service.TryPlace("cinema", anchor),
                    "공사 중 철거 뒤 같은 앵커에 다시 배치할 수 있어야 한다");
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
        public void Dispatcher_DemolishingConstruction_RefundsTargetTypeCost()
        {
            TileDataSO hospitalData =
                ScriptableObject.CreateInstance<TileDataSO>();

            try
            {
                const int hospitalCost = 800;
                hospitalData.Initialize(
                    "hospital",
                    "병원",
                    TileType.Hospital,
                    hospitalCost,
                    0,
                    0,
                    string.Empty);

                SimConfig config = SimConfig.Default();
                config.GridWidth = 20;
                config.GridHeight = 20;
                config.DayLengthSeconds = 24f;
                config.ConstructionHoursHospital = 4f;
                var events = new SimEventHub();
                var engine = new SimEngine(config, events);
                var economy = new TestEconomy();
                var services = new CityFlowServices(
                    events,
                    engine,
                    engine,
                    economy: economy,
                    stats: engine);
                var dispatcher = new PlacementActionDispatcher(
                    new[] { hospitalData },
                    useFakeMode: false);
                Vector2Int anchor = new Vector2Int(2, 3);
                Assert.IsTrue(engine.Place(anchor, TileType.Hospital));
                Assert.AreEqual(
                    TileType.UnderConstruction,
                    engine.GetTileType(anchor));

                dispatcher.PlaceInfrastructure(
                    new Vector2Int(3, 4),
                    TileType.Empty,
                    PlacementDirection.North,
                    services);

                Assert.AreEqual(
                    hospitalCost,
                    economy.Coins,
                    "공사 중 비앵커 철거도 목표 타입 Hospital 단가로 환불해야 한다");
                Assert.AreEqual(TileType.Empty, engine.GetTileType(anchor));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hospitalData);
            }
        }

        [Test]
        public void LegacySave_RestoresSpecialBuildingAndVisitsAtWorldOrigin()
        {
            BuildingCatalogSO catalog =
                AssetDatabase.LoadAssetAtPath<BuildingCatalogSO>(CatalogPath);
            Assert.NotNull(catalog);

            GameObject serviceObject = null;

            try
            {
                var worldGrid = new TestWorldGridAccess();
                SimConfig config = SimConfig.Default();
                var events = new SimEventHub();
                var engine = new SimEngine(config, events, worldGrid);
                var save = new SaveService(
                    engine,
                    new JsonSaveRepository(),
                    new SystemSaveClock(),
                    worldGridAccess: worldGrid);
                var services = new CityFlowServices(
                    events,
                    engine,
                    engine,
                    save,
                    stats: engine);

                serviceObject = new GameObject(
                    "LegacySpecialBuildingWorldMigrationTest");
                ResearchUnlockService research =
                    serviceObject.AddComponent<ResearchUnlockService>();
                research.Initialize(services);

                SpecialBuildingService buildingService =
                    serviceObject.AddComponent<SpecialBuildingService>();
                SetPrivateField(buildingService, "catalog", catalog);
                buildingService.Initialize(services);

                services.RegisterPopulation(new TestPopulation(10));
                services.RegisterGameCalendar(new TestCalendar());
                SpecialBuildingVisitService visitService =
                    serviceObject.AddComponent<SpecialBuildingVisitService>();
                visitService.Initialize(services);

                save.RestoreSnapshot(new GameSaveData
                {
                    SaveVersion = SaveConstants.CurrentSaveVersion,
                    GridWidth = 20,
                    GridHeight = 20,
                    Simulation = new SimSaveData
                    {
                        GridWidth = 20,
                        GridHeight = 20,
                        PlacedTiles = new[]
                        {
                            new TileSaveData
                            {
                                X = 2,
                                Y = 3,
                                Type = TileType.SpecialBuilding,
                                Direction = PlacementDirection.East
                            }
                        }
                    },
                    SpecialBuildings = new SpecialBuildingSaveData
                    {
                        Buildings = new[]
                        {
                            new SpecialBuildingInstanceSaveData
                            {
                                BuildingId = "cinema",
                                X = 2,
                                Y = 3,
                                Direction = PlacementDirection.East
                            }
                        }
                    },
                    SpecialBuildingVisits = new SpecialBuildingVisitSaveData
                    {
                        HasState = true,
                        LastProcessedTotalDay = 8L,
                        Statistics = new[]
                        {
                            new SpecialBuildingVisitStatisticsSaveData
                            {
                                BuildingId = "cinema",
                                X = 2,
                                Y = 3,
                                Day = 8L,
                                PlannedToday = 3,
                                TotalPlannedVisits = 19L
                            }
                        }
                    }
                });

                var migratedAnchor = new Vector2Int(92, 93);
                Assert.IsTrue(buildingService.TryGetBuilding(
                    migratedAnchor,
                    out SpecialBuildingInstance building));
                Assert.AreEqual("cinema", building.BuildingId);
                Assert.AreEqual(PlacementDirection.East, building.Direction);
                Assert.IsTrue(visitService.TryGetStatistics(
                    migratedAnchor,
                    out SpecialBuildingVisitStatistics statistics));
                Assert.AreEqual(8L, statistics.Day);
                Assert.AreEqual(3, statistics.PlannedToday);
                Assert.AreEqual(19L, statistics.TotalPlannedVisits);
            }
            finally
            {
                if (serviceObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(serviceObject);
                }

            }
        }

        private static RuntimeContext CreateRuntime(
            BuildingCatalogSO catalog,
            out GameObject serviceObject,
            bool unlockCinema = true,
            float constructionHoursSpecial = 0f)
        {
            SimConfig config = SimConfig.Default();
            config.GridWidth = 20;
            config.GridHeight = 20;
            config.ConstructionHoursSpecial = constructionHoursSpecial;
            if (constructionHoursSpecial > 0f)
            {
                config.DayLengthSeconds = 24f;
            }
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
                UnlockCinemaForTest(research);
            }

            SpecialBuildingService service =
                serviceObject.AddComponent<SpecialBuildingService>();
            SetPrivateField(service, "catalog", catalog);
            service.Initialize(services);

            return new RuntimeContext(
                engine,
                save,
                service,
                research,
                services);
        }

        private static void UnlockCinemaForTest(ResearchUnlockService research)
        {
            research.RestoreSnapshot(new ResearchSaveData
            {
                UnlockedResearchIds = new[]
                {
                    "research_building_coffee_shop",
                    "research_building_video_store",
                    "research_building_cinema"
                }
            });
            Assert.IsTrue(research.IsUnlocked(
                "research_building_cinema"));
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
                ResearchUnlockService research,
                CityFlowServices services)
            {
                Engine = engine;
                Save = save;
                Service = service;
                Research = research;
                Services = services;
            }

            public SimEngine Engine { get; }
            public SaveService Save { get; }
            public SpecialBuildingService Service { get; }
            public ResearchUnlockService Research { get; }
            public CityFlowServices Services { get; }
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

        private sealed class TestWorldGridAccess : IWorldGridAccess
        {
            public int WorldWidth => 200;
            public int WorldHeight => 200;
            public int ChunkSize => 10;
            public int ChunkColumns => 20;
            public int ChunkRows => 20;
            public Vector2Int InitialPlayableOrigin => new(90, 90);
            public Vector2Int InitialPlayableSize => new(20, 20);

            public event Action<GridChunkId> ChunkUnlocked
            {
                add { }
                remove { }
            }

            public event Action AccessRestored
            {
                add { }
                remove { }
            }

            public bool IsInsideWorld(Vector2Int tile) =>
                tile.x >= 0 && tile.x < WorldWidth &&
                tile.y >= 0 && tile.y < WorldHeight;

            public bool IsTileUnlocked(Vector2Int tile) =>
                IsAreaUnlocked(tile, Vector2Int.one);

            public bool IsChunkUnlocked(GridChunkId chunk) =>
                chunk.X is 9 or 10 && chunk.Y is 9 or 10;

            public bool IsAreaUnlocked(
                Vector2Int anchor,
                Vector2Int footprint)
            {
                Vector2Int max = anchor + footprint;
                return anchor.x >= 90 && anchor.y >= 90 &&
                       max.x <= 110 && max.y <= 110;
            }

            public bool TryGetChunkId(
                Vector2Int tile,
                out GridChunkId chunk)
            {
                if (!IsInsideWorld(tile))
                {
                    chunk = default;
                    return false;
                }

                chunk = new GridChunkId(
                    tile.x / ChunkSize,
                    tile.y / ChunkSize);
                return true;
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
            public float RealSecondsPerGameDay => 24f;
            public int HoursPerDay => 24;
            public float TimeOfDay01 => Hour / 24f;

            public event Action<int> HourChanged;
            public event Action<int> DayChanged;
            public event Action<int> MonthChanged;

            public void AdvanceDay()
            {
                Day++;
                TotalDays++;
                DayChanged?.Invoke(Day);
            }

            public void SetHour(int hour)
            {
                Hour = Mathf.Clamp(hour, 0, 23);
                HourChanged?.Invoke(Hour);
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
