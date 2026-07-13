using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Content
{
    /// <summary>
    /// 도시 전체 인구를 관리하는 시스템입니다.
    ///
    /// 주거 건물이 건설되면 인구를 증가시키고,
    /// 주거 건물이 철거되면 해당 인구를 감소시킵니다.
    /// </summary>
    public class PopulationSystem : MonoBehaviour
    {
        [Header("초기 인구")]
        [Tooltip(
            "게임을 처음 시작할 때 적용되는 인구입니다. " +
            "일반적으로 0으로 설정합니다."
        )]
        [Min(0)]
        [SerializeField]
        private int startingPopulation;

        [Header("현재 상태")]
        [Tooltip(
            "현재 도시의 전체 인구입니다. " +
            "플레이 중 확인용이며 직접 수정하지 않는 것을 권장합니다."
        )]
        [Min(0)]
        [SerializeField]
        private int currentPopulation;

        /*
         * 같은 건물이 여러 번 등록되어
         * 인구가 중복 증가하는 문제를 방지하기 위한 목록입니다.
         */
        private readonly HashSet<BuildingPopulationData>
            registeredBuildings =
                new HashSet<BuildingPopulationData>();

        /// <summary>
        /// 현재 도시 전체 인구입니다.
        /// </summary>
        public int CurrentPopulation =>
            currentPopulation;

        /// <summary>
        /// 인구가 변경되었을 때 호출되는 이벤트입니다.
        ///
        /// HUD, 건물 해금 시스템 등이 이 이벤트를 구독하여
        /// 화면이나 해금 상태를 갱신할 수 있습니다.
        /// </summary>
        public event Action<int> PopulationChanged;

        private void Awake()
        {
            currentPopulation =
                Mathf.Max(0, startingPopulation);
        }

        /// <summary>
        /// 건설이 완료된 건물을 인구 시스템에 등록합니다.
        ///
        /// 주거 건물이면서 populationValue가 1 이상인 경우에만
        /// 인구가 증가합니다.
        /// </summary>
        public bool RegisterBuilding(
            BuildingPopulationData buildingData
        )
        {
            if (buildingData == null)
            {
                Debug.LogWarning(
                    "[PopulationSystem] " +
                    "등록하려는 건물 데이터가 없습니다.",
                    this
                );

                return false;
            }

            if (!buildingData.IsResidential)
            {
                return false;
            }

            if (buildingData.PopulationValue <= 0)
            {
                Debug.LogWarning(
                    $"[PopulationSystem] " +
                    $"{buildingData.name}의 인구 증가량이 " +
                    "0 이하이므로 등록하지 않습니다.",
                    buildingData
                );

                return false;
            }

            /*
             * HashSet.Add는 이미 등록된 건물이라면
             * false를 반환합니다.
             */
            if (!registeredBuildings.Add(buildingData))
            {
                Debug.LogWarning(
                    $"[PopulationSystem] " +
                    $"{buildingData.name}은 이미 등록된 건물입니다.",
                    buildingData
                );

                return false;
            }

            AddPopulation(
                buildingData.PopulationValue,
                buildingData.name
            );

            buildingData.MarkAsRegistered();

            return true;
        }

        /// <summary>
        /// 철거되는 건물을 인구 시스템에서 제거합니다.
        ///
        /// 건설 당시 추가했던 인구만큼 현재 인구에서 감소시킵니다.
        /// </summary>
        public bool UnregisterBuilding(
            BuildingPopulationData buildingData
        )
        {
            if (buildingData == null)
            {
                Debug.LogWarning(
                    "[PopulationSystem] " +
                    "제거하려는 건물 데이터가 없습니다.",
                    this
                );

                return false;
            }

            if (!registeredBuildings.Remove(buildingData))
            {
                Debug.LogWarning(
                    $"[PopulationSystem] " +
                    $"{buildingData.name}은 등록되지 않은 건물입니다.",
                    buildingData
                );

                return false;
            }

            RemovePopulation(
                buildingData.PopulationValue,
                buildingData.name
            );

            buildingData.MarkAsUnregistered();

            return true;
        }

        /// <summary>
        /// 지정된 수치만큼 인구를 증가시킵니다.
        ///
        /// 건물 등록 이외의 이벤트로 인구를 추가해야 할 때도
        /// 사용할 수 있습니다.
        /// </summary>
        public void AddPopulation(
            int amount,
            string reason = "population added"
        )
        {
            if (amount <= 0)
            {
                return;
            }

            currentPopulation += amount;

            PopulationChanged?.Invoke(
                currentPopulation
            );

            Debug.Log(
                $"[PopulationSystem] " +
                $"Population increased. " +
                $"Reason: {reason}, " +
                $"Added: {amount}, " +
                $"Current: {currentPopulation}"
            );
        }

        /// <summary>
        /// 지정된 수치만큼 인구를 감소시킵니다.
        ///
        /// 인구는 0 아래로 내려가지 않습니다.
        /// </summary>
        public void RemovePopulation(
            int amount,
            string reason = "population removed"
        )
        {
            if (amount <= 0)
            {
                return;
            }

            currentPopulation =
                Mathf.Max(
                    0,
                    currentPopulation - amount
                );

            PopulationChanged?.Invoke(
                currentPopulation
            );

            Debug.Log(
                $"[PopulationSystem] " +
                $"Population decreased. " +
                $"Reason: {reason}, " +
                $"Removed: {amount}, " +
                $"Current: {currentPopulation}"
            );
        }

        /// <summary>
        /// 저장된 인구 값을 불러올 때 사용합니다.
        ///
        /// 저장 시스템이 완성되면 저장 데이터의 인구 값을
        /// 이 함수에 전달하면 됩니다.
        /// </summary>
        public void RestorePopulation(
            int savedPopulation
        )
        {
            currentPopulation =
                Mathf.Max(
                    0,
                    savedPopulation
                );

            PopulationChanged?.Invoke(
                currentPopulation
            );

            Debug.Log(
                $"[PopulationSystem] " +
                $"Population restored: " +
                $"{currentPopulation}"
            );
        }

        /// <summary>
        /// 새 게임 시작 또는 테스트 초기화 시
        /// 인구와 등록 건물 목록을 초기화합니다.
        /// </summary>
        public void ResetPopulation()
        {
            registeredBuildings.Clear();

            currentPopulation =
                Mathf.Max(
                    0,
                    startingPopulation
                );

            PopulationChanged?.Invoke(
                currentPopulation
            );

            Debug.Log(
                $"[PopulationSystem] " +
                $"Population reset: " +
                $"{currentPopulation}"
            );
        }

        /// <summary>
        /// 특정 건물이 현재 인구 시스템에
        /// 등록되어 있는지 확인합니다.
        /// </summary>
        public bool IsBuildingRegistered(
            BuildingPopulationData buildingData
        )
        {
            if (buildingData == null)
            {
                return false;
            }

            return registeredBuildings.Contains(
                buildingData
            );
        }
    }
}