using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Content.Transit;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.DebugTools;
using CityFlow.Save;
using CityFlow.Sim;
using CityFlow.View;
using CityFlow.ViewKit;
using NUnit.Framework;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CityFlow.Tests.ViewEditMode
{
    public sealed class AmbulanceFeatureTests
    {
        [Test]
        public void RuntimeAssets_UseEmergencyVehicleSpeed()
        {
            EmergencyIncidentConfigSO config =
                AssetDatabase.LoadAssetAtPath<
                    EmergencyIncidentConfigSO>(
                    "Assets/05_ScriptableObjects/CityFlow/Emergency/EmergencyIncidentConfig.asset");
            GameObject vehiclePrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/02_Prefabs/Vehicles/AmbulanceVehicle.prefab");

            Assert.That(config, Is.Not.Null);
            Assert.That(vehiclePrefab, Is.Not.Null);
            Assert.That(
                config.TravelSecondsPerTile,
                Is.EqualTo(0.45f).Within(0.0001f));
            Assert.That(
                config.AmbulancesPerHospital,
                Is.EqualTo(2));
            Assert.That(
                config.MaximumAutomaticIncidentsPerDay,
                Is.EqualTo(1));
            Assert.That(
                config.IncidentDefinitions.Count,
                Is.EqualTo(6));
            Assert.That(
                config.IncidentDefinitions[0]
                    .ResponseDeadlineHours,
                Is.EqualTo(3));
            Assert.That(
                config.IncidentDefinitions[5]
                    .ResponseDeadlineHours,
                Is.EqualTo(24));

            BusRoute route =
                vehiclePrefab.GetComponent<BusRoute>();
            Assert.That(route, Is.Not.Null);
            Assert.That(
                route.SecondsPerTile,
                Is.EqualTo(
                    config.TravelSecondsPerTile)
                    .Within(0.0001f));
        }

        [Test]
        public void RouteDefaults_StopBeforeDestinationParkingEntrance()
        {
            GameObject vehiclePrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/02_Prefabs/Vehicles/AmbulanceVehicle.prefab");
            GameObject instance =
                Object.Instantiate(vehiclePrefab);

            try
            {
                AmbulanceVehicleAgent agent =
                    instance.GetComponent<
                        AmbulanceVehicleAgent>();
                BusRoute route =
                    instance.GetComponent<BusRoute>();
                Assert.That(agent, Is.Not.Null);
                Assert.That(route, Is.Not.Null);

                typeof(AmbulanceVehicleAgent)
                    .GetMethod(
                        "ConfigureRouteDefaults",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic)
                    .Invoke(agent, null);

                Assert.That(
                    route.UseRoadsideStopApproach,
                    Is.True,
                    "The ambulance must stop on the destination road before entering a parking area.");
                Assert.That(
                    route.RoadsideStopSetbackTiles,
                    Is.EqualTo(1),
                    "The destination parking entrance must remain clear for other vehicles.");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void ParkingTransitionDuration_ScalesWithRemainingDistance()
        {
            Assert.That(
                AmbulanceWorldView
                    .CalculateParkingTransitionDuration(
                        remainingDistance: 5f,
                        nominalSpeed: 2f),
                Is.EqualTo(2.5f).Within(0.0001f),
                "A late visual must finish the remaining road distance at vehicle speed instead of crossing several tiles in one fixed-duration snap.");
            Assert.That(
                AmbulanceWorldView
                    .CalculateParkingTransitionDuration(
                        remainingDistance: 0f,
                        nominalSpeed: 2f),
                Is.EqualTo(0.1f).Within(0.0001f));
        }

        [Test]
        public void RuntimeVisualScale_MatchesConfiguredVehicleFootprint()
        {
            EmergencyIncidentConfigSO config =
                AssetDatabase.LoadAssetAtPath<
                    EmergencyIncidentConfigSO>(
                    "Assets/05_ScriptableObjects/CityFlow/Emergency/EmergencyIncidentConfig.asset");
            GameObject visualPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/02_Prefabs/Vehicles/AmbulanceVehicleVisual.prefab");
            GameObject instance =
                Object.Instantiate(visualPrefab);

            try
            {
                instance.transform.localScale =
                    AmbulanceWorldView
                        .CalculateVisualScale(
                            instance.transform,
                            config,
                            1f);
                instance.transform.localRotation =
                    AmbulanceWorldView
                        .CreateRotation(
                            Vector2.right);

                Renderer renderer =
                    instance.GetComponentInChildren<
                        Renderer>(true);
                Assert.That(renderer, Is.Not.Null);
                Assert.That(
                    renderer.bounds.size.x,
                    Is.EqualTo(
                        config.VehicleLengthTiles)
                        .Within(0.001f),
                    "The visible ambulance length must match its traffic footprint.");
                Assert.That(
                    renderer.bounds.size.y,
                    Is.EqualTo(
                        config.VehicleWidthTiles)
                        .Within(0.001f),
                    "The visible ambulance width must match its traffic footprint.");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void ExternalVehicleVisual_UsesSharedVehicleSelection()
        {
            GameObject visualPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/02_Prefabs/Vehicles/AmbulanceVehicleVisual.prefab");
            GameObject cityObject =
                new("VehicleSelectionTestCity");
            GameObject owner =
                new("ExternalVehicleOwner");
            GameObject visual =
                Object.Instantiate(visualPrefab);

            try
            {
                MainCityView cityView =
                    cityObject.AddComponent<MainCityView>();
                cityView.RegisterExternalSelectableVehicle(
                    owner,
                    visual.transform,
                    Vector3.back);

                Collider selectionCollider =
                    visual.GetComponentInChildren<
                        Collider>(true);
                Assert.That(
                    selectionCollider,
                    Is.Not.Null,
                    "External vehicles need the same raycast target as commute cars.");
                Assert.That(
                    cityView.TryResolveSelectableVehicle(
                        selectionCollider.transform,
                        out Transform selectedTarget,
                        out Vector3 localTravelAxis),
                    Is.True);
                Assert.That(
                    selectedTarget,
                    Is.SameAs(visual.transform));
                Assert.That(
                    localTravelAxis,
                    Is.EqualTo(Vector3.back));

                cityView.UnregisterExternalSelectableVehicle(
                    owner);
                Assert.That(
                    cityView.TryResolveSelectableVehicle(
                        selectionCollider.transform,
                        out _,
                        out _),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(visual);
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(cityObject);
            }
        }

        [TestCase(1f, 0f)]
        [TestCase(0f, 1f)]
        [TestCase(-1f, 0f)]
        [TestCase(0f, -1f)]
        public void VisualRotation_IsUprightAndFacesTravelDirection(
            float directionX,
            float directionY)
        {
            Vector2 direction =
                new(directionX, directionY);
            Quaternion rotation =
                AmbulanceWorldView.CreateRotation(direction);
            Vector3 vehicleNose =
                rotation * Vector3.back;
            Vector3 vehicleUp =
                rotation * Vector3.up;

            Assert.That(
                Vector3.Dot(
                    vehicleNose,
                    new Vector3(
                        direction.x,
                        direction.y,
                        0f)),
                Is.GreaterThan(0.999f),
                "The ambulance front must face its travel direction.");
            Assert.That(
                Vector3.Dot(vehicleUp, Vector3.back),
                Is.GreaterThan(0.999f),
                "The ambulance roof must face the game camera.");
        }

        [Test]
        public void TrafficQueueAdvance_DoesNotWaitForVisualTileArrival()
        {
            var owner =
                new GameObject(
                    "AmbulanceQueueAdvance_Test");
            var routeOwner =
                new GameObject(
                    "AmbulanceQueueAdvance_Route");
            owner.SetActive(false);

            try
            {
                AmbulanceWorldView worldView =
                    owner.AddComponent<AmbulanceWorldView>();
                MainCityView cityView =
                    routeOwner.AddComponent<MainCityView>();
                RoutePolyline trafficRoute =
                    cityView.BakeTrafficRoute(
                        new[]
                        {
                            Vector2Int.zero,
                            Vector2Int.right
                        },
                        -0.38f);
                FieldInfo followerField =
                    typeof(AmbulanceWorldView)
                        .GetField(
                            "routeFollower",
                            BindingFlags.Instance |
                            BindingFlags.NonPublic);
                object follower =
                    followerField?.GetValue(worldView);
                MethodInfo setTargetMethod =
                    follower?.GetType()
                        .GetMethod("SetTarget");
                MethodInfo canEnterMethod =
                    typeof(AmbulanceWorldView)
                        .GetMethod(
                            "CanEnterTile",
                            BindingFlags.Instance |
                            BindingFlags.NonPublic);

                Assert.That(trafficRoute, Is.Not.Null);
                Assert.That(follower, Is.Not.Null);
                Assert.That(setTargetMethod, Is.Not.Null);
                Assert.That(canEnterMethod, Is.Not.Null);

                setTargetMethod.Invoke(
                    follower,
                    new object[]
                    {
                        trafficRoute,
                        0f,
                        trafficRoute.Length,
                        false
                    });

                bool canAdvance =
                    (bool)canEnterMethod.Invoke(
                        worldView,
                        new object[]
                        {
                            Vector2Int.zero,
                            Vector2Int.right
                        });

                Assert.That(
                    canAdvance,
                    Is.True,
                    "The queue may grant the next road tile before the visual reaches the current tile center.");
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(routeOwner);
            }
        }

        [Test]
        public void AuthorizedMotion_MaintainsCruiseUntilAuthorityIsHeld()
        {
            RoutePolyline path =
                RoutePolyline.Bake(
                    new BakeInput
                    {
                        Tiles = new[]
                        {
                            Vector2Int.zero,
                            Vector2Int.right,
                            Vector2Int.right * 2
                        },
                        GridOrigin = Vector2Int.zero,
                        TileSize = 1f,
                        LaneOffset = 0.2f,
                        IsRoundabout = _ => false,
                        CornerRadiusFraction = 0.25f,
                        OrbitRadius = 0.35f,
                        SamplesPerSegment = 8
                    });
            System.Type followerType =
                typeof(AmbulanceWorldView)
                    .Assembly
                    .GetType(
                        "CityFlow.View.BufferedRouteFollower");
            Assert.That(followerType, Is.Not.Null);

            object follower =
                System.Activator.CreateInstance(
                    followerType,
                    true);
            MethodInfo setTarget =
                followerType.GetMethod("SetTarget");
            MethodInfo setAuthorizedTarget =
                followerType.GetMethod(
                    "SetAuthorizedTarget");
            MethodInfo markHeld =
                followerType.GetMethod(
                    "MarkAuthorityHeld");
            MethodInfo calculateCandidate =
                followerType.GetMethod(
                    "CalculateCandidateDistance");
            MethodInfo commitCandidate =
                followerType.GetMethod(
                    "CommitCandidate");
            PropertyInfo speedProperty =
                followerType.GetProperty("Speed");
            PropertyInfo distanceProperty =
                followerType.GetProperty(
                    "CurrentDistance");

            Assert.That(setTarget, Is.Not.Null);
            Assert.That(setAuthorizedTarget, Is.Not.Null);
            Assert.That(markHeld, Is.Not.Null);
            Assert.That(calculateCandidate, Is.Not.Null);
            Assert.That(commitCandidate, Is.Not.Null);

            float startDistance =
                path.DistanceAtTile(0);
            float authorityDistance =
                path.DistanceAtTile(1);
            setTarget.Invoke(
                follower,
                new object[]
                {
                    path,
                    startDistance,
                    startDistance,
                    true
                });
            setAuthorizedTarget.Invoke(
                follower,
                new object[]
                {
                    path,
                    startDistance,
                    authorityDistance,
                    false
                });

            const float deltaTime = 0.02f;
            const float nominalSpeed = 4f;
            float currentDistance = startDistance;
            for (int step = 0;
                 step < 100 &&
                 currentDistance <
                 authorityDistance - 0.12f;
                 step++)
            {
                float candidate =
                    (float)calculateCandidate.Invoke(
                        follower,
                        new object[]
                        {
                            deltaTime,
                            nominalSpeed
                        });
                commitCandidate.Invoke(
                    follower,
                    new object[]
                    {
                        candidate,
                        1f
                    });
                currentDistance =
                    (float)distanceProperty.GetValue(
                        follower);
            }

            float cruisingSpeed =
                (float)speedProperty.GetValue(follower);
            markHeld.Invoke(follower, null);
            calculateCandidate.Invoke(
                follower,
                new object[]
                {
                    deltaTime,
                    nominalSpeed
                });
            float brakingSpeed =
                (float)speedProperty.GetValue(follower);

            Assert.That(
                cruisingSpeed,
                Is.GreaterThan(nominalSpeed * 0.9f),
                "An advancing queue authority should be crossed at cruise speed like regular cars.");
            Assert.That(
                brakingSpeed,
                Is.LessThan(cruisingSpeed),
                "The ambulance should brake only after the simulation holds its authority boundary.");
        }

        [Test]
        public void TrafficProjection_UsesSharedQueueSlotAndLinkProgress()
        {
            var owner =
                new GameObject(
                    "AmbulanceTrafficProjection_Test");

            try
            {
                MainCityView cityView =
                    owner.AddComponent<MainCityView>();
                RoutePolyline path =
                    cityView.BakeTrafficRoute(
                        new[]
                        {
                            Vector2Int.zero,
                            Vector2Int.right,
                            Vector2Int.right * 2,
                            Vector2Int.right * 3
                        },
                        -0.38f);
                float queueGap =
                    cityView.VehicleMinHeadway *
                    cityView.TileSize;
                float frontDistance =
                    path.ReprojectDistance(
                        2,
                        0f,
                        0f,
                        -1f,
                        0f,
                        -1f,
                        cityView.RoundaboutTransitionSpanTiles);
                float queuedDistance =
                    path.ReprojectDistance(
                        2,
                        queueGap * 2f,
                        0f,
                        -1f,
                        0f,
                        -1f,
                        cityView.RoundaboutTransitionSpanTiles);
                float linkDistance =
                    path.ReprojectDistance(
                        2,
                        0f,
                        0f,
                        -1f,
                        0.5f,
                        -1f,
                        cityView.RoundaboutTransitionSpanTiles);

                Assert.That(
                    frontDistance - queuedDistance,
                    Is.EqualTo(
                            cityView.VehicleMinHeadway *
                            cityView.TileSize * 2f)
                        .Within(0.0001f),
                    "The ambulance must occupy the same physical queue slots as regular cars.");
                Assert.That(
                    linkDistance,
                    Is.EqualTo(
                            Mathf.Lerp(
                                path.DistanceAtTile(2),
                                path.DistanceAtTile(3),
                                0.5f))
                        .Within(0.0001f),
                    "The ambulance must consume the shared link progress instead of jumping tile centers.");
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [TestCase(1, 0)]
        [TestCase(0, 1)]
        [TestCase(-1, 0)]
        [TestCase(0, -1)]
        public void SharedTrafficRoute_UsesRightLaneForEveryDirection(
            int directionX,
            int directionY)
        {
            var owner =
                new GameObject(
                    "AmbulanceRightLane_Test");

            try
            {
                MainCityView cityView =
                    owner.AddComponent<MainCityView>();
                Vector2Int direction =
                    new(directionX, directionY);
                Vector2Int[] roadTiles =
                {
                    Vector2Int.zero,
                    direction
                };
                const float depth = -0.38f;
                RoutePolyline trafficRoute =
                    cityView.BakeTrafficRoute(
                        roadTiles,
                        depth);

                Assert.That(trafficRoute, Is.Not.Null);

                Sample start =
                    trafficRoute.SampleAt(0f);
                Vector3 expectedRightOffset =
                    new(
                        direction.y *
                        cityView.LaneOffset *
                        cityView.TileSize,
                        -direction.x *
                        cityView.LaneOffset *
                        cityView.TileSize,
                        0f);
                Vector3 expectedPosition =
                    cityView.GridToLocal(
                        roadTiles[0],
                        depth) +
                    expectedRightOffset;

                Assert.That(
                    Vector3.Distance(
                        start.Pos,
                        expectedPosition),
                    Is.LessThan(0.001f),
                    "Every travel direction must use its right-hand lane.");
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void SharedTrafficRoute_UsesParkingSpursForHospitalEntryAndExit()
        {
            var owner =
                new GameObject(
                    "AmbulanceParkingSpur_Test");

            try
            {
                MainCityView cityView =
                    owner.AddComponent<MainCityView>();
                const float depth = -0.38f;
                Vector3 parkingExit =
                    new(-0.25f, -0.3f, depth);
                Vector3 parkingEntry =
                    new(2.25f, 0.8f, depth);
                RoutePolyline route =
                    cityView.BakeTrafficRoute(
                        new[]
                        {
                            Vector2Int.zero,
                            Vector2Int.right
                        },
                        depth,
                        parkingExit,
                        parkingEntry);

                Assert.That(route, Is.Not.Null);
                Sample first = route.SampleAt(0f);
                Sample firstRoad =
                    route.SampleAt(
                        route.DistanceAtTile(0));
                Sample last =
                    route.SampleAt(route.Length);

                Assert.That(first.IsSpur, Is.True);
                Assert.That(last.IsSpur, Is.True);
                Assert.That(
                    Vector3.Distance(
                        first.Pos,
                        parkingExit),
                    Is.LessThan(0.001f));
                Assert.That(
                    Vector3.Distance(
                        last.Pos,
                        parkingEntry),
                    Is.LessThan(0.001f));
                Assert.That(
                    route.DistanceAtTile(0),
                    Is.GreaterThan(0f),
                    "The ambulance must travel from its parking pose into the shared road curve instead of snapping to the lane.");
                Assert.That(
                    firstRoad.IsSpur,
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void HospitalExitSpur_DoesNotCurveAwayFromTheRoadBeforeMerging()
        {
            var owner =
                new GameObject(
                    "AmbulanceParkingOvershoot_Test");

            try
            {
                MainCityView cityView =
                    owner.AddComponent<MainCityView>();
                const float depth = -0.38f;
                Vector3 parkingPosition =
                    new(1.25f, 0.5f, depth);
                RoutePolyline route =
                    cityView.BakeTrafficRoute(
                        new[]
                        {
                            Vector2Int.zero,
                            Vector2Int.up
                        },
                        depth,
                        parkingPosition,
                        null,
                        clampAnchorSpurOvershoot: true);

                Assert.That(route, Is.Not.Null);
                float roadDistance =
                    route.DistanceAtTile(0);
                Vector3 firstRoadPosition =
                    route.SampleAt(roadDistance).Pos;
                float previousDistance =
                    Vector3.Distance(
                        parkingPosition,
                        firstRoadPosition);

                for (int i = 1; i <= 16; i++)
                {
                    Sample sample =
                        route.SampleAt(
                            roadDistance * i / 16f);
                    float distanceToRoad =
                        Vector3.Distance(
                            sample.Pos,
                            firstRoadPosition);

                    Assert.That(
                        distanceToRoad,
                        Is.LessThanOrEqualTo(
                            previousDistance + 0.0001f),
                        "The parking connector must approach the frontage lane monotonically instead of leaving the road and turning back.");
                    previousDistance = distanceToRoad;
                }
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void SharedTrafficRoute_KeepsAmbulanceOnRightLaneThroughTurn()
        {
            var owner =
                new GameObject(
                    "AmbulanceTrafficRoute_Test");

            try
            {
                MainCityView cityView =
                    owner.AddComponent<MainCityView>();
                EmergencyIncidentConfigSO config =
                    AssetDatabase.LoadAssetAtPath<
                        EmergencyIncidentConfigSO>(
                        "Assets/05_ScriptableObjects/CityFlow/Emergency/EmergencyIncidentConfig.asset");
                MeshFilter vehicleMesh =
                    config?.VehicleVisualPrefab
                        ?.GetComponentInChildren<MeshFilter>(
                            true);
                Vector2Int[] roadTiles =
                {
                    new(0, 0),
                    new(1, 0),
                    new(1, 1)
                };
                const float depth = -0.38f;
                RoutePolyline trafficRoute =
                    cityView.BakeTrafficRoute(
                        roadTiles,
                        depth);

                Assert.That(trafficRoute, Is.Not.Null);
                Assert.That(config, Is.Not.Null);
                Assert.That(vehicleMesh, Is.Not.Null);
                Assert.That(
                    vehicleMesh.sharedMesh,
                    Is.Not.Null);
                Assert.That(
                    trafficRoute.TileCount,
                    Is.EqualTo(roadTiles.Length));

                Sample start =
                    trafficRoute.SampleAt(0f);
                Sample end =
                    trafficRoute.SampleAt(
                        trafficRoute.Length);
                Vector3 expectedStart =
                    cityView.GridToLocal(
                        roadTiles[0],
                        depth) +
                    new Vector3(
                        0f,
                        -cityView.LaneOffset *
                        cityView.TileSize,
                        0f);
                Vector3 expectedEnd =
                    cityView.GridToLocal(
                        roadTiles[2],
                        depth) +
                    new Vector3(
                        cityView.LaneOffset *
                        cityView.TileSize,
                        0f,
                        0f);

                Assert.That(
                    Vector3.Distance(
                        start.Pos,
                        expectedStart),
                    Is.LessThan(0.001f));
                Assert.That(
                    Vector3.Distance(
                        end.Pos,
                        expectedEnd),
                    Is.LessThan(0.001f));
                Assert.That(
                    Vector3.Dot(
                        start.Dir.normalized,
                        Vector3.right),
                    Is.GreaterThan(0.999f));
                Assert.That(
                    Vector3.Dot(
                        end.Dir.normalized,
                        Vector3.up),
                    Is.GreaterThan(0.999f));

                for (int sampleIndex = 0;
                     sampleIndex <= 40;
                     sampleIndex++)
                {
                    Sample sample =
                        trafficRoute.SampleAt(
                            trafficRoute.Length *
                            sampleIndex /
                            40f);
                    bool remainsOnRoad = false;
                    for (int tileIndex = 0;
                         tileIndex < roadTiles.Length;
                         tileIndex++)
                    {
                        Vector3 center =
                            cityView.GridToLocal(
                                roadTiles[tileIndex],
                                depth);
                        float halfTile =
                            cityView.TileSize * 0.5f +
                            0.0001f;
                        if (Mathf.Abs(
                                sample.Pos.x -
                                center.x) <= halfTile &&
                            Mathf.Abs(
                                sample.Pos.y -
                                center.y) <= halfTile)
                        {
                            remainsOnRoad = true;
                            break;
                        }
                    }

                    Assert.That(
                        remainsOnRoad,
                        Is.True,
                        $"Shared traffic route left the road at sample {sampleIndex}.");

                    Vector2 forward =
                        new(
                            sample.Dir.x,
                            sample.Dir.y);
                    forward.Normalize();
                    Vector2 right =
                        new(
                            forward.y,
                            -forward.x);
                    float halfLength =
                        config.VehicleLengthTiles *
                        cityView.TileSize *
                        0.5f;
                    float halfWidth =
                        config.VehicleWidthTiles *
                        cityView.TileSize *
                        0.5f;

                    for (int lengthSign = -1;
                         lengthSign <= 1;
                         lengthSign += 2)
                    {
                        for (int widthSign = -1;
                             widthSign <= 1;
                             widthSign += 2)
                        {
                            Vector2 corner =
                                new Vector2(
                                    sample.Pos.x,
                                    sample.Pos.y) +
                                forward *
                                halfLength *
                                lengthSign +
                                right *
                                halfWidth *
                                widthSign;
                            bool bodyRemainsOnRoad =
                                false;

                            for (int tileIndex = 0;
                                 tileIndex <
                                 roadTiles.Length;
                                 tileIndex++)
                            {
                                Vector3 center =
                                    cityView.GridToLocal(
                                        roadTiles[
                                            tileIndex],
                                        depth);
                                float halfTile =
                                    cityView.TileSize *
                                    0.5f +
                                    0.0001f;
                                if (Mathf.Abs(
                                        corner.x -
                                        center.x) <=
                                    halfTile &&
                                    Mathf.Abs(
                                        corner.y -
                                        center.y) <=
                                    halfTile)
                                {
                                    bodyRemainsOnRoad =
                                        true;
                                    break;
                                }
                            }

                            Assert.That(
                                bodyRemainsOnRoad,
                                Is.True,
                                $"Ambulance body left the road at sample {sampleIndex}.");
                        }
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void TrafficPathContinuity_RejectsOffRoadChord()
        {
            Assert.That(
                AmbulanceWorldView
                    .IsContinuousRoadTileSequence(
                        new[]
                        {
                            new Vector2Int(0, 0),
                            new Vector2Int(1, 0),
                            new Vector2Int(1, 1)
                        }),
                Is.True);
            Assert.That(
                AmbulanceWorldView
                    .IsContinuousRoadTileSequence(
                        new[]
                        {
                            new Vector2Int(0, 0),
                            new Vector2Int(2, 0)
                        }),
                Is.False,
                "A missing road tile must not be drawn as a direct off-road chord.");
            Assert.That(
                AmbulanceWorldView
                    .IsContinuousRoadTileSequence(
                        new[]
                        {
                            new Vector2Int(0, 0),
                            new Vector2Int(1, 1)
                        }),
                Is.False,
                "A diagonal gap must not be drawn across non-road space.");
        }

        [Test]
        public void AmbulanceRoadsideFallback_ReachesBuildingWithoutRightSideApproach()
        {
            SimConfig config = SimConfig.Default();
            var events = new SimEventHub();
            var engine =
                new SimEngine(config, events);
            Vector2Int hospital = new(1, 2);
            Vector2Int incidentLocation = new(6, 2);
            GameObject owner =
                new("Ambulance Roadside Fallback Test");

            try
            {
                for (int x = 1; x <= 6; x++)
                {
                    Assert.That(
                        engine.Place(
                            new Vector2Int(x, 1),
                            TileType.Road),
                        Is.True);
                }

                Assert.That(
                    engine.Place(
                        hospital,
                        TileType.Hospital),
                    Is.True);
                Assert.That(
                    engine.Place(
                        incidentLocation,
                        TileType.House),
                    Is.True);

                var services =
                    new CityFlowServices(
                        events,
                        engine,
                        engine,
                        stats: engine);
                BusRoute route =
                    owner.AddComponent<BusRoute>();
                route.Initialize(services);
                route.UseRoadsideStopApproach = true;
                route.RoadsideStopFilter = _ => true;
                route.LoopRoute = false;

                Assert.That(
                    route.ConfigureRoute(
                        new[]
                        {
                            hospital,
                            incidentLocation
                        },
                        shouldLoop: false),
                    Is.True);
                Assert.That(
                    route.StartRoute(),
                    Is.True,
                    "An ambulance must use another legal adjacent road when the strict right-side stopping direction is unreachable.");
                Assert.That(
                    route.CurrentRoadPath,
                    Is.Not.Empty);
                Assert.That(
                    route.CurrentRoadPath[
                        route.CurrentRoadPath.Count - 1],
                    Is.EqualTo(
                        new Vector2Int(6, 1)));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void AmbulanceRoadsideStop_BacksOutOfRoundaboutFootprint()
        {
            SimConfig config = SimConfig.Default();
            config.AutoDetectSignals = false;
            var events = new SimEventHub();
            var engine = new SimEngine(config, events);
            Vector2Int hospital = new(1, 2);
            Vector2Int incidentLocation = new(6, 2);
            Vector2Int roundabout = new(5, 1);
            GameObject owner =
                new("Ambulance Roundabout Hold Test");

            try
            {
                for (int x = 1; x <= 7; x++)
                {
                    Assert.That(
                        engine.Place(
                            new Vector2Int(x, 1),
                            TileType.Road),
                        Is.True);
                }

                Assert.That(
                    engine.Place(
                        new Vector2Int(5, 0),
                        TileType.Road),
                    Is.True);
                Assert.That(
                    engine.Place(
                        new Vector2Int(5, 2),
                        TileType.Road),
                    Is.True);
                Assert.That(
                    engine.Place(
                        hospital,
                        TileType.Hospital),
                    Is.True);
                Assert.That(
                    engine.Place(
                        incidentLocation,
                        TileType.House),
                    Is.True);
                Assert.That(
                    engine.TryPlaceRoundabout(roundabout),
                    Is.True);
                engine.Tick(config.TickInterval);

                var services =
                    new CityFlowServices(
                        events,
                        engine,
                        engine,
                        stats: engine);
                services.RegisterRoadTraffic(
                    engine.RoadTraffic);
                services.RegisterRoadRoutePlanning(engine);

                BusRoute route =
                    owner.AddComponent<BusRoute>();
                route.ConfigureRoadTrafficAgent(
                    RoadTrafficAgentKind.FeatureVehicle,
                    VehicleFootprint.StandardDefault,
                    holdAtDestination: true);
                route.Initialize(services);
                route.UseRoadsideStopApproach = true;
                route.RoadsideStopFilter = _ => true;
                route.RoadsideStopSetbackTiles = 1;
                route.LoopRoute = false;

                Assert.That(
                    route.ConfigureRoute(
                        new[]
                        {
                            hospital,
                            incidentLocation
                        },
                        shouldLoop: false),
                    Is.True);
                Assert.That(route.StartRoute(), Is.True);
                Assert.That(
                    route.CurrentRoadPath,
                    Is.Not.Empty);

                Vector2Int holdTile =
                    route.CurrentRoadPath[
                        route.CurrentRoadPath.Count - 1];
                Assert.That(
                    engine.IsInRoundaboutFootprint(holdTile),
                    Is.False,
                    "An ambulance must not hold and rebuild its return route inside the roundabout state machine.");
                Assert.That(
                    engine.RoadTraffic.IsSafeHoldTile(holdTile),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void ExternalTransport_WaitsForPhysicalArrivalAndReturn()
        {
            CreateEmergencyTestWorld(
                useExternalTransport: true,
                out GameObject owner,
                out EmergencyIncidentSystem system,
                out EmergencyIncidentConfigSO config);

            try
            {
                Assert.That(
                    system.TryCreateIncidentAt(
                        new Vector2Int(4, 12)),
                    Is.True);
                EmergencyIncident incident =
                    system.ActiveIncidents[0];
                Assert.That(
                    incident.State,
                    Is.EqualTo(
                        EmergencyIncidentState.AmbulanceOutbound));

                system.Tick(100f);
                Assert.That(
                    incident.State,
                    Is.EqualTo(
                        EmergencyIncidentState.AmbulanceOutbound),
                    "Treatment must wait for the physical ambulance.");

                Assert.That(
                    system.TryMarkAmbulanceArrived(
                        incident.IncidentId),
                    Is.True);
                Assert.That(
                    incident.State,
                    Is.EqualTo(
                        EmergencyIncidentState.Treating));

                system.Tick(100f);
                Assert.That(
                    incident.State,
                    Is.EqualTo(
                        EmergencyIncidentState.AmbulanceReturning));

                system.Tick(100f);
                Assert.That(
                    incident.State,
                    Is.EqualTo(
                        EmergencyIncidentState.AmbulanceReturning),
                    "Resolution must wait for the ambulance to return.");

                Assert.That(
                    system.TryMarkAmbulanceReturned(
                        incident.IncidentId),
                    Is.True);
                Assert.That(
                    system.ActiveIncidentCount,
                    Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void HospitalFleet_ParksBeforeDispatch_AndReusesVehicleAfterReturn()
        {
            SimConfig simulationConfig =
                SimConfig.Default();
            var events = new SimEventHub();
            var engine =
                new SimEngine(
                    simulationConfig,
                    events);
            Vector2Int hospital =
                new(13, 14);
            Assert.That(
                ContentFeaturePrototypeScenario
                    .BuildPrototypeCity(engine),
                Is.GreaterThan(0));
            Assert.That(
                engine.Remove(hospital),
                Is.True);

            var services =
                new CityFlowServices(
                    events,
                    engine,
                    engine,
                    stats: engine);
            GameObject owner =
                new("Ambulance Fleet Test");
            GameObject cityViewObject =
                new("Ambulance Fleet City View");

            try
            {
                MainCityView cityView =
                    cityViewObject.AddComponent<
                        MainCityView>();
                GameObject hospitalVisual =
                    new("Hospital Parking Test Visual");
                hospitalVisual.transform.SetParent(
                    cityView.transform,
                    false);
                GameObject parkingSlot =
                    new("ParkingSlot_0");
                parkingSlot.transform.SetParent(
                    hospitalVisual.transform,
                    false);
                parkingSlot.transform.localPosition =
                    new Vector3(0.25f, -0.2f, 0f);

                System.Type tileVisualType =
                    typeof(MainCityView).GetNestedType(
                        "TileVisual",
                        BindingFlags.NonPublic);
                object tileVisual =
                    System.Activator.CreateInstance(
                        tileVisualType,
                        true);
                tileVisualType
                    .GetField(
                        "Object",
                        BindingFlags.Instance |
                        BindingFlags.Public)
                    .SetValue(
                        tileVisual,
                        hospitalVisual);
                FieldInfo tileVisualsField =
                    typeof(MainCityView).GetField(
                        "tileVisuals",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                System.Collections.IDictionary tileVisuals =
                    tileVisualsField.GetValue(cityView) as
                        System.Collections.IDictionary;
                tileVisuals.Add(hospital, tileVisual);

                EmergencyIncidentConfigSO config =
                    AssetDatabase.LoadAssetAtPath<
                        EmergencyIncidentConfigSO>(
                        "Assets/05_ScriptableObjects/CityFlow/Emergency/EmergencyIncidentConfig.asset");
                GameObject vehiclePrefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/02_Prefabs/Vehicles/AmbulanceVehicle.prefab");
                Assert.That(config, Is.Not.Null);
                Assert.That(vehiclePrefab, Is.Not.Null);

                EmergencyIncidentSystem system =
                    owner.AddComponent<
                        EmergencyIncidentSystem>();
                AmbulanceDispatchService dispatch =
                    owner.AddComponent<
                        AmbulanceDispatchService>();

                SerializedObject systemValues =
                    new(system);
                systemValues.FindProperty("config")
                    .objectReferenceValue = config;
                systemValues.FindProperty(
                        "enableAutomaticSpawn")
                    .boolValue = false;
                systemValues.FindProperty(
                        "useExternalAmbulanceTransport")
                    .boolValue = true;
                systemValues
                    .ApplyModifiedPropertiesWithoutUndo();

                SerializedObject dispatchValues =
                    new(dispatch);
                dispatchValues.FindProperty(
                        "incidentSystem")
                    .objectReferenceValue = system;
                dispatchValues.FindProperty("config")
                    .objectReferenceValue = config;
                dispatchValues.FindProperty(
                        "ambulanceVehiclePrefab")
                    .objectReferenceValue = vehiclePrefab;
                dispatchValues
                    .ApplyModifiedPropertiesWithoutUndo();

                dispatch.Initialize(services);
                system.Initialize(services);

                Assert.That(
                    dispatch.TotalVehicleCount,
                    Is.Zero,
                    "No ambulance may exist before a hospital is installed.");
                Assert.That(
                    engine.Place(
                        hospital,
                        TileType.Hospital),
                    Is.True);
                engine.Tick(
                    simulationConfig.TickInterval);
                typeof(AmbulanceDispatchService)
                    .GetMethod(
                        "LateUpdate",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic)
                    .Invoke(dispatch, null);

                Assert.That(
                    dispatch.TotalVehicleCount,
                    Is.EqualTo(
                        config.AmbulancesPerHospital));
                Assert.That(
                    dispatch.ParkedVehicleCount,
                    Is.EqualTo(
                        config.AmbulancesPerHospital));
                Assert.That(
                    dispatch.ActiveVehicleCount,
                    Is.Zero);

                AmbulanceVehicleAgent parkedAgent =
                    owner.GetComponentInChildren<
                        AmbulanceVehicleAgent>();
                Assert.That(parkedAgent, Is.Not.Null);
                Assert.That(
                    parkedAgent.IsAssigned,
                    Is.False);
                Assert.That(
                    parkedAgent.GetComponent<
                            AmbulanceWorldView>()
                        .HasVisibleAmbulance,
                    Is.True,
                    "Installing a hospital must show its ambulance in a parking slot before an incident exists.");

                Assert.That(
                    system.TryCreateIncidentAt(
                        new Vector2Int(4, 12)),
                    Is.True);
                EmergencyIncident incident =
                    system.ActiveIncidents[0];
                Assert.That(
                    dispatch.ActiveVehicleCount,
                    Is.EqualTo(1),
                    "The already parked ambulance must be assigned without spawning a replacement.");
                Assert.That(
                    parkedAgent.IsAssigned,
                    Is.True);

                float outboundRemaining =
                    incident.StateRemainingSeconds;
                EmergencyIncidentSaveData outboundSnapshot =
                    system.CreateSnapshot();

                system.RestoreSnapshot(outboundSnapshot);
                incident = system.ActiveIncidents[0];

                Assert.That(
                    incident.State,
                    Is.EqualTo(
                        EmergencyIncidentState
                            .AmbulanceOutbound));
                Assert.That(
                    incident.StateRemainingSeconds,
                    Is.EqualTo(outboundRemaining)
                        .Within(0.0001f));
                Assert.That(
                    dispatch.ActiveVehicleCount,
                    Is.EqualTo(1),
                    "Restoring the outbound trip must not dispatch a duplicate ambulance.");
                Assert.That(
                    dispatch.TotalVehicleCount,
                    Is.EqualTo(
                        config.AmbulancesPerHospital));

                Assert.That(
                    system.TryMarkAmbulanceArrived(
                        incident.IncidentId),
                    Is.True);
                float treatingRemaining =
                    incident.StateRemainingSeconds;
                EmergencyIncidentSaveData treatingSnapshot =
                    system.CreateSnapshot();

                system.RestoreSnapshot(treatingSnapshot);
                incident = system.ActiveIncidents[0];

                Assert.That(
                    incident.State,
                    Is.EqualTo(
                        EmergencyIncidentState.Treating));
                Assert.That(
                    incident.StateRemainingSeconds,
                    Is.EqualTo(treatingRemaining)
                        .Within(0.0001f));
                Assert.That(
                    dispatch.ActiveVehicleCount,
                    Is.EqualTo(1),
                    "Restoring treatment must reconnect exactly one hospital ambulance.");
                Assert.That(
                    dispatch.TotalVehicleCount,
                    Is.EqualTo(
                        config.AmbulancesPerHospital));

                system.Tick(
                    config.TreatmentSeconds + 0.01f);
                Assert.That(
                    incident.State,
                    Is.EqualTo(
                        EmergencyIncidentState
                            .AmbulanceReturning));
                float returningRemaining =
                    incident.StateRemainingSeconds;
                EmergencyIncidentSaveData returningSnapshot =
                    system.CreateSnapshot();

                system.RestoreSnapshot(returningSnapshot);
                incident = system.ActiveIncidents[0];

                Assert.That(
                    incident.State,
                    Is.EqualTo(
                        EmergencyIncidentState
                            .AmbulanceReturning));
                Assert.That(
                    incident.StateRemainingSeconds,
                    Is.EqualTo(returningRemaining)
                        .Within(0.0001f));
                Assert.That(
                    dispatch.ActiveVehicleCount,
                    Is.EqualTo(1),
                    "Restoring the return trip must reconnect exactly one hospital ambulance.");
                Assert.That(
                    dispatch.TotalVehicleCount,
                    Is.EqualTo(
                        config.AmbulancesPerHospital));
                Assert.That(
                    system.TryMarkAmbulanceReturned(
                        incident.IncidentId),
                    Is.True);

                Assert.That(
                    dispatch.ActiveVehicleCount,
                    Is.Zero);
                Assert.That(
                    dispatch.ParkedVehicleCount,
                    Is.EqualTo(
                        config.AmbulancesPerHospital));
                Assert.That(
                    owner.GetComponentInChildren<
                        AmbulanceVehicleAgent>(),
                    Is.SameAs(parkedAgent),
                    "The hospital vehicle must be reused instead of destroyed and recreated after every call.");
                Assert.That(
                    parkedAgent.GetComponent<
                            AmbulanceWorldView>()
                        .HasVisibleAmbulance,
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(cityViewObject);
            }
        }

        [Test]
        public void HospitalFleet_RebuildsAfterSaveRestore()
        {
            SimConfig simulationConfig =
                SimConfig.Default();
            var sourceEvents = new SimEventHub();
            var sourceEngine =
                new SimEngine(
                    simulationConfig,
                    sourceEvents);
            Assert.That(
                ContentFeaturePrototypeScenario
                    .BuildPrototypeCity(sourceEngine),
                Is.GreaterThan(0));

            var events = new SimEventHub();
            var engine =
                new SimEngine(
                    simulationConfig,
                    events);
            var save =
                new SaveService(
                    engine,
                    repository: null,
                    clock: null);
            var services =
                new CityFlowServices(
                    events,
                    engine,
                    engine,
                    save,
                    stats: engine);
            GameObject owner =
                new("Ambulance Restore Fleet Test");
            GameObject cityViewObject =
                new("Ambulance Restore City View");

            try
            {
                cityViewObject.AddComponent<MainCityView>();
                EmergencyIncidentConfigSO config =
                    AssetDatabase.LoadAssetAtPath<
                        EmergencyIncidentConfigSO>(
                        "Assets/05_ScriptableObjects/CityFlow/Emergency/EmergencyIncidentConfig.asset");
                GameObject vehiclePrefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/02_Prefabs/Vehicles/AmbulanceVehicle.prefab");
                Assert.That(config, Is.Not.Null);
                Assert.That(vehiclePrefab, Is.Not.Null);

                EmergencyIncidentSystem system =
                    owner.AddComponent<
                        EmergencyIncidentSystem>();
                AmbulanceDispatchService dispatch =
                    owner.AddComponent<
                        AmbulanceDispatchService>();

                SerializedObject systemValues =
                    new(system);
                systemValues.FindProperty("config")
                    .objectReferenceValue = config;
                systemValues.FindProperty(
                        "enableAutomaticSpawn")
                    .boolValue = false;
                systemValues
                    .ApplyModifiedPropertiesWithoutUndo();

                SerializedObject dispatchValues =
                    new(dispatch);
                dispatchValues.FindProperty(
                        "incidentSystem")
                    .objectReferenceValue = system;
                dispatchValues.FindProperty("config")
                    .objectReferenceValue = config;
                dispatchValues.FindProperty(
                        "ambulanceVehiclePrefab")
                    .objectReferenceValue = vehiclePrefab;
                dispatchValues
                    .ApplyModifiedPropertiesWithoutUndo();

                system.Initialize(services);
                dispatch.Initialize(services);
                Assert.That(
                    dispatch.TotalVehicleCount,
                    Is.Zero);

                engine.RestoreSnapshot(
                    sourceEngine.CreateSnapshot());
                MethodInfo publishRestoreCompleted =
                    typeof(SaveService).GetMethod(
                        "PublishRestoreCompleted",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic);
                Assert.That(
                    publishRestoreCompleted,
                    Is.Not.Null);
                publishRestoreCompleted.Invoke(
                    save,
                    new object[] { 0.0, false });

                Assert.That(
                    dispatch.TotalVehicleCount,
                    Is.Zero,
                    "Fleet synchronization must wait until restore subscribers finish.");
                typeof(AmbulanceDispatchService)
                    .GetMethod(
                        "LateUpdate",
                        BindingFlags.Instance |
                        BindingFlags.NonPublic)
                    .Invoke(dispatch, null);

                Assert.That(
                    system.HospitalTiles.Count,
                    Is.GreaterThan(0));
                Assert.That(
                    dispatch.TotalVehicleCount,
                    Is.EqualTo(
                        system.HospitalTiles.Count *
                        config.AmbulancesPerHospital));
                Assert.That(
                    dispatch.ParkedVehicleCount,
                    Is.EqualTo(
                        dispatch.TotalVehicleCount));
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(cityViewObject);
            }
        }

        [Test]
        public void RandomTarget_AvoidsImmediateRepeat()
        {
            CreateEmergencyTestWorld(
                useExternalTransport: false,
                out GameObject owner,
                out EmergencyIncidentSystem system,
                out EmergencyIncidentConfigSO config);

            try
            {
                Random.InitState(151);
                Assert.That(
                    system.TryCreateRandomIncident(),
                    Is.True);
                Vector2Int firstTarget =
                    system.ActiveIncidents[0].Location;

                system.Tick(100f);
                system.Tick(100f);
                system.Tick(100f);
                Assert.That(
                    system.ActiveIncidentCount,
                    Is.Zero);

                Assert.That(
                    system.TryCreateRandomIncident(),
                    Is.True);
                Vector2Int secondTarget =
                    system.ActiveIncidents[0].Location;

                Assert.That(
                    secondTarget,
                    Is.Not.EqualTo(firstTarget),
                    "Another building must be selected while one is available.");
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void AutomaticDispatch_UsesRandomOneToThreeGameDayInterval()
        {
            var calendar = new TestGameCalendar();
            CreateEmergencyTestWorld(
                useExternalTransport: false,
                out GameObject owner,
                out EmergencyIncidentSystem system,
                out EmergencyIncidentConfigSO config,
                enableAutomaticSpawn: true,
                gameCalendar: calendar);

            try
            {
                FieldInfo nextDispatchField =
                    typeof(EmergencyIncidentSystem)
                        .GetField(
                            "nextAutomaticDispatchDay",
                            BindingFlags.Instance |
                            BindingFlags.NonPublic);
                Assert.That(nextDispatchField, Is.Not.Null);

                long dueDay =
                    (long)nextDispatchField.GetValue(system);
                Assert.That(dueDay, Is.InRange(1L, 3L));

                while (calendar.TotalDays < dueDay - 1L)
                {
                    calendar.AdvanceDay();
                    Assert.That(
                        system.ActiveIncidentCount,
                        Is.Zero);
                }

                calendar.AdvanceDay();
                Assert.That(
                    system.ActiveIncidentCount,
                    Is.EqualTo(1),
                    "Automatic ambulance dispatch must occur on its scheduled game day.");

                long nextDueDay =
                    (long)nextDispatchField.GetValue(system);
                Assert.That(
                    nextDueDay - calendar.TotalDays,
                    Is.InRange(1L, 3L));
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void ResponseDeadline_FailsAndPublishesHappinessHook()
        {
            var calendar = new TestGameCalendar();
            CreateEmergencyTestWorld(
                useExternalTransport: true,
                out GameObject owner,
                out EmergencyIncidentSystem system,
                out EmergencyIncidentConfigSO config,
                gameCalendar: calendar);
            EmergencyIncidentDefinitionSO definition =
                ScriptableObject.CreateInstance<
                    EmergencyIncidentDefinitionSO>();

            try
            {
                SerializedObject definitionValues =
                    new(definition);
                definitionValues.FindProperty("incidentId")
                    .stringValue = "deadline_test";
                definitionValues.FindProperty(
                        "responseDeadlineHours")
                    .intValue = 3;
                definitionValues.FindProperty(
                        "failureHappinessPenalty")
                    .floatValue = 2.5f;
                definitionValues
                    .ApplyModifiedPropertiesWithoutUndo();

                SerializedObject configValues =
                    new(config);
                SerializedProperty definitions =
                    configValues.FindProperty(
                        "incidentDefinitions");
                definitions.arraySize = 1;
                definitions.GetArrayElementAtIndex(0)
                    .objectReferenceValue = definition;
                configValues
                    .ApplyModifiedPropertiesWithoutUndo();

                EmergencyIncidentOutcomeEvent outcome =
                    default;
                bool receivedOutcome = false;
                FieldInfo servicesField =
                    typeof(EmergencyIncidentSystem)
                        .GetField(
                            "services",
                            BindingFlags.Instance |
                            BindingFlags.NonPublic);
                var services =
                    (CityFlowServices)servicesField
                        .GetValue(system);
                services.Events
                    .EmergencyIncidentOutcomeReported +=
                    value =>
                    {
                        outcome = value;
                        receivedOutcome = true;
                    };

                Assert.That(
                    system.TryCreateIncidentAt(
                        new Vector2Int(4, 12)),
                    Is.True);
                EmergencyIncident incident =
                    system.ActiveIncidents[0];
                Assert.That(
                    incident.DeadlineAbsoluteHour,
                    Is.EqualTo(3L));

                calendar.AdvanceHours(3);

                Assert.That(receivedOutcome, Is.True);
                Assert.That(
                    outcome.Outcome,
                    Is.EqualTo(
                        EmergencyIncidentOutcome.Failed));
                Assert.That(
                    outcome.FailureReason,
                    Is.EqualTo(
                        EmergencyIncidentFailureReason
                            .ResponseDeadlineExceeded));
                Assert.That(
                    outcome.SuggestedHappinessDelta,
                    Is.EqualTo(-2.5f));
                Assert.That(
                    incident.State,
                    Is.EqualTo(
                        EmergencyIncidentState
                            .AmbulanceReturningAfterFailure));
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(config);
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void AutomaticDispatch_RespectsConfiguredDailyMaximum()
        {
            var calendar = new TestGameCalendar();
            CreateEmergencyTestWorld(
                useExternalTransport: true,
                out GameObject owner,
                out EmergencyIncidentSystem system,
                out EmergencyIncidentConfigSO config,
                gameCalendar: calendar);

            try
            {
                SerializedObject values = new(config);
                values.FindProperty("maximumActiveIncidents")
                    .intValue = 3;
                values.FindProperty(
                        "maximumAutomaticIncidentsPerDay")
                    .intValue = 2;
                values
                    .ApplyModifiedPropertiesWithoutUndo();

                Assert.That(
                    system.TryCreateAutomaticIncident(),
                    Is.True);
                Assert.That(
                    system.TryCreateAutomaticIncident(),
                    Is.True);
                Assert.That(
                    system.TryCreateAutomaticIncident(),
                    Is.False);
                Assert.That(
                    system.AutomaticDispatchCountToday,
                    Is.EqualTo(2));

                calendar.AdvanceDay();
                Assert.That(
                    system.AutomaticDispatchCountToday,
                    Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void TestDispatch_BypassesDailyMaximumWithoutChangingSchedule()
        {
            var calendar = new TestGameCalendar();
            CreateEmergencyTestWorld(
                useExternalTransport: true,
                out GameObject owner,
                out EmergencyIncidentSystem system,
                out EmergencyIncidentConfigSO config,
                gameCalendar: calendar);

            try
            {
                SerializedObject values = new(config);
                values.FindProperty("maximumActiveIncidents")
                    .intValue = 3;
                values.FindProperty(
                        "maximumAutomaticIncidentsPerDay")
                    .intValue = 1;
                values.ApplyModifiedPropertiesWithoutUndo();

                FieldInfo nextDispatchField =
                    typeof(EmergencyIncidentSystem)
                        .GetField(
                            "nextAutomaticDispatchDay",
                            BindingFlags.Instance |
                            BindingFlags.NonPublic);
                Assert.That(nextDispatchField, Is.Not.Null);
                long scheduledDay =
                    (long)nextDispatchField.GetValue(system);

                Assert.That(
                    system.TryCreateAutomaticIncident(),
                    Is.True);
                Assert.That(
                    system.TryCreateAutomaticIncident(),
                    Is.False);
                Assert.That(
                    system.AutomaticDispatchCountToday,
                    Is.EqualTo(1));

                Assert.That(
                    system.TryCreateTestIncidentNow(),
                    Is.True);
                Assert.That(
                    system.ActiveIncidentCount,
                    Is.EqualTo(2));
                Assert.That(
                    system.AutomaticDispatchCountToday,
                    Is.EqualTo(1));
                Assert.That(
                    (long)nextDispatchField.GetValue(system),
                    Is.EqualTo(scheduledDay));
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(config);
            }
        }

        [TestCase(
            EmergencyIncidentState.WaitingForHospital)]
        [TestCase(
            EmergencyIncidentState.AmbulanceOutbound)]
        [TestCase(
            EmergencyIncidentState.Treating)]
        [TestCase(
            EmergencyIncidentState.AmbulanceReturning)]
        [TestCase(
            EmergencyIncidentState
                .AmbulanceReturningAfterFailure)]
        public void IncidentSnapshot_RoundTripsActiveStateWithoutDuplicateOutcome(
            EmergencyIncidentState expectedState)
        {
            CreateEmergencyTestWorld(
                useExternalTransport: true,
                out GameObject owner,
                out EmergencyIncidentSystem system,
                out EmergencyIncidentConfigSO config);
            FieldInfo servicesField =
                typeof(EmergencyIncidentSystem).GetField(
                    "services",
                    BindingFlags.Instance |
                    BindingFlags.NonPublic);
            Assert.That(servicesField, Is.Not.Null);
            var services =
                (CityFlowServices)servicesField.GetValue(system);
            int outcomeCount = 0;
            services.Events.EmergencyIncidentOutcomeReported +=
                _ => outcomeCount++;

            try
            {
                if (expectedState ==
                    EmergencyIncidentState.WaitingForHospital)
                {
                    var engine = services.TileData as SimEngine;
                    Assert.That(engine, Is.Not.Null);
                    var hospitals =
                        new Vector2Int[
                            system.HospitalTiles.Count];
                    for (int i = 0;
                         i < hospitals.Length;
                         i++)
                    {
                        hospitals[i] =
                            system.HospitalTiles[i];
                    }

                    for (int i = 0;
                         i < hospitals.Length;
                         i++)
                    {
                        Assert.That(
                            engine.Remove(hospitals[i]),
                            Is.True);
                    }

                    system.RebuildLocations();
                    Assert.That(
                        system.HospitalTiles.Count,
                        Is.Zero);
                }

                Assert.That(
                    system.TryCreateIncidentAt(
                        new Vector2Int(4, 12)),
                    Is.True);
                EmergencyIncident incident =
                    system.ActiveIncidents[0];

                switch (expectedState)
                {
                    case EmergencyIncidentState.Treating:
                        Assert.That(
                            system.TryMarkAmbulanceArrived(
                                incident.IncidentId),
                            Is.True);
                        break;

                    case EmergencyIncidentState
                        .AmbulanceReturning:
                        Assert.That(
                            system.TryMarkAmbulanceArrived(
                                incident.IncidentId),
                            Is.True);
                        system.Tick(
                            config.TreatmentSeconds + 0.01f);
                        break;

                    case EmergencyIncidentState
                        .AmbulanceReturningAfterFailure:
                        Assert.That(
                            system.TryFailIncident(
                                incident.IncidentId,
                                EmergencyIncidentFailureReason
                                    .DestinationUnreachable),
                            Is.True);
                        break;
                }

                Assert.That(
                    incident.State,
                    Is.EqualTo(expectedState));
                float remainingSeconds =
                    incident.StateRemainingSeconds;
                EmergencyIncidentFailureReason failureReason =
                    incident.FailureReason;
                int outcomesBeforeRestore = outcomeCount;
                EmergencyIncidentSaveData snapshot =
                    system.CreateSnapshot();

                system.RestoreSnapshot(snapshot);

                Assert.That(
                    system.ActiveIncidentCount,
                    Is.EqualTo(1));
                EmergencyIncident restored =
                    system.ActiveIncidents[0];
                Assert.That(
                    restored.State,
                    Is.EqualTo(expectedState));
                Assert.That(
                    restored.StateRemainingSeconds,
                    Is.EqualTo(remainingSeconds)
                        .Within(0.0001f));
                Assert.That(
                    restored.FailureReason,
                    Is.EqualTo(failureReason));
                Assert.That(
                    outcomeCount,
                    Is.EqualTo(outcomesBeforeRestore),
                    "Restore must not publish a duplicate success or failure outcome.");

                EmergencyIncidentSaveData roundTrip =
                    system.CreateSnapshot();
                Assert.That(
                    roundTrip.ActiveIncidents,
                    Has.Length.EqualTo(1));
                Assert.That(
                    roundTrip.ActiveIncidents[0].State,
                    Is.EqualTo((int)expectedState));
                Assert.That(
                    roundTrip.ActiveIncidents[0]
                        .StateRemainingSeconds,
                    Is.EqualTo(remainingSeconds)
                        .Within(0.0001f));

                switch (expectedState)
                {
                    case EmergencyIncidentState
                        .AmbulanceOutbound:
                        Assert.That(
                            system.TryMarkAmbulanceArrived(
                                restored.IncidentId),
                            Is.True);
                        Assert.That(
                            system.TryMarkAmbulanceArrived(
                                restored.IncidentId),
                            Is.False);
                        Assert.That(
                            outcomeCount,
                            Is.EqualTo(
                                outcomesBeforeRestore));
                        break;

                    case EmergencyIncidentState.Treating:
                        system.Tick(
                            remainingSeconds + 0.01f);
                        Assert.That(
                            restored.State,
                            Is.EqualTo(
                                EmergencyIncidentState
                                    .AmbulanceReturning));
                        Assert.That(
                            outcomeCount,
                            Is.EqualTo(
                                outcomesBeforeRestore));
                        break;

                    case EmergencyIncidentState
                        .AmbulanceReturning:
                        Assert.That(
                            system.TryMarkAmbulanceReturned(
                                restored.IncidentId),
                            Is.True);
                        Assert.That(
                            system.TryMarkAmbulanceReturned(
                                restored.IncidentId),
                            Is.False);
                        Assert.That(
                            outcomeCount,
                            Is.EqualTo(
                                outcomesBeforeRestore + 1));
                        break;

                    case EmergencyIncidentState
                        .AmbulanceReturningAfterFailure:
                        Assert.That(
                            system.TryMarkAmbulanceReturned(
                                restored.IncidentId),
                            Is.True);
                        Assert.That(
                            system.TryMarkAmbulanceReturned(
                                restored.IncidentId),
                            Is.False);
                        Assert.That(
                            outcomeCount,
                            Is.EqualTo(
                                outcomesBeforeRestore));
                        break;

                    case EmergencyIncidentState
                        .WaitingForHospital:
                        system.Tick(0f);
                        Assert.That(
                            system.ActiveIncidentCount,
                            Is.EqualTo(1));
                        Assert.That(
                            restored.State,
                            Is.EqualTo(
                                EmergencyIncidentState
                                    .WaitingForHospital));
                        Assert.That(
                            outcomeCount,
                            Is.EqualTo(
                                outcomesBeforeRestore));
                        break;
                }
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void IncidentSnapshot_PreservesDeadlineAndDailyLimit()
        {
            var calendar = new TestGameCalendar();
            CreateEmergencyTestWorld(
                useExternalTransport: true,
                out GameObject owner,
                out EmergencyIncidentSystem system,
                out EmergencyIncidentConfigSO config,
                gameCalendar: calendar);

            try
            {
                Assert.That(
                    system.TryCreateAutomaticIncident(),
                    Is.True);
                EmergencyIncident original =
                    system.ActiveIncidents[0];
                EmergencyIncidentSaveData snapshot =
                    system.CreateSnapshot();
                long deadline =
                    original.DeadlineAbsoluteHour;

                system.RestoreSnapshot(snapshot);

                Assert.That(
                    system.ActiveIncidentCount,
                    Is.EqualTo(1));
                Assert.That(
                    system.ActiveIncidents[0]
                        .DeadlineAbsoluteHour,
                    Is.EqualTo(deadline));
                Assert.That(
                    system.AutomaticDispatchCountToday,
                    Is.EqualTo(1));
                Assert.That(
                    system.TryCreateAutomaticIncident(),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(config);
            }
        }

        private static void CreateEmergencyTestWorld(
            bool useExternalTransport,
            out GameObject owner,
            out EmergencyIncidentSystem system,
            out EmergencyIncidentConfigSO config,
            bool enableAutomaticSpawn = false,
            TestGameCalendar gameCalendar = null)
        {
            SimConfig simulationConfig =
                SimConfig.Default();
            var events = new SimEventHub();
            var engine =
                new SimEngine(
                    simulationConfig,
                    events);

            Assert.That(
                ContentFeaturePrototypeScenario
                    .BuildPrototypeCity(engine),
                Is.GreaterThan(0));

            config =
                ScriptableObject.CreateInstance<
                    EmergencyIncidentConfigSO>();
            SerializedObject configValues =
                new(config);
            configValues.FindProperty(
                    "maximumActiveIncidents")
                .intValue = 1;
            configValues.FindProperty("houseWeight")
                .floatValue = 1f;
            configValues.FindProperty("officeWeight")
                .floatValue = 1f;
            configValues.FindProperty("schoolWeight")
                .floatValue = 1f;
            configValues.FindProperty(
                    "specialBuildingWeight")
                .floatValue = 1f;
            configValues.FindProperty(
                    "recentTargetHistorySize")
                .intValue = 1;
            configValues.FindProperty(
                    "travelSecondsPerTile")
                .floatValue = 0.01f;
            configValues.FindProperty("treatmentSeconds")
                .floatValue = 0.01f;
            configValues.ApplyModifiedPropertiesWithoutUndo();

            owner =
                new GameObject(
                    "EmergencyIncidentSystem_Test");
            system =
                owner.AddComponent<
                    EmergencyIncidentSystem>();
            SerializedObject systemValues =
                new(system);
            systemValues.FindProperty("config")
                .objectReferenceValue = config;
            systemValues.FindProperty(
                    "enableAutomaticSpawn")
                .boolValue = enableAutomaticSpawn;
            systemValues.FindProperty(
                    "useExternalAmbulanceTransport")
                .boolValue = useExternalTransport;
            systemValues.ApplyModifiedPropertiesWithoutUndo();

            var services =
                new CityFlowServices(
                    events,
                    engine,
                    engine,
                    stats: engine);
            if (gameCalendar != null)
            {
                services.RegisterGameCalendar(
                    gameCalendar);
            }

            system.Initialize(services);
            Assert.That(system.IsInitialized, Is.True);
        }

        private sealed class TestGameCalendar :
            IGameCalendarService
        {
            public int Year => 1;
            public int Month => 1;
            public int Day { get; private set; } = 1;
            public int Hour { get; private set; }
            public int TotalMonths => 1;
            public long TotalDays { get; private set; }
            public float RealSecondsPerGameHour => 1f;
            public float RealSecondsPerGameDay => 24f;
            public int HoursPerDay => 24;
            public float TimeOfDay01 => 0f;

            public event System.Action<int> HourChanged;
            public event System.Action<int> DayChanged;
            public event System.Action<int> MonthChanged;

            public void AdvanceDay()
            {
                TotalDays++;
                Day++;
                Hour = 0;
                DayChanged?.Invoke(Day);
            }

            public void AdvanceHours(int hours)
            {
                int remaining = Mathf.Max(0, hours);

                while (remaining-- > 0)
                {
                    Hour++;

                    if (Hour >= HoursPerDay)
                    {
                        Hour = 0;
                        TotalDays++;
                        Day++;
                        DayChanged?.Invoke(Day);
                    }

                    HourChanged?.Invoke(Hour);
                }
            }
        }

    }
}
