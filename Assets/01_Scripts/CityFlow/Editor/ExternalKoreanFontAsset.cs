using System;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CityFlow.EditorTools
{
    public static class ExternalKoreanFontAsset
    {
        public const string FontAssetPath =
            "Assets/99_Download/Fonts/NanumGothic SDF.asset";

        private const string SourceFontPath =
            "Assets/99_Download/Fonts/NanumGothic-Regular.ttf";

        private const string BuildSlotPrefabPath =
            "Assets/02_Prefabs/UI_BuildSlot.prefab";

        private const string ValidationCharacters =
            "한글 폰트 검증 저장 불러오기 삭제 시민 요청 새로운 일자리가 필요해요";

        [InitializeOnLoadMethod]
        private static void ScheduleConfiguration()
        {
            EditorApplication.delayCall += ConfigureAfterDomainReload;
        }

        public static TMP_FontAsset LoadConfigured()
        {
            TMP_FontAsset fontAsset =
                AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (fontAsset == null)
            {
                Debug.LogError(
                    $"Required external font asset is missing: '{FontAssetPath}'.");
                return null;
            }

            Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (sourceFont == null)
            {
                Debug.LogError(
                    $"Source font is missing: '{SourceFontPath}'.");
                return null;
            }

            EnsureSourceFontReference(fontAsset, sourceFont);
            return fontAsset;
        }

        [MenuItem("Tools/GreenLight/UI/Validate External Korean Font")]
        public static void ValidateKoreanGlyphs()
        {
            TMP_FontAsset fontAsset = LoadConfigured();
            if (fontAsset == null)
            {
                throw new InvalidOperationException(
                    "The external Korean font could not be configured.");
            }

            if (!fontAsset.TryAddCharacters(
                    ValidationCharacters,
                    out string missingCharacters))
            {
                throw new InvalidOperationException(
                    $"The external Korean font could not create these glyphs: " +
                    $"'{missingCharacters}'.");
            }

            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssetIfDirty(fontAsset);
            ValidateTextMesh(fontAsset);
            ValidateBuildSlotPrefab(fontAsset);
            Debug.Log(
                $"[ExternalKoreanFontAsset] Korean glyph validation passed: " +
                $"'{ValidationCharacters}'.");
        }

        private static void ConfigureAfterDomainReload()
        {
            EditorApplication.delayCall -= ConfigureAfterDomainReload;
            LoadConfigured();
        }

        private static void EnsureSourceFontReference(
            TMP_FontAsset fontAsset,
            Font sourceFont)
        {
            string sourceGuid = AssetDatabase.AssetPathToGUID(SourceFontPath);
            SerializedObject serializedFont = new SerializedObject(fontAsset);
            SerializedProperty sourceFontProperty =
                serializedFont.FindProperty("m_SourceFontFile");
            SerializedProperty sourceGuidProperty =
                serializedFont.FindProperty("m_SourceFontFileGUID");
            SerializedProperty creationSourceGuidProperty =
                serializedFont.FindProperty(
                    "m_CreationSettings.sourceFontFileGUID");
            SerializedProperty populationModeProperty =
                serializedFont.FindProperty("m_AtlasPopulationMode");

            bool requiresUpdate =
                sourceFontProperty.objectReferenceValue != sourceFont
                || sourceGuidProperty.stringValue != sourceGuid
                || creationSourceGuidProperty.stringValue != sourceGuid
                || populationModeProperty.intValue
                    != (int)AtlasPopulationMode.Dynamic;

            if (!requiresUpdate)
            {
                return;
            }

            sourceFontProperty.objectReferenceValue = sourceFont;
            sourceGuidProperty.stringValue = sourceGuid;
            creationSourceGuidProperty.stringValue = sourceGuid;
            populationModeProperty.intValue =
                (int)AtlasPopulationMode.Dynamic;

            serializedFont.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssetIfDirty(fontAsset);

            Debug.Log(
                $"[ExternalKoreanFontAsset] Reconnected '{FontAssetPath}' " +
                $"to source font '{SourceFontPath}'.");
        }

        private static void ValidateTextMesh(TMP_FontAsset fontAsset)
        {
            GameObject canvasObject = new GameObject(
                "ExternalKoreanFontValidationCanvas",
                typeof(Canvas));

            try
            {
                GameObject textObject = new GameObject(
                    "ExternalKoreanFontValidationText",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(TextMeshProUGUI));
                textObject.transform.SetParent(
                    canvasObject.transform,
                    false);

                TextMeshProUGUI text =
                    textObject.GetComponent<TextMeshProUGUI>();
                text.font = fontAsset;
                text.text = ValidationCharacters;
                text.ForceMeshUpdate(
                    ignoreActiveState: true,
                    forceTextReparsing: true);

                if (text.textInfo.characterCount == 0
                    || text.textInfo.meshInfo.Length == 0
                    || text.textInfo.meshInfo[0].mesh.vertexCount == 0)
                {
                    throw new InvalidOperationException(
                        "The external Korean font did not create a text mesh.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }

        private static void ValidateBuildSlotPrefab(
            TMP_FontAsset fontAsset)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    BuildSlotPrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Required prefab is missing: '{BuildSlotPrefabPath}'.");
            }

            TextMeshProUGUI[] texts =
                prefab.GetComponentsInChildren<TextMeshProUGUI>(true);
            if (texts.Length == 0)
            {
                throw new InvalidOperationException(
                    $"No TextMeshProUGUI component was found in " +
                    $"'{BuildSlotPrefabPath}'.");
            }

            foreach (TextMeshProUGUI text in texts)
            {
                if (text.font != fontAsset
                    || text.fontSharedMaterial != fontAsset.material)
                {
                    throw new InvalidOperationException(
                        $"'{text.name}' in '{BuildSlotPrefabPath}' does not " +
                        "reference the standard external Korean font.");
                }
            }
        }
    }

    internal sealed class ExternalKoreanFontBuildValidator
        : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1;

        public void OnPreprocessBuild(BuildReport report)
        {
            try
            {
                ExternalKoreanFontAsset.ValidateKoreanGlyphs();
            }
            catch (Exception exception)
            {
                throw new BuildFailedException(
                    $"External Korean font validation failed: " +
                    $"{exception.Message}");
            }
        }
    }
}
