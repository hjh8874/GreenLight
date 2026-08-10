using CityFlow.Bootstrap;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityFlow.UI
{
    public static class HiringStatusOverlayInstaller
    {
        private const string ResourcePath =
            "CityFlow/UI/UI_HiringStatusSystem";

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneCallback()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Object.FindFirstObjectByType<CityBootstrap>(
                    FindObjectsInactive.Include) == null ||
                Object.FindFirstObjectByType<HiringStatusOverlay>(
                    FindObjectsInactive.Include) != null)
            {
                return;
            }

            GameObject prefab = Resources.Load<GameObject>(ResourcePath);
            if (prefab == null)
            {
                Debug.LogError(
                    $"[HiringStatusOverlayInstaller] Missing Resources " +
                    $"prefab: {ResourcePath}");
                return;
            }

            GameObject instance = Object.Instantiate(prefab);
            instance.name = "HiringStatusSystem";
            SceneManager.MoveGameObjectToScene(instance, scene);
        }
    }
}
