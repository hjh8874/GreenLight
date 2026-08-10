using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Sim.Quests;
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
        private QuestClearBurst clearBurst;
        private bool burstSubscribed;
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

            if (questUI != null)
            {
                BindQuestUI();
            }
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

            RectTransform topBar = FindQuestTopBar(canvas);
            Transform questParent = topBar != null && topBar.parent != null
                ? topBar.parent
                : canvas.transform;
            questUI = QuestBubbleUI.Create(questParent, topBar);
            BindQuestUI();
            AttachClearBurst();
        }

        // 클리어 축하 연출을 여기서 만든다. 팝업이 런타임 생성이라
        // 프리팹이나 씬 배선으로는 붙일 수 없고, 통합 담당자에게 컴포넌트 추가를
        // 요구하는 것도 제공 기준상 미완성이다(리뷰 #251).
        // 이 설치자는 [RuntimeInitializeOnLoadMethod] 로 돌므로 씬 작업이 0이다.
        //
        // 연출 컴포넌트는 팝업이 아니라 호스트에 붙인다 — 팝업은 숨겨질 때
        // SetActive(false) 가 되므로 거기 붙이면 정작 터질 때 비활성이다.
        private void AttachClearBurst()
        {
            if (clearBurst == null)
            {
                clearBurst = gameObject.AddComponent<QuestClearBurst>();
            }

            clearBurst.SetAnchor(questUI != null
                ? questUI.transform as RectTransform
                : null);

            if (burstSubscribed || questSystem == null)
            {
                return;
            }

            burstSubscribed = true;
            questSystem.QuestCompleted += OnQuestCompletedForBurst;
        }

        // 연출은 어느 퀘스트가 끝났는지 몰라도 된다 — id 를 흘리지 않는다.
        private void OnQuestCompletedForBurst(CityQuestId _)
        {
            clearBurst?.PlayBurst();
        }

        private void OnDestroy()
        {
            if (burstSubscribed && questSystem != null)
            {
                questSystem.QuestCompleted -= OnQuestCompletedForBurst;
                burstSubscribed = false;
            }
        }

        private void BindQuestUI()
        {
            questUI.Bind(
                questSystem,
                boundBootstrap.Services);
            FindAnyObjectByType<EmergencyIncidentSystem>()
                ?.RepublishActiveAlerts();
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

        private static RectTransform FindQuestTopBar(Canvas canvas)
        {
            RectTransform[] rects =
                canvas.GetComponentsInChildren<RectTransform>(true);
            foreach (RectTransform rect in rects)
            {
                if (rect.name == "HUD_TopBar")
                {
                    return rect;
                }
            }

            return null;
        }
    }
}
