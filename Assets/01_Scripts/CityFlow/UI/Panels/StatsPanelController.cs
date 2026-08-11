using System.Collections;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.WorldGrid;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public class StatsPanelController : MonoBehaviour, ICityFlowServiceConsumer
    {
        private const float CongestionDensityThreshold = 0.7f;
        private const int IncomeChartBucketCount = 10;
        private const float DashboardWidth = 540f;
        private const float DashboardHeight = 360f;

        private static readonly Color DashboardColor =
            new(0.055f, 0.075f, 0.095f, 0.985f);
        private static readonly Color SurfaceColor =
            new(0.085f, 0.11f, 0.14f, 1f);
        private static readonly Color SurfaceBorderColor =
            new(0.22f, 0.29f, 0.36f, 0.85f);
        private static readonly Color PrimaryTextColor =
            new(0.94f, 0.97f, 1f, 1f);
        private static readonly Color SecondaryTextColor =
            new(0.61f, 0.69f, 0.77f, 1f);
        private static readonly Color AccentColor =
            new(0.13f, 0.78f, 0.66f, 1f);
        private static readonly Color GoodColor =
            new(0.22f, 0.82f, 0.55f, 1f);
        private static readonly Color WarningColor =
            new(1f, 0.68f, 0.20f, 1f);
        private static readonly Color DangerColor =
            new(0.95f, 0.31f, 0.31f, 1f);

        [Header("Legacy UI Elements")]
        [SerializeField] private TMP_Text txtJamCount;
        [SerializeField] private TMP_Text txtCoinsPerMinute;

        private CityFlowServices _services;
        private IWorldGridService _worldGrid;
        private Coroutine _updateRoutine;
        private readonly Queue<ArrivalCoinSample> _arrivalCoinSamples = new();
        private readonly HashSet<Vector2Int> _roadTiles = new();
        private readonly int[] _incomeBuckets = new int[IncomeChartBucketCount];
        private readonly Image[] _incomeBars = new Image[IncomeChartBucketCount];
        private long _arrivalCoinsInLastMinute;
        private bool _roadCacheRebuildPending;

        private RectTransform _dashboardRoot;
        private TMP_Text _snapshotTimeText;
        private TMP_Text _trafficStatusText;
        private Image _trafficStatusSurface;
        private TMP_Text _activeVehicleValue;
        private TMP_Text _trafficSummaryText;
        private TMP_Text _congestedRoadValue;
        private TMP_Text _citySummaryText;
        private Image _congestionFill;
        private TMP_Text _incomeChartCaption;
        private TMP_Text _pendingIncomeText;

        internal RectTransform DashboardRootForTest => _dashboardRoot;
        internal RectTransform PanelAlignmentRect =>
            _dashboardRoot != null
                ? _dashboardRoot
                : transform as RectTransform;
        internal IReadOnlyList<Image> IncomeBarsForTest => _incomeBars;

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
            EnsurePresentation();
        }

        public void Configure(TMP_Text jamCount, TMP_Text coinsPerMinute)
        {
            // 이전 디버그 조립기가 만든 두 줄 텍스트는 호환 입력으로만 받는다.
            // 새 대시보드는 자체 표시 요소를 제공하므로 중복 표시는 숨긴다.
            HideLegacyText(jamCount);
            HideLegacyText(coinsPerMinute);
            EnsurePresentation();
        }

        public void Initialize(CityFlowServices services)
        {
            EnsurePresentation();
            UnbindServices();
            _services = services;
            _roadTiles.Clear();
            _arrivalCoinSamples.Clear();
            _arrivalCoinsInLastMinute = 0;
            _roadCacheRebuildPending = false;

            if (_services == null)
            {
                RefreshStatsPresentation();
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
            RefreshStatsPresentation();
        }

        private void OnEnable()
        {
            EnsurePresentation();
            RefreshStatsPresentation();
            if (!Application.isPlaying)
            {
                return;
            }
            if (_updateRoutine != null)
            {
                StopCoroutine(_updateRoutine);
            }
            _updateRoutine = StartCoroutine(UpdateStatsRoutine());
        }

        private void OnDisable()
        {
            if (_updateRoutine == null)
            {
                return;
            }

            StopCoroutine(_updateRoutine);
            _updateRoutine = null;
        }

        private void OnDestroy()
        {
            UnbindServices();
        }

        private IEnumerator UpdateStatsRoutine()
        {
            var wait = new WaitForSecondsRealtime(1f);
            while (true)
            {
                RefreshStatsPresentation();
                yield return wait;
            }
        }

        private void RefreshStatsPresentation()
        {
            if (_roadCacheRebuildPending)
            {
                RebuildRoadCache();
            }

            RemoveExpiredArrivalSamples();

            int jamCount = CountCongestedRoads();
            int roadCount = _roadTiles.Count;
            float congestionRatio = roadCount > 0
                ? Mathf.Clamp01((float)jamCount / roadCount)
                : 0f;
            int activeVehicles = _services?.Stats?.ActiveVehicleCount ?? 0;
            int population = _services?.Population?.CurrentPopulation ?? 0;
            int lastDayArrivals = _services?.Stats?.LastDayArrivalCount ?? 0;
            long wallet = _services?.Economy?.Coins ?? 0;
            long pendingIncome = _services?.WeeklyEconomy?.PendingCoins ?? 0;

            SetText(_activeVehicleValue, $"{activeVehicles:N0}대");
            SetText(_congestedRoadValue, $"{jamCount:N0}곳");
            SetText(txtCoinsPerMinute, $"{_arrivalCoinsInLastMinute:N0}");
            SetText(
                _citySummaryText,
                $"인구 {population:N0}명  ·  어제 도착 {lastDayArrivals:N0}회  ·  보유 재화 {wallet:N0}");
            SetText(
                _pendingIncomeText,
                pendingIncome > 0
                    ? $"정산 대기 {pendingIncome:N0}"
                    : "정산 대기 없음");

            TrafficState trafficState = EvaluateTrafficState(roadCount, congestionRatio);
            SetText(_trafficStatusText, trafficState.Label);
            if (_trafficStatusText != null)
            {
                _trafficStatusText.color = trafficState.Color;
            }
            if (_trafficStatusSurface != null)
            {
                _trafficStatusSurface.color = WithAlpha(trafficState.Color, 0.16f);
            }
            SetText(
                _trafficSummaryText,
                roadCount <= 0
                    ? "도로 정보 없음"
                    : $"정체 {jamCount:N0}곳 / 전체 {roadCount:N0}칸  ·  혼잡 {congestionRatio * 100f:0.#}%");
            if (_congestionFill != null)
            {
                _congestionFill.fillAmount = congestionRatio;
                _congestionFill.color = trafficState.Color;
            }

            SetText(_snapshotTimeText, CreateSnapshotLabel());
            RefreshIncomeChart();
        }

        private void OnArrival(ArrivalEvent arrival)
        {
            if (arrival.Coins <= 0)
            {
                return;
            }

            _arrivalCoinSamples.Enqueue(
                new ArrivalCoinSample(Time.unscaledTime, arrival.Coins));
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
            if (_services?.TileData == null)
            {
                return 0;
            }

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
                BindWorldGrid(null);
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
            while (_arrivalCoinSamples.Count > 0 &&
                   _arrivalCoinSamples.Peek().Time < cutoff)
            {
                _arrivalCoinsInLastMinute -=
                    _arrivalCoinSamples.Dequeue().Coins;
            }
        }

        private void RefreshIncomeChart()
        {
            for (int index = 0; index < _incomeBuckets.Length; index++)
            {
                _incomeBuckets[index] = 0;
            }

            float now = Time.unscaledTime;
            foreach (ArrivalCoinSample sample in _arrivalCoinSamples)
            {
                float age = now - sample.Time;
                if (age < 0f || age >= IncomeChartBucketCount)
                {
                    continue;
                }

                int bucket = IncomeChartBucketCount - 1 - Mathf.FloorToInt(age);
                _incomeBuckets[bucket] += sample.Coins;
            }

            int peak = 0;
            for (int index = 0; index < _incomeBuckets.Length; index++)
            {
                peak = Mathf.Max(peak, _incomeBuckets[index]);
            }

            for (int index = 0; index < _incomeBars.Length; index++)
            {
                Image bar = _incomeBars[index];
                if (bar == null)
                {
                    continue;
                }

                float normalized = peak > 0
                    ? (float)_incomeBuckets[index] / peak
                    : 0f;
                RectTransform rect = bar.rectTransform;
                rect.sizeDelta = new Vector2(
                    rect.sizeDelta.x,
                    Mathf.Lerp(3f, 56f, normalized));
                bar.color = _incomeBuckets[index] > 0
                    ? AccentColor
                    : WithAlpha(SecondaryTextColor, 0.18f);
            }

            SetText(
                _incomeChartCaption,
                peak > 0
                    ? $"최근 10초 최고 +{peak:N0}"
                    : "최근 10초 기록 없음");
        }

        private string CreateSnapshotLabel()
        {
            IGameCalendarService calendar = _services?.GameCalendar;
            if (calendar == null)
            {
                return "실시간 집계";
            }

            return $"{calendar.Month:00}월 {calendar.Day:00}일 " +
                   $"{calendar.Hour:00}:00 기준";
        }

        internal static TrafficState EvaluateTrafficState(
            int roadCount,
            float congestionRatio)
        {
            if (roadCount <= 0)
            {
                return new TrafficState(
                    "분석 대기",
                    new Color(0.61f, 0.69f, 0.77f, 1f));
            }
            if (congestionRatio < 0.05f)
            {
                return new TrafficState("원활", GoodColor);
            }
            if (congestionRatio < 0.20f)
            {
                return new TrafficState("주의", WarningColor);
            }

            return new TrafficState("혼잡", DangerColor);
        }

        internal readonly struct TrafficState
        {
            public TrafficState(string label, Color color)
            {
                Label = label;
                Color = color;
            }

            public string Label { get; }
            public Color Color { get; }
        }

        private void EnsurePresentation()
        {
            if (_dashboardRoot != null)
            {
                return;
            }

            Transform existing = transform.Find("StatsDashboard");
            if (existing != null)
            {
                _dashboardRoot = existing as RectTransform;
                BindExistingPresentation();
                return;
            }

            TMP_Text styleSource = FindStyleSource();
            HideLegacyText(txtJamCount);
            HideLegacyText(txtCoinsPerMinute);
            ConfigureBackdrop();

            GameObject dashboard = CreateSurface(
                "StatsDashboard",
                transform,
                DashboardColor,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-12f, 12f),
                new Vector2(DashboardWidth, DashboardHeight),
                true);
            _dashboardRoot = dashboard.GetComponent<RectTransform>();
            Image dashboardImage = dashboard.GetComponent<Image>();
            if (dashboardImage != null)
            {
                dashboardImage.raycastTarget = true;
            }

            CreateText(
                "Title",
                _dashboardRoot,
                "도시 통계",
                new Vector2(16f, -14f),
                new Vector2(180f, 28f),
                20f,
                FontStyles.Bold,
                PrimaryTextColor,
                TextAlignmentOptions.MidlineLeft,
                styleSource);
            _snapshotTimeText = CreateText(
                "SnapshotTime",
                _dashboardRoot,
                "실시간 집계",
                new Vector2(280f, -17f),
                new Vector2(150f, 24f),
                10.5f,
                FontStyles.Normal,
                SecondaryTextColor,
                TextAlignmentOptions.MidlineRight,
                styleSource);
            CreateTrafficStatusChip(styleSource);
            CreateKpiCards(styleSource);
            CreateTrafficPanel(styleSource);
            CreateIncomePanel(styleSource);
            _citySummaryText = CreateText(
                "CitySummary",
                _dashboardRoot,
                "인구 0명  ·  어제 도착 0회  ·  보유 재화 0",
                new Vector2(16f, -326f),
                new Vector2(508f, 20f),
                9.5f,
                FontStyles.Normal,
                SecondaryTextColor,
                TextAlignmentOptions.Center,
                styleSource);
        }

        private void BindExistingPresentation()
        {
            _snapshotTimeText = FindDashboardText("SnapshotTime");
            _trafficStatusSurface = FindDashboardImage("TrafficStatusChip");
            _trafficStatusText = FindDashboardText("TrafficStatusChip/Value");
            _activeVehicleValue = FindDashboardText("ActiveVehicles/Value");
            _trafficSummaryText = FindDashboardText("TrafficHealth/Summary");
            _congestedRoadValue = FindDashboardText("CongestedRoads/Value");
            _citySummaryText = FindDashboardText("CitySummary");
            _congestionFill =
                FindDashboardImage("TrafficHealth/CongestionProgress/Fill");
            txtJamCount = _trafficSummaryText;
            txtCoinsPerMinute = FindDashboardText("IncomePerMinute/Value");
            _pendingIncomeText = FindDashboardText("IncomeTrend/PendingIncome");
            _incomeChartCaption = FindDashboardText("IncomeTrend/ChartCaption");
            for (int index = 0; index < _incomeBars.Length; index++)
            {
                _incomeBars[index] = FindDashboardImage(
                    $"IncomeTrend/Chart/Bar_{index:00}");
            }
        }

        private TMP_Text FindDashboardText(string path)
        {
            Transform target = _dashboardRoot?.Find(path);
            return target != null ? target.GetComponent<TMP_Text>() : null;
        }

        private Image FindDashboardImage(string path)
        {
            Transform target = _dashboardRoot?.Find(path);
            return target != null ? target.GetComponent<Image>() : null;
        }

        private void ConfigureBackdrop()
        {
            RectTransform panel = transform as RectTransform;
            if (panel != null)
            {
                panel.anchorMin = Vector2.zero;
                panel.anchorMax = Vector2.one;
                panel.offsetMin = Vector2.zero;
                panel.offsetMax = Vector2.zero;
            }

            Image image = GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            image.color = Color.clear;
            // 검은 배경은 제거하되 투명 영역을 통한 월드 클릭은 차단한다.
            image.raycastTarget = true;
        }

        private void CreateTrafficStatusChip(TMP_Text styleSource)
        {
            GameObject chip = CreateSurface(
                "TrafficStatusChip",
                _dashboardRoot,
                WithAlpha(GoodColor, 0.16f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(438f, -12f),
                new Vector2(86f, 32f),
                false);
            _trafficStatusSurface = chip.GetComponent<Image>();
            _trafficStatusText = CreateText(
                "Value",
                chip.transform,
                "분석 대기",
                new Vector2(8f, -4f),
                new Vector2(70f, 24f),
                11.5f,
                FontStyles.Bold,
                GoodColor,
                TextAlignmentOptions.Center,
                styleSource);
        }

        private void CreateKpiCards(TMP_Text styleSource)
        {
            _activeVehicleValue = CreateMetricCard(
                "ActiveVehicles",
                "활성 차량",
                "도로 위 차량",
                new Vector2(16f, -58f),
                new Color(0.26f, 0.68f, 1f, 1f),
                styleSource);
            _congestedRoadValue = CreateMetricCard(
                "CongestedRoads",
                "정체 구역",
                "혼잡 기준 초과",
                new Vector2(186f, -58f),
                WarningColor,
                styleSource);
            txtCoinsPerMinute = CreateMetricCard(
                "IncomePerMinute",
                "분당 수입",
                "최근 60초",
                new Vector2(356f, -58f),
                AccentColor,
                styleSource);
        }

        private TMP_Text CreateMetricCard(
            string name,
            string label,
            string detail,
            Vector2 position,
            Color accent,
            TMP_Text styleSource)
        {
            GameObject card = CreateSurface(
                name,
                _dashboardRoot,
                SurfaceColor,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                position,
                new Vector2(154f, 78f),
                true);
            CreateStretchImage(
                "Accent",
                card.transform,
                accent,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -4f),
                Vector2.zero);
            CreateText(
                "Label",
                card.transform,
                label,
                new Vector2(12f, -10f),
                new Vector2(130f, 18f),
                10f,
                FontStyles.Normal,
                SecondaryTextColor,
                TextAlignmentOptions.MidlineLeft,
                styleSource);
            TMP_Text value = CreateText(
                "Value",
                card.transform,
                "0",
                new Vector2(12f, -29f),
                new Vector2(130f, 28f),
                20f,
                FontStyles.Bold,
                PrimaryTextColor,
                TextAlignmentOptions.MidlineLeft,
                styleSource);
            CreateText(
                "Detail",
                card.transform,
                detail,
                new Vector2(12f, -58f),
                new Vector2(130f, 14f),
                8.5f,
                FontStyles.Normal,
                SecondaryTextColor,
                TextAlignmentOptions.MidlineLeft,
                styleSource);
            return value;
        }

        private void CreateTrafficPanel(TMP_Text styleSource)
        {
            GameObject panel = CreateSurface(
                "TrafficHealth",
                _dashboardRoot,
                SurfaceColor,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(16f, -148f),
                new Vector2(250f, 158f),
                true);
            CreateText(
                "Title",
                panel.transform,
                "교통 흐름",
                new Vector2(14f, -10f),
                new Vector2(180f, 22f),
                14f,
                FontStyles.Bold,
                PrimaryTextColor,
                TextAlignmentOptions.MidlineLeft,
                styleSource);
            _trafficSummaryText = CreateText(
                "Summary",
                panel.transform,
                "도로 정보 없음",
                new Vector2(14f, -40f),
                new Vector2(222f, 22f),
                9.5f,
                FontStyles.Normal,
                SecondaryTextColor,
                TextAlignmentOptions.MidlineLeft,
                styleSource);

            GameObject progress = CreateSurface(
                "CongestionProgress",
                panel.transform,
                new Color(0.035f, 0.05f, 0.065f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(14f, -72f),
                new Vector2(222f, 12f),
                false);
            GameObject fill = CreateStretchImage(
                "Fill",
                progress.transform,
                GoodColor,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            _congestionFill = fill.GetComponent<Image>();
            _congestionFill.type = Image.Type.Filled;
            _congestionFill.fillMethod = Image.FillMethod.Horizontal;
            _congestionFill.fillOrigin = 0;
            _congestionFill.fillAmount = 0f;

            CreateText(
                "Hint",
                panel.transform,
                "혼잡 20% 이상이면 흐름 개선이 필요합니다.",
                new Vector2(14f, -101f),
                new Vector2(222f, 34f),
                9f,
                FontStyles.Normal,
                SecondaryTextColor,
                TextAlignmentOptions.MidlineLeft,
                styleSource);

            txtJamCount = _trafficSummaryText;
        }

        private void CreateIncomePanel(TMP_Text styleSource)
        {
            GameObject panel = CreateSurface(
                "IncomeTrend",
                _dashboardRoot,
                SurfaceColor,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(276f, -148f),
                new Vector2(248f, 158f),
                true);
            CreateText(
                "Title",
                panel.transform,
                "도착 수입",
                new Vector2(14f, -10f),
                new Vector2(120f, 22f),
                14f,
                FontStyles.Bold,
                PrimaryTextColor,
                TextAlignmentOptions.MidlineLeft,
                styleSource);
            CreateText(
                "Period",
                panel.transform,
                "최근 60초",
                new Vector2(142f, -11f),
                new Vector2(92f, 20f),
                9f,
                FontStyles.Normal,
                SecondaryTextColor,
                TextAlignmentOptions.MidlineRight,
                styleSource);
            _pendingIncomeText = CreateText(
                "PendingIncome",
                panel.transform,
                "정산 대기 없음",
                new Vector2(14f, -38f),
                new Vector2(220f, 18f),
                9f,
                FontStyles.Normal,
                SecondaryTextColor,
                TextAlignmentOptions.MidlineLeft,
                styleSource);

            GameObject chartArea = CreateSurface(
                "Chart",
                panel.transform,
                new Color(0.035f, 0.05f, 0.065f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(14f, -62f),
                new Vector2(220f, 66f),
                false);
            for (int index = 0; index < IncomeChartBucketCount; index++)
            {
                GameObject bar = CreateStretchImage(
                    $"Bar_{index:00}",
                    chartArea.transform,
                    WithAlpha(SecondaryTextColor, 0.18f),
                    Vector2.zero,
                    Vector2.zero,
                    Vector2.zero,
                    Vector2.zero);
                RectTransform rect = bar.GetComponent<RectTransform>();
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(16f + index * 21f, 6f);
                rect.sizeDelta = new Vector2(12f, 3f);
                _incomeBars[index] = bar.GetComponent<Image>();
            }
            _incomeChartCaption = CreateText(
                "ChartCaption",
                panel.transform,
                "최근 10초 기록 없음",
                new Vector2(14f, -133f),
                new Vector2(220f, 18f),
                8.5f,
                FontStyles.Normal,
                SecondaryTextColor,
                TextAlignmentOptions.MidlineRight,
                styleSource);
        }

        private TMP_Text FindStyleSource()
        {
            if (txtJamCount != null)
            {
                return txtJamCount;
            }
            if (txtCoinsPerMinute != null)
            {
                return txtCoinsPerMinute;
            }

            return GetComponentInChildren<TMP_Text>(true);
        }

        private static void HideLegacyText(TMP_Text text)
        {
            if (text != null)
            {
                text.gameObject.SetActive(false);
            }
        }

        private GameObject CreateSurface(
            string objectName,
            Transform parent,
            Color color,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 size,
            bool outline)
        {
            var surface = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            surface.transform.SetParent(parent, false);
            RectTransform rect = surface.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            Image image = surface.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;

            if (outline)
            {
                Outline border = surface.AddComponent<Outline>();
                border.effectColor = SurfaceBorderColor;
                border.effectDistance = new Vector2(1f, -1f);
                border.useGraphicAlpha = true;
            }

            return surface;
        }

        private static GameObject CreateStretchImage(
            string objectName,
            Transform parent,
            Color color,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            var imageObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            imageObject.transform.SetParent(parent, false);
            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return imageObject;
        }

        private static TMP_Text CreateText(
            string objectName,
            Transform parent,
            string value,
            Vector2 anchoredPosition,
            Vector2 size,
            float fontSize,
            FontStyles fontStyle,
            Color color,
            TextAlignmentOptions alignment,
            TMP_Text styleSource)
        {
            var textObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            if (styleSource != null)
            {
                text.font = styleSource.font;
                text.fontSharedMaterial = styleSource.fontSharedMaterial;
            }
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }

        private static Color WithAlpha(Color color, float alpha) =>
            new(color.r, color.g, color.b, alpha);
    }
}
