using TMPro;
using UnityEngine;

namespace CityFlow.TilemapTest
{
    public sealed class TilemapTestTrafficDebugPanel : MonoBehaviour
    {
        [SerializeField] private TilemapTestTrafficFlowManager trafficFlowManager;
        [SerializeField] private TextMeshProUGUI statusText;

        public TilemapTestTrafficFlowManager TrafficFlowManager => trafficFlowManager;
        public TextMeshProUGUI StatusText => statusText;

        public void Configure(TilemapTestTrafficFlowManager manager, TextMeshProUGUI text)
        {
            trafficFlowManager = manager;
            statusText = text;
        }

        private void Awake()
        {
            if (trafficFlowManager == null)
            {
                trafficFlowManager = FindFirstObjectByType<TilemapTestTrafficFlowManager>();
            }

            if (statusText == null)
            {
                statusText = GetComponentInChildren<TextMeshProUGUI>(true);
            }
        }

        private void Update()
        {
            if (trafficFlowManager == null || statusText == null)
            {
                return;
            }

            int hour = Mathf.FloorToInt(trafficFlowManager.CurrentHour);
            int minute = Mathf.FloorToInt((trafficFlowManager.CurrentHour - hour) * 60f);

            statusText.text =
                $"Day {trafficFlowManager.CurrentDayIndex + 1:00}  {hour:00}:{minute:00}\n" +
                $"Vehicles: {trafficFlowManager.ActiveVehicleCount}\n" +
                $"Waiting Trips: {trafficFlowManager.TotalWaitingTrips}\n" +
                $"Missed Commute: {trafficFlowManager.TotalMissedCommutes}\n" +
                $"  Work {trafficFlowManager.MissedWorkerCommutes} / School {trafficFlowManager.MissedStudentCommutes}\n" +
                $"Missed Return: {trafficFlowManager.TotalMissedReturns}\n" +
                $"  Work {trafficFlowManager.MissedWorkerReturns} / School {trafficFlowManager.MissedStudentReturns}\n" +
                $"Day Length: {GetRealSecondsPerDay():0.0}s";
        }

        private float GetRealSecondsPerDay()
        {
            float minutesPerSecond = Mathf.Max(0.01f, trafficFlowManager.MinutesPerRealSecond);
            return 1440f / minutesPerSecond;
        }
    }
}

/*
Unity implementation:
1. Add this component to Canvas_Main/SafeAreaRoot/OverlayRoot/Debug/Panel_Debug.
2. Assign a TilemapTestTrafficFlowManager and a TextMeshProUGUI status label.
3. It displays the 24-hour simulation clock, waiting trips, and missed commute/return counts.
*/
