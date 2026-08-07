using CityFlow.Content;
using CityFlow.Content.Transit;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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

            Assert.That(calls, Is.Not.Null);
            Assert.That(dispatch, Is.Not.Null);

            SerializedObject callValues = new(calls);
            SerializedObject dispatchValues = new(dispatch);
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
                SerializedObject values = new(system);
                values.FindProperty("config")
                    .objectReferenceValue = config;
                values.ApplyModifiedPropertiesWithoutUndo();

                var source = new PoliceDispatchSaveData
                {
                    NextCallId = 18,
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
    }
}
