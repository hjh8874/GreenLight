using System;
using System.Reflection;
using CityFlow.Content.Transit;
using CityFlow.UI;
using CityFlow.UI.Controllers;
using CityFlow.UI.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Tests.EditMode
{
    public sealed class BusStopIntegrationGuardTests
    {
        [Test]
        public void BusStopUi_IsUnavailableWithoutRegistry()
        {
            InfrastructureDataSO busStopData =
                CreateInfrastructureData(
                    InfrastructureKind.BusStop);
            InfrastructureDataSO signalData =
                CreateInfrastructureData(
                    InfrastructureKind.Signal);
            GameObject panelObject =
                new("BuildPanel");
            GameObject infraPage =
                new("InfraPage");
            GameObject slotObject =
                new("SignalSlot");
            GameObject coordinatorObject =
                new("InfrastructurePlacementCoordinator");

            try
            {
                slotObject.transform.SetParent(
                    infraPage.transform,
                    false);
                slotObject
                    .AddComponent<InfrastructureSlotController>()
                    .Configure(signalData);

                BuildPanelController panel =
                    panelObject.AddComponent<BuildPanelController>();
                SetPrivateField(
                    panel,
                    "categoryPages",
                    new[] { infraPage });
                SetPrivateField(
                    panel,
                    "busStopData",
                    busStopData);
                InvokePrivate(panel, "EnsureBusStopSlot");

                Assert.That(
                    infraPage.transform.childCount,
                    Is.EqualTo(1));

                InfrastructurePlacementCoordinator coordinator =
                    coordinatorObject.AddComponent<
                        InfrastructurePlacementCoordinator>();
                coordinator.StartPlacement(busStopData);
                Assert.That(
                    coordinator.IsBuildingMode,
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(coordinatorObject);
                Object.DestroyImmediate(slotObject);
                Object.DestroyImmediate(infraPage);
                Object.DestroyImmediate(panelObject);
                Object.DestroyImmediate(signalData);
                Object.DestroyImmediate(busStopData);
            }
        }

        [Test]
        public void BusStopUi_IsAvailableWithRegistry()
        {
            InfrastructureDataSO busStopData =
                CreateInfrastructureData(
                    InfrastructureKind.BusStop);
            InfrastructureDataSO signalData =
                CreateInfrastructureData(
                    InfrastructureKind.Signal);
            GameObject panelObject =
                new("BuildPanel");
            GameObject infraPage =
                new("InfraPage");
            GameObject slotObject =
                new("SignalSlot");
            GameObject integrationObject = null;
            GameObject coordinatorObject =
                new("InfrastructurePlacementCoordinator");

            try
            {
                GameObject integrationPrefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/02_Prefabs/PR151_ContentFeaturePrototype.prefab");
                Assert.That(integrationPrefab, Is.Not.Null);
                integrationObject =
                    Object.Instantiate(integrationPrefab);
                Assert.That(
                    integrationObject.GetComponent<BusStopRegistry>(),
                    Is.Not.Null);
                slotObject.transform.SetParent(
                    infraPage.transform,
                    false);
                slotObject
                    .AddComponent<InfrastructureSlotController>()
                    .Configure(signalData);

                BuildPanelController panel =
                    panelObject.AddComponent<BuildPanelController>();
                SetPrivateField(
                    panel,
                    "categoryPages",
                    new[] { infraPage });
                SetPrivateField(
                    panel,
                    "busStopData",
                    busStopData);
                InvokePrivate(panel, "EnsureBusStopSlot");

                InfrastructureSlotController[] slots =
                    infraPage.GetComponentsInChildren<
                        InfrastructureSlotController>(true);
                Assert.That(slots.Length, Is.EqualTo(2));
                Assert.That(
                    Array.Exists(
                        slots,
                        slot =>
                            slot.InfraData != null &&
                            slot.InfraData.Kind ==
                            InfrastructureKind.BusStop),
                    Is.True);

                InfrastructurePlacementCoordinator coordinator =
                    coordinatorObject.AddComponent<
                        InfrastructurePlacementCoordinator>();
                coordinator.StartPlacement(busStopData);
                Assert.That(
                    coordinator.IsBuildingMode,
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(coordinatorObject);
                Object.DestroyImmediate(integrationObject);
                Object.DestroyImmediate(slotObject);
                Object.DestroyImmediate(infraPage);
                Object.DestroyImmediate(panelObject);
                Object.DestroyImmediate(signalData);
                Object.DestroyImmediate(busStopData);
            }
        }

        private static InfrastructureDataSO
            CreateInfrastructureData(
                InfrastructureKind kind)
        {
            InfrastructureDataSO data =
                ScriptableObject.CreateInstance<
                    InfrastructureDataSO>();
            data.Kind = kind;
            data.InfrastructureName = kind.ToString();
            return data;
        }

        private static void SetPrivateField(
            object target,
            string fieldName,
            object value)
        {
            FieldInfo field =
                target.GetType().GetField(
                    fieldName,
                    BindingFlags.Instance |
                    BindingFlags.NonPublic);
            Assert.That(
                field,
                Is.Not.Null,
                $"Field {fieldName} was not found.");
            field.SetValue(target, value);
        }

        private static void InvokePrivate(
            object target,
            string methodName)
        {
            MethodInfo method =
                target.GetType().GetMethod(
                    methodName,
                    BindingFlags.Instance |
                    BindingFlags.NonPublic);
            Assert.That(
                method,
                Is.Not.Null,
                $"Method {methodName} was not found.");
            method.Invoke(target, null);
        }
    }
}
