using UnityEditor;
using UnityEngine;
using TMPro;
using CityFlow.EditorTools;

namespace CityFlow.Editor
{
    public static class FontReplacer
    {
        [MenuItem("Tools/Fix Fonts (Replace with NanumGothic)")]
        public static void ReplaceFonts()
        {
            TMP_FontAsset newFont = ExternalKoreanFontAsset.LoadConfigured();
            if (newFont == null)
            {
                Debug.LogError(
                    $"NanumGothic SDF.asset was not found or invalid at " +
                    $"'{ExternalKoreanFontAsset.FontAssetPath}'. " +
                    "Install the external font assets before replacing fonts.");
                return;
            }

            TextMeshProUGUI[] texts = Object.FindObjectsOfType<TextMeshProUGUI>(true);
            int count = 0;
            foreach (var t in texts)
            {
                if (t.font != newFont)
                {
                    Undo.RecordObject(t, "Replace Font");
                    t.font = newFont;
                    EditorUtility.SetDirty(t);
                    count++;
                }
            }
            Debug.Log($"[Font Fix] Successfully replaced font on {count} text components in the current scene.");
        }
    }
}
