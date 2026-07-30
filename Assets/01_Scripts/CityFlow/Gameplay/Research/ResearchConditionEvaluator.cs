using System;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Gameplay.Research
{
    public enum ResearchConditionKind { DailyArrivals, Population, BuildingCount }

    // 사다리 한 칸. SO 리스트 직렬화용 plain class — 에셋 편집만으로 사다리를 바꾼다.
    [Serializable]
    public sealed class ResearchEntry
    {
        public string researchId;
        public string displayName;
        public ResearchConditionKind conditionKind;
        public int threshold;
        public TileType targetTileType;   // BuildingCount 에서만 사용
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
        public static bool IsSatisfied(ResearchEntry entry, in ResearchConditionInputs inputs) =>
            CurrentValue(entry, inputs) >= Mathf.Max(0, entry.threshold);

        public static int CurrentValue(ResearchEntry entry, in ResearchConditionInputs inputs) =>
            entry.conditionKind switch
            {
                ResearchConditionKind.DailyArrivals => inputs.LastDayArrivals,
                ResearchConditionKind.Population => inputs.Population,
                ResearchConditionKind.BuildingCount =>
                    inputs.CountBuildings?.Invoke(entry.targetTileType) ?? 0,
                _ => 0,
            };
    }
}
