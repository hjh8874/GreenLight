using UnityEditor;
using UnityEditor.SceneManagement;

namespace CityFlow.EditorTools
{
    [InitializeOnLoad]
    public static class PlayModeStartSceneInitializer
    {
        private const string TitleScenePath = "Assets/00_Scenes/TitleScene.unity";

        static PlayModeStartSceneInitializer()
        {
            EditorApplication.delayCall += ConfigureStartScene;
        }

        private static void ConfigureStartScene()
        {
            SceneAsset titleScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(TitleScenePath);
            if (titleScene != null && EditorSceneManager.playModeStartScene != titleScene)
            {
                EditorSceneManager.playModeStartScene = titleScene;
            }
        }
    }
}
