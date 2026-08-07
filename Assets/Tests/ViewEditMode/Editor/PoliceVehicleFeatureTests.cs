using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Content.Transit;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.Sim;
using CityFlow.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CityFlow.Tests.ViewEditMode
{
    public sealed class PoliceVehicleFeatureTests
    {
        private const string ConfigPath =
            "Assets/05_ScriptableObjects/CityFlow/Police/PoliceDispatchConfig.asset";
        private const string ContentPrefabPath =
            "Assets/02_Prefabs/Vehicles/PoliceContent.prefab";
        private const string VehiclePrefabPath =
            "Assets/02_Prefabs/Vehicles/PoliceVehicle.prefab";
        private const string VisualPrefabPath =
            "Assets/02_Prefabs/Vehicles/PoliceVehicleVisual.prefab";
        private const string StandardFootprintPath =
            "Assets/05_ScriptableObjects/CityFlow/Traffic/StandardVehicleFootprint.asset";

        [Test]
        public void ContentPrefab_IsReadyForSinglePrefabIntegration()
        {
            GameObject content =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    ContentPrefabPath);
            PoliceDispatchConfigSO config =
                AssetDatabase.LoadAssetAtPath<
                    PoliceDispatchConfigSO>(ConfigPath);

            Assert.That(content, Is.Not.Null);
            Assert.That(config, Is.Not.Null);

            PoliceCallSystem calls =
                content.GetComponent<PoliceCallSystem>();
            PoliceDispatchService dispatch =
                content.GetComponent<PoliceDispatchService>();
            PolicePatrolScheduler patrol =
                content.GetComponent<PolicePatrolScheduler>();

            Assert.That(calls, Is.Not.Null);
            Assert.That(dispatch, Is.Not.Null);
            Assert.That(patrol, Is.Not.Null);

            SerializedObject callValues = new(calls);
            SerializedObject dispatchValues = new(dispatch);
            SerializedObject patrolValues = new(patrol);
            Assert.That(
                callValues.FindProperty("config")
                    .objectReferenceValue,
                Is.SameAs(config));
            Assert.That(
                dispatchValues.FindProperty("callSystem")
                    .objectReferenceValue,
                Is.SameAs(calls));
            Assert.That(
                dispatchValues.FindProperty("config")
                    .objectReferenceValue,
                Is.SameAs(config));
            Assert.That(
                dispatchValues.FindProperty("policeVehiclePrefab")
                    .objectReferenceValue,
                Is.Not.Null);
            Assert.That(
                callValues.FindProperty("patrolScheduler")
                    .objectReferenceValue,
                Is.SameAs(patrol));
            Assert.That(
                dispatchValues.FindProperty("patrolScheduler")
                    .objectReferenceValue,
                Is.SameAs(patrol));
            Assert.That(
                patrolValues.FindProperty("config")
                    .objectReferenceValue,
                Is.SameAs(config));
        }

        [Test]
        public void VehiclePrefab_UsesSharedRoadTrafficComponents()
        {
            GameObject vehicle =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    VehiclePrefabPath);

            Assert.That(vehicle, Is.Not.Null);
            Assert.That(vehicle.GetComponent<BusRoute>(), Is.Not.Null);
            Assert.That(
                vehicle.GetComponent<PoliceVehicleAgent>(),
                Is.Not.Null);
            Assert.That(
                vehicle.GetComponent<AmbulanceWorldView>(),
                Is.Not.Null);
        }

        [Test]
        public void Config_UsesStandardPassengerCarFootprint()
        {
            PoliceDispatchConfigSO config =
                AssetDatabase.LoadAssetAtPath<
                    PoliceDispatchConfigSO>(ConfigPath);
            VehicleFootprintProfileSO standard =
                AssetDatabase.LoadAssetAtPath<
                    VehicleFootprintProfileSO>(
                    StandardFootprintPath);

            Assert.That(config, Is.Not.Null);
            Assert.That(standard, Is.Not.Null);
            Assert.That(config.VehiclesPerStation, Is.EqualTo(2));
            Assert.That(config.EnableDailyPatrol, Is.True);
            Assert.That(config.PatrolStartHour, Is.EqualTo(10));
            Assert.That(config.PatrolAreaSize, Is.EqualTo(40));
            Assert.That(config.PatrolVehiclesPerStation, Is.EqualTo(1));
            Assert.That(
                config.VehicleFootprint,
                Is.EqualTo(standard.Footprint));
        }

        [Test]
        public void VisualPrefab_MatchesConfiguredFootprintAfterScaling()
        {
            PoliceDispatchConfigSO config =
                AssetDatabase.LoadAssetAtPath<
                    PoliceDispatchConfigSO>(ConfigPath);
            GameObject visualPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    VisualPrefabPath);
            GameObject instance = Object.Instantiate(visualPrefab);

            try
            {
                instance.transform.localScale =
                    AmbulanceWorldView.CalculateVisualScale(
                        instance.transform,
                        config,
                        1f);

                Renderer renderer =
                    instance.GetComponentInChildren<Renderer>(true);
                Assert.That(renderer, Is.Not.Null);
                Assert.That(
                    renderer.bounds.size.x,
                    Is.EqualTo(
                            config.VehicleFootprint.LengthTiles)
                        .Within(0.001f));
                Assert.That(
                    renderer.bounds.size.y,
                    Is.EqualTo(
                            config.VehicleFootprint.WidthTiles)
                        .Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void CallSnapshot_RoundTripsAssignmentAndRuntimeState()
        {
            PoliceDispatchConfigSO config =
                AssetDatabase.LoadAssetAtPath<
                    PoliceDispatchConfigSO>(ConfigPath);
            GameObject owner = new("Police Save Test");

            try
            {
                PoliceCallSystem system =
                    owner.AddComponent<PoliceCallSystem>();
                PolicePatrolScheduler patrol =
                    owner.AddComponent<PolicePatrolScheduler>();
                SerializedObject values = new(system);
                values.FindProperty("config")
                    .objectReferenceValue = config;
                values.FindProperty("patrolScheduler")
                    .objectReferenceValue = patrol;
                values.ApplyModifiedPropertiesWithoutUndo();

                var source = new PoliceDispatchSaveData
                {
                    NextCallId = 18,
                    HasLastPatrolTotalDay = true,
                    LastPatrolTotalDay = 12,
                    ActiveCalls = new[]
                    {
                        new PoliceCallEntrySaveData
                        {
                            CallId = 17,
                            ExternalRequestId = "crime_17",
                            TargetX = 104,
                            TargetY = 97,
                            StationX = 99,
                            StationY = 92,
                            AssignedVehicleSlot = 1,
                            State = (int)PoliceCallState
                                .VehicleReturningAfterFailure,
                            HandlingSeconds = 4f,
                            RemainingHandlingSeconds = 1.5f,
                            FailureReason = (int)
                                PoliceCallFailureReason
                                    .DestinationUnreachable
                        }
                    }
                };

                system.RestoreSnapshot(source);
                PoliceDispatchSaveData roundTrip =
                    system.CreateSnapshot();

                Assert.That(roundTrip.NextCallId, Is.EqualTo(18));
                Assert.That(roundTrip.HasLastPatrolTotalDay, Is.True);
                Assert.That(roundTrip.LastPatrolTotalDay, Is.EqualTo(12));
                Assert.That(roundTrip.ActiveCalls, Has.Length.EqualTo(1));
                PoliceCallEntrySaveData restored =
                    roundTrip.ActiveCalls[0];
                Assert.That(restored.CallId, Is.EqualTo(17));
                Assert.That(
                    restored.ExternalRequestId,
                    Is.EqualTo("crime_17"));
                Assert.That(restored.TargetX, Is.EqualTo(104));
                Assert.That(restored.TargetY, Is.EqualTo(97));
                Assert.That(restored.StationX, Is.EqualTo(99));
                Assert.That(restored.StationY, Is.EqualTo(92));
                Assert.That(restored.AssignedVehicleSlot, Is.EqualTo(1));
                Assert.That(
                    restored.State,
                    Is.EqualTo((int)PoliceCallState
                        .VehicleReturningAfterFailure));
                Assert.That(
                    restored.FailureReason,
                    Is.EqualTo((int)PoliceCallFailureReason
                        .DestinationUnreachable));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void PatrolPlanner_BuildsLoopWithinFourTwentyTileChunks()
        {
            var roads = new HashSet<Vector2Int>
            {
                new(100, 101),
                new(101, 101),
                new(101, 102),
                new(100, 102),
                new(150, 150)
            };
            var planner = new PolicePatrolRoutePlanner();

            bool built = planner.TryBuildPlan(
                new Vector2Int(100, 100),
                new Vector2Int(100, 101),
                40,
                new RoadTileData(roads),
                new TestWorldGridAccess(),
                out PolicePatrolPlan plan);

            Assert.That(built, Is.True);
            Assert.That(plan.UsedLoop, Is.True);
            Assert.That(plan.ScannedChunkCount, Is.EqualTo(4));
            Assert.That(plan.ScannedTileCount, Is.EqualTo(1600));
            Assert.That(plan.Route.Tiles[0], Is.EqualTo(
                new Vector2Int(100, 101)));
            Assert.That(
                plan.Route.Tiles[plan.Route.TileCount - 1],
                Is.EqualTo(new Vector2Int(100, 101)));
            Assert.That(
                plan.Route.Tiles,
                Has.None.EqualTo(new Vector2Int(150, 150)));
        }

        [Test]
        public void PatrolPlanner_BuildsOutAndBackOnDeadEndRoad()
        {
            var roads = new HashSet<Vector2Int>
            {
                new(100, 101),
                new(100, 102),
                new(100, 103)
            };
            var planner = new PolicePatrolRoutePlanner();

            bool built = planner.TryBuildPlan(
                new Vector2Int(100, 100),
                new Vector2Int(100, 101),
                40,
                new RoadTileData(roads),
                new TestWorldGridAccess(),
                out PolicePatrolPlan plan);

            Assert.That(built, Is.True);
            Assert.That(plan.UsedLoop, Is.False);
            CollectionAssert.AreEqual(
                new[]
                {
                    new Vector2Int(100, 101),
                    new Vector2Int(100, 102),
                    new Vector2Int(100, 103),
                    new Vector2Int(100, 102),
                    new Vector2Int(100, 101)
                },
                plan.Route.Tiles);
        }

        [Test]
        public void PatrolRoute_UsesInjectedPathAndSharedRoadTraffic()
        {
            SimConfig config = SimConfig.Default();
            config.AutoDetectSignals = false;
            var events = new SimEventHub();
            var engine = new SimEngine(config, events);
            var home = new Vector2Int(2, 2);
            var roadPath = new[]
            {
                new Vector2Int(2, 1),
                new Vector2Int(3, 1),
                new Vector2Int(4, 1),
                new Vector2Int(3, 1),
                new Vector2Int(2, 1)
            };
            GameObject owner = new("Police Patrol Route Test");

            try
            {
                for (int index = 0; index < 3; index++)
                {
                    Assert.That(
                        engine.Place(
                            new Vector2Int(2 + index, 1),
                            TileType.Road),
                        Is.True);
                }

                var services = new CityFlowServices(
                    events,
                    engine,
                    engine,
                    stats: engine);
                services.RegisterRoadTraffic(engine.RoadTraffic);

                BusRoute route = owner.AddComponent<BusRoute>();
                route.ConfigureRoadTrafficAgent(
                    RoadTrafficAgentKind.FeatureVehicle,
                    VehicleFootprint.StandardDefault,
                    holdAtDestination: true);
                route.Initialize(services);

                Assert.That(
                    route.ConfigurePreplannedRoadRoute(
                        home,
                        new RoadRoutePlan(roadPath)),
                    Is.True);
                Assert.That(route.StartRoute(), Is.True);
                Assert.That(
                    engine.RoadTraffic.RegisteredAgentCount,
                    Is.EqualTo(1));
                CollectionAssert.AreEqual(
                    new[]
                    {
                        home,
                        roadPath[0],
                        roadPath[1],
                        roadPath[2],
                        roadPath[3],
                        roadPath[4],
                        home
                    },
                    route.CurrentRoadPath);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        private sealed class RoadTileData : IReadOnlyTileData
        {
            private readonly HashSet<Vector2Int> roads;

            public RoadTileData(HashSet<Vector2Int> roads)
            {
                this.roads = roads ?? throw new ArgumentNullException(
                    nameof(roads));
            }

            public CongestionLevel GetCongestion(Vector2Int tile) =>
                CongestionLevel.Free;

            public float GetDensity01(Vector2Int tile) => 0f;

            public int GetQueueCount(Vector2Int tile, Dir entryDir) => 0;

            public TileType GetTileType(Vector2Int tile) =>
                roads.Contains(tile) ? TileType.Road : TileType.Empty;

            public PlacementDirection GetDirection(Vector2Int tile) =>
                PlacementDirection.North;

            public Vector2Int GetFootprintSize(TileType type) =>
                Vector2Int.one;

            public bool TryGetFootprintAnchor(
                Vector2Int tile,
                out Vector2Int anchor)
            {
                anchor = tile;
                return false;
            }

            public bool IsFootprintAnchor(Vector2Int tile) => false;

            public bool TryGetConstructionProgress01(
                Vector2Int tile,
                out float progress01)
            {
                progress01 = 0f;
                return false;
            }

            public bool TryGetConstructionTargetType(
                Vector2Int tile,
                out TileType targetType)
            {
                targetType = TileType.Empty;
                return false;
            }
        }

        private sealed class TestWorldGridAccess : IWorldGridAccess
        {
            public int WorldWidth => 200;
            public int WorldHeight => 200;
            public int ChunkSize => 20;
            public int ChunkColumns => 10;
            public int ChunkRows => 10;
            public Vector2Int InitialPlayableOrigin =>
                new(90, 90);
            public Vector2Int InitialPlayableSize =>
                new(20, 20);

            public event Action<GridChunkId> ChunkUnlocked;
            public event Action AccessRestored;

            public bool IsInsideWorld(Vector2Int tile) =>
                tile.x >= 0 && tile.x < WorldWidth &&
                tile.y >= 0 && tile.y < WorldHeight;

            public bool IsTileUnlocked(Vector2Int tile) =>
                IsInsideWorld(tile);

            public bool IsChunkUnlocked(GridChunkId chunk) =>
                chunk.X >= 0 && chunk.X < ChunkColumns &&
                chunk.Y >= 0 && chunk.Y < ChunkRows;

            public bool IsAreaUnlocked(
                Vector2Int anchor,
                Vector2Int footprint)
            {
                Vector2Int max = anchor + footprint - Vector2Int.one;
                return IsInsideWorld(anchor) && IsInsideWorld(max);
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
    }
}
