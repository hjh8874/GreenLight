using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Content.Transit
{
    /// <summary>
    /// 일반 버스 정류장, 학교, 주거 건물의 기준 타일을 관리합니다.
    ///
    /// 학교와 주거 건물은 타일 데이터에서 자동 검색합니다.
    /// 일반 정류장은 현재 단계에서 씬에 직렬화되는 고정 좌표로 관리합니다.
    /// 플레이 중 건설 가능한 정류장은 추후 SaveData 연동이 필요합니다.
    /// </summary>
    public sealed class BusStopRegistry :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        [Header("그리드 크기")]
        [SerializeField, Min(1)]
        private int gridWidth = GridUtil.DefaultWidth;

        [SerializeField, Min(1)]
        private int gridHeight = GridUtil.DefaultHeight;

        [Header("초기화")]
        [SerializeField]
        [Tooltip("초기화 시 학교와 주거 건물 목록을 다시 검색합니다.")]
        private bool rebuildOnInitialize = true;

        [Header("고정 버스 정류장")]
        [SerializeField]
        [Tooltip(
            "현재 단계에서는 씬에 고정 배치되는 정류장 좌표입니다. " +
            "플레이 중 건설되는 정류장은 추후 SaveData 연동이 필요합니다.")]
        private List<Vector2Int> busStops = new();

        [Header("디버그")]
        [SerializeField]
        [Tooltip("활성화하면 목록 재구성 결과를 출력합니다.")]
        private bool verboseLogging;

        private readonly List<Vector2Int> schools = new();
        private readonly List<Vector2Int> residentialStops = new();

        private readonly HashSet<Vector2Int> busStopSet = new();
        private readonly HashSet<Vector2Int> schoolSet = new();
        private readonly HashSet<Vector2Int> residentialSet = new();

        private CityFlowServices services;
        private IReadOnlyTileData tileData;

        private bool isInitialized;
        private bool isPlacedSubscribed;
        private bool isRestoreSubscribed;

        public IReadOnlyList<Vector2Int> BusStops => busStops;
        public IReadOnlyList<Vector2Int> Schools => schools;
        public IReadOnlyList<Vector2Int> ResidentialStops => residentialStops;

        public int BusStopCount => busStops.Count;
        public int SchoolCount => schools.Count;
        public int ResidentialStopCount => residentialStops.Count;
        public bool IsInitialized => isInitialized;

        public event Action RegistryChanged;

        public void Initialize(
            CityFlowServices services)
        {
            if (!isActiveAndEnabled ||
                isInitialized)
            {
                return;
            }

            if (services == null)
            {
                Debug.LogError(
                    "[BusStopRegistry] CityFlowServices가 없습니다.",
                    this);
                return;
            }

            if (services.TileData == null)
            {
                Debug.LogError(
                    "[BusStopRegistry] IReadOnlyTileData가 등록되지 않았습니다.",
                    this);
                return;
            }

            this.services = services;
            tileData = services.TileData;

            RebuildBusStopSet();

            isInitialized = true;
            SubscribeEvents();

            if (rebuildOnInitialize)
            {
                RebuildFromTileData();
            }
        }

        private void OnEnable()
        {
            if (isInitialized)
            {
                SubscribeEvents();
            }
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();

            services = null;
            tileData = null;
            isInitialized = false;
        }

        private void SubscribeEvents()
        {
            SubscribePlaced();
            SubscribeRestore();
        }

        private void UnsubscribeEvents()
        {
            UnsubscribePlaced();
            UnsubscribeRestore();
        }

        private void SubscribePlaced()
        {
            if (isPlacedSubscribed ||
                services?.Events == null)
            {
                return;
            }

            services.Events.Placed += OnPlaced;
            isPlacedSubscribed = true;
        }

        private void UnsubscribePlaced()
        {
            if (!isPlacedSubscribed ||
                services?.Events == null)
            {
                return;
            }

            services.Events.Placed -= OnPlaced;
            isPlacedSubscribed = false;
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
            RestoreCompletedEvent restoreEvent)
        {
            RebuildBusStopSet();
            RebuildFromTileData();
        }

        private void OnPlaced(
            PlacedEvent placedEvent)
        {
            Vector2Int tile =
                placedEvent.Tile;

            if (placedEvent.IsRemove)
            {
                RemoveSchoolInternal(tile);
                RemoveResidentialStopInternal(tile);

                NotifyRegistryChanged();
                return;
            }

            RegisterTypedTile(
                tile,
                placedEvent.Type);

            SortAll();
            NotifyRegistryChanged();
        }

        public void RebuildFromTileData()
        {
            if (tileData == null)
            {
                return;
            }

            schools.Clear();
            residentialStops.Clear();

            schoolSet.Clear();
            residentialSet.Clear();

            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    Vector2Int tile =
                        new Vector2Int(x, y);

                    TileType tileType =
                        tileData.GetTileType(tile);

                    if (TileFootprint.IsBuilding(tileType) &&
                        !tileData.IsFootprintAnchor(tile))
                    {
                        continue;
                    }

                    RegisterTypedTile(
                        tile,
                        tileType);
                }
            }

            SortAll();
            NotifyRegistryChanged();

            if (verboseLogging)
            {
                Debug.Log(
                    $"[BusStopRegistry] 목록 재구성 완료. " +
                    $"일반 정류장: {busStops.Count}, " +
                    $"학교: {schools.Count}, " +
                    $"주거지역: {residentialStops.Count}",
                    this);
            }
        }

        private void RebuildBusStopSet()
        {
            busStopSet.Clear();

            for (int i = busStops.Count - 1;
                 i >= 0;
                 i--)
            {
                Vector2Int tile =
                    busStops[i];

                if (!IsInsideGrid(tile) ||
                    !busStopSet.Add(tile))
                {
                    busStops.RemoveAt(i);
                }
            }

            SortTiles(busStops);
        }

        private void RegisterTypedTile(
            Vector2Int tile,
            TileType tileType)
        {
            switch (tileType)
            {
                case TileType.School:
                    RegisterSchoolInternal(tile);
                    RemoveResidentialStopInternal(tile);
                    break;

                case TileType.House:
                    RegisterResidentialStopInternal(tile);
                    RemoveSchoolInternal(tile);
                    break;

                default:
                    RemoveSchoolInternal(tile);
                    RemoveResidentialStopInternal(tile);
                    break;
            }
        }

        public bool RegisterBusStop(
            Vector2Int tile)
        {
            if (!IsInsideGrid(tile))
            {
                Debug.LogWarning(
                    $"[BusStopRegistry] 그리드 밖의 정류장은 등록할 수 없습니다: {tile}",
                    this);
                return false;
            }

            if (!busStopSet.Add(tile))
            {
                return false;
            }

            busStops.Add(tile);
            SortTiles(busStops);
            NotifyRegistryChanged();

            return true;
        }

        public bool RemoveBusStop(
            Vector2Int tile)
        {
            if (!RemoveBusStopInternal(tile))
            {
                return false;
            }

            NotifyRegistryChanged();
            return true;
        }

        private bool RemoveBusStopInternal(
            Vector2Int tile)
        {
            if (!busStopSet.Remove(tile))
            {
                return false;
            }

            busStops.Remove(tile);
            return true;
        }

        public bool ContainsBusStop(
            Vector2Int tile)
        {
            return busStopSet.Contains(tile);
        }

        public List<Vector2Int> CopyBusStops()
        {
            return new List<Vector2Int>(
                busStops);
        }

        public bool RegisterSchool(
            Vector2Int tile)
        {
            if (!RegisterSchoolInternal(tile))
            {
                return false;
            }

            SortTiles(schools);
            NotifyRegistryChanged();
            return true;
        }

        private bool RegisterSchoolInternal(
            Vector2Int tile)
        {
            if (!schoolSet.Add(tile))
            {
                return false;
            }

            schools.Add(tile);
            return true;
        }

        public bool RemoveSchool(
            Vector2Int tile)
        {
            if (!RemoveSchoolInternal(tile))
            {
                return false;
            }

            NotifyRegistryChanged();
            return true;
        }

        private bool RemoveSchoolInternal(
            Vector2Int tile)
        {
            if (!schoolSet.Remove(tile))
            {
                return false;
            }

            schools.Remove(tile);
            return true;
        }

        public bool ContainsSchool(
            Vector2Int tile)
        {
            return schoolSet.Contains(tile);
        }

        public bool TryGetFirstSchool(
            out Vector2Int schoolTile)
        {
            if (schools.Count > 0)
            {
                schoolTile = schools[0];
                return true;
            }

            schoolTile = default;
            return false;
        }

        public bool RegisterResidentialStop(
            Vector2Int tile)
        {
            if (!RegisterResidentialStopInternal(tile))
            {
                return false;
            }

            SortTiles(residentialStops);
            NotifyRegistryChanged();
            return true;
        }

        private bool RegisterResidentialStopInternal(
            Vector2Int tile)
        {
            if (!residentialSet.Add(tile))
            {
                return false;
            }

            residentialStops.Add(tile);
            return true;
        }

        public bool RemoveResidentialStop(
            Vector2Int tile)
        {
            if (!RemoveResidentialStopInternal(tile))
            {
                return false;
            }

            NotifyRegistryChanged();
            return true;
        }

        private bool RemoveResidentialStopInternal(
            Vector2Int tile)
        {
            if (!residentialSet.Remove(tile))
            {
                return false;
            }

            residentialStops.Remove(tile);
            return true;
        }

        public bool ContainsResidentialStop(
            Vector2Int tile)
        {
            return residentialSet.Contains(tile);
        }

        public List<Vector2Int>
            CopyResidentialStops()
        {
            return new List<Vector2Int>(
                residentialStops);
        }

        private void NotifyRegistryChanged()
        {
            RegistryChanged?.Invoke();
        }

        private void SortAll()
        {
            SortTiles(busStops);
            SortTiles(schools);
            SortTiles(residentialStops);
        }

        private static void SortTiles(
            List<Vector2Int> tiles)
        {
            tiles.Sort(CompareTiles);
        }

        private static int CompareTiles(
            Vector2Int left,
            Vector2Int right)
        {
            int yCompare =
                left.y.CompareTo(right.y);

            return yCompare != 0
                ? yCompare
                : left.x.CompareTo(right.x);
        }

        private bool IsInsideGrid(
            Vector2Int tile)
        {
            return
                tile.x >= 0 &&
                tile.y >= 0 &&
                tile.x < gridWidth &&
                tile.y < gridHeight;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            gridWidth =
                Mathf.Max(
                    1,
                    gridWidth);

            gridHeight =
                Mathf.Max(
                    1,
                    gridHeight);

            RebuildBusStopSet();
        }
#endif
    }
}
