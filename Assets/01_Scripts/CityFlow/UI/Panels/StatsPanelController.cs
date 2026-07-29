using System.Collections;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.WorldGrid;
using TMPro;
using UnityEngine;

namespace CityFlow.UI
{
    public class StatsPanelController : MonoBehaviour, ICityFlowServiceConsumer
    {
        private const float CongestionDensityThreshold = 0.7f;

        [Header("UI Elements")]
        [SerializeField] private TMP_Text txtJamCount;
        [SerializeField] private TMP_Text txtCoinsPerMinute;

        private CityFlowServices _services;
        private IWorldGridService _worldGrid;
        private Coroutine _updateRoutine;
        private readonly Queue<ArrivalCoinSample> _arrivalCoinSamples = new();
        private readonly HashSet<Vector2Int> _roadTiles = new();
        private long _arrivalCoinsInLastMinute;
        private bool _roadCacheRebuildPending;

        private readonly struct ArrivalCoinSample
        {
            public readonly float Time;
            public readonly int Coins;

            public ArrivalCoinSample(float time, int coins)
            {
                Time = time;
                Coins = coins;
            }
        }

        private void Awake()
        {
            EnsureTextElements();
        }

        public void Configure(TMP_Text jamCount, TMP_Text coinsPerMinute)
        {
            txtJamCount = jamCount;
            txtCoinsPerMinute = coinsPerMinute;
        }

        public void Initialize(CityFlowServices services)
        {
            UnbindServices();
            _services = services;
            _roadTiles.Clear();
            _roadCacheRebuildPending = false;

            if (_services == null)
            {
                return;
            }

            _services.Events.Arrival += OnArrival;
            _services.Events.Placed += OnPlaced;
            _services.WorldGridRegistered += OnWorldGridRegistered;
            BindWorldGrid(_services.WorldGrid);

            if (_services.Save != null)
            {
                _services.Save.RestoreCompleted += OnRestoreCompleted;
            }

            RebuildRoadCache();
        }

        private void OnEnable()
        {
            // 패널이 켜질 때 스로틀링 업데이트 시작
            if (_updateRoutine != null) StopCoroutine(_updateRoutine);
            _updateRoutine = StartCoroutine(UpdateStatsRoutine());
        }

        private void OnDisable()
        {
            if (_updateRoutine != null) StopCoroutine(_updateRoutine);
        }

        private void OnDestroy()
        {
            UnbindServices();
        }

        private IEnumerator UpdateStatsRoutine()
        {
            WaitForSeconds wait = new WaitForSeconds(1.0f); // 통계창은 1초 주기로 갱신 (성능 최적화)

            while (true)
            {
                if (_services != null && _services.TileData != null)
                {
                    if (_roadCacheRebuildPending)
                    {
                        RebuildRoadCache();
                    }

                    int jamCount = CountCongestedRoads();

                    if (txtJamCount != null) txtJamCount.text = $"정체 구역: {jamCount}곳";

                    RemoveExpiredArrivalSamples();
                    if (txtCoinsPerMinute != null)
                    {
                        txtCoinsPerMinute.text = $"도착 수입: 분당 {_arrivalCoinsInLastMinute:N0}";
                    }
                }
                yield return wait;
            }
        }

        private void OnArrival(ArrivalEvent arrival)
        {
            if (arrival.Coins <= 0)
            {
                return;
            }

            _arrivalCoinSamples.Enqueue(new ArrivalCoinSample(Time.unscaledTime, arrival.Coins));
            _arrivalCoinsInLastMinute += arrival.Coins;
            RemoveExpiredArrivalSamples();
        }

        private void OnPlaced(PlacedEvent placed)
        {
            if (placed.IsRemove)
            {
                _roadTiles.Remove(placed.Tile);
                return;
            }

            if (placed.Type == TileType.Road)
            {
                _roadTiles.Add(placed.Tile);
            }
        }

        private void OnChunkUnlocked(GridChunkId chunk)
        {
            if (_services?.TileData == null || _worldGrid == null)
            {
                return;
            }

            UnlockedGridTileScanner.VisitChunk(
                _worldGrid,
                chunk,
                ScanRoadTile);
        }

        private void OnWorldGridRegistered(IWorldGridService service)
        {
            BindWorldGrid(service);
            RebuildRoadCache();
        }

        private void BindWorldGrid(IWorldGridService service)
        {
            if (ReferenceEquals(_worldGrid, service))
            {
                return;
            }

            if (_worldGrid != null)
            {
                _worldGrid.ChunkUnlocked -= OnChunkUnlocked;
                _worldGrid.AccessRestored -= OnWorldGridAccessRestored;
            }

            _worldGrid = service;
            if (_worldGrid != null)
            {
                _worldGrid.ChunkUnlocked += OnChunkUnlocked;
                _worldGrid.AccessRestored += OnWorldGridAccessRestored;
            }
        }

        private void OnWorldGridAccessRestored()
        {
            _roadCacheRebuildPending = true;
        }

        private void OnRestoreCompleted(RestoreCompletedEvent _)
        {
            _roadCacheRebuildPending = true;
        }

        private void RebuildRoadCache()
        {
            _roadTiles.Clear();
            _roadCacheRebuildPending = false;

            if (_services?.TileData == null)
            {
                return;
            }

            int scannedTileCount = UnlockedGridTileScanner.VisitUnlockedTiles(
                _worldGrid,
                GridUtil.DefaultWidth,
                GridUtil.DefaultHeight,
                ScanRoadTile);

            Debug.Log(
                $"[StatsPanelController] Rebuilt road cache from " +
                $"{scannedTileCount} unlocked tiles.",
                this);
        }

        private void ScanRoadTile(Vector2Int tile)
        {
            if (_services.TileData.GetTileType(tile) == TileType.Road)
            {
                _roadTiles.Add(tile);
            }
        }

        private int CountCongestedRoads()
        {
            int congestedRoadCount = 0;
            foreach (Vector2Int tile in _roadTiles)
            {
                if (_services.TileData.GetDensity01(tile) >
                    CongestionDensityThreshold)
                {
                    congestedRoadCount++;
                }
            }

            return congestedRoadCount;
        }

        private void UnbindServices()
        {
            if (_services == null)
            {
                return;
            }

            _services.Events.Arrival -= OnArrival;
            _services.Events.Placed -= OnPlaced;
            _services.WorldGridRegistered -= OnWorldGridRegistered;

            if (_services.Save != null)
            {
                _services.Save.RestoreCompleted -= OnRestoreCompleted;
            }

            BindWorldGrid(null);
            _services = null;
        }

        private void RemoveExpiredArrivalSamples()
        {
            float cutoff = Time.unscaledTime - 60f;
            while (_arrivalCoinSamples.Count > 0 && _arrivalCoinSamples.Peek().Time < cutoff)
            {
                _arrivalCoinsInLastMinute -= _arrivalCoinSamples.Dequeue().Coins;
            }
        }

        private void EnsureTextElements()
        {
            if (txtJamCount == null)
            {
                txtJamCount = CreateText("JamCountText", new Vector2(30f, -30f));
            }

            if (txtCoinsPerMinute == null)
            {
                txtCoinsPerMinute = CreateText("CoinsPerMinuteText", new Vector2(30f, -75f));
            }
        }

        private TMP_Text CreateText(string objectName, Vector2 anchoredPosition)
        {
            GameObject textObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(transform, false);

            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(420f, 40f);

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.fontSize = 26f;
            text.color = new Color(0.12f, 0.12f, 0.12f, 1f);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }
    }
}
