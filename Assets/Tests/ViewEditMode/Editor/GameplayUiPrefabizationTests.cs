using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CityFlow.Gameplay.Quests;
using CityFlow.UI;
using CityFlow.UI.Controllers;
using CityFlow.UI.Feed;
using CityFlow.View;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CityFlow.Tests.ViewEditMode
{
    public sealed class GameplayUiPrefabizationTests
    {
        private const string OutputRoot =
            "Assets/02_Prefabs/UI/Gameplay";
        private const string SharedAssetRoot =
            "Assets/02_Prefabs/UI/Shared/LayerLab";
        private const string DownloadRoot = "Assets/99_Download/";
        private const string IntegratedScenePath =
            "Assets/00_Scenes/CityFlowIntegrated_cmt.unity";
        private const string EnvironmentVisualPrefabPath =
            "Assets/02_Prefabs/Environment/" +
            "EnvironmentVisualSystem.prefab";
        private const string PoliceContentPrefabPath =
            "Assets/02_Prefabs/Vehicles/PoliceContent.prefab";
        private const string HiringStatusPrefabPath =
            "Assets/02_Prefabs/UI/HiringStatusSystem.prefab";

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
            Scene testScene = default;
            try
            {
                testScene = EditorSceneManager.NewPreviewScene();
                GameObject rootAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{OutputRoot}/UI_MainCanvasRoot.prefab");
                Assert.That(rootAsset, Is.Not.Null);

                GameObject rootInstance =
                    PrefabUtility.InstantiatePrefab(
                        rootAsset,
                        testScene) as GameObject;
                Assert.That(rootInstance, Is.Not.Null);

                Transform controllerHostTransform =
                    rootInstance.transform.Find("FloatingWindowContentRoot");
                Assert.That(controllerHostTransform, Is.Not.Null);
                GameObject controllerHost = controllerHostTransform.gameObject;
                PlacementController placement =
                    controllerHost.AddComponent<PlacementController>();
                TileSelectionController tileSelection =
                    controllerHost.AddComponent<TileSelectionController>();
                Canvas titleBarCanvas =
                    controllerHost.AddComponent<Canvas>();
                controllerHost.AddComponent<CanvasScaler>();
                FloatingWindowTitleBarController titleBar =
                    controllerHost.AddComponent<
                        FloatingWindowTitleBarController>();

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

                ConfirmPopupController confirmPopup =
                    rootInstance.GetComponentInChildren<
                        ConfirmPopupController>(true);
                AnalysisCardController analysisCard =
                    rootInstance.GetComponentInChildren<
                        AnalysisCardController>(true);
                Canvas contentCanvas =
                    rootInstance.GetComponent<Canvas>();
                RectTransform contentRoot =
                    rootInstance.transform.Find(
                        "FloatingWindowContentRoot") as RectTransform;
                Assert.That(confirmPopup, Is.Not.Null);
                Assert.That(analysisCard, Is.Not.Null);
                Assert.That(contentCanvas, Is.Not.Null);
                Assert.That(contentRoot, Is.Not.Null);
                AssertObjectReference(
                    placement,
                    "confirmPopup",
                    confirmPopup);
                AssertObjectReference(
                    tileSelection,
                    "analysisCard",
                    analysisCard);
                AssertObjectReference(
                    titleBar,
                    "contentCanvas",
                    contentCanvas);
                AssertObjectReference(
                    titleBar,
                    "contentRoot",
                    contentRoot);
                Assert.That(
                    QuestRuntimeHost.SelectTargetCanvas(
                        new[] { titleBarCanvas, contentCanvas }),
                    Is.SameAs(contentCanvas));
            }
            finally
            {
                if (testScene.IsValid() && testScene.isLoaded)
                {
                    EditorSceneManager.ClosePreviewScene(testScene);
                }
            }
        }

        [Test]
        public void BuildingPlacementBinding_KeepsBuildMenuOpen()
        {
            var placementObject = new GameObject("PlacementController");
            var dockObject = new GameObject("UIDockController");
            try
            {
                PlacementController placement =
                    placementObject.AddComponent<PlacementController>();
                UIDockController dock =
                    dockObject.AddComponent<UIDockController>();
                FieldInfo currentMenuField =
                    typeof(UIDockController).GetField(
                        "_currentMenu",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance);
                Assert.That(currentMenuField, Is.Not.Null);
                currentMenuField.SetValue(
                    dock,
                    UIDockController.MenuType.Build);

                MainCityView.BindPlacementBuildMenuState(
                    placement,
                    dock);

                Assert.That(placement.IsBuildMenuOpen, Is.Not.Null);
                Assert.That(placement.IsBuildMenuOpen(), Is.True);
                FieldInfo completionEventField =
                    typeof(PlacementController).GetField(
                        "BuildingPlacementCompleted",
                        BindingFlags.NonPublic |
                        BindingFlags.Instance);
                Assert.That(completionEventField, Is.Not.Null);
                Delegate completionHandlers =
                    completionEventField.GetValue(placement) as Delegate;
                bool closesDock = completionHandlers?
                    .GetInvocationList()
                    .Any(handler =>
                        ReferenceEquals(handler.Target, dock) &&
                        handler.Method.Name ==
                            nameof(UIDockController.CloseAllPanels)) ??
                    false;
                Assert.That(
                    closesDock,
                    Is.False,
                    "Completing a building must keep the Build tab open " +
                    "after the building placement selection ends.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(dockObject);
                UnityEngine.Object.DestroyImmediate(placementObject);
            }
        }

        [Test]
        public void IntegratedScene_UsesSharedMainCanvasRootPrefab()
        {
            Scene scene = SceneManager.GetSceneByPath(IntegratedScenePath);
            bool openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    IntegratedScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                GameObject[] roots = scene.GetRootGameObjects();
                Assert.That(
                    roots.Count(root => root.name == "UI_MainCanvas"),
                    Is.Zero,
                    "The legacy scene-owned main canvas must be removed.");

                GameplayUiRuntimeBinder[] binders = roots
                    .Select(root =>
                        root.GetComponent<GameplayUiRuntimeBinder>())
                    .Where(binder => binder != null)
                    .ToArray();
                Assert.That(binders, Has.Length.EqualTo(1));
                Assert.That(binders[0].name, Is.EqualTo("UI_MainCanvasRoot"));
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        binders[0].gameObject),
                    Is.EqualTo($"{OutputRoot}/UI_MainCanvasRoot.prefab"));
                Assert.That(
                    PrefabUtility.GetNearestPrefabInstanceRoot(
                        binders[0].gameObject),
                    Is.SameAs(binders[0].gameObject));

                GameplayUiRuntimeBinder binder = binders[0];
                FloatingWindowTitleBarController titleBar =
                    FindSceneComponent<FloatingWindowTitleBarController>(
                        scene);
                Canvas contentCanvas = binder.GetComponent<Canvas>();

                Assert.That(titleBar, Is.Not.Null);
                Assert.That(contentCanvas, Is.Not.Null);

                Canvas titleBarCanvas = titleBar.GetComponent<Canvas>();
                Assert.That(titleBarCanvas, Is.Not.Null);
                Assert.That(
                    QuestRuntimeHost.SelectTargetCanvas(
                        new[] { titleBarCanvas, contentCanvas }),
                    Is.SameAs(contentCanvas));
                Assert.That(
                    contentCanvas.transform.Find(
                        "FloatingWindowContentRoot/HUD_TopBar"),
                    Is.Not.Null);
            }
            finally
            {
                if (openedForTest)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        [Test]
        public void IntegratedScene_ContainsPostSecondBuildFeatureUnits()
        {
            Scene scene = SceneManager.GetSceneByPath(IntegratedScenePath);
            bool openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    IntegratedScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                AssertScenePrefabInstance(
                    scene,
                    EnvironmentVisualPrefabPath);
                AssertScenePrefabInstance(scene, PoliceContentPrefabPath);
                AssertScenePrefabInstance(scene, HiringStatusPrefabPath);

                GameObject mainUi = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{OutputRoot}/UI_MainCanvasRoot.prefab");
                Assert.That(mainUi, Is.Not.Null);
                Assert.That(
                    mainUi.GetComponentInChildren<
                        ResearchPanelController>(true),
                    Is.Not.Null);

                GameObject feed = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{OutputRoot}/UI_GreenFeedDock.prefab");
                Assert.That(feed, Is.Not.Null);
                Assert.That(
                    feed.GetComponentsInChildren<TMP_Text>(true)
                        .Any(text => text.text == "빵빵"),
                    Is.True);
            }
            finally
            {
                if (openedForTest)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
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

        private static T FindSceneComponent<T>(Scene scene)
            where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<T>(true))
                .SingleOrDefault();
        }

        private static GameObject AssertScenePrefabInstance(
            Scene scene,
            string prefabPath)
        {
            GameObject[] instances = scene.GetRootGameObjects()
                .Where(root =>
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        root) == prefabPath)
                .ToArray();
            Assert.That(
                instances,
                Has.Length.EqualTo(1),
                prefabPath);
            Assert.That(instances[0].activeSelf, Is.True, prefabPath);
            return instances[0];
        }

        private static void AssertObjectReference(
            Component target,
            string propertyName,
            UnityEngine.Object expected)
        {
            SerializedObject serialized = new(target);
            SerializedProperty property =
                serialized.FindProperty(propertyName);
            Assert.That(property, Is.Not.Null, propertyName);
            Assert.That(
                property.objectReferenceValue,
                Is.SameAs(expected),
                propertyName);
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
