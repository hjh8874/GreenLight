using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityFlow.EditorTools
{
    // Tools/GreenLight/교통 디버그 씬 생성:
    // 현재 열린 (메인)씬을 <name>_Debug 로 복사하고 TrafficDebugRig 프리팹을 주입해 연다.
    //   리그 = 6종배치(SandboxPlacementControls) + 신호튜닝(DebugSignalTuner)
    //        + 세이브격리(DebugDisableSaving) + HUD·골드·배속·초기화(DebugCityControls)
    // 항상 최신 메인에서 재생성하므로 디버그 씬이 낡지 않는다. 라이브 세이브(save_v1.json)는
    // 리그의 DebugDisableSaving가 IsSavingEnabled=false 로 완전 격리(오프라인 정산·자동저장 무력화).
    public static class TrafficDebugSceneMenu
    {
        private const string RigPrefabPath = "Assets/02_Prefabs/TrafficDebugRig.prefab";
        private const string DebugSuffix = "_Debug";

        [MenuItem("Tools/GreenLight/교통 디버그 씬 생성")]
        public static void CreateTrafficDebugScene()
        {
            Scene active = EditorSceneManager.GetActiveScene();

            if (string.IsNullOrEmpty(active.path))
            {
                EditorUtility.DisplayDialog("교통 디버그",
                    "현재 씬이 저장돼 있지 않습니다.\n디버그할 메인 씬을 먼저 열고 저장하세요.", "확인");
                return;
            }

            string srcName = System.IO.Path.GetFileNameWithoutExtension(active.path);
            if (srcName.EndsWith(DebugSuffix))
            {
                EditorUtility.DisplayDialog("교통 디버그",
                    "이미 디버그 씬입니다.\n원본 메인 씬을 열고 실행하세요.", "확인");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            if (rig == null)
            {
                EditorUtility.DisplayDialog("교통 디버그",
                    $"리그 프리팹을 찾을 수 없습니다:\n{RigPrefabPath}", "확인");
                return;
            }

            string srcPath = active.path;
            string dir = System.IO.Path.GetDirectoryName(srcPath).Replace("\\", "/");
            string dstPath = $"{dir}/{srcName}{DebugSuffix}.unity";

            // 최신 메인에서 재생성: 기존 디버그 씬이 있으면 덮어쓴다.
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(dstPath) != null)
            {
                AssetDatabase.DeleteAsset(dstPath);
            }

            if (!AssetDatabase.CopyAsset(srcPath, dstPath))
            {
                EditorUtility.DisplayDialog("교통 디버그", $"씬 복사 실패:\n{dstPath}", "확인");
                return;
            }

            AssetDatabase.Refresh();
            Scene dbg = EditorSceneManager.OpenScene(dstPath, OpenSceneMode.Single);

            // 리그 주입(중복 방지)
            if (GameObject.Find("TrafficDebugRig") == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(rig, dbg);
                instance.name = "TrafficDebugRig";
                Undo.RegisterCreatedObjectUndo(instance, "Add TrafficDebugRig");
            }

            EditorSceneManager.MarkSceneDirty(dbg);
            EditorSceneManager.SaveScene(dbg);

            // 강제-타이틀 이니셜라이저는 제거됨(develop) — Play가 이 씬을 직접 실행하도록 보장.
            EditorSceneManager.playModeStartScene = null;

            Debug.Log($"[TrafficDebug] 디버그 씬 생성 완료: {dstPath}\n" +
                      "리그 주입 + 세이브 격리. Play 버튼으로 바로 실행하세요. " +
                      "(1~5=장치배치, 0=철거, ,/. [/] G/V=신호, 우측 버튼=골드/배속/초기화)");
        }
    }
}
