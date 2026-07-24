using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Content.Traffic
{
    [CreateAssetMenu(
        fileName = "VehicleVisitProfile",
        menuName = "CityFlow/Traffic/Vehicle Visit Profile")]
    public sealed class VehicleVisitProfileSO : ScriptableObject
    {
        [Serializable]
        public sealed class DestinationWeight
        {
            [SerializeField]
            private CityDestinationType destinationType;

            [Min(0)]
            [SerializeField]
            private int weight = 1;

            [Tooltip("이 장소에 방문한 뒤 다음 목적지를 집으로 강제합니다.")]
            [SerializeField]
            private bool returnHomeAfterVisit;

            public CityDestinationType DestinationType =>
                destinationType;

            public int Weight => weight;

            public bool ReturnHomeAfterVisit =>
                returnHomeAfterVisit;
        }

        [Serializable]
        public sealed class TimeSchedule
        {
            [Range(0, 23)]
            [SerializeField]
            private int startHour;

            [Range(0, 23)]
            [SerializeField]
            private int endHour = 23;

            [SerializeField]
            private List<DestinationWeight> destinations = new();

            public IReadOnlyList<DestinationWeight> Destinations =>
                destinations;

            public bool ContainsHour(int hour)
            {
                hour = Mathf.Clamp(hour, 0, 23);

                if (startHour <= endHour)
                {
                    return hour >= startHour &&
                           hour <= endHour;
                }

                // 22시부터 다음 날 5시처럼 자정을 넘는 범위입니다.
                return hour >= startHour ||
                       hour <= endHour;
            }
        }

        [Header("Schedule")]
        [SerializeField]
        private List<TimeSchedule> schedules = new();

        [Header("Fallback")]
        [Tooltip("현재 시간에 맞는 스케줄이 없거나 목적지를 찾지 못한 경우 집으로 돌아갑니다.")]
        [SerializeField]
        private bool returnHomeWhenNoRule = true;

        public bool ReturnHomeWhenNoRule =>
            returnHomeWhenNoRule;

        public bool TrySelectDestinationType(
            int currentHour,
            out CityDestinationType destinationType,
            out bool returnHomeAfterVisit)
        {
            destinationType = CityDestinationType.None;
            returnHomeAfterVisit = false;

            TimeSchedule schedule =
                FindSchedule(currentHour);

            if (schedule == null)
            {
                return false;
            }

            int totalWeight = 0;

            foreach (DestinationWeight destination
                     in schedule.Destinations)
            {
                if (destination == null)
                {
                    continue;
                }

                if (destination.DestinationType ==
                    CityDestinationType.None)
                {
                    continue;
                }

                totalWeight += Mathf.Max(0, destination.Weight);
            }

            if (totalWeight <= 0)
            {
                return false;
            }

            int randomValue = UnityEngine.Random.Range(
                0,
                totalWeight);

            int accumulatedWeight = 0;

            foreach (DestinationWeight destination
                     in schedule.Destinations)
            {
                if (destination == null)
                {
                    continue;
                }

                int weight = Mathf.Max(0, destination.Weight);

                if (weight == 0)
                {
                    continue;
                }

                accumulatedWeight += weight;

                if (randomValue >= accumulatedWeight)
                {
                    continue;
                }

                destinationType =
                    destination.DestinationType;

                returnHomeAfterVisit =
                    destination.ReturnHomeAfterVisit;

                return true;
            }

            return false;
        }

        private TimeSchedule FindSchedule(int currentHour)
        {
            foreach (TimeSchedule schedule in schedules)
            {
                if (schedule != null &&
                    schedule.ContainsHour(currentHour))
                {
                    return schedule;
                }
            }

            return null;
        }
    }
}