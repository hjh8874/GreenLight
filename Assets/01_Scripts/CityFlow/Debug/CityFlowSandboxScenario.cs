using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.DebugTools
{
    // CityFlowSandbox_cmt test scenario.
    // Seeds repeatable real-SimEngine layouts for FlowBurst and view checks.
    public sealed class CityFlowSandboxScenario : MonoBehaviour, ICityFlowServiceConsumer
    {
        private enum ScenarioMode
        {
            FlowBurstSmoke,
            DenseViewDemo
        }

        [SerializeField] private ScenarioMode mode = ScenarioMode.FlowBurstSmoke;
        [SerializeField] private float relieveAfterSeconds = 3f;
        [SerializeField] private bool logEvents = true;

        private CityFlowServices services;
        private float elapsed;
        private bool relieved;
        private static bool seeded;
        private static bool relievedOnce;

        public static bool HasSeeded => seeded;
        public static bool HasRelieved => relievedOnce;

        public static void MarkSeeded()
        {
            seeded = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            seeded = false;
            relievedOnce = false;
        }

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            this.services = services;
            if (seeded)
            {
                return;
            }

            if (logEvents)
            {
                services.Events.FlowBurst += OnFlowBurst;
                services.Events.Arrival += OnArrival;
            }

            switch (mode)
            {
                case ScenarioMode.DenseViewDemo:
                    BuildDenseViewDemo(services.Placement);
                    seeded = true;
                    Debug.Log("[SANDBOX] DenseViewDemo ready - check congestion/vehicle view.");
                    break;
                default:
                    BuildFlowBurstSmoke(services.Placement);
                    seeded = true;
                    Debug.Log("[SANDBOX] FlowBurstSmoke ready - removing houses after 3 seconds.");
                    break;
            }
        }

        private void OnDestroy()
        {
            if (services == null || !logEvents)
            {
                return;
            }

            services.Events.FlowBurst -= OnFlowBurst;
            services.Events.Arrival -= OnArrival;
        }

        private void Update()
        {
            if (services == null || relieved || mode != ScenarioMode.FlowBurstSmoke)
            {
                return;
            }

            elapsed += Time.deltaTime;
            if (elapsed < relieveAfterSeconds)
            {
                return;
            }

            relieved = true;

            for (int x = 0; x <= 6; x++)
            {
                services.Placement.Remove(new Vector2Int(x, 0));
            }

            relievedOnce = true;
            Debug.Log("[SANDBOX] Removed 7 houses - one FlowBurst should appear soon.");
        }

        public static void BuildFlowBurstSmoke(IPlacementService placement)
        {
            for (int x = 0; x <= 12; x++)
            {
                placement.Place(new Vector2Int(x, 1), TileType.Road);
            }

            for (int x = 0; x <= 11; x++)
            {
                placement.Place(new Vector2Int(x, 0), TileType.House);
            }

            placement.Place(new Vector2Int(12, 0), TileType.Office);
        }

        public static void BuildDenseViewDemo(IPlacementService placement)
        {
            for (int x = 1; x <= 18; x++)
            {
                placement.Place(new Vector2Int(x, 10), TileType.Road);
            }

            for (int y = 5; y <= 15; y++)
            {
                placement.Place(new Vector2Int(4, y), TileType.Road);
                placement.Place(new Vector2Int(13, y), TileType.Road);
                placement.Place(new Vector2Int(16, y), TileType.Road);
            }

            for (int y = 5; y <= 15; y += 2)
            {
                placement.Place(new Vector2Int(3, y), TileType.House);
                placement.Place(new Vector2Int(5, y), TileType.House);
                placement.Place(new Vector2Int(12, y), TileType.Office);
                placement.Place(new Vector2Int(17, y), TileType.Office);
            }

            placement.Place(new Vector2Int(13, 14), TileType.School);
            placement.Place(new Vector2Int(16, 6), TileType.School);
        }

        private static void OnFlowBurst(FlowBurstEvent e)
        {
            Debug.Log($"[SANDBOX] FlowBurst! tile={e.Tile} reward={e.Reward}");
        }

        private static void OnArrival(ArrivalEvent e)
        {
            Debug.Log($"[SANDBOX] Arrival at {e.Destination} +{e.Coins}coin");
        }
    }
}
