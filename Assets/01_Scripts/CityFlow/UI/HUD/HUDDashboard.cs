using System;
using CityFlow.Bootstrap;
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
        [SerializeField] private TextMeshProUGUI efficiencyText;
        [Header("Congestion UI")]
        [Tooltip("UI Slider를 사용하는 경우 연결")]
        [SerializeField] private Slider congestionSlider;
        [Tooltip("단일 Image(Filled 모드)를 사용하는 경우 연결 (색상 변경 포함)")]
        [SerializeField] private Image congestionImageFill;
        [SerializeField] private Color colorSmooth = Color.green;
        [SerializeField] private Color colorJam = Color.red;
        [SerializeField] private GameObject flowBurstEffect;

        [Header("Settings")]
        [SerializeField] private float updateInterval = 0.2f;

        private CityFlowServices _services;
        private IEconomyService _economy;
        private float _updateTimer;
        
        // Cached state from events
        private long _currentCoins;
        private float _currentStability01 = 1f;

        // Display state for DOTween
        private float _displayedCoins;
        private float _displayedVehicles;

        public void Configure(
            TextMeshProUGUI time,
            TextMeshProUGUI vehicleCount,
            TextMeshProUGUI coin,
            TextMeshProUGUI efficiency,
            Slider congestion,
            Image congestionFill,
            GameObject burstEffect)
        {
            timeText = time;
            vehicleCountText = vehicleCount;
            coinText = coin;
            efficiencyText = efficiency;
            congestionSlider = congestion;
            congestionImageFill = congestionFill;
            flowBurstEffect = burstEffect;
        }

        public void Initialize(CityFlowServices services)
        {
            _services = services;

            // 이벤트 구독 (구독해야 코어 엔진에서 데이터가 날아옵니다)
            _services.Events.StabilityChanged += OnStabilityChanged;
            _services.Events.FlowBurst += OnFlowBurst;
            _services.EconomyRegistered += OnEconomyRegistered;

            if (_services.Economy != null)
            {
                BindEconomy(_services.Economy);
            }
            else
            {
                _services.Events.Arrival += OnArrival;
            }

            // 초기 UI 갱신
            UpdateUI();
        }

        private void OnDestroy()
        {
            if (_services != null && _services.Events != null)
            {
                // 메모리 누수 방지 이벤트 해제
                _services.Events.StabilityChanged -= OnStabilityChanged;
                _services.Events.Arrival -= OnArrival;
                _services.Events.FlowBurst -= OnFlowBurst;
                _services.EconomyRegistered -= OnEconomyRegistered;
            }

            if (_economy != null)
            {
                _economy.CoinsChanged -= OnCoinsChanged;
            }
        }

        private void OnStabilityChanged(StabilityEvent e)
        {
            _currentStability01 = e.Stability01;
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

        private void OnCoinsChanged(long coins)
        {
            _currentCoins = coins;
            UpdateUI();
        }

        private void OnFlowBurst(FlowBurstEvent e)
        {
            if (flowBurstEffect != null)
            {
                // Flow Burst 발동 시 이펙트를 켜줍니다 (1초 뒤 자동 꺼짐)
                flowBurstEffect.SetActive(true);
                Invoke(nameof(TurnOffBurstEffect), 1.0f);
            }
        }

        private void TurnOffBurstEffect()
        {
            if (flowBurstEffect != null)
                flowBurstEffect.SetActive(false);
        }

        private void Start()
        {
            UpdateUI(); // UI 씬 단독 테스트 시에도 초기 텍스트 포맷을 잡아주기 위함
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
            // 1. 시간 표시 (00:00 포맷)
            if (timeText != null)
            {
                int minutes = Mathf.FloorToInt(Time.time / 60f);
                int seconds = Mathf.FloorToInt(Time.time % 60f);
                timeText.text = $"[Time] {minutes:00}:{seconds:00}";
            }

            // 2. 가짜 차량 수 조립 (On-the-fly 합성 규칙 적용)
            if (vehicleCountText != null)
            {
                // 안정도와 코인을 적절히 섞어 유저가 변화를 느낄 수 있는 가짜 데이터를 만듭니다.
                int targetVehicleCount = (int)(_currentStability01 * 500) + (int)(_currentCoins / 10);
                
                vehicleCountText.DOKill();
                DOTween.To(() => _displayedVehicles, x => 
                {
                    _displayedVehicles = x;
                    vehicleCountText.text = $"[Cars] {Mathf.RoundToInt(_displayedVehicles):N0}";
                }, targetVehicleCount, updateInterval).SetEase(Ease.Linear).SetTarget(vehicleCountText);
            }

            // 3. 코인 지갑
            if (coinText != null)
            {
                coinText.DOKill();
                DOTween.To(() => _displayedCoins, x => 
                {
                    _displayedCoins = x;
                    coinText.text = $"[Coins] {Mathf.RoundToInt(_displayedCoins):N0}";
                }, _currentCoins, updateInterval).SetEase(Ease.Linear).SetTarget(coinText);
            }

            // 4. 효율 (%)
            if (efficiencyText != null)
            {
                int efficiencyPercent = Mathf.RoundToInt(_currentStability01 * 100f);
                efficiencyText.text = $"[Eff] {efficiencyPercent}%";
            }

            // 5. 혼잡도 바 (혼잡도는 1 - 효율 로 계산)
            float congestion = 1f - _currentStability01;
            
            // Slider 방식 갱신
            if (congestionSlider != null) congestionSlider.value = congestion;
            
            // Image(Fill Amount) 방식 갱신 및 색상 보간 (녹색 -> 붉은색)
            if (congestionImageFill != null)
            {
                congestionImageFill.fillAmount = congestion;
                congestionImageFill.color = Color.Lerp(colorSmooth, colorJam, congestion);
            }
        }
    }
}
