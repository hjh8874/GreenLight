using System.Reflection;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace CityFlow.Sim.Tests
{
    public sealed class ContentFeatureLogicTests
    {
        [Test]
        public void BusRuntime_BoardAndLeave_RespectCapacity()
        {
            var runtime = new BusRuntime(5);

            Assert.That(runtime.Board(8), Is.EqualTo(5));
            Assert.That(
                runtime.CurrentPassengers,
                Is.EqualTo(5));
            Assert.That(runtime.Board(1), Is.Zero);
            Assert.That(runtime.Leave(3), Is.EqualTo(3));
            Assert.That(
                runtime.CurrentPassengers,
                Is.EqualTo(2));
        }

        [Test]
        public void EmergencyIncident_CompletesExpectedFlow()
        {
            var incident = new EmergencyIncident(
                7,
                new Vector2Int(2, 6),
                TileType.House);

            incident.Dispatch(
                new Vector2Int(14, 6),
                1f);
            Assert.That(
                incident.State,
                Is.EqualTo(
                    EmergencyIncidentState.AmbulanceOutbound));

            Assert.That(incident.Advance(1f), Is.True);
            incident.BeginTreatment(0.5f);
            Assert.That(incident.Advance(0.5f), Is.True);
            incident.BeginReturn(1f);
            Assert.That(incident.Advance(1f), Is.True);
            incident.Resolve();

            Assert.That(
                incident.State,
                Is.EqualTo(
                    EmergencyIncidentState.Resolved));
            Assert.That(incident.IsFinished, Is.True);
        }

        [Test]
        public void EmergencyIncident_NegativeTime_DoesNotAdvance()
        {
            var incident = new EmergencyIncident(
                1,
                Vector2Int.zero,
                TileType.Office);

            incident.Dispatch(Vector2Int.one, 1f);

            Assert.That(
                incident.Advance(-10f),
                Is.False);
            Assert.That(
                incident.StateRemainingSeconds,
                Is.EqualTo(1f));
        }

        [Test]
        public void PrototypeAssets_AreReadyForSceneIntegration()
        {
            const string prefabPath =
                "Assets/02_Prefabs/Vehicles/CityBusContent.prefab";
            const string scenePath =
                "Assets/00_Scenes/Debug/PR151_ContentPrototype_cmt.unity";

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    prefabPath);
            SceneAsset scene =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    scenePath);
            BusDefinitionSO busConfig =
                AssetDatabase.LoadAssetAtPath<
                    BusDefinitionSO>(
                    "Assets/05_ScriptableObjects/CityFlow/Transit/CityBusDefinition.asset");
            Object busStopData =
                AssetDatabase.LoadMainAssetAtPath(
                    "Assets/Resources/CityFlow/InfrastructureData/BusStopData.asset");
            EmergencyIncidentConfigSO emergencyConfig =
                AssetDatabase.LoadAssetAtPath<
                    EmergencyIncidentConfigSO>(
                    "Assets/05_ScriptableObjects/CityFlow/Emergency/EmergencyIncidentConfig.asset");

            Assert.That(prefab, Is.Not.Null);
            Assert.That(scene, Is.Not.Null);
            Assert.That(busConfig, Is.Not.Null);
            Assert.That(emergencyConfig, Is.Not.Null);
            Assert.That(busStopData, Is.Not.Null);
            Assert.That(
                GameObjectUtility
                    .GetMonoBehavioursWithMissingScriptCount(
                        prefab),
                Is.Zero);

            MonoBehaviour[] components =
                prefab.GetComponents<MonoBehaviour>();
            Assert.That(
                HasComponentNamed(
                    components,
                    "CityBusService"),
                Is.True);
            Assert.That(
                HasComponentNamed(
                    components,
                    "EmergencyIncidentSystem"),
                Is.True);
            Assert.That(
                HasComponentNamed(
                    components,
                    "ContentFeaturePrototypeView"),
                Is.False);
            Assert.That(
                HasComponentNamed(
                    components,
                    "CityBusStopWorldView"),
                Is.True);
            Assert.That(
                HasComponentNamed(
                    components,
                    "ContentFeaturePrototypeScenario"),
                Is.False);
        }

        [Test]
        public void BusStopInfrastructure_RequiresRoadsideAndPersists()
        {
            SimConfig config = SimConfig.Default();
            var source = new SimEngine(config, new SimEventHub());
            Vector2Int road = new(2, 2);
            Vector2Int approachRoad = new(3, 2);
            Vector2Int stop = new(2, 3);
            Vector2Int invalidEndpointStop = new(1, 2);

            Assert.That(source.Place(road, TileType.Road), Is.True);
            Assert.That(
                source.Place(approachRoad, TileType.Road),
                Is.True);
            Assert.That(source.CanPlaceBusStop(stop), Is.True);
            Assert.That(
                source.CanPlaceBusStop(invalidEndpointStop),
                Is.False,
                "A stop without a right-lane arrival approach must be rejected.");
            Assert.That(source.TryPlaceBusStop(stop), Is.True);
            Assert.That(source.TryPlaceBusStop(stop), Is.False);
            Assert.That(
                source.CanPlaceBusStop(new Vector2Int(8, 8)),
                Is.False);

            SimSaveData snapshot = source.CreateSnapshot();
            var restored = new SimEngine(config, new SimEventHub());
            restored.RestoreSnapshot(snapshot);

            Assert.That(restored.BusStopTiles.Count, Is.EqualTo(1));
            Assert.That(restored.BusStopTiles[0], Is.EqualTo(stop));
            Assert.That(
                restored.CanPlace(stop, TileType.Road),
                Is.False);
            Assert.That(
                restored.Place(stop, TileType.Road),
                Is.False);
            Assert.That(restored.TryRemoveBusStop(stop), Is.True);
            Assert.That(restored.BusStopTiles.Count, Is.Zero);
        }

        [Test]
        public void BusStopInfrastructure_BlocksOverlappingPlacementAndLastAccessRemoval()
        {
            SimConfig config = SimConfig.Default();
            var source = new SimEngine(config, new SimEventHub());
            Vector2Int primaryRoad = new(2, 2);
            Vector2Int primaryApproach = new(3, 2);
            Vector2Int alternativeRoad = new(3, 3);
            Vector2Int alternativeApproach = new(3, 4);
            Vector2Int stop = new(2, 3);
            Vector2Int buildingAnchor = new(1, 3);

            Assert.That(
                source.Place(primaryRoad, TileType.Road),
                Is.True);
            Assert.That(
                source.Place(primaryApproach, TileType.Road),
                Is.True);
            Assert.That(source.TryPlaceBusStop(stop), Is.True);

            Assert.That(
                source.CanPlace(stop, TileType.Road),
                Is.False);
            Assert.That(
                source.Place(stop, TileType.Road),
                Is.False);
            Assert.That(
                source.CanPlace(
                    buildingAnchor,
                    TileType.House),
                Is.False);
            Assert.That(
                source.Place(
                    buildingAnchor,
                    TileType.House),
                Is.False);

            Assert.That(
                source.Remove(primaryRoad),
                Is.False,
                "The last access road must remain while the stop is installed.");
            Assert.That(
                source.Remove(primaryApproach),
                Is.False,
                "The required approach road must remain while the stop is installed.");
            Assert.That(
                source.Place(alternativeRoad, TileType.Road),
                Is.True);
            Assert.That(
                source.Place(alternativeApproach, TileType.Road),
                Is.True);
            Assert.That(source.Remove(primaryRoad), Is.True);
            Assert.That(source.BusStopTiles, Does.Contain(stop));

            SimSaveData snapshot = source.CreateSnapshot();
            var restored =
                new SimEngine(config, new SimEventHub());
            restored.RestoreSnapshot(snapshot);

            Assert.That(
                restored.BusStopTiles,
                Does.Contain(stop));
            Assert.That(
                restored.CanPlace(stop, TileType.Road),
                Is.False);
            Assert.That(
                restored.Place(
                    buildingAnchor,
                    TileType.House),
                Is.False);
            Assert.That(
                restored.Remove(alternativeRoad),
                Is.False);
        }

        [Test]
        public void BusStopInfrastructure_InvalidSavedStop_LogsRestoreWarning()
        {
            SimConfig config = SimConfig.Default();
            var restored =
                new SimEngine(config, new SimEventHub());
            Vector2Int invalidStop = new(8, 8);
            var snapshot = new SimSaveData
            {
                BusStops = new[]
                {
                    new BusStopSaveData
                    {
                        X = invalidStop.x,
                        Y = invalidStop.y
                    }
                }
            };

            LogAssert.Expect(
                LogType.Warning,
                $"[SimEngine] 저장된 버스 정류장 {invalidStop}을(를) " +
                "복원할 수 없습니다. 빈 타일과 인접 도로를 확인하세요.");

            restored.RestoreSnapshot(snapshot);

            Assert.That(restored.BusStopTiles, Is.Empty);
        }

        [Test]
        public void BusStopInfrastructure_RestoresLegacySingleRoadStopOnly()
        {
            SimConfig config = SimConfig.Default();
            var source =
                new SimEngine(config, new SimEventHub());
            Vector2Int accessRoad = new(2, 2);
            Vector2Int unrelatedRoad = new(8, 8);
            Vector2Int legacyStop = new(2, 3);

            Assert.That(
                source.Place(accessRoad, TileType.Road),
                Is.True);
            Assert.That(
                source.Place(unrelatedRoad, TileType.Road),
                Is.True);
            Assert.That(
                source.CanPlaceBusStop(legacyStop),
                Is.False,
                "New stops must still require a valid right-lane approach.");

            SimSaveData snapshot = source.CreateSnapshot();
            snapshot.BusStops = new[]
            {
                new BusStopSaveData
                {
                    X = legacyStop.x,
                    Y = legacyStop.y
                }
            };

            var restored =
                new SimEngine(config, new SimEventHub());
            restored.RestoreSnapshot(snapshot);

            Assert.That(
                restored.BusStopTiles,
                Does.Contain(legacyStop),
                "A legacy stop with one adjacent road must survive restore.");
            Assert.That(
                restored.CanPlaceBusStop(legacyStop),
                Is.False);
            Assert.That(
                restored.Remove(unrelatedRoad),
                Is.True,
                "A legacy stop must not block unrelated road removal.");
            Assert.That(
                restored.Remove(accessRoad),
                Is.False,
                "The legacy stop's last access road must remain.");
        }

        [Test]
        public void IntegrationPrefab_InitializesWithoutChangingExistingTiles()
        {
            const string prefabPath =
                "Assets/02_Prefabs/Vehicles/CityBusContent.prefab";
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    prefabPath);
            Assert.That(prefab, Is.Not.Null);

            SimConfig config = SimConfig.Default();
            var events = new SimEventHub();
            var engine = new SimEngine(config, events);
            Vector2Int stop = new(2, 3);

            PlaceRoadLoop(engine);
            Assert.That(
                engine.Place(
                    new Vector2Int(8, 8),
                    TileType.House),
                Is.True);
            Assert.That(engine.TryPlaceBusStop(stop), Is.True);

            TileType[,] before =
                CaptureTiles(engine, config);
            GameObject instance =
                Object.Instantiate(prefab);

            try
            {
                Assert.That(
                    HasComponentNamed(
                        instance.GetComponents<MonoBehaviour>(),
                        "ContentFeaturePrototypeScenario"),
                    Is.False);

                System.Type servicesType =
                    System.Type.GetType(
                    "CityFlow.Bootstrap.CityFlowServices, Assembly-CSharp");
                Assert.That(servicesType, Is.Not.Null);
                object services =
                    System.Activator.CreateInstance(
                    servicesType,
                    events,
                    engine,
                    engine,
                    null,
                    null,
                    engine);
                MonoBehaviour[] consumers =
                    instance.GetComponents<MonoBehaviour>();

                for (int i = 0; i < consumers.Length; i++)
                {
                    MethodInfo initialize =
                        consumers[i].GetType().GetMethod(
                            "Initialize",
                            BindingFlags.Instance |
                            BindingFlags.Public,
                            null,
                            new[] { servicesType },
                            null);

                    if (initialize != null)
                    {
                        initialize.Invoke(
                            consumers[i],
                            new[] { services });
                    }
                }

                AssertTilesEqual(
                    before,
                    engine,
                    config);

                MonoBehaviour bus = FindComponentNamed(
                    consumers,
                    "CityBusService");
                Assert.That(bus, Is.Not.Null);
                object runtime =
                    bus.GetType()
                        .GetProperty("Runtime")
                        ?.GetValue(bus);
                Assert.That(runtime, Is.Not.Null);
                object state =
                    runtime.GetType()
                        .GetProperty("State")
                        ?.GetValue(runtime);
                Assert.That(
                    state?.ToString(),
                    Is.EqualTo("Moving"));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void BusStopRoutePolicy_StopsAtPassByStation()
        {
            var accessRoads = new[]
            {
                new Vector2Int(9, 9),
                new Vector2Int(6, 11),
                new Vector2Int(12, 11),
                new Vector2Int(8, 15)
            };

            int encountered = BusStopRoutePolicy
                .FindStopIndexAtRoad(
                    accessRoads,
                    currentStopIndex: 1,
                    scheduledStopIndex: 2,
                    roadTile: new Vector2Int(8, 15));

            Assert.That(encountered, Is.EqualTo(3));
        }

        private static bool HasComponentNamed(
            MonoBehaviour[] components,
            string typeName)
        {
            return FindComponentNamed(
                       components,
                       typeName) != null;
        }

        private static MonoBehaviour FindComponentNamed(
            MonoBehaviour[] components,
            string typeName)
        {
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null &&
                    components[i].GetType().Name == typeName)
                {
                    return components[i];
                }
            }

            return null;
        }

        private static void PlaceRoadLoop(SimEngine engine)
        {
            var roads = new[]
            {
                new Vector2Int(1, 2),
                new Vector2Int(2, 2),
                new Vector2Int(3, 2),
                new Vector2Int(3, 3),
                new Vector2Int(3, 4),
                new Vector2Int(2, 4),
                new Vector2Int(1, 4),
                new Vector2Int(1, 3)
            };

            for (int i = 0; i < roads.Length; i++)
            {
                Assert.That(
                    engine.Place(roads[i], TileType.Road),
                    Is.True);
            }
        }

        private static TileType[,] CaptureTiles(
            SimEngine engine,
            SimConfig config)
        {
            var result = new TileType[
                config.GridWidth,
                config.GridHeight];

            for (int y = 0; y < config.GridHeight; y++)
            {
                for (int x = 0; x < config.GridWidth; x++)
                {
                    result[x, y] =
                        engine.GetTileType(
                            new Vector2Int(x, y));
                }
            }

            return result;
        }

        private static void AssertTilesEqual(
            TileType[,] expected,
            SimEngine actual,
            SimConfig config)
        {
            for (int y = 0; y < config.GridHeight; y++)
            {
                for (int x = 0; x < config.GridWidth; x++)
                {
                    Vector2Int tile = new(x, y);
                    Assert.That(
                        actual.GetTileType(tile),
                        Is.EqualTo(expected[x, y]),
                        $"Integration prefab changed tile {tile}.");
                }
            }
        }
    }
}
