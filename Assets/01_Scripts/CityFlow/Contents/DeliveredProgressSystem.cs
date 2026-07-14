using System;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Content
{
    /// <summary>
    /// 차량의 누적 도착 횟수를 관리하는 진행도 시스템입니다.
    ///
    /// 기존 FlowSolver.DeliveredTotal은 이번 틱의 처리량이므로
    /// 장기 해금 조건으로 사용하지 않습니다.
    ///
    /// 이 시스템의 LifetimeDeliveredTotal은
    /// 게임 전체 기간 동안 누적되는 별도의 진행도 값입니다.
    /// </summary>
    public sealed class DeliveredProgressSystem :
        MonoBehaviour,
        ICityFlowServiceConsumer,
        IReadOnlyDeliveredProgress
    {
        [Header("현재 누적 도착 진행도")]

        [Tooltip(
            "새 게임 시작 이후 누적된 전체 도착 횟수입니다. " +
            "로터리, 입체 교차로, 도시 단계 해금에 사용합니다."
        )]
        [Min(0)]
        [SerializeField]
        private long lifetimeDeliveredTotal;

        private CityFlowServices services;

        /// <summary>
        /// 누적 도착 횟수입니다.
        /// </summary>
        public long LifetimeDeliveredTotal =>
            lifetimeDeliveredTotal;

        /// <summary>
        /// 누적 도착값이 변경되었을 때 발생합니다.
        /// </summary>
        public event Action<long>
            LifetimeDeliveredChanged;

        /// <summary>
        /// CityBootstrap에서 서비스를 전달받아
        /// 도착 이벤트를 구독합니다.
        /// </summary>
        public void Initialize(
            CityFlowServices services
        )
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (services == null)
            {
                Debug.LogError(
                    "[DeliveredProgressSystem] " +
                    "CityFlowServices가 없습니다.",
                    this
                );

                return;
            }

            this.services = services;

            if (services.Events == null)
            {
                Debug.LogError(
                    "[DeliveredProgressSystem] " +
                    "SimEventHub가 없습니다.",
                    this
                );

                return;
            }

            services.Events.Arrival += OnArrival;

            PublishChanged();

            Debug.Log(
                $"[DeliveredProgressSystem] " +
                $"초기화 완료. " +
                $"누적 도착: {lifetimeDeliveredTotal}",
                this
            );
        }

        private void OnDestroy()
        {
            if (services?.Events == null)
            {
                return;
            }

            services.Events.Arrival -= OnArrival;
        }

        /// <summary>
        /// 차량 도착 이벤트를 받을 때마다
        /// 누적 도착값을 1 증가시킵니다.
        ///
        /// 현재 코드는 Arrival 이벤트가 차량 1대당
        /// 한 번 발생한다는 전제로 작성되어 있습니다.
        /// </summary>
        private void OnArrival(
            ArrivalEvent arrivalEvent
        )
        {
            AddDeliveredCount(
                1L,
                "arrival event"
            );
        }

        /// <summary>
        /// 누적 도착값을 증가시킵니다.
        ///
        /// 추후 여러 대의 도착을 한 번에 처리하는 경우에도
        /// 이 메서드를 그대로 사용할 수 있습니다.
        /// </summary>
        public void AddDeliveredCount(
            long amount,
            string reason = "external arrival"
        )
        {
            if (amount <= 0L)
            {
                Debug.LogWarning(
                    $"[DeliveredProgressSystem] " +
                    $"증가량은 1 이상이어야 합니다. " +
                    $"Amount: {amount}",
                    this
                );

                return;
            }

            if (lifetimeDeliveredTotal >
                long.MaxValue - amount)
            {
                lifetimeDeliveredTotal =
                    long.MaxValue;

                Debug.LogWarning(
                    "[DeliveredProgressSystem] " +
                    "누적 도착값이 long 최대값에 도달했습니다.",
                    this
                );
            }
            else
            {
                lifetimeDeliveredTotal +=
                    amount;
            }

            PublishChanged();

            Debug.Log(
                $"[DeliveredProgressSystem] " +
                $"누적 도착 증가. " +
                $"Reason: {reason}, " +
                $"Added: {amount}, " +
                $"Total: {lifetimeDeliveredTotal}",
                this
            );
        }

        /// <summary>
        /// 저장 데이터에서 누적 도착값을 복원할 때 사용합니다.
        ///
        /// 현재 단계에서는 세이브 시스템에 직접 등록하지 않고,
        /// 기존 Progression 저장 구현체가 이 메서드를 호출하도록 합니다.
        /// </summary>
        public void RestoreLifetimeDeliveredTotal(
            long restoredValue
        )
        {
            lifetimeDeliveredTotal =
                Math.Max(
                    0L,
                    restoredValue
                );

            PublishChanged();

            Debug.Log(
                $"[DeliveredProgressSystem] " +
                $"누적 도착 복원 완료. " +
                $"Total: {lifetimeDeliveredTotal}",
                this
            );
        }

        /// <summary>
        /// 새 게임 시작이나 테스트 초기화에 사용합니다.
        /// </summary>
        public void ResetProgress()
        {
            lifetimeDeliveredTotal = 0L;

            PublishChanged();

            Debug.Log(
                "[DeliveredProgressSystem] " +
                "누적 도착 진행도가 초기화되었습니다.",
                this
            );
        }

        /// <summary>
        /// 지정한 누적 도착 조건을 만족했는지 확인합니다.
        /// </summary>
        public bool HasReached(
            long requiredDeliveredTotal
        )
        {
            long safeRequirement =
                Math.Max(
                    0L,
                    requiredDeliveredTotal
                );

            return lifetimeDeliveredTotal >=
                   safeRequirement;
        }

        /// <summary>
        /// 로터리 해금 조건을 만족했는지 확인합니다.
        /// </summary>
        public bool IsRoundaboutUnlocked()
        {
            return HasReached(100L);
        }

        /// <summary>
        /// 입체 교차로 해금 조건을 만족했는지 확인합니다.
        /// </summary>
        public bool IsOverpassUnlocked()
        {
            return HasReached(500L);
        }

        /// <summary>
        /// 현재 누적 도착값 변경 이벤트를 발생시킵니다.
        /// </summary>
        private void PublishChanged()
        {
            LifetimeDeliveredChanged?.Invoke(
                lifetimeDeliveredTotal
            );
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            lifetimeDeliveredTotal =
                Math.Max(
                    0L,
                    lifetimeDeliveredTotal
                );
        }
#endif
    }
}