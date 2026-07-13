using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Content
{
    /// <summary>
    /// Sim의 타일 데이터를 기준으로
    /// 도시 전체 인구를 관리하는 시스템입니다.
    ///
    /// 프리팹이나 View에 의존하지 않으며,
    /// PlacedEvent를 구독하여 인구를 증감시킵니다.
    /// </summary>
    public sealed class PopulationSystem :
        MonoBehaviour,
        ICityFlowServiceConsumer,
        IReadOnlyPopulationData
    {
        [Header("인구 설정")]
        [SerializeField]
        private PopulationConfigSO populationConfig;

        [Header("초기 그리드 크기")]
        [Tooltip(
            "현재 Sim 그리드의 가로 크기입니다. " +
            "MainCityView 또는 GridUtil 설정과 같게 맞춥니다."
        )]
        [Min(1)]
        [SerializeField]
        private int gridWidth =
            GridUtil.DefaultWidth;

        [Tooltip(
            "현재 Sim 그리드의 세로 크기입니다. " +
            "MainCityView 또는 GridUtil 설정과 같게 맞춥니다."
        )]
        [Min(1)]
        [SerializeField]
        private int gridHeight =
            GridUtil.DefaultHeight;

        [Header("현재 상태")]
        [Tooltip(
            "현재 도시의 전체 인구입니다. " +
            "Sim 타일 데이터를 기준으로 자동 계산됩니다."
        )]
        [SerializeField]
        private int currentPopulation;

        /*
         * 현재 인구에 반영된 타일과
         * 해당 타일의 인구 값을 저장합니다.
         *
         * 같은 PlacedEvent가 중복 발생하더라도
         * 인구가 두 번 증가하지 않도록 방어합니다.
         */
        private readonly Dictionary<
            Vector2Int,
            int
        > registeredPopulationTiles =
            new Dictionary<Vector2Int, int>();

        private CityFlowServices services;
        private IReadOnlyTileData tileData;

        public int CurrentPopulation =>
            currentPopulation;

        public event Action<int> PopulationChanged;

        /// <summary>
        /// CityFlowServices에서 타일 데이터와 이벤트를 받아
        /// 인구 시스템을 초기화합니다.
        /// </summary>
        public void Initialize(
            CityFlowServices services
        )
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (services == null)
            {
                Debug.LogError(
                    "[PopulationSystem] " +
                    "CityFlowServices가 없습니다.",
                    this
                );

                return;
            }

            this.services = services;
            tileData = services.TileData;

            if (populationConfig == null)
            {
                Debug.LogError(
                    "[PopulationSystem] " +
                    "PopulationConfigSO가 연결되지 않았습니다.",
                    this
                );

                return;
            }

            if (tileData == null)
            {
                Debug.LogError(
                    "[PopulationSystem] " +
                    "IReadOnlyTileData를 찾을 수 없습니다.",
                    this
                );

                return;
            }

            /*
             * 이벤트를 연결하기 전에 현재 Sim 타일 상태를
             * 한 번 스캔하여 인구를 계산합니다.
             *
             * 저장된 인구 값을 별도로 Restore하지 않으므로
             * 세이브/로드 더블 카운팅을 방지합니다.
             */
            RebuildPopulationFromTileData();

            services.Events.Placed += OnPlaced;
        }

        private void OnDestroy()
        {
            if (services == null)
            {
                return;
            }

            services.Events.Placed -= OnPlaced;
        }

        /// <summary>
        /// 현재 Sim의 모든 타일을 읽어서
        /// 인구를 처음부터 다시 계산합니다.
        ///
        /// 세이브 파일 로드 완료 후에도 이 함수를 호출하면
        /// 저장된 맵 상태를 기준으로 정확한 인구를 재구성할 수 있습니다.
        /// </summary>
        public void RebuildPopulationFromTileData()
        {
            if (tileData == null ||
                populationConfig == null)
            {
                return;
            }

            registeredPopulationTiles.Clear();

            long totalPopulation = 0L;

            for (int y = 0;
                 y < gridHeight;
                 y++)
            {
                for (int x = 0;
                     x < gridWidth;
                     x++)
                {
                    Vector2Int tile =
                        new Vector2Int(x, y);

                    TileType tileType =
                        tileData.GetTileType(tile);

                    int populationValue =
                        populationConfig
                            .GetPopulationValue(
                                tileType
                            );

                    if (populationValue <= 0)
                    {
                        continue;
                    }

                    registeredPopulationTiles.Add(
                        tile,
                        populationValue
                    );

                    totalPopulation +=
                        populationValue;
                }
            }

            currentPopulation =
                totalPopulation >
                int.MaxValue
                    ? int.MaxValue
                    : (int)totalPopulation;

            PopulationChanged?.Invoke(
                currentPopulation
            );

            Debug.Log(
                $"[PopulationSystem] " +
                $"Population rebuilt from tile data. " +
                $"Residential tiles: " +
                $"{registeredPopulationTiles.Count}, " +
                $"Population: {currentPopulation}",
                this
            );
        }

        /// <summary>
        /// 타일 배치 또는 철거 이벤트를 받아
        /// 해당 타일의 인구 상태를 갱신합니다.
        /// </summary>
        private void OnPlaced(
            PlacedEvent e
        )
        {
            if (populationConfig == null)
            {
                return;
            }

            if (e.IsRemove)
            {
                RemovePopulationTile(
                    e.Tile
                );

                return;
            }

            int newPopulationValue =
                populationConfig
                    .GetPopulationValue(
                        e.Type
                    );

            SetPopulationTile(
                e.Tile,
                newPopulationValue
            );
        }

        /// <summary>
        /// 타일의 인구 값을 새 값으로 변경합니다.
        ///
        /// 동일한 타일에 같은 이벤트가 중복 발생해도
        /// 기존 값과의 차이만 반영합니다.
        /// </summary>
        private void SetPopulationTile(
            Vector2Int tile,
            int newPopulationValue
        )
        {
            int safeNewValue =
                Mathf.Max(
                    0,
                    newPopulationValue
                );

            registeredPopulationTiles
                .TryGetValue(
                    tile,
                    out int oldPopulationValue
                );

            if (oldPopulationValue ==
                safeNewValue)
            {
                return;
            }

            if (safeNewValue <= 0)
            {
                registeredPopulationTiles.Remove(
                    tile
                );
            }
            else
            {
                registeredPopulationTiles[tile] =
                    safeNewValue;
            }

            ApplyPopulationDifference(
                safeNewValue -
                oldPopulationValue,
                tile
            );
        }

        /// <summary>
        /// 타일이 철거됐을 때
        /// 해당 타일이 제공하던 인구를 제거합니다.
        /// </summary>
        private void RemovePopulationTile(
            Vector2Int tile
        )
        {
            if (!registeredPopulationTiles
                .TryGetValue(
                    tile,
                    out int populationValue
                ))
            {
                /*
                 * 이미 제거된 타일이거나
                 * 원래 인구를 제공하지 않는 타일입니다.
                 *
                 * 중복 철거 이벤트가 들어와도
                 * 인구가 다시 감소하지 않습니다.
                 */
                return;
            }

            registeredPopulationTiles.Remove(
                tile
            );

            ApplyPopulationDifference(
                -populationValue,
                tile
            );
        }

        /// <summary>
        /// 계산된 인구 차이를 현재 인구에 반영합니다.
        /// </summary>
        private void ApplyPopulationDifference(
            int difference,
            Vector2Int tile
        )
        {
            if (difference == 0)
            {
                return;
            }

            long calculatedPopulation =
                (long)currentPopulation +
                difference;

            if (calculatedPopulation <= 0L)
            {
                currentPopulation = 0;
            }
            else if (calculatedPopulation >
                     int.MaxValue)
            {
                currentPopulation =
                    int.MaxValue;
            }
            else
            {
                currentPopulation =
                    (int)calculatedPopulation;
            }

            PopulationChanged?.Invoke(
                currentPopulation
            );

            Debug.Log(
                $"[PopulationSystem] " +
                $"Population changed. " +
                $"Tile: ({tile.x}, {tile.y}), " +
                $"Difference: {difference}, " +
                $"Current: {currentPopulation}",
                this
            );
        }

        /// <summary>
        /// 특정 타일이 현재 인구 계산에
        /// 등록되어 있는지 확인합니다.
        /// </summary>
        public bool IsPopulationTile(
            Vector2Int tile
        )
        {
            return registeredPopulationTiles
                .ContainsKey(tile);
        }

        /// <summary>
        /// 특정 타일이 제공하는 현재 인구 값을 반환합니다.
        /// </summary>
        public int GetPopulationAt(
            Vector2Int tile
        )
        {
            return registeredPopulationTiles
                .TryGetValue(
                    tile,
                    out int value
                )
                    ? value
                    : 0;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            gridWidth =
                Mathf.Max(
                    1,
                    gridWidth
                );

            gridHeight =
                Mathf.Max(
                    1,
                    gridHeight
                );
        }
#endif
    }
}