using System;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Content
{
    /// <summary>
    /// 차량 도착 이벤트를 기준으로
    /// 게임 전체 누적 도착 횟수를 관리합니다.
    ///
    /// FlowSolver.DeliveredTotal처럼 현재 틱 처리량을 나타내는 값과 달리,
    /// LifetimeDeliveredTotal은 새 게임 시작 이후 계속 누적되는 진행도 값입니다.
    ///
    /// 실제 해금 임계값은 이 클래스에 하드코딩하지 않습니다.
    /// ShopItemDataSO.RequiredTotalArrivals를 정본으로 사용합니다.
    /// </summary>
    public sealed class DeliveredProgressSystem :
        MonoBehaviour,
        ICityFlowServiceConsumer,
        IReadOnlyDeliveredProgress
    {
        [Header("누적 도착 진행도")]

        [Tooltip(
            "새 게임 시작 이후 누적된 전체 도착 횟수입니다. " +
            "현재는 런타임 누적값이며, 세이브 연동은 기존 Progression 저장 구조에 연결해야 합니다."
        )]
        [Min(0)]
        [SerializeField]
        private long lifetimeDeliveredTotal;

        private CityFlowServices services;
        private bool isInitialized;
        private bool isSubscribed;

        /// <summary>
        /// 새 게임 시작 이후 누적된 전체 도착 횟수입니다.
        /// </summary>
        public long LifetimeDeliveredTotal =>
            lifetimeDeliveredTotal;

        /// <summary>
        /// 누적 도착값이 변경될 때 최신 값을 전달합니다.
        /// </summary>
        public event Action<long>
            LifetimeDeliveredChanged;

        /// <summary>
        /// CityBootstrap에서 CityFlowServices를 전달받아
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

            if (isInitialized)
            {
                /*
                 * CityBootstrap과 Start의 보조 초기화가
                 * 모두 호출되어도 이벤트를 중복 구독하지 않습니다.
                 */
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

            if (services.Events == null)
            {
                Debug.LogError(
                    "[DeliveredProgressSystem] " +
                    "SimEventHub가 연결되지 않았습니다.",
                    this
                );

                return;
            }

            this.services = services;
            isInitialized = true;

            SubscribeEvents();
            PublishChanged();

            Debug.Log(
                $"[DeliveredProgressSystem] 초기화 완료. " +
                $"누적 도착: {lifetimeDeliveredTotal}",
                this
            );
        }

        /// <summary>
        /// CityBootstrap의 자동 초기화 대상에서 누락됐을 경우를 대비한
        /// 보조 초기화입니다.
        ///
        /// 이 코드가 있어도 DeliveredProgressSystem 컴포넌트는
        /// 반드시 활성 씬의 GameObject에 추가되어 있어야 합니다.
        /// </summary>
        private void Start()
        {
            if (isInitialized)
            {
                return;
            }

            CityBootstrap bootstrap =
                FindAnyObjectByType<CityBootstrap>();

            if (bootstrap?.Services == null)
            {
                Debug.LogWarning(
                    "[DeliveredProgressSystem] " +
                    "CityBootstrap 또는 Services를 찾지 못했습니다. " +
                    "Services 오브젝트에 컴포넌트가 추가되어 있는지 확인하세요.",
                    this
                );

                return;
            }

            Initialize(
                bootstrap.Services
            );
        }

        private void OnEnable()
        {
            if (isInitialized)
            {
                SubscribeEvents();
            }
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();
        }

        /// <summary>
        /// Arrival 이벤트를 한 번만 구독합니다.
        /// </summary>
        private void SubscribeEvents()
        {
            if (isSubscribed ||
                services?.Events == null)
            {
                return;
            }

            services.Events.Arrival +=
                OnArrival;

            isSubscribed = true;
        }

        /// <summary>
        /// Arrival 이벤트 구독을 해제합니다.
        /// </summary>
        private void UnsubscribeEvents()
        {
            if (!isSubscribed ||
                services?.Events == null)
            {
                return;
            }

            services.Events.Arrival -=
                OnArrival;

            isSubscribed = false;
        }

        /// <summary>
        /// 차량 도착 이벤트를 받으면
        /// 누적 도착 횟수를 증가시킵니다.
        ///
        /// 현재는 Arrival 이벤트 1회를
        /// 차량 1대 도착으로 처리합니다.
        ///
        /// ArrivalEvent가 여러 대의 도착량을 포함하는 구조라면
        /// 이후 이벤트의 실제 도착 수 필드를 사용해야 합니다.
        /// </summary>
        private void OnArrival(
            ArrivalEvent arrivalEvent
        )
        {
            AddDeliveredCount(
                1L,
                "arrival"
            );
        }

        /// <summary>
        /// 누적 도착 횟수를 증가시킵니다.
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
                    $"도착 증가량은 1 이상이어야 합니다. " +
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
        /// 저장된 누적 도착값을 복원합니다.
        ///
        /// 기존 IProgressionSaveSource 구현체의
        /// RestoreSnapshot에서 호출할 예정입니다.
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
        /// 새 게임 또는 테스트 초기화 시
        /// 누적 도착값을 0으로 변경합니다.
        /// </summary>
        public void ResetProgress()
        {
            lifetimeDeliveredTotal = 0L;

            PublishChanged();

            Debug.Log(
                "[DeliveredProgressSystem] " +
                "누적 도착값을 초기화했습니다.",
                this
            );
        }

        /// <summary>
        /// 전달받은 누적 도착 임계값을 만족했는지 확인합니다.
        ///
        /// 이 메서드에는 로터리 100, 입체 교차로 500 같은
        /// 특정 상품의 값을 직접 넣지 않습니다.
        ///
        /// 호출자는 ShopItemDataSO.RequiredTotalArrivals를
        /// 전달해야 합니다.
        /// </summary>
        public bool HasReached(
            long requiredTotalArrivals
        )
        {
            long safeRequirement =
                Math.Max(
                    0L,
                    requiredTotalArrivals
                );

            return lifetimeDeliveredTotal >=
                   safeRequirement;
        }

        /// <summary>
        /// 누적 도착 변경 이벤트를 발생시킵니다.
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