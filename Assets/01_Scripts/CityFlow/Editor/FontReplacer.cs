using UnityEditor;
using UnityEngine;
using TMPro;

namespace CityFlow.Editor
{
    public static class FontReplacer
    {
        [MenuItem("Tools/Fix Fonts (Replace with NanumGothic)")]
        public static void ReplaceFonts()
        {
            TMP_FontAsset newFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/03_Art/Fonts/NanumGothic SDF.asset");
            if (newFont == null)
            {
                Debug.LogError("NanumGothic SDF.asset not found!");
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
