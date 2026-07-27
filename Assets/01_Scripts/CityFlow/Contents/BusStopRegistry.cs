using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Content.Transit
{
    /// <summary>
    /// 버스 정류장, 학교, 주거지역의 타일 좌표를 관리합니다.
    ///
    /// 매 프레임 타일 전체를 검색하지 않고,
    /// 초기화 시 한 번 스캔한 뒤 Placed 이벤트로 목록을 갱신합니다.
    /// </summary>
    public sealed class BusStopRegistry :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        [Header("그리드 크기")]
        [Min(1)]
        [SerializeField]
        private int gridWidth = GridUtil.DefaultWidth;

        [Min(1)]
        [SerializeField]
        private int gridHeight = GridUtil.DefaultHeight;

        [Header("버스 정류장 판별")]
        [Tooltip(
            "현재 TileType에 BusStop이 없다면 정류장 타일을 수동 등록해서 사용합니다."
        )]
        [SerializeField]
        private bool rebuildOnInitialize = true;

        private readonly List<Vector2Int> busStops = new();
        private readonly List<Vector2Int> schools = new();
        private readonly List<Vector2Int> residentialStops = new();

        private readonly HashSet<Vector2Int> busStopSet = new();
        private readonly HashSet<Vector2Int> schoolSet = new();
        private readonly HashSet<Vector2Int> residentialSet = new();

        private CityFlowServices services;
        private IReadOnlyTileData tileData;
        private IBusStopInfrastructureService busStopInfrastructure;

        private bool isInitialized;
        private bool isSubscribed;
        private bool isRestoreSubscribed;

        public IReadOnlyList<Vector2Int> BusStops => busStops;
        public IReadOnlyList<Vector2Int> Schools => schools;
        public IReadOnlyList<Vector2Int> ResidentialStops => residentialStops;

        public int BusStopCount => busStops.Count;
        public int SchoolCount => schools.Count;
        public int ResidentialStopCount => residentialStops.Count;

        public event Action RegistryChanged;

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (isInitialized)
            {
                return;
            }

            if (services == null)
            {
                Debug.LogError(
                    "[BusStopRegistry] CityFlowServices가 없습니다.",
                    this
                );
                return;
            }

            if (services.TileData == null)
            {
                Debug.LogError(
                    "[BusStopRegistry] IReadOnlyTileData가 등록되지 않았습니다.",
                    this
                );
                return;
            }

            this.services = services;
            tileData = services.TileData;
            busStopInfrastructure =
                services.Placement as IBusStopInfrastructureService;
            isInitialized = true;

            if (rebuildOnInitialize)
            {
                RebuildFromTileData();
            }

            SubscribeEvents();
            SubscribeRestore();
        }

        private void Start()
        {
            if (isInitialized)
            {
                return;
            }

            CityBootstrap bootstrap =
                FindAnyObjectByType<CityBootstrap>();

            if (bootstrap?.Services == null)
            {
                Debug.LogWarning(
                    "[BusStopRegistry] CityBootstrap 또는 Services를 찾지 못했습니다.",
                    this
                );
                return;
            }

            Initialize(bootstrap.Services);
        }

        private void OnEnable()
        {
            if (isInitialized)
            {
                SubscribeEvents();
                SubscribeRestore();
            }
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
            UnsubscribeRestore();
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();
            UnsubscribeRestore();
        }

        private void SubscribeEvents()
        {
            if (isSubscribed || services?.Events == null)
            {
                return;
            }

            services.Events.Placed += OnPlaced;
            isSubscribed = true;
        }

        private void UnsubscribeEvents()
        {
            if (!isSubscribed || services?.Events == null)
            {
                return;
            }

            services.Events.Placed -= OnPlaced;
            isSubscribed = false;
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

        /// <summary>
        /// 현재 타일 데이터를 기준으로 학교와 주거지역 목록을 다시 구성합니다.
        ///
        /// 버스 정류장은 전용 TileType이 현재 확인되지 않았으므로
        /// 수동 등록 목록을 유지합니다.
        /// </summary>
        public void RebuildFromTileData()
        {
            if (tileData == null)
            {
                return;
            }

            busStops.Clear();
            busStopSet.Clear();

            if (busStopInfrastructure != null)
            {
                IReadOnlyList<Vector2Int> installedStops =
                    busStopInfrastructure.BusStopTiles;

                for (int i = 0; i < installedStops.Count; i++)
                {
                    Vector2Int stop = installedStops[i];
                    if (busStopSet.Add(stop))
                    {
                        busStops.Add(stop);
                    }
                }
            }

            schools.Clear();
            residentialStops.Clear();

            schoolSet.Clear();
            residentialSet.Clear();

            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    Vector2Int tile = new(x, y);
                    TileType type = tileData.GetTileType(tile);

                    if (TileFootprint.IsBuilding(type) &&
                        !tileData.IsFootprintAnchor(tile))
                    {
                        continue;
                    }

                    RegisterTypedTile(tile, type);
                }
            }

            SortAll();

            RegistryChanged?.Invoke();

            Debug.Log(
                $"[BusStopRegistry] 목록 재구성 완료. " +
                $"정류장: {busStops.Count}, " +
                $"학교: {schools.Count}, " +
                $"주거지역: {residentialStops.Count}",
                this
            );
        }

        private void OnPlaced(PlacedEvent placedEvent)
        {
            Vector2Int tile = placedEvent.Tile;

            if (placedEvent.IsRemove)
            {
                RemoveSchool(tile);
                RemoveResidentialStop(tile);

                RegistryChanged?.Invoke();
                return;
            }

            RegisterTypedTile(tile, placedEvent.Type);
            SortAll();

            RegistryChanged?.Invoke();
        }

        private void RegisterTypedTile(
            Vector2Int tile,
            TileType tileType
        )
        {
            switch (tileType)
            {
                case TileType.School:
                    RegisterSchool(tile);
                    RemoveResidentialStop(tile);
                    break;

                case TileType.House:
                    RegisterResidentialStop(tile);
                    RemoveSchool(tile);
                    break;

                default:
                    RemoveSchool(tile);
                    RemoveResidentialStop(tile);
                    break;
            }
        }

        public bool RegisterBusStop(Vector2Int tile)
        {
            if (!busStopSet.Add(tile))
            {
                return false;
            }

            busStops.Add(tile);
            SortTiles(busStops);

            RegistryChanged?.Invoke();
            return true;
        }

        public bool RemoveBusStop(Vector2Int tile)
        {
            if (!busStopSet.Remove(tile))
            {
                return false;
            }

            busStops.Remove(tile);

            RegistryChanged?.Invoke();
            return true;
        }

        public bool RegisterSchool(Vector2Int tile)
        {
            if (!schoolSet.Add(tile))
            {
                return false;
            }

            schools.Add(tile);
            SortTiles(schools);
            return true;
        }

        public bool RemoveSchool(Vector2Int tile)
        {
            if (!schoolSet.Remove(tile))
            {
                return false;
            }

            schools.Remove(tile);
            return true;
        }

        public bool RegisterResidentialStop(Vector2Int tile)
        {
            if (!residentialSet.Add(tile))
            {
                return false;
            }

            residentialStops.Add(tile);
            SortTiles(residentialStops);
            return true;
        }

        public bool RemoveResidentialStop(Vector2Int tile)
        {
            if (!residentialSet.Remove(tile))
            {
                return false;
            }

            residentialStops.Remove(tile);
            return true;
        }

        public bool ContainsBusStop(Vector2Int tile)
        {
            return busStopSet.Contains(tile);
        }

        public bool ContainsSchool(Vector2Int tile)
        {
            return schoolSet.Contains(tile);
        }

        public bool ContainsResidentialStop(Vector2Int tile)
        {
            return residentialSet.Contains(tile);
        }

        public bool TryGetFirstSchool(out Vector2Int schoolTile)
        {
            if (schools.Count > 0)
            {
                schoolTile = schools[0];
                return true;
            }

            schoolTile = default;
            return false;
        }

        public List<Vector2Int> CopyBusStops()
        {
            return new List<Vector2Int>(busStops);
        }

        public List<Vector2Int> CopyResidentialStops()
        {
            return new List<Vector2Int>(residentialStops);
        }

        private void SortAll()
        {
            SortTiles(busStops);
            SortTiles(schools);
            SortTiles(residentialStops);
        }

        private static void SortTiles(List<Vector2Int> tiles)
        {
            tiles.Sort(CompareTiles);
        }

        private static int CompareTiles(
            Vector2Int left,
            Vector2Int right
        )
        {
            int yCompare = left.y.CompareTo(right.y);

            return yCompare != 0
                ? yCompare
                : left.x.CompareTo(right.x);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            gridWidth = Mathf.Max(1, gridWidth);
            gridHeight = Mathf.Max(1, gridHeight);
        }
#endif
    }
}
