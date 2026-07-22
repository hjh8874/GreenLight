using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Content
{
    public sealed class BuildingEffectService : MonoBehaviour
    {
        private float patientEventElapsedSeconds;

    public event Action<BuildingDefinitionSO> PatientEventRequested;
        public event Action<BuildingDefinitionSO, int> EmergencyTransportCompleted;

        public int CalculateCoveredHouseCount(
            BuildingDefinitionSO school,
            IReadOnlyList<BuildingDefinitionSO> nearbyBuildings)
        {
            if (school == null ||
                school.category != BuildingCategory.Education ||
                nearbyBuildings == null)
            {
                return 0;
            }

            int residentialCount = 0;

            for (int i = 0; i < nearbyBuildings.Count; i++)
            {
                BuildingDefinitionSO building = nearbyBuildings[i];

                if (building != null &&
                    building.category == BuildingCategory.Residential)
                {
                    residentialCount++;
                }
            }

            return Mathf.Min(
                residentialCount,
                school.SchoolCoverageCapacity);
        }

        public int CalculatePopulationCapBonus(
            BuildingDefinitionSO school,
            int coveredHouseCount)
        {
            if (school == null ||
                school.category != BuildingCategory.Education)
            {
                return 0;
            }

            int safeCoveredCount = Mathf.Clamp(
                coveredHouseCount,
                0,
                school.SchoolCoverageCapacity);

            return safeCoveredCount *
                   school.CoveredPopulationCapBonus;
        }

        public bool TickPatientEvent(
            BuildingDefinitionSO hospital,
            float deltaTime)
        {
            if (hospital == null ||
                hospital.category != BuildingCategory.Medical)
            {
                patientEventElapsedSeconds = 0f;
                return false;
            }

            float interval =
                hospital.PatientEventIntervalSeconds;

            if (interval <= 0f)
            {
                patientEventElapsedSeconds = 0f;
                return false;
            }

            patientEventElapsedSeconds +=
                Mathf.Max(0f, deltaTime);

            if (patientEventElapsedSeconds < interval)
            {
                return false;
            }

            patientEventElapsedSeconds %= interval;

            PatientEventRequested?.Invoke(hospital);
            return true;
        }

        public int CompleteEmergencyTransport(
            BuildingDefinitionSO hospital)
        {
            if (hospital == null ||
                hospital.category != BuildingCategory.Medical)
            {
                return 0;
            }

            int reward = hospital.EmergencyReward;

            EmergencyTransportCompleted?.Invoke(
                hospital,
                reward);

            return reward;
        }

        public void ResetPatientEventTimer()
        {
            patientEventElapsedSeconds = 0f;
        }
    }

}
