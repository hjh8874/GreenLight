using CityFlow.Managers;
using DG.Tweening;
using UnityEngine;

namespace CityFlow.UI.Quests
{
    // 퀘스트 클리어 축하 연출. 팝업이 사라지는 자리에서 컨페티가 터지고
    // 카메라가 살짝 흔들린다.
    //
    // 이 컴포넌트는 "언제 터질지"를 모른다. 순수 연출 유닛이라
    // 퀘스트 시스템 타입을 참조하지 않는다 — 호출은 QuestRuntimeHost 가 한다.
    // (리뷰 #251: UI 가 구현 타입을 직접 탐색하면 Contracts 경계를 우회한다.)
    public sealed class QuestClearBurst : MonoBehaviour
    {
        private const string ConfettiResourcePath =
            "CityFlow/FX_QuestClearConfetti";
        private const string ClearSfxId = "quest_clear";
        private const float ShakeDuration = 0.22f;
        // 멀미 방지 — FlowBurstJuice.MaxShakeStrength(0.4)보다 약하게 잡는다.
        // 퀘스트 클리어는 자주 일어나므로 더 조심해야 한다.
        private const float ShakeStrength = 0.18f;
        private const float ConfettiLifetimeSeconds = 3f;
        // 파티클을 카메라 앞 몇 유닛에 놓을지. 2D 직교라 값 자체는 중요하지 않고
        // 근/원평면 사이이기만 하면 된다.
        private const float ConfettiCameraDistance = 10f;

        private GameObject confettiPrefab;
        private RectTransform anchor;
        private Canvas anchorCanvas;
        private Tween shakeTween;

        private void Awake()
        {
            confettiPrefab = Resources.Load<GameObject>(ConfettiResourcePath);
            if (confettiPrefab == null)
            {
                // 조용히 실패하면 "흔들림만 나고 컨페티가 없다"는 증상만 남아
                // 원인을 찾기 어렵다(리뷰 #251 P1).
                Debug.LogWarning(
                    $"[QuestClearBurst] 컨페티 프리팹을 찾지 못했다: Resources/{ConfettiResourcePath}. " +
                    "카메라 흔들림과 효과음만 재생된다.");
            }
        }

        // 터질 자리를 알려준다. 퀘스트 팝업은 런타임에 생성되므로
        // 인스펙터로는 꽂을 수 없다 — 생성한 쪽이 넘겨준다.
        internal void SetAnchor(RectTransform value)
        {
            anchor = value;
            anchorCanvas = value != null
                ? value.GetComponentInParent<Canvas>()
                : null;
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

            GameObject instance = Instantiate(
                confettiPrefab,
                ResolveBurstWorldPosition(),
                Quaternion.identity);
            instance.name = "QuestClearConfetti";
            Destroy(instance, ConfettiLifetimeSeconds);
        }

        // 파티클은 월드 공간에 둔다. UI 캔버스의 자식으로 넣으면
        // Screen Space - Overlay 에서는 렌더되지 않거나 스케일이 어긋난다
        // (리뷰 #251 자동검증 지적). 앵커의 화면 좌표를 월드로 되돌려 쓴다.
        private Vector3 ResolveBurstWorldPosition()
        {
            Camera cam = Camera.main;
            if (cam == null || anchor == null)
            {
                return transform.position;
            }

            Camera uiCamera =
                anchorCanvas != null &&
                anchorCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? anchorCanvas.worldCamera
                    : null;
            Vector3 screenPoint = RectTransformUtility.WorldToScreenPoint(
                uiCamera,
                anchor.position);
            return cam.ScreenToWorldPoint(new Vector3(
                screenPoint.x,
                screenPoint.y,
                ConfettiCameraDistance));
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

        private void OnDestroy()
        {
            shakeTween?.Kill(complete: true);
        }
    }
}
