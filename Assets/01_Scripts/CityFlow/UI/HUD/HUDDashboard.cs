using System;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace CityFlow.UI
{
    public sealed class HUDDashboard : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("UI References")]
        [SerializeField] private TextMeshProUGUI timeText;
        [SerializeField] private TextMeshProUGUI vehicleCountText;
        [SerializeField] private TextMeshProUGUI coinText;

        [Header("Settings")]
        [SerializeField] private float updateInterval = 0.2f;
        [SerializeField] private bool normalizeLayoutOnStart;

        private CityFlowServices _services;
        private IReadOnlyCityStats _cityStats;
        private IEconomyService _economy;
        private IGameCalendarService _gameCalendar;
        private IWeeklyEconomyService _weeklyEconomy;
        private PopulationSystem _populationSystem;
        private float _updateTimer;
        
        // Cached state from events
        private long _currentCoins;
        private long _pendingCoins;

        // Cache for target values to avoid redundant string allocations
        private int _lastTargetVehicles = -1;
        private int _lastTargetPopulation = -1;
        private long _lastTargetCoins = -1;

        public void Configure(
            TextMeshProUGUI time,
            TextMeshProUGUI vehicleCount,
            TextMeshProUGUI coin,
            GameObject _)
        {
            timeText = time;
            vehicleCountText = vehicleCount;
            coinText = coin;
        }

        public void Initialize(CityFlowServices services)
        {
            _services = services;
            _cityStats = services.Stats;
            _populationSystem =
                FindAnyObjectByType<PopulationSystem>();

            if (_populationSystem != null)
            {
                _populationSystem.PopulationChanged +=
                    OnPopulationChanged;
            }

            if (normalizeLayoutOnStart)
            {
                NormalizeHeaderLayout();
            }

            // 이벤트 구독 (구독해야 코어 엔진에서 데이터가 날아옵니다)
            _services.EconomyRegistered += OnEconomyRegistered;
            _services.GameCalendarRegistered += OnGameCalendarRegistered;
            _services.WeeklyEconomyRegistered += OnWeeklyEconomyRegistered;

            if (_services.Economy != null)
            {
                BindEconomy(_services.Economy);
            }
            else
            {
                _services.Events.Arrival += OnArrival;
            }

            if (_services.GameCalendar != null)
            {
                BindGameCalendar(_services.GameCalendar);
            }

            if (_services.WeeklyEconomy != null)
            {
                BindWeeklyEconomy(_services.WeeklyEconomy);
            }

            // 초기 UI 갱신
            UpdateUI();
        }

        private void OnDestroy()
        {
            if (_services != null && _services.Events != null)
            {
                // 메모리 누수 방지 이벤트 해제
                _services.Events.Arrival -= OnArrival;
                _services.EconomyRegistered -= OnEconomyRegistered;
                _services.GameCalendarRegistered -= OnGameCalendarRegistered;
                _services.WeeklyEconomyRegistered -= OnWeeklyEconomyRegistered;
            }

            if (_economy != null)
            {
                _economy.CoinsChanged -= OnCoinsChanged;
            }

            if (_gameCalendar != null)
            {
                _gameCalendar.HourChanged -= OnCalendarChanged;
                _gameCalendar.DayChanged -= OnCalendarChanged;
                _gameCalendar.MonthChanged -= OnCalendarChanged;
            }

            if (_weeklyEconomy != null)
            {
                _weeklyEconomy.PendingCoinsChanged -= OnPendingCoinsChanged;
            }

            if (_populationSystem != null)
            {
                _populationSystem.PopulationChanged -=
                    OnPopulationChanged;
            }
        }


        private void OnArrival(ArrivalEvent e)
        {
            _currentCoins += e.Coins;
        }

        private void OnEconomyRegistered(IEconomyService economy)
        {
            BindEconomy(economy);
        }

        private void BindEconomy(IEconomyService economy)
        {
            if (_economy == economy)
            {
                return;
            }

            if (_economy != null)
            {
                _economy.CoinsChanged -= OnCoinsChanged;
            }

            _services.Events.Arrival -= OnArrival;
            _economy = economy;
            _economy.CoinsChanged += OnCoinsChanged;
            OnCoinsChanged(_economy.Coins);
        }

        private void OnGameCalendarRegistered(IGameCalendarService gameCalendar)
        {
            BindGameCalendar(gameCalendar);
        }

        private void BindGameCalendar(IGameCalendarService gameCalendar)
        {
            if (_gameCalendar == gameCalendar)
            {
                return;
            }

            if (_gameCalendar != null)
            {
                _gameCalendar.HourChanged -= OnCalendarChanged;
                _gameCalendar.DayChanged -= OnCalendarChanged;
                _gameCalendar.MonthChanged -= OnCalendarChanged;
            }

            _gameCalendar = gameCalendar;
            _gameCalendar.HourChanged += OnCalendarChanged;
            _gameCalendar.DayChanged += OnCalendarChanged;
            _gameCalendar.MonthChanged += OnCalendarChanged;
            UpdateUI();
        }

        private void OnCalendarChanged(int _)
        {
            UpdateUI();
        }

        private void OnCoinsChanged(long coins)
        {
            _currentCoins = coins;
            UpdateUI();
        }

        private void OnWeeklyEconomyRegistered(IWeeklyEconomyService weeklyEconomy)
        {
            BindWeeklyEconomy(weeklyEconomy);
        }

        private void BindWeeklyEconomy(IWeeklyEconomyService weeklyEconomy)
        {
            if (_weeklyEconomy == weeklyEconomy)
            {
                return;
            }

            if (_weeklyEconomy != null)
            {
                _weeklyEconomy.PendingCoinsChanged -= OnPendingCoinsChanged;
            }

            _weeklyEconomy = weeklyEconomy;
            _weeklyEconomy.PendingCoinsChanged += OnPendingCoinsChanged;
            OnPendingCoinsChanged(_weeklyEconomy.PendingCoins);
        }

        private void OnPendingCoinsChanged(long pendingCoins)
        {
            _pendingCoins = Math.Max(0L, pendingCoins);
            RefreshCoinText();
        }

        private void OnPopulationChanged(int population)
        {
            _lastTargetPopulation = -1;
            UpdateUI();
        }

        private void Start()
        {
            if (normalizeLayoutOnStart)
            {
                NormalizeHeaderLayout();
            }

            UpdateUI(); // UI 씬 단독 테스트 시에도 초기 텍스트 포맷을 잡아주기 위함
        }

        private void NormalizeHeaderLayout()
        {
            ConfigureHeaderText(timeText, new Vector2(16f, -14f), new Vector2(210f, 30f));
            ConfigureHeaderText(vehicleCountText, new Vector2(240f, -14f), new Vector2(220f, 30f));
            ConfigureHeaderText(coinText, new Vector2(480f, -14f), new Vector2(280f, 30f));
        }

        private static void ConfigureHeaderText(TextMeshProUGUI text, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            if (text == null)
            {
                return;
            }

            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;

            text.alignment = TextAlignmentOptions.Left;
            text.fontSize = 18f;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
        }

        private void Update()
        {
            // UI 씬 단독 테스트 시 타이머라도 돌아가게 _services null 체크 제거

            // 0.2초 스로틀링 (가비지 컬렉션 및 리빌드 방지)
            _updateTimer += Time.deltaTime;
            if (_updateTimer >= updateInterval)
            {
                _updateTimer = 0f;
                UpdateUI();
            }
        }

        private void UpdateUI()
        {
            // 1. 시간 표시 (00:00 포맷 및 게임 달력 연동)
            if (timeText != null)
            {
                if (_gameCalendar != null)
                {
                    timeText.text = $"{_gameCalendar.Month:D2}월 {_gameCalendar.Day:D2}일 {_gameCalendar.Hour:D2}:00";
                }
                else
                {
                    timeText.text = "--:--"; // 기본값
                }
            }

            // 2. 계약 계층에서 제공하는 실제 활성 차량 수
            if (vehicleCountText != null)
            {
                int targetVehicleCount = _cityStats?.ActiveVehicleCount ?? 0;
                int targetPopulation =
                    _populationSystem?.CurrentPopulation ?? 0;

                if (targetVehicleCount != _lastTargetVehicles ||
                    targetPopulation != _lastTargetPopulation)
                {
                    _lastTargetVehicles = targetVehicleCount;
                    _lastTargetPopulation = targetPopulation;
                    vehicleCountText.text =
                        $"차량: {targetVehicleCount:N0}대  " +
                        $"인구: {targetPopulation:N0}명";
                }
            }

            // 3. 코인 지갑
            if (coinText != null)
            {
                if (_currentCoins != _lastTargetCoins)
                {
                    _lastTargetCoins = _currentCoins;
                    RefreshCoinText();
                }
            }
        }

        private void RefreshCoinText()
        {
            if (coinText == null)
            {
                return;
            }

            coinText.text =
                $"재화: {_currentCoins:N0}  " +
                $"대기: {_pendingCoins:N0}";
        }
    }
}
