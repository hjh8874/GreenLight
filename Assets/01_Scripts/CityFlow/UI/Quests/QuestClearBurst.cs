using CityFlow.Bootstrap;
using CityFlow.Gameplay.Quests;
using CityFlow.Managers;
using CityFlow.Sim.Quests;
using DG.Tweening;
using UnityEngine;

namespace CityFlow.UI.Quests
{
    // 퀘스트 클리어 축하 연출. 팝업이 사라지는 그 자리에서 컨페티가 터지고
    // 카메라가 살짝 흔들린다.
    //
    // FlowBurstJuice 와 같은 구조다 — 이벤트만 듣는 독립 유닛이라
    // 팝업(QuestBubbleUI) 교체·재생성에 흔들리지 않는다.
    //
    // ⚠️ CityQuestSystem.ViewStateChanged 가 아니라 QuestCompleted 를 듣는다.
    // ViewStateChanged 는 "지금 보여줄 퀘스트"가 바뀔 때마다 울려서
    // 우선순위 끼어들기·세이브 복원에서도 발생한다 — 거기 걸면 엉뚱할 때 터진다.
    public sealed class QuestClearBurst : MonoBehaviour, ICityFlowServiceConsumer
    {
        private const string ConfettiResourcePath =
            "CityFlow/FX_QuestClearConfetti";
        private const string ClearSfxId = "quest_clear";
        private const float ShakeDuration = 0.22f;
        // 멀미 방지 — FlowBurstJuice.MaxShakeStrength(0.4)보다 약하게 잡는다.
        // 퀘스트 클리어는 자주 일어나므로 더 조심해야 한다.
        private const float ShakeStrength = 0.18f;
        private const float ConfettiLifetimeSeconds = 3f;

        [SerializeField] private RectTransform burstAnchor;

        private CityQuestSystemBridge bridge;
        private GameObject confettiPrefab;
        private Tween shakeTween;

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            confettiPrefab = Resources.Load<GameObject>(ConfettiResourcePath);
            bridge = new CityQuestSystemBridge(this);
            bridge.Bind();
        }

        private void OnDestroy()
        {
            bridge?.Unbind();
            shakeTween?.Kill(complete: true);
        }

        internal void PlayBurst()
        {
            SpawnConfetti();
            ShakeCamera();
            // 카탈로그에 클립이 없으면 SoundManager 가 조용히 no-op 한다(에셋 없어도 무사고).
            SoundManager.Instance?.PlaySfx(ClearSfxId, 0.7f);
        }

        private void SpawnConfetti()
        {
            if (confettiPrefab == null)
            {
                return;
            }

            // 팝업이 사라지는 자리에서 터진다. 앵커가 없으면 화면 중앙.
            Transform parent = burstAnchor != null
                ? burstAnchor
                : transform;
            GameObject instance = Instantiate(
                confettiPrefab,
                parent.position,
                Quaternion.identity,
                parent);
            instance.name = "QuestClearConfetti";
            Destroy(instance, ConfettiLifetimeSeconds);
        }

        private void ShakeCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            // complete:true — 진행 중 셰이크를 원점 복귀시키고 죽인다.
            // cam.transform.DOKill 은 카메라 이동·전환 트윈까지 끄므로 쓰지 않는다.
            shakeTween?.Kill(complete: true);
            shakeTween = cam.transform
                .DOShakePosition(ShakeDuration, ShakeStrength)
                .SetUpdate(true);
        }

        // 씬에 CityQuestSystem 이 언제 생기는지 보장되지 않아 지연 바인딩한다.
        private sealed class CityQuestSystemBridge
        {
            private readonly QuestClearBurst owner;
            private CityQuestSystem system;

            internal CityQuestSystemBridge(QuestClearBurst owner)
            {
                this.owner = owner;
            }

            internal void Bind()
            {
                if (system != null)
                {
                    return;
                }

                system = Object.FindFirstObjectByType<CityQuestSystem>();
                if (system == null)
                {
                    return;
                }

                system.QuestCompleted += OnQuestCompleted;
            }

            internal void Unbind()
            {
                if (system == null)
                {
                    return;
                }

                system.QuestCompleted -= OnQuestCompleted;
                system = null;
            }

            private void OnQuestCompleted(CityQuestId id)
            {
                owner.PlayBurst();
            }
        }
    }
}
