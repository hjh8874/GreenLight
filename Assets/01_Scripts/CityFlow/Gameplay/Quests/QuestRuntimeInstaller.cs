using CityFlow.Bootstrap;
using CityFlow.UI.Quests;
using UnityEngine;

namespace CityFlow.Gameplay.Quests
{
    public static class QuestRuntimeInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            QuestRuntimeHost existing = Object.FindAnyObjectByType<QuestRuntimeHost>();

            if (existing != null)
            {
                return;
            }

            GameObject hostObject = new GameObject("QuestRuntime");
            Object.DontDestroyOnLoad(hostObject);
            hostObject.AddComponent<QuestRuntimeHost>();
        }
    }

    public sealed class QuestRuntimeHost : MonoBehaviour
    {
        private const float SearchInterval = 0.5f;

        private CityQuestSystem questSystem;
        private CityBootstrap boundBootstrap;
        private QuestBubbleUI questUI;
        private float searchElapsed;

        private void Awake()
        {
            questSystem = gameObject.AddComponent<CityQuestSystem>();
            searchElapsed = SearchInterval;
        }

        private void Update()
        {
            searchElapsed += Time.unscaledDeltaTime;

            if (searchElapsed < SearchInterval)
            {
                return;
            }

            searchElapsed = 0f;
            TryBindServices();
            TryCreateUI();
        }

        private void TryBindServices()
        {
            CityBootstrap bootstrap = FindAnyObjectByType<CityBootstrap>();

            if (bootstrap == null || bootstrap.Services == null || ReferenceEquals(boundBootstrap, bootstrap))
            {
                return;
            }

            boundBootstrap = bootstrap;
            questSystem.Initialize(bootstrap.Services);
        }

        private void TryCreateUI()
        {
            if (boundBootstrap == null || questUI != null)
            {
                return;
            }

            Canvas canvas = FindTargetCanvas();

            if (canvas == null)
            {
                return;
            }

            questUI = QuestBubbleUI.Create(canvas.transform);
            questUI.Bind(questSystem);
        }

        private static Canvas FindTargetCanvas()
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include);

            foreach (Canvas canvas in canvases)
            {
                if (canvas.name == "UI_MainCanvas")
                {
                    return canvas;
                }
            }

            foreach (Canvas canvas in canvases)
            {
                if (canvas.isRootCanvas)
                {
                    return canvas;
                }
            }

            return null;
        }
    }
}
