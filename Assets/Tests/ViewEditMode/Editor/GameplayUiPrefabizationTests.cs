using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CityFlow.UI;
using CityFlow.UI.Controllers;
using CityFlow.UI.Feed;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CityFlow.Tests.ViewEditMode
{
    public sealed class GameplayUiPrefabizationTests
    {
        private const string OutputRoot =
            "Assets/02_Prefabs/UI/Gameplay";
        private const string SharedAssetRoot =
            "Assets/02_Prefabs/UI/Shared/LayerLab";
        private const string DownloadRoot = "Assets/99_Download/";

        private static readonly string[] PrefabNames =
        {
            "UI_MainCanvasRoot.prefab",
            "UI_HudTopBar.prefab",
            "UI_TopLeftActionDock.prefab",
            "UI_DockRight.prefab",
            "UI_BuildPanel.prefab",
            "UI_SettingsPanel.prefab",
            "UI_GreenFeedDock.prefab"
        };

        [Test]
        public void GeneratedPrefabs_ExistWithoutMissingScripts()
        {
            for (int index = 0; index < PrefabNames.Length; index++)
            {
                string path = $"{OutputRoot}/{PrefabNames[index]}";
                GameObject prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(path);

                Assert.That(prefab, Is.Not.Null, path);
                Transform[] transforms =
                    prefab.GetComponentsInChildren<Transform>(true);
                for (int transformIndex = 0;
                     transformIndex < transforms.Length;
                     transformIndex++)
                {
                    Component[] components =
                        transforms[transformIndex].GetComponents<Component>();
                    Assert.That(
                        components.All(component => component != null),
                        Is.True,
                        $"Missing Script: {path} / " +
                        GetHierarchyPath(
                            transforms[transformIndex],
                            prefab.transform));
                }
            }
        }

        [Test]
        public void GeneratedPrefabs_DoNotReferenceIgnoredDownloadAssets()
        {
            string[] serializedAssetPaths = AssetDatabase.FindAssets(
                    string.Empty,
                    new[] { OutputRoot, SharedAssetRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(
                        ".prefab",
                        StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(
                        ".asset",
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

            for (int index = 0;
                 index < serializedAssetPaths.Length;
                 index++)
            {
                string path = serializedAssetPaths[index];
                string yaml = File.ReadAllText(path);
                string[] ignoredPaths = Regex.Matches(
                        yaml,
                        @"guid:\s*([0-9a-fA-F]{32})")
                    .Cast<Match>()
                    .Select(match => AssetDatabase.GUIDToAssetPath(
                        match.Groups[1].Value))
                    .Where(assetPath => assetPath.StartsWith(
                        DownloadRoot,
                        StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                Assert.That(
                    ignoredPaths,
                    Is.Empty,
                    $"Ignored local UI dependencies remain in {path}: " +
                    string.Join(", ", ignoredPaths));
            }
        }

        [Test]
        public void GeneratedRoot_UsesNestedModulePrefabs()
        {
            AssertNestedPrefab(
                "UI_MainCanvasRoot.prefab",
                "FloatingWindowContentRoot/HUD_TopBar",
                "UI_HudTopBar.prefab");
            AssertNestedPrefab(
                "UI_MainCanvasRoot.prefab",
                "FloatingWindowContentRoot/Dock_Right",
                "UI_DockRight.prefab");
            AssertNestedPrefab(
                "UI_MainCanvasRoot.prefab",
                "FloatingWindowContentRoot/Build_Panel",
                "UI_BuildPanel.prefab");
            AssertNestedPrefab(
                "UI_MainCanvasRoot.prefab",
                "FloatingWindowContentRoot/SubPanels_Right/Setting_Panel ",
                "UI_SettingsPanel.prefab");
            AssertNestedPrefab(
                "UI_MainCanvasRoot.prefab",
                "FloatingWindowContentRoot/GreenSNSFeedDock",
                "UI_GreenFeedDock.prefab");
            AssertNestedPrefab(
                "UI_HudTopBar.prefab",
                "TopLeftActionDock",
                "UI_TopLeftActionDock.prefab");
        }

        [Test]
        public void GeneratedRoot_AutomaticallyBindsPlacementController()
        {
            GameObject placementOwner = new("PlacementController");
            GameObject selectionOwner = new("TileSelectionController");
            GameObject rootInstance = null;
            try
            {
                PlacementController placement =
                    placementOwner.AddComponent<PlacementController>();
                TileSelectionController tileSelection =
                    selectionOwner.AddComponent<TileSelectionController>();
                GameObject rootAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{OutputRoot}/UI_MainCanvasRoot.prefab");
                Assert.That(rootAsset, Is.Not.Null);

                rootInstance = PrefabUtility.InstantiatePrefab(rootAsset)
                    as GameObject;
                Assert.That(rootInstance, Is.Not.Null);

                GameplayUiRuntimeBinder binder =
                    rootInstance.GetComponent<GameplayUiRuntimeBinder>();
                Assert.That(binder, Is.Not.Null);
                Assert.That(binder.BindRuntimeReferences(), Is.True);
                Assert.That(binder.IsPlacementBound, Is.True);
                Assert.That(binder.IsDockUiBound, Is.True);
                Assert.That(binder.IsBuildPanelBound, Is.True);
                Assert.That(binder.IsGreenFeedBound, Is.True);

                AssertPlacementReference(
                    rootInstance.GetComponentInChildren<UIDockController>(true),
                    placement);
                AssertPlacementReference(
                    rootInstance.GetComponentInChildren<BuildPanelController>(
                        true),
                    placement);
                AssertGreenFeedReferences(rootInstance, tileSelection);
                Assert.That(
                    rootInstance.GetComponentInChildren<UIDockController>(true)
                        .HasExternalUiReferences,
                    Is.True);
                Assert.That(
                    rootInstance.GetComponentInChildren<BuildPanelController>(
                        true).HasRuntimeReferences,
                    Is.True);
            }
            finally
            {
                if (rootInstance != null)
                {
                    UnityEngine.Object.DestroyImmediate(rootInstance);
                }

                UnityEngine.Object.DestroyImmediate(placementOwner);
                UnityEngine.Object.DestroyImmediate(selectionOwner);
            }
        }

        private static void AssertGreenFeedReferences(
            GameObject root,
            TileSelectionController expectedTileSelection)
        {
            GreenFeedPanelController controller =
                root.GetComponentInChildren<GreenFeedPanelController>(true);
            Assert.That(controller, Is.Not.Null);

            GreenFeedHoverRelay[] relays =
                root.GetComponentsInChildren<GreenFeedHoverRelay>(true);
            Assert.That(relays, Is.Not.Empty);
            for (int index = 0; index < relays.Length; index++)
            {
                Assert.That(relays[index].Controller, Is.SameAs(controller));
                if (relays[index].Action ==
                    GreenFeedHoverRelay.ClickAction.Locate)
                {
                    Assert.That(
                        relays[index].TileSelection,
                        Is.SameAs(expectedTileSelection));
                }
            }

            Assert.That(controller.TickerView, Is.Not.Null);
        }

        private static void AssertNestedPrefab(
            string ownerFileName,
            string relativePath,
            string expectedFileName)
        {
            string ownerPath = $"{OutputRoot}/{ownerFileName}";
            GameObject owner =
                AssetDatabase.LoadAssetAtPath<GameObject>(ownerPath);
            Assert.That(owner, Is.Not.Null, ownerPath);

            Transform nested = owner.transform.Find(relativePath);
            Assert.That(nested, Is.Not.Null, $"{ownerPath}/{relativePath}");
            string actualPath =
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    nested.gameObject);
            Assert.That(
                actualPath,
                Is.EqualTo($"{OutputRoot}/{expectedFileName}"),
                $"{ownerPath}/{relativePath}");
        }

        private static void AssertPlacementReference(
            Component target,
            PlacementController expected)
        {
            Assert.That(target, Is.Not.Null);
            SerializedObject serialized = new(target);
            SerializedProperty property =
                serialized.FindProperty("placementController");
            Assert.That(property, Is.Not.Null);
            Assert.That(property.objectReferenceValue, Is.SameAs(expected));
        }

        private static string GetHierarchyPath(
            Transform target,
            Transform root)
        {
            List<string> segments = new();
            Transform current = target;
            while (current != null)
            {
                segments.Add(current.name);
                if (current == root)
                {
                    break;
                }

                current = current.parent;
            }

            segments.Reverse();
            return string.Join("/", segments);
        }
    }
}
