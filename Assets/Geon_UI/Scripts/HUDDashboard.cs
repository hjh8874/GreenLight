using System;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI.Geon
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
        private float _updateTimer;
        
        // Cached state from events
        private long _currentCoins;
        private float _currentStability01 = 1f;

        public void Initialize(CityFlowServices services)
        {
            _services = services;

            // 이벤트 구독 (구독해야 코어 엔진에서 데이터가 날아옵니다)
            _services.Events.StabilityChanged += OnStabilityChanged;
            _services.Events.Arrival += OnArrival;
            _services.Events.FlowBurst += OnFlowBurst;

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

        private void Update()
        {
            if (_services == null) return;

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
                timeText.text = $"{minutes:00}:{seconds:00}";
            }

            // 2. 가짜 차량 수 조립 (On-the-fly 합성 규칙 적용)
            if (vehicleCountText != null)
            {
                // 안정도와 코인을 적절히 섞어 유저가 변화를 느낄 수 있는 가짜 데이터를 만듭니다.
                int mockVehicleCount = (int)(_currentStability01 * 500) + (int)(_currentCoins / 10);
                vehicleCountText.text = mockVehicleCount.ToString("N0"); // 천단위 콤마 표시
            }

            // 3. 코인 지갑
            if (coinText != null)
            {
                coinText.text = _currentCoins.ToString("N0");
            }

            // 4. 효율 (%)
            if (efficiencyText != null)
            {
                int efficiencyPercent = Mathf.RoundToInt(_currentStability01 * 100f);
                efficiencyText.text = $"{efficiencyPercent}%";
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
