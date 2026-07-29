using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using System.Collections.Generic;

namespace CityFlow.UI.Editor
{
    public class GlobalUIReplacerWindow : EditorWindow
    {
        // ── 교체할 새 에셋 슬롯 ──
        private Sprite newPanelSprite;      // Unity "Background" 대체용
        private Sprite newButtonSprite;     // Unity "UISprite" 대체용
        private Sprite newKnobSprite;       // Unity "Knob" 대체용
        private Sprite newCheckmarkSprite;  // Unity "Checkmark" 대체용

        private GameObject targetRoot;      // 씬 내부의 부모 오브젝트 (비워두면 씬 전체)
        private DefaultAsset targetFolder;  // 특정 프리팹 폴더만 교체하고 싶을 때

        [MenuItem("CityFlow/UI/Global Asset Replacer", false, 50)]
        public static void ShowWindow()
        {
            var window = GetWindow<GlobalUIReplacerWindow>("UI Asset Replacer");
            window.minSize = new Vector2(400, 300);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("🎨 Global UI Asset Replacer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("유니티 기본 내장 스프라이트(Background, UISprite, Knob 등)를 싹 찾아내어 지정한 새 에셋으로 1초 만에 일괄 교체해 줍니다. (Ctrl+Z 지원)", MessageType.Info);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("1. 교체할 새 에셋 등록 (비워두면 무시)", EditorStyles.boldLabel);
            newPanelSprite = (Sprite)EditorGUILayout.ObjectField("New Panel (ex. Background)", newPanelSprite, typeof(Sprite), false);
            newButtonSprite = (Sprite)EditorGUILayout.ObjectField("New Button (ex. UISprite)", newButtonSprite, typeof(Sprite), false);
            newKnobSprite = (Sprite)EditorGUILayout.ObjectField("New Knob (ex. Slider Handle)", newKnobSprite, typeof(Sprite), false);
            newCheckmarkSprite = (Sprite)EditorGUILayout.ObjectField("New Checkmark (ex. Toggle)", newCheckmarkSprite, typeof(Sprite), false);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("2. 변환 대상 설정", EditorStyles.boldLabel);
            targetRoot = (GameObject)EditorGUILayout.ObjectField("Target Root (Scene)", targetRoot, typeof(GameObject), true);
            targetFolder = (DefaultAsset)EditorGUILayout.ObjectField("Target Folder (Prefabs)", targetFolder, typeof(DefaultAsset), false);

            EditorGUILayout.Space();
            if (GUILayout.Button("🚀 Replace All in Target Root (Scene)", GUILayout.Height(30)))
            {
                ReplaceInScene();
            }

            if (GUILayout.Button("📦 Replace All in Target Folder (Prefabs)", GUILayout.Height(30)))
            {
                ReplaceInPrefabs();
            }
        }

        private void ReplaceInScene()
        {
            Image[] images;
            if (targetRoot != null)
            {
                images = targetRoot.GetComponentsInChildren<Image>(true);
            }
            else
            {
                images = FindObjectsOfType<Image>(true);
            }

            int count = PerformReplacement(images);
            Debug.Log($"[UI Asset Replacer] 씬 내부에서 총 {count}개의 UI 에셋을 교체했습니다.");
            EditorUtility.DisplayDialog("완료", $"씬 내부에서 총 {count}개의 유니티 기본 에셋을 교체했습니다!\n(Ctrl+Z로 취소 가능)", "확인");
        }

        private void ReplaceInPrefabs()
        {
            if (targetFolder == null)
            {
                EditorUtility.DisplayDialog("오류", "타겟 폴더를 지정해 주세요.", "확인");
                return;
            }

            string folderPath = AssetDatabase.GetAssetPath(targetFolder);
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });

            int count = 0;
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    EditorUtility.DisplayProgressBar("프리팹 스캔 중", path, (float)i / guids.Length);

                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) continue;

                    Image[] images = prefab.GetComponentsInChildren<Image>(true);
                    int changedInPrefab = PerformReplacement(images);

                    if (changedInPrefab > 0)
                    {
                        EditorUtility.SetDirty(prefab);
                        count += changedInPrefab;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
            }

            Debug.Log($"[UI Asset Replacer] 프리팹 폴더에서 총 {count}개의 UI 에셋을 교체했습니다.");
            EditorUtility.DisplayDialog("완료", $"해당 폴더의 프리팹들에서 총 {count}개의 유니티 기본 에셋을 교체했습니다!", "확인");
        }

        private int PerformReplacement(IEnumerable<Image> images)
        {
            int changedCount = 0;

            foreach (var img in images)
            {
                if (img.sprite == null) continue;

                string spriteName = img.sprite.name;
                bool isBuiltIn = IsBuiltInSprite(img.sprite);

                if (!isBuiltIn) continue;

                Sprite newSprite = null;

                // 유니티 기본 스프라이트 판별 후 매칭
                if (spriteName.Equals("Background") && newPanelSprite != null)
                {
                    newSprite = newPanelSprite;
                }
                else if (spriteName.Equals("UISprite") && newButtonSprite != null)
                {
                    newSprite = newButtonSprite;
                }
                else if (spriteName.Equals("Knob") && newKnobSprite != null)
                {
                    newSprite = newKnobSprite;
                }
                else if (spriteName.Equals("Checkmark") && newCheckmarkSprite != null)
                {
                    newSprite = newCheckmarkSprite;
                }

                if (newSprite != null)
                {
                    Undo.RecordObject(img, "Replace UI Asset");
                    img.sprite = newSprite;
                    
                    // 만약 새로운 스프라이트가 Sliced 속성이 없다면, Type을 Simple로 바꿔주는 편의성 로직 (옵션)
                    if (newSprite.border == Vector4.zero && img.type == Image.Type.Sliced)
                    {
                        img.type = Image.Type.Simple;
                    }

                    changedCount++;
                }
            }

            return changedCount;
        }

        private bool IsBuiltInSprite(Sprite sprite)
        {
            string path = AssetDatabase.GetAssetPath(sprite);
            // 유니티 기본 에셋은 Resources/unity_builtin_extra 내부에 존재 (Path가 "Resources/unity_builtin_extra" 거나 비어있거나 "Library/..." 임)
            if (string.IsNullOrEmpty(path)) return true;
            if (path.Equals("Resources/unity_builtin_extra")) return true;
            if (path.Equals("Library/unity default resources")) return true;
            
            // 일반 프로젝트 내 에셋이면 "Assets/..." 로 시작함
            return !path.StartsWith("Assets/");
        }
    }
}
