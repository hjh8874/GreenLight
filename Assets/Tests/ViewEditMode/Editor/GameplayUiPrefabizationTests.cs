using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        private const string SourceScenePath =
            "Assets/00_Scenes/CityFlowIntegrated_cmt.unity";
        private const string OutputRoot =
            "Assets/02_Prefabs/UI/Gameplay";
        private const string SharedAssetRoot =
            "Assets/02_Prefabs/UI/Shared/LayerLab";
        private const string DownloadRoot = "Assets/99_Download/";

        private static readonly ModuleSpec[] Modules =
        {
            new(
                "UI_MainCanvasRoot.prefab",
                "UI_MainCanvas"),
            new(
                "UI_HudTopBar.prefab",
                "UI_MainCanvas/FloatingWindowContentRoot/HUD_TopBar"),
            new(
                "UI_TopLeftActionDock.prefab",
                "UI_MainCanvas/FloatingWindowContentRoot/HUD_TopBar/" +
                "TopLeftActionDock"),
            new(
                "UI_DockRight.prefab",
                "UI_MainCanvas/FloatingWindowContentRoot/Dock_Right"),
            new(
                "UI_BuildPanel.prefab",
                "UI_MainCanvas/FloatingWindowContentRoot/Build_Panel"),
            new(
                "UI_SettingsPanel.prefab",
                "UI_MainCanvas/FloatingWindowContentRoot/SubPanels_Right/" +
                "Setting_Panel "),
            new(
                "UI_GreenFeedDock.prefab",
                "UI_MainCanvas/FloatingWindowContentRoot/GreenSNSFeedDock")
        };

        [Test]
        public void GeneratedPrefabs_ExistWithoutMissingScripts()
        {
            for (int index = 0; index < Modules.Length; index++)
            {
                string path = $"{OutputRoot}/{Modules[index].FileName}";
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
        public void GeneratedPrefabs_MatchSourceVisualHierarchy()
        {
            Scene sourceScene = SceneManager.GetSceneByPath(SourceScenePath);
            bool openedScene = !sourceScene.IsValid() || !sourceScene.isLoaded;
            if (openedScene)
            {
                sourceScene = EditorSceneManager.OpenScene(
                    SourceScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                for (int index = 0; index < Modules.Length; index++)
                {
                    ModuleSpec module = Modules[index];
                    Transform source = FindByPath(
                        sourceScene,
                        module.HierarchyPath);
                    string prefabPath =
                        $"{OutputRoot}/{module.FileName}";
                    GameObject prefab =
                        AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

                    Assert.That(source, Is.Not.Null, module.HierarchyPath);
                    Assert.That(prefab, Is.Not.Null, prefabPath);
                    CompareVisualTree(
                        source,
                        prefab.transform,
                        module.HierarchyPath,
                        false,
                        false);
                }
            }
            finally
            {
                if (openedScene && sourceScene.IsValid() &&
                    sourceScene.isLoaded)
                {
                    EditorSceneManager.CloseScene(sourceScene, true);
                }
            }
        }

        private static void CompareVisualTree(
            Transform source,
            Transform prefab,
            string path,
            bool compareName = true,
            bool compareRectTransform = true)
        {
            if (compareName)
            {
                Assert.That(prefab.name, Is.EqualTo(source.name), path);
            }
            Assert.That(
                prefab.gameObject.activeSelf,
                Is.EqualTo(source.gameObject.activeSelf),
                path);
            Assert.That(prefab.childCount, Is.EqualTo(source.childCount), path);

            CompareComponentTypes(source, prefab, path);
            if (compareRectTransform)
            {
                CompareRectTransform(source as RectTransform,
                    prefab as RectTransform, path);
            }
            CompareImage(source.GetComponent<Image>(),
                prefab.GetComponent<Image>(), path);
            CompareText(source.GetComponent<TMP_Text>(),
                prefab.GetComponent<TMP_Text>(), path);
            CompareLayout(source.GetComponent<LayoutGroup>(),
                prefab.GetComponent<LayoutGroup>(), path);
            CompareLayoutElement(source.GetComponent<LayoutElement>(),
                prefab.GetComponent<LayoutElement>(), path);
            CompareSlider(source.GetComponent<Slider>(),
                prefab.GetComponent<Slider>(), path);

            for (int index = 0; index < source.childCount; index++)
            {
                Transform sourceChild = source.GetChild(index);
                Transform prefabChild = prefab.GetChild(index);
                CompareVisualTree(
                    sourceChild,
                    prefabChild,
                    $"{path}/{sourceChild.name}");
            }
        }

        private static void CompareComponentTypes(
            Transform source,
            Transform prefab,
            string path)
        {
            string[] sourceTypes = source.GetComponents<Component>()
                .Select(component => component?.GetType().FullName ??
                    "<Missing Script>")
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            string[] prefabTypes = prefab.GetComponents<Component>()
                .Select(component => component?.GetType().FullName ??
                    "<Missing Script>")
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Assert.That(prefabTypes, Is.EqualTo(sourceTypes), path);
        }

        private static void CompareRectTransform(
            RectTransform source,
            RectTransform prefab,
            string path)
        {
            Assert.That(prefab == null, Is.EqualTo(source == null), path);
            if (source == null)
            {
                return;
            }

            bool sourceIsSliderDriven = IsSliderDriven(source);
            bool prefabIsSliderDriven = IsSliderDriven(prefab);
            Assert.That(prefabIsSliderDriven,
                Is.EqualTo(sourceIsSliderDriven), path);
            if (sourceIsSliderDriven)
            {
                return;
            }

            AssertVector(prefab.anchorMin, source.anchorMin,
                $"{path}.anchorMin");
            AssertVector(prefab.anchorMax, source.anchorMax,
                $"{path}.anchorMax");
            AssertVector(prefab.pivot, source.pivot,
                $"{path}.pivot");
            AssertVector(
                prefab.anchoredPosition,
                source.anchoredPosition,
                $"{path}.anchoredPosition");
            AssertVector(prefab.sizeDelta, source.sizeDelta,
                $"{path}.sizeDelta");
            AssertVector(prefab.localScale, source.localScale,
                $"{path}.localScale");
        }

        private static void CompareImage(
            Image source,
            Image prefab,
            string path)
        {
            Assert.That(prefab == null, Is.EqualTo(source == null), path);
            if (source == null)
            {
                return;
            }

            SerializedObject sourceSerialized = new(source);
            SerializedObject prefabSerialized = new(prefab);
            SerializedProperty sourceSprite =
                sourceSerialized.FindProperty("m_Sprite");
            SerializedProperty prefabSprite =
                prefabSerialized.FindProperty("m_Sprite");

            AssertColor(
                prefabSerialized.FindProperty("m_Color").colorValue,
                sourceSerialized.FindProperty("m_Color").colorValue,
                $"{path}.imageColor");
            Assert.That(
                prefabSerialized.FindProperty("m_Type").intValue,
                Is.EqualTo(
                    sourceSerialized.FindProperty("m_Type").intValue),
                path);
            Assert.That(
                prefabSerialized.FindProperty("m_RaycastTarget").boolValue,
                Is.EqualTo(sourceSerialized
                    .FindProperty("m_RaycastTarget").boolValue),
                path);
            Assert.That(
                prefabSprite.objectReferenceValue?.name,
                Is.EqualTo(sourceSprite.objectReferenceValue?.name),
                path);
        }

        private static void CompareText(
            TMP_Text source,
            TMP_Text prefab,
            string path)
        {
            Assert.That(prefab == null, Is.EqualTo(source == null), path);
            if (source == null)
            {
                return;
            }

            Assert.That(prefab.text, Is.EqualTo(source.text), path);
            Assert.That(prefab.fontSize, Is.EqualTo(source.fontSize), path);
            Assert.That(prefab.fontStyle, Is.EqualTo(source.fontStyle), path);
            Assert.That(prefab.alignment, Is.EqualTo(source.alignment), path);
            Assert.That(
                prefab.raycastTarget,
                Is.EqualTo(source.raycastTarget),
                path);
            Assert.That(
                prefab.font?.name,
                Is.EqualTo(source.font?.name),
                path);
            AssertColor(prefab.color, source.color, path);
        }

        private static void CompareLayout(
            LayoutGroup source,
            LayoutGroup prefab,
            string path)
        {
            Assert.That(prefab == null, Is.EqualTo(source == null), path);
            if (source == null)
            {
                return;
            }

            Assert.That(prefab.padding.left,
                Is.EqualTo(source.padding.left), path);
            Assert.That(prefab.padding.right,
                Is.EqualTo(source.padding.right), path);
            Assert.That(prefab.padding.top,
                Is.EqualTo(source.padding.top), path);
            Assert.That(prefab.padding.bottom,
                Is.EqualTo(source.padding.bottom), path);
            Assert.That(
                prefab.childAlignment,
                Is.EqualTo(source.childAlignment),
                path);

            if (source is HorizontalOrVerticalLayoutGroup sourceLinear &&
                prefab is HorizontalOrVerticalLayoutGroup prefabLinear)
            {
                Assert.That(
                    prefabLinear.spacing,
                    Is.EqualTo(sourceLinear.spacing),
                    path);
            }

            if (source is GridLayoutGroup sourceGrid &&
                prefab is GridLayoutGroup prefabGrid)
            {
                AssertVector(prefabGrid.cellSize,
                    sourceGrid.cellSize, path);
                AssertVector(prefabGrid.spacing,
                    sourceGrid.spacing, path);
                Assert.That(prefabGrid.constraint,
                    Is.EqualTo(sourceGrid.constraint), path);
                Assert.That(prefabGrid.constraintCount,
                    Is.EqualTo(sourceGrid.constraintCount), path);
            }
        }

        private static void CompareLayoutElement(
            LayoutElement source,
            LayoutElement prefab,
            string path)
        {
            Assert.That(prefab == null, Is.EqualTo(source == null), path);
            if (source == null)
            {
                return;
            }

            Assert.That(prefab.minWidth, Is.EqualTo(source.minWidth), path);
            Assert.That(prefab.minHeight, Is.EqualTo(source.minHeight), path);
            Assert.That(prefab.preferredWidth,
                Is.EqualTo(source.preferredWidth), path);
            Assert.That(prefab.preferredHeight,
                Is.EqualTo(source.preferredHeight), path);
            Assert.That(prefab.flexibleWidth,
                Is.EqualTo(source.flexibleWidth), path);
            Assert.That(prefab.flexibleHeight,
                Is.EqualTo(source.flexibleHeight), path);
        }

        private static void CompareSlider(
            Slider source,
            Slider prefab,
            string path)
        {
            Assert.That(prefab == null, Is.EqualTo(source == null), path);
            if (source == null)
            {
                return;
            }

            SerializedObject sourceSerialized = new(source);
            SerializedObject prefabSerialized = new(prefab);
            string[] comparableProperties =
            {
                "m_MinValue",
                "m_MaxValue",
                "m_WholeNumbers",
                "m_Value",
                "m_Direction"
            };
            for (int index = 0;
                 index < comparableProperties.Length;
                 index++)
            {
                string propertyName = comparableProperties[index];
                Assert.That(
                    prefabSerialized.FindProperty(propertyName)
                        .boxedValue,
                    Is.EqualTo(sourceSerialized
                        .FindProperty(propertyName).boxedValue),
                    $"{path}.{propertyName}");
            }

            Assert.That(
                prefab.fillRect?.name,
                Is.EqualTo(source.fillRect?.name),
                $"{path}.fillRect");
            Assert.That(
                prefab.handleRect?.name,
                Is.EqualTo(source.handleRect?.name),
                $"{path}.handleRect");
        }

        private static bool IsSliderDriven(RectTransform target)
        {
            Slider slider = target.GetComponentInParent<Slider>(true);
            return slider != null &&
                (slider.fillRect == target || slider.handleRect == target);
        }

        private static Transform FindByPath(
            Scene scene,
            string hierarchyPath)
        {
            string[] segments = hierarchyPath.Split('/');
            GameObject root = scene.GetRootGameObjects().FirstOrDefault(
                candidate => candidate.name == segments[0]);
            if (root == null)
            {
                return null;
            }

            Transform current = root.transform;
            for (int index = 1; index < segments.Length; index++)
            {
                current = current.Find(segments[index]);
                if (current == null)
                {
                    return null;
                }
            }

            return current;
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

        private static void AssertVector(
            Vector2 actual,
            Vector2 expected,
            string path)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f), path);
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f), path);
        }

        private static void AssertVector(
            Vector3 actual,
            Vector3 expected,
            string path)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f), path);
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f), path);
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.001f), path);
        }

        private static void AssertColor(
            Color actual,
            Color expected,
            string path)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.001f), path);
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.001f), path);
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.001f), path);
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.001f), path);
        }

        private readonly struct ModuleSpec
        {
            public ModuleSpec(string fileName, string hierarchyPath)
            {
                FileName = fileName;
                HierarchyPath = hierarchyPath;
            }

            public string FileName { get; }
            public string HierarchyPath { get; }
        }
    }
}
