using UnityEngine;

namespace CityFlow.Content.Traffic
{
    public interface IGameHourProvider
    {
        int CurrentHour { get; }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(VehicleDestinationPlanner))]
    public sealed class VehicleActivityController : MonoBehaviour
    {
        private enum ActivityState
        {
            WaitingForDestination,
            Moving,
            Staying
        }

        [Header("External Components")]
        [Tooltip(
            "IVehicleDestinationReceiver를 구현한 기존 차량 이동 스크립트를 연결합니다.")]
        [SerializeField]
        private MonoBehaviour destinationReceiverSource;

        [Tooltip(
            "IGameHourProvider를 구현한 게임 시간 시스템을 연결합니다.")]
        [SerializeField]
        private MonoBehaviour gameHourProviderSource;

        [Header("Visit Duration")]
        [Min(0f)]
        [SerializeField]
        private float minimumStaySeconds = 2f;

        [Min(0f)]
        [SerializeField]
        private float maximumStaySeconds = 5f;

        [Header("Retry")]
        [Min(0.1f)]
        [SerializeField]
        private float retryIntervalSeconds = 2f;

        [Header("Testing")]
        [Range(0, 23)]
        [SerializeField]
        private int testHour = 8;

        [SerializeField]
        private bool useTestHourWhenProviderMissing = true;

        private VehicleDestinationPlanner destinationPlanner;
        private IVehicleDestinationReceiver destinationReceiver;
        private IGameHourProvider gameHourProvider;

        private ActivityState currentState;
        private float stateTimer;

        private void Awake()
        {
            destinationPlanner =
                GetComponent<VehicleDestinationPlanner>();

            ResolveDestinationReceiver();
            ResolveGameHourProvider();
        }

        private void Start()
        {
            currentState =
                ActivityState.WaitingForDestination;

            stateTimer = 0f;

            TryStartNextTrip();
        }

        private void Update()
        {
            switch (currentState)
            {
                case ActivityState.WaitingForDestination:
                    UpdateWaitingState();
                    break;

                case ActivityState.Moving:
                    UpdateMovingState();
                    break;

                case ActivityState.Staying:
                    UpdateStayingState();
                    break;
            }
        }

        private void UpdateWaitingState()
        {
            stateTimer -= Time.deltaTime;

            if (stateTimer > 0f)
            {
                return;
            }

            TryStartNextTrip();
        }

        private void UpdateMovingState()
        {
            if (destinationReceiver == null)
            {
                EnterRetryState();
                return;
            }

            if (!destinationReceiver.HasArrivedAtDestination)
            {
                return;
            }

            destinationPlanner.NotifyDestinationReached();

            currentState = ActivityState.Staying;

            stateTimer = Random.Range(
                minimumStaySeconds,
                Mathf.Max(
                    minimumStaySeconds,
                    maximumStaySeconds));
        }

        private void UpdateStayingState()
        {
            stateTimer -= Time.deltaTime;

            if (stateTimer > 0f)
            {
                return;
            }

            TryStartNextTrip();
        }

        private void TryStartNextTrip()
        {
            if (destinationReceiver == null)
            {
                EnterRetryState();
                return;
            }

            int currentHour = GetCurrentHour();

            BuildingDestination destination =
                destinationPlanner.SelectNextDestination(
                    currentHour);

            if (destination == null)
            {
                EnterRetryState();
                return;
            }

            destinationReceiver.SetDestination(
                destination.VehicleStopPoint);

            currentState = ActivityState.Moving;
            stateTimer = 0f;
        }

        private void EnterRetryState()
        {
            currentState =
                ActivityState.WaitingForDestination;

            stateTimer = retryIntervalSeconds;
        }

        private int GetCurrentHour()
        {
            if (gameHourProvider != null)
            {
                return Mathf.Clamp(
                    gameHourProvider.CurrentHour,
                    0,
                    23);
            }

            return useTestHourWhenProviderMissing
                ? testHour
                : 0;
        }

        private void ResolveDestinationReceiver()
        {
            if (destinationReceiverSource != null)
            {
                destinationReceiver =
                    destinationReceiverSource
                    as IVehicleDestinationReceiver;
            }

            if (destinationReceiver != null)
            {
                return;
            }

            MonoBehaviour[] behaviours =
                GetComponents<MonoBehaviour>();

            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IVehicleDestinationReceiver receiver)
                {
                    destinationReceiver = receiver;
                    destinationReceiverSource = behaviour;
                    return;
                }
            }

            Debug.LogError(
                $"{gameObject.name}: " +
                "IVehicleDestinationReceiver를 구현한 " +
                "차량 이동 스크립트를 찾지 못했습니다.",
                this);
        }

        private void ResolveGameHourProvider()
        {
            if (gameHourProviderSource != null)
            {
                gameHourProvider =
                    gameHourProviderSource
                    as IGameHourProvider;
            }

            if (gameHourProvider != null)
            {
                return;
            }

            MonoBehaviour[] behaviours =
                FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);

            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IGameHourProvider provider)
                {
                    gameHourProvider = provider;
                    gameHourProviderSource = behaviour;
                    return;
                }
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            minimumStaySeconds =
                Mathf.Max(0f, minimumStaySeconds);

            maximumStaySeconds =
                Mathf.Max(
                    minimumStaySeconds,
                    maximumStaySeconds);

            retryIntervalSeconds =
                Mathf.Max(0.1f, retryIntervalSeconds);
        }
#endif
    }
}