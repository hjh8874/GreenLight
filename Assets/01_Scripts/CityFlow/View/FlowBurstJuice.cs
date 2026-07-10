using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Managers;
using DG.Tweening;
using UnityEngine;

namespace CityFlow.View
{
    // FlowBurst(체증 해소 보상) 청각·카메라 연출 전용. 엔진 이벤트만 듣는 독립 유닛 —
    // 버스트 비주얼(FlowBurstView·MainCityView)과 무관, 뷰 교체·중복에 안 흔들림.
    public sealed class FlowBurstJuice : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private string burstSfxId = "flow_burst";
        [SerializeField] private float shakeDuration = 0.2f;

        public const float MaxShakeStrength = 0.4f;   // 멀미 방지 상한(월드 유닛)

        private CityFlowServices services;

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled) return;
            this.services = services;
            services.Events.FlowBurst += OnFlowBurst;
        }

        private void OnDestroy()
        {
            if (services != null) services.Events.FlowBurst -= OnFlowBurst;
        }

        private void OnFlowBurst(FlowBurstEvent e)
        {
            // 사운드: 카탈로그에 클립 없으면 SoundManager가 조용히 no-op(에셋 없어도 무사고).
            SoundManager.Instance?.PlaySfx(burstSfxId, VolumeFor(e.Reward));

            // 카메라 펀치: 2D 직교라 xy만. SetUpdate(true) = 일시정지 무관.
            Camera cam = Camera.main;
            if (cam != null)
            {
                // complete:true 필수 — 진행 중 셰이크를 원점 복귀시키고 죽인다. 기본 DOKill은
                // 흔들린 중간 위치에 카메라를 남겨 다음 셰이크가 거기를 기준점 삼음 → 누적 드리프트.
                cam.transform.DOKill(complete: true);
                cam.transform.DOShakePosition(shakeDuration, ShakeStrengthFor(e.Reward))
                    .SetUpdate(true);
            }
        }

        // Reward → SFX 볼륨 [0,1]. 보상 10에서 대략 최대치 근처(로그 완만).
        public static float VolumeFor(int reward)
        {
            if (reward <= 0) return 0f;
            return Mathf.Clamp01(reward / 10f);
        }

        // Reward → 카메라 셰이크 세기 [0, MaxShakeStrength].
        public static float ShakeStrengthFor(int reward)
        {
            if (reward <= 0) return 0f;
            return Mathf.Min(MaxShakeStrength, MaxShakeStrength * (reward / 10f));
        }
    }
}
