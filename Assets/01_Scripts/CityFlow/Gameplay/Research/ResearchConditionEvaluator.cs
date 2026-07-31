using System;
using System.Collections.Generic;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Gameplay.Research
{
    public enum ResearchConditionKind { DailyArrivals, Population, BuildingCount }

    [Serializable]
    public sealed class ResearchRequirement
    {
        [InspectorName("조건 종류")]
        public ResearchConditionKind conditionKind;

        [InspectorName("목표 수치")]
        [Min(0)]
        public int threshold;

        [InspectorName("대상 건물")]
        [Tooltip("조건 종류가 BuildingCount일 때만 사용합니다.")]
        public TileType targetTileType;
    }

    // 사다리 한 칸. SO 리스트 직렬화용 plain class — 에셋 편집만으로 사다리를 바꾼다.
    [Serializable]
    public sealed class ResearchEntry
    {
        [InspectorName("연구 ID")]
        [Tooltip("건물과 저장 데이터가 참조하는 고유 ID입니다.")]
        public string researchId;

        [InspectorName("선행 연구 ID")]
        [Tooltip("비어 있으면 최상위 연구입니다.")]
        public string prerequisiteId;

        [InspectorName("표시 이름")]
        public string displayName;

        [InspectorName("단일 조건 종류")]
        [Tooltip("아래 조건 목록이 비어 있을 때 사용하는 호환용 단일 조건입니다.")]
        public ResearchConditionKind conditionKind;

        [InspectorName("단일 조건 목표 수치")]
        [Min(0)]
        public int threshold;

        [InspectorName("단일 조건 대상 건물")]
        public TileType targetTileType;   // BuildingCount 에서만 사용

        [InspectorName("연구 비용")]
        [Min(0)]
        public int researchCost;

        [InspectorName("연구 시간 (게임 시간)")]
        [Min(0)]
        public int researchDurationHours;

        [InspectorName("해금 조건 목록 (모두 만족)")]
        [Tooltip("하나 이상 등록하면 위 단일 조건 대신 이 조건들을 모두 만족해야 합니다.")]
        public List<ResearchRequirement> requirements = new();
    }

    // 평가에 필요한 현재값 묶음. 서비스가 채워 넘긴다 — 판정은 이 값만 본다(순수).
    public readonly struct ResearchConditionInputs
    {
        public readonly int LastDayArrivals;
        public readonly int Population;
        public readonly Func<TileType, int> CountBuildings;

        public ResearchConditionInputs(
            int lastDayArrivals, int population, Func<TileType, int> countBuildings)
        {
            LastDayArrivals = lastDayArrivals;
            Population = population;
            CountBuildings = countBuildings;
        }
    }

    // 순수 판정 — MonoBehaviour 없음, 결정론, EditMode 가 직접 때린다.
    public static class ResearchConditionEvaluator
    {
        public static bool IsSatisfied(
            ResearchEntry entry,
            in ResearchConditionInputs inputs)
        {
            if (entry == null)
            {
                return false;
            }

            if (entry.requirements != null && entry.requirements.Count > 0)
            {
                for (int index = 0; index < entry.requirements.Count; index++)
                {
                    ResearchRequirement requirement =
                        entry.requirements[index];
                    if (requirement == null ||
                        CurrentValue(requirement, inputs) <
                        Mathf.Max(0, requirement.threshold))
                    {
                        return false;
                    }
                }

                return true;
            }

            return CurrentValue(entry, inputs) >=
                   Mathf.Max(0, entry.threshold);
        }

        public static int CurrentValue(ResearchEntry entry, in ResearchConditionInputs inputs) =>
            entry == null
                ? 0
                : CurrentValue(
                    entry.conditionKind,
                    entry.targetTileType,
                    inputs);

        public static int CurrentValue(
            ResearchRequirement requirement,
            in ResearchConditionInputs inputs) =>
            requirement == null
                ? 0
                : CurrentValue(
                    requirement.conditionKind,
                    requirement.targetTileType,
                    inputs);

        private static int CurrentValue(
            ResearchConditionKind conditionKind,
            TileType targetTileType,
            in ResearchConditionInputs inputs) =>
            conditionKind switch
            {
                ResearchConditionKind.DailyArrivals => inputs.LastDayArrivals,
                ResearchConditionKind.Population => inputs.Population,
                ResearchConditionKind.BuildingCount =>
                    inputs.CountBuildings?.Invoke(targetTileType) ?? 0,
                _ => 0,
            };
    }
}
