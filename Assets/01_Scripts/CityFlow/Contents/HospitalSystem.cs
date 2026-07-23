using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Content
{
    /// <summary>
    /// 병원과 주거 타일의 배치 상태를 기준으로
    /// 병원 의료 혜택과 안정도 보너스를 관리합니다.
    /// </summary>
    public sealed class HospitalSystem :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        [Header("병원 데이터")]

        [SerializeField]
        [Tooltip("병원용 BuildingDefinitionSO")]
        private BuildingDefinitionSO hospitalDefinition;

        [Header("그리드 크기")]

        [SerializeField]
        [Min(1)]
        private int gridWidth =
            GridUtil.DefaultWidth;

        [SerializeField]
        [Min(1)]
        private int gridHeight =
            GridUtil.DefaultHeight;

        [Header("현재 상태")]

        [SerializeField]
        private int currentHospitalStabilityBonus;

        private readonly List<Vector2Int>
            hospitalTiles = new();

        private readonly List<Vector2Int>
            houseTiles = new();

        private CityFlowServices services;
        private IReadOnlyTileData tileData;
        private bool isRestoreSubscribed;

        public int CurrentHospitalStabilityBonus =>
            currentHospitalStabilityBonus;

        public event Action<int>
            HospitalStabilityBonusChanged;

        public void Initialize(
            CityFlowServices services)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (services == null)
            {
                Debug.LogError(
                    "[HospitalSystem] " +
                    "CityFlowServices가 없습니다.",
                    this);

                return;
            }

            if (hospitalDefinition == null)
            {
                Debug.LogError(
                    "[HospitalSystem] " +
                    "병원 BuildingDefinitionSO가 연결되지 않았습니다.",
                    this);

                return;
            }

            if (!hospitalDefinition.IsHospital)
            {
                Debug.LogError(
                    "[HospitalSystem] " +
                    "연결된 데이터가 Medical 카테고리가 아닙니다.",
                    this);

                return;
            }

            this.services = services;
            tileData = services.TileData;

            if (tileData == null)
            {
                Debug.LogError(
                    "[HospitalSystem] " +
                    "IReadOnlyTileData를 찾을 수 없습니다.",
                    this);

                return;
            }

            RebuildFromTileData();

            services.Events.Placed += OnPlaced;
            SubscribeRestore();
        }

        private void OnDestroy()
        {
            if (services == null)
            {
                return;
            }

            services.Events.Placed -= OnPlaced;
            UnsubscribeRestore();
        }

        private void OnPlaced(
            PlacedEvent e)
        {
            if (e.Type != TileType.Hospital &&
                e.Type != TileType.House)
            {
                return;
            }

            /*
             * 병원 또는 집의 배치·철거는
             * 전체 의료 배정 결과를 바꿀 수 있으므로
             * 현재 타일 데이터를 기준으로 다시 계산합니다.
             */
            RebuildFromTileData();
        }

        private void SubscribeRestore()
        {
            if (isRestoreSubscribed ||
                services?.Save == null)
            {
                return;
            }

            services.Save.RestoreCompleted +=
                OnRestoreCompleted;

            isRestoreSubscribed = true;
        }

        private void UnsubscribeRestore()
        {
            if (!isRestoreSubscribed ||
                services?.Save == null)
            {
                return;
            }

            services.Save.RestoreCompleted -=
                OnRestoreCompleted;

            isRestoreSubscribed = false;
        }

        private void OnRestoreCompleted(
            RestoreCompletedEvent _)
        {
            RebuildFromTileData();
        }

        public void RebuildFromTileData()
        {
            if (tileData == null ||
                hospitalDefinition == null)
            {
                return;
            }

            hospitalTiles.Clear();
            houseTiles.Clear();

            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    Vector2Int tile =
                        new Vector2Int(x, y);

                    TileType tileType =
                        tileData.GetTileType(tile);

                    if (tileType == TileType.Hospital)
                    {
                        if (tileData.IsFootprintAnchor(tile))
                        {
                            hospitalTiles.Add(tile);
                        }
                    }
                    else if (tileType == TileType.House)
                    {
                        if (tileData.IsFootprintAnchor(tile))
                        {
                            houseTiles.Add(tile);
                        }
                    }
                }
            }

            RecalculateHospitalEffect();
        }

        private void RecalculateHospitalEffect()
        {
            long totalStabilityBonus = 0L;

            for (int i = 0;
                 i < hospitalTiles.Count;
                 i++)
            {
                int coveredHouseCount =
                    HospitalEffectCalculator
                        .CalculateCoveredHouseCount(
                            hospitalTiles[i],
                            hospitalDefinition
                                .HospitalCoverageRadius,
                            hospitalDefinition
                                .HospitalPatientCapacity,
                            houseTiles);

                int hospitalBonus =
                    HospitalEffectCalculator
                        .CalculateStabilityBonus(
                            coveredHouseCount,
                            hospitalDefinition
                                .HospitalStabilityBonus);

                totalStabilityBonus +=
                    hospitalBonus;

                if (totalStabilityBonus >=
                    int.MaxValue)
                {
                    totalStabilityBonus =
                        int.MaxValue;

                    break;
                }
            }

            int newBonus =
                (int)totalStabilityBonus;

            if (currentHospitalStabilityBonus ==
                newBonus)
            {
                return;
            }

            currentHospitalStabilityBonus =
                newBonus;

            HospitalStabilityBonusChanged?.Invoke(
                currentHospitalStabilityBonus);

            Debug.Log(
                $"[HospitalSystem] " +
                $"Hospitals: {hospitalTiles.Count}, " +
                $"Houses: {houseTiles.Count}, " +
                $"Stability bonus: " +
                $"{currentHospitalStabilityBonus}",
                this);
        }
    }
}
