using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CityFlow.UI.Editor
{
    public static class GameplayUiPrefabBaker
    {
        internal const string SourceScenePath =
            "Assets/00_Scenes/CityFlowIntegrated_cmt.unity";
        internal const string OutputRoot =
            "Assets/02_Prefabs/UI/Gameplay";
        internal const string SharedAssetRoot =
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

        [MenuItem("CityFlow/UI/Prefabs/Bake Gameplay UI Prefabs")]
        public static void BakeAll()
        {
            BakeAllInternal();
        }

        public static void BakeAllFromCommandLine()
        {
            try
            {
                BakeAllInternal();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }

            EditorApplication.Exit(0);
        }

        internal static IReadOnlyList<string> GetOutputPaths()
        {
            return Modules
                .Select(module => $"{OutputRoot}/{module.FileName}")
                .ToArray();
        }

        private static void BakeAllInternal()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Gameplay UI Prefabs cannot be baked in Play Mode.");
            }

            Scene originalActiveScene =
                SceneManager.GetActiveScene();
            Scene sourceScene = default;
            bool openedSourceScene = false;

            try
            {
                sourceScene = SceneManager.GetSceneByPath(SourceScenePath);
                if (!sourceScene.IsValid() || !sourceScene.isLoaded)
                {
                    sourceScene = EditorSceneManager.OpenScene(
                        SourceScenePath,
                        OpenSceneMode.Additive);
                    openedSourceScene = true;
                }
                else if (sourceScene.isDirty)
                {
                    throw new InvalidOperationException(
                        $"The prefab source scene has unsaved changes: " +
                        $"'{SourceScenePath}'.");
                }

                GameObject sourceCanvas = FindRoot(
                    sourceScene,
                    "UI_MainCanvas");
                if (sourceCanvas == null)
                {
                    throw new InvalidOperationException(
                        $"UI_MainCanvas was not found in '{SourceScenePath}'.");
                }

                EnsureFolder(OutputRoot);
                EnsureFolder(SharedAssetRoot);

                DependencyMap dependencyMap =
                    CopyTrackedDependencies(sourceCanvas);
                for (int index = 0; index < Modules.Length; index++)
                {
                    BakeModule(
                        sourceScene,
                        Modules[index],
                        dependencyMap);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log(
                    $"[GameplayUiPrefabBaker] Baked {Modules.Length} " +
                    $"prefabs without modifying '{SourceScenePath}'.");
            }
            finally
            {
                if (openedSourceScene && sourceScene.IsValid() &&
                    sourceScene.isLoaded)
                {
                    EditorSceneManager.CloseScene(sourceScene, true);
                }

                if (originalActiveScene.IsValid() &&
                    originalActiveScene.isLoaded &&
                    SceneManager.GetActiveScene() != originalActiveScene)
                {
                    SceneManager.SetActiveScene(originalActiveScene);
                }
            }
        }

        private static void BakeModule(
            Scene sourceScene,
            ModuleSpec module,
            DependencyMap dependencyMap)
        {
            Transform source = FindByPath(sourceScene, module.HierarchyPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"Gameplay UI source was not found: " +
                    $"'{module.HierarchyPath}'.");
            }

            GameObject clone = Object.Instantiate(source.gameObject);
            clone.name = source.gameObject.name;
            clone.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                RemapDownloadedAssets(clone, dependencyMap);
                ClearExternalSceneReferences(clone);
                RestoreRectTransforms(source, clone.transform);
                clone.hideFlags = HideFlags.None;

                string prefabPath =
                    $"{OutputRoot}/{module.FileName}";
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(
                    clone,
                    prefabPath,
                    out bool success);
                if (!success || prefab == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to save gameplay UI prefab: " +
                        $"'{prefabPath}'.");
                }

                RestoreSavedPrefabLayout(source, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(clone);
            }
        }

        private static DependencyMap CopyTrackedDependencies(
            GameObject sourceCanvas)
        {
            Object[] dependencies = EditorUtility.CollectDependencies(
                new Object[] { sourceCanvas });
            string[] paths = dependencies
                .Select(AssetDatabase.GetAssetPath)
                .Where(path => !string.IsNullOrEmpty(path) &&
                    path.StartsWith(
                        DownloadRoot,
                        StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            DependencyMap map = new();
            List<string> destinationPaths = new();
            for (int index = 0; index < paths.Length; index++)
            {
                string sourcePath = paths[index];
                string relativePath = sourcePath.Substring(
                    DownloadRoot.Length);
                string destinationPath =
                    $"{SharedAssetRoot}/{relativePath}";
                EnsureFolder(Path.GetDirectoryName(destinationPath)
                    ?.Replace('\\', '/'));

                if (AssetDatabase.LoadMainAssetAtPath(destinationPath) == null)
                {
                    if (!AssetDatabase.CopyAsset(
                        sourcePath,
                        destinationPath))
                    {
                        throw new InvalidOperationException(
                            $"Failed to copy UI dependency from " +
                            $"'{sourcePath}' to '{destinationPath}'.");
                    }
                }

                AssetDatabase.ImportAsset(
                    destinationPath,
                    ImportAssetOptions.ForceSynchronousImport);
                destinationPaths.Add(destinationPath);
                map.GuidMap[AssetDatabase.AssetPathToGUID(sourcePath)] =
                    AssetDatabase.AssetPathToGUID(destinationPath);
            }

            for (int index = 0; index < paths.Length; index++)
            {
                AddAssetMappings(
                    paths[index],
                    destinationPaths[index],
                    map.ObjectMap);
            }

            RemapCopiedAssetDependencies(destinationPaths, map);
            map.ObjectMap.Clear();
            for (int index = 0; index < paths.Length; index++)
            {
                AddAssetMappings(
                    paths[index],
                    destinationPaths[index],
                    map.ObjectMap);
            }

            return map;
        }

        private static void RemapCopiedAssetDependencies(
            IEnumerable<string> destinationPaths,
            DependencyMap dependencyMap)
        {
            foreach (string destinationPath in destinationPaths)
            {
                if (!string.Equals(
                    Path.GetExtension(destinationPath),
                    ".asset",
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Object[] destinationAssets =
                    AssetDatabase.LoadAllAssetsAtPath(destinationPath);
                for (int index = 0;
                     index < destinationAssets.Length;
                     index++)
                {
                    RemapSerializedReferences(
                        destinationAssets[index],
                        dependencyMap);
                }
            }

            AssetDatabase.SaveAssets();
            foreach (string destinationPath in destinationPaths)
            {
                if (string.Equals(
                    Path.GetExtension(destinationPath),
                    ".asset",
                    StringComparison.OrdinalIgnoreCase))
                {
                    AssetDatabase.ImportAsset(
                        destinationPath,
                        ImportAssetOptions.ForceSynchronousImport |
                        ImportAssetOptions.ForceUpdate);
                }
            }
        }

        private static void AddAssetMappings(
            string sourcePath,
            string destinationPath,
            IDictionary<Object, Object> map)
        {
            Object[] sourceAssets =
                AssetDatabase.LoadAllAssetsAtPath(sourcePath);
            Object[] destinationAssets =
                AssetDatabase.LoadAllAssetsAtPath(destinationPath);

            for (int sourceIndex = 0;
                 sourceIndex < sourceAssets.Length;
                 sourceIndex++)
            {
                Object sourceAsset = sourceAssets[sourceIndex];
                Object destinationAsset = destinationAssets.FirstOrDefault(
                    candidate => candidate != null &&
                        candidate.GetType() == sourceAsset.GetType() &&
                        string.Equals(
                            candidate.name,
                            sourceAsset.name,
                            StringComparison.Ordinal));
                if (destinationAsset != null)
                {
                    map[sourceAsset] = destinationAsset;
                }
            }
        }

        private static void RemapDownloadedAssets(
            GameObject root,
            DependencyMap dependencyMap)
        {
            Component[] components = root.GetComponentsInChildren<Component>(
                true);
            for (int index = 0; index < components.Length; index++)
            {
                Component component = components[index];
                if (component == null)
                {
                    continue;
                }

                RemapSerializedReferences(component, dependencyMap);
            }
        }

        private static void RemapSerializedReferences(
            Object target,
            DependencyMap dependencyMap)
        {
            if (target == null)
            {
                return;
            }

            SerializedObject serialized = new(target);
            SerializedProperty property = serialized.GetIterator();
            bool changed = false;
            while (property.NextVisible(true))
            {
                if (property.propertyType ==
                    SerializedPropertyType.ObjectReference)
                {
                    Object sourceReference =
                        property.objectReferenceValue;
                    if (sourceReference != null &&
                        dependencyMap.ObjectMap.TryGetValue(
                            sourceReference,
                            out Object destinationReference))
                    {
                        property.objectReferenceValue =
                            destinationReference;
                        changed = true;
                    }
                }
                else if (property.propertyType ==
                    SerializedPropertyType.String &&
                    dependencyMap.GuidMap.TryGetValue(
                        property.stringValue,
                        out string destinationGuid))
                {
                    property.stringValue = destinationGuid;
                    changed = true;
                }
            }

            if (changed)
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(target);
            }
        }

        private static void ClearExternalSceneReferences(GameObject root)
        {
            Component[] components = root.GetComponentsInChildren<Component>(
                true);
            for (int index = 0; index < components.Length; index++)
            {
                Component component = components[index];
                if (component == null)
                {
                    throw new InvalidOperationException(
                        $"Missing Script exists below '{root.name}'.");
                }

                SerializedObject serialized = new(component);
                SerializedProperty property = serialized.GetIterator();
                bool enterChildren = true;
                bool changed = false;
                while (property.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (property.propertyType !=
                        SerializedPropertyType.ObjectReference)
                    {
                        continue;
                    }

                    Object reference = property.objectReferenceValue;
                    if (!IsExternalSceneReference(root, reference))
                    {
                        continue;
                    }

                    property.objectReferenceValue = null;
                    changed = true;
                }

                if (changed)
                {
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        private static void RestoreRectTransforms(
            Transform source,
            Transform destination)
        {
            if (source is RectTransform sourceRect &&
                destination is RectTransform destinationRect)
            {
                destinationRect.anchorMin = sourceRect.anchorMin;
                destinationRect.anchorMax = sourceRect.anchorMax;
                destinationRect.pivot = sourceRect.pivot;
                destinationRect.anchoredPosition =
                    sourceRect.anchoredPosition;
                destinationRect.sizeDelta = sourceRect.sizeDelta;
                destinationRect.localPosition = sourceRect.localPosition;
                destinationRect.localRotation = sourceRect.localRotation;
                destinationRect.localScale = sourceRect.localScale;
            }

            if (source.childCount != destination.childCount)
            {
                throw new InvalidOperationException(
                    $"Hierarchy changed while cloning '{source.name}'.");
            }

            for (int index = 0; index < source.childCount; index++)
            {
                RestoreRectTransforms(
                    source.GetChild(index),
                    destination.GetChild(index));
            }
        }

        private static void RestoreSavedPrefabLayout(
            Transform source,
            string prefabPath)
        {
            GameObject prefabAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
            {
                throw new InvalidOperationException(
                    $"Failed to load prefab layout: '{prefabPath}'.");
            }

            RestoreSerializedRectTransforms(
                source,
                prefabAsset.transform);
            PrefabUtility.SavePrefabAsset(prefabAsset);
        }

        private static void RestoreSerializedRectTransforms(
            Transform source,
            Transform destination)
        {
            if (source is RectTransform sourceRect &&
                destination is RectTransform destinationRect)
            {
                SerializedObject serialized = new(destinationRect);
                serialized.FindProperty("m_AnchorMin").vector2Value =
                    sourceRect.anchorMin;
                serialized.FindProperty("m_AnchorMax").vector2Value =
                    sourceRect.anchorMax;
                serialized.FindProperty("m_AnchoredPosition").vector2Value =
                    sourceRect.anchoredPosition;
                serialized.FindProperty("m_SizeDelta").vector2Value =
                    sourceRect.sizeDelta;
                serialized.FindProperty("m_Pivot").vector2Value =
                    sourceRect.pivot;
                serialized.FindProperty("m_LocalPosition").vector3Value =
                    sourceRect.localPosition;
                serialized.FindProperty("m_LocalRotation").quaternionValue =
                    sourceRect.localRotation;
                serialized.FindProperty("m_LocalScale").vector3Value =
                    sourceRect.localScale;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(destinationRect);
            }

            if (source.childCount != destination.childCount)
            {
                throw new InvalidOperationException(
                    $"Saved prefab hierarchy changed below " +
                    $"'{source.name}'.");
            }

            for (int index = 0; index < source.childCount; index++)
            {
                RestoreSerializedRectTransforms(
                    source.GetChild(index),
                    destination.GetChild(index));
            }
        }

        private static bool IsExternalSceneReference(
            GameObject root,
            Object reference)
        {
            Transform target = reference switch
            {
                GameObject gameObject => gameObject.transform,
                Component component => component.transform,
                _ => null
            };
            if (target == null || !target.gameObject.scene.IsValid())
            {
                return false;
            }

            return target != root.transform &&
                !target.IsChildOf(root.transform);
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                if (string.Equals(
                    roots[index].name,
                    name,
                    StringComparison.Ordinal))
                {
                    return roots[index];
                }
            }

            return null;
        }

        private static Transform FindByPath(
            Scene scene,
            string hierarchyPath)
        {
            string[] segments = hierarchyPath.Split('/');
            GameObject root = FindRoot(scene, segments[0]);
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

        private static void EnsureFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) ||
                AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folderPath)
                ?.Replace('\\', '/');
            string name = Path.GetFileName(folderPath);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
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

        private sealed class DependencyMap
        {
            public Dictionary<Object, Object> ObjectMap { get; } = new();
            public Dictionary<string, string> GuidMap { get; } =
                new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
