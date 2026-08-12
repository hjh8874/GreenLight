using System;
using System.Linq;
using CityFlow.View;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CityFlow.Tests.ViewEditMode
{
    public sealed class FreeFlowStreakPresentationTests
    {
        private const string ProfilePath =
            "Assets/05_ScriptableObjects/Resources/CityFlow/" +
            "FreeFlowStreakVfxProfile.asset";
        private const string ProjectPrefabRoot =
            "Assets/02_Prefabs/Vehicles/Effects/FreeFlowStreak/";

        [Test]
        public void Profile_UsesProjectOwnedVfxPrefabs()
        {
            FreeFlowStreakVfxProfileSO profile =
                AssetDatabase.LoadAssetAtPath<
                    FreeFlowStreakVfxProfileSO>(ProfilePath);
            Assert.That(profile, Is.Not.Null);

            GameObject[] prefabs =
            {
                profile.StageTwoPrefab,
                profile.StageThreeGlowPrefab,
                profile.StageThreeStarsPrefab,
                profile.BottleneckMarkerPrefab
            };
            for (int index = 0; index < prefabs.Length; index++)
            {
                Assert.That(prefabs[index], Is.Not.Null);
                string path = AssetDatabase.GetAssetPath(prefabs[index]);
                Assert.That(
                    path.StartsWith(
                        ProjectPrefabRoot,
                        StringComparison.Ordinal),
                    Is.True,
                    path);

                string[] externalPrefabDependencies =
                    AssetDatabase.GetDependencies(path, true)
                        .Where(dependency =>
                            dependency.StartsWith(
                                "Assets/99_Download/",
                                StringComparison.OrdinalIgnoreCase) &&
                            dependency.EndsWith(
                                ".prefab",
                                StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                Assert.That(
                    externalPrefabDependencies,
                    Is.Empty,
                    path);
            }
        }

        [Test]
        public void StageThreeWrapper_ShowsRearTrailAndBillboardNumber()
        {
            FreeFlowStreakVfxProfileSO profile =
                AssetDatabase.LoadAssetAtPath<
                    FreeFlowStreakVfxProfileSO>(ProfilePath);
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.StageThreeStarsPrefab, Is.Not.Null);

            GameObject vehicle =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject cameraObject = new("StageThreeCamera");
            GameObject presentationObject = null;
            try
            {
                vehicle.name = "StageThreeVehicle";
                BoxCollider vehicleCollider =
                    vehicle.GetComponent<BoxCollider>();
                vehicleCollider.center = new Vector3(0f, 0f, -0.5f);
                presentationObject = PrefabUtility.InstantiatePrefab(
                    profile.StageThreeStarsPrefab) as GameObject;
                Assert.That(presentationObject, Is.Not.Null);
                presentationObject.transform.SetParent(
                    vehicle.transform,
                    false);
                presentationObject.transform.localScale =
                    Vector3.one * profile.VfxScale;

                FreeFlowStreakStageThreePresentation presentation =
                    presentationObject.GetComponent<
                        FreeFlowStreakStageThreePresentation>();
                Assert.That(presentation, Is.Not.Null);
                presentation.RefreshLayout();

                TrailRenderer trail = presentation.Trail;
                TextMesh label = presentation.StageLabel;
                Assert.That(trail, Is.Not.Null);
                Assert.That(label, Is.Not.Null);
                Assert.That(label.text, Is.EqualTo("3"));

                Vector3 trailVehicleLocal = vehicle.transform
                    .InverseTransformPoint(trail.transform.position);
                Vector3 labelVehicleLocal = vehicle.transform
                    .InverseTransformPoint(label.transform.position);
                Assert.That(trailVehicleLocal.x, Is.LessThan(-0.4f));
                Assert.That(
                    Mathf.Abs(trailVehicleLocal.y),
                    Is.LessThan(0.001f));
                Assert.That(trailVehicleLocal.z, Is.LessThan(0f));
                Assert.That(labelVehicleLocal.z, Is.LessThan(-0.5f));

                Camera camera = cameraObject.AddComponent<Camera>();
                cameraObject.transform.position = new Vector3(1f, 2f, -5f);
                presentation.BillboardStageLabel(camera);
                Vector3 toCamera =
                    (camera.transform.position - label.transform.position)
                    .normalized;
                Assert.That(
                    Vector3.Dot(-label.transform.forward, toCamera),
                    Is.GreaterThan(0.999f));

                trail.SetPositions(new[]
                {
                    trail.transform.position,
                    trail.transform.position + Vector3.right
                });
                presentation.DeactivatePresentation();
                Assert.That(trail.emitting, Is.False);
                presentation.ActivatePresentation();
                Assert.That(trail.emitting, Is.True);
                Assert.That(trail.positionCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(presentationObject);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(vehicle);
            }
        }
    }
}
