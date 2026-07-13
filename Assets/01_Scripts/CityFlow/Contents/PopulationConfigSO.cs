using System;
using System.Collections.Generic;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Content
{
    [CreateAssetMenu(
        fileName = "PopulationConfig",
        menuName = "CityFlow/Content/Population Config"
    )]
    public class PopulationConfigSO : ScriptableObject
    {
        [Serializable]
        public class TilePopulationEntry
        {
            [Tooltip("인구를 제공하는 타일 종류입니다.")]
            public TileType tileType;

            [Tooltip(
                "해당 타일 하나가 제공하는 인구입니다. " +
                "주거 건물이 아니라면 0으로 설정합니다."
            )]
            [Min(0)]
            public int populationValue;
        }

        [Header("타일별 인구 설정")]
        [Tooltip(
            "타일 종류별 인구 증가량을 설정합니다. " +
            "예: House = 5"
        )]
        [SerializeField]
        private List<TilePopulationEntry> populationEntries =
            new List<TilePopulationEntry>
            {
                new TilePopulationEntry
                {
                    tileType = TileType.House,
                    populationValue = 5
                }
            };

        /// <summary>
        /// 지정한 타일 종류가 제공하는 인구를 반환합니다.
        ///
        /// 설정 목록에 없는 타일은 인구 0을 반환합니다.
        /// </summary>
        public int GetPopulationValue(
            TileType tileType
        )
        {
            for (int i = 0;
                 i < populationEntries.Count;
                 i++)
            {
                TilePopulationEntry entry =
                    populationEntries[i];

                if (entry == null)
                {
                    continue;
                }

                if (entry.tileType == tileType)
                {
                    return Mathf.Max(
                        0,
                        entry.populationValue
                    );
                }
            }

            return 0;
        }

        /// <summary>
        /// 지정한 타일이 인구를 제공하는
        /// 주거 타일인지 확인합니다.
        /// </summary>
        public bool IsResidential(
            TileType tileType
        )
        {
            return GetPopulationValue(tileType) > 0;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            HashSet<TileType> registeredTypes =
                new HashSet<TileType>();

            for (int i = 0;
                 i < populationEntries.Count;
                 i++)
            {
                TilePopulationEntry entry =
                    populationEntries[i];

                if (entry == null)
                {
                    continue;
                }

                entry.populationValue =
                    Mathf.Max(
                        0,
                        entry.populationValue
                    );

                if (!registeredTypes.Add(
                    entry.tileType
                ))
                {
                    Debug.LogWarning(
                        $"[PopulationConfigSO] " +
                        $"중복된 TileType 설정이 있습니다: " +
                        $"{entry.tileType}",
                        this
                    );
                }
            }
        }
#endif
    }
}