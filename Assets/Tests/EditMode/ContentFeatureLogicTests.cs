using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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
                "Assets/02_Prefabs/PR151_ContentFeaturePrototype.prefab";
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
                    "Assets/05_ScriptableObjects/PR151_CityBusDefinition.asset");
            Object busStopData =
                AssetDatabase.LoadMainAssetAtPath(
                    "Assets/Resources/CityFlow/InfrastructureData/BusStopData.asset");
            EmergencyIncidentConfigSO emergencyConfig =
                AssetDatabase.LoadAssetAtPath<
                    EmergencyIncidentConfigSO>(
                    "Assets/05_ScriptableObjects/PR151_EmergencyIncidentConfig.asset");

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
        }

        [Test]
        public void BusStopInfrastructure_RequiresRoadsideAndPersists()
        {
            SimConfig config = SimConfig.Default();
            var source = new SimEngine(config, new SimEventHub());
            Vector2Int road = new(2, 2);
            Vector2Int stop = new(2, 3);

            Assert.That(source.Place(road, TileType.Road), Is.True);
            Assert.That(source.CanPlaceBusStop(stop), Is.True);
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
            Assert.That(restored.TryRemoveBusStop(stop), Is.True);
            Assert.That(restored.BusStopTiles.Count, Is.Zero);
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
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null &&
                    components[i].GetType().Name == typeName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
