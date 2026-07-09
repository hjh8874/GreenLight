using System.Collections;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace CityFlow.UI
{
    public class AnalysisCardController : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("UI Elements")]
        [SerializeField] private TMP_Text txtTitle; // 피그마의 '일반 도로 (Lv.2)' 등 헤더 표시용
        [SerializeField] private TMP_Text txtTileCoord;
        [SerializeField] private TMP_Text txtVehicleId;
        [SerializeField] private TMP_Text txtVehicleType;
        [SerializeField] private TMP_Text txtWaitTime;
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color warningColor = Color.red;

        [Header("Local Congestion Bar")]
        [SerializeField] private Image imgCongestionFill; // 타일 전용 혼잡도 바

        [Header("Footer Buttons")]
        [SerializeField] private Button btnResolveJam;
        [SerializeField] private Button btnUpgrade;

        [Header("Debug / Testing")]
        [SerializeField] private bool useFakeMode = false; // 코어 연동을 위해 끕니다.
        [SerializeField] [Range(0f, 1f)] private float fakeDensity = 0.8f; // 테스트용 80% 혼잡도

        private CityFlowServices _services;
        private Coroutine _updateRoutine;
        private float _currentWaitTime = 0f;
        private Vector2Int _currentTile;
        private bool _isClosing = false;

        public void Configure(
            TMP_Text title,
            TMP_Text tileCoord,
            TMP_Text vehicleId,
            TMP_Text vehicleType,
            TMP_Text waitTime,
            Image congestionFill,
            Button resolveJam,
            Button upgrade,
            bool fakeMode)
        {
            txtTitle = title;
            txtTileCoord = tileCoord;
            txtVehicleId = vehicleId;
            txtVehicleType = vehicleType;
            txtWaitTime = waitTime;
            imgCongestionFill = congestionFill;
            btnResolveJam = resolveJam;
            btnUpgrade = upgrade;
            useFakeMode = fakeMode;
        }

        public void Initialize(CityFlowServices services)
        {
            _services = services;
        }

        private void Start()
        {
            if (btnResolveJam != null) btnResolveJam.onClick.AddListener(OnResolveJamClicked);
            if (btnUpgrade != null) btnUpgrade.onClick.AddListener(OnUpgradeClicked);
        }

        private void OnResolveJamClicked()
        {
            Debug.Log($"[AnalysisCard] {_currentTile} 정체 해소 스킬 발동!");
        }

        private void OnUpgradeClicked()
        {
            Debug.Log($"[AnalysisCard] {_currentTile} 타일 업그레이드 클릭!");
        }

        public void OpenCard(Vector2Int tile)
        {
            if (gameObject.activeSelf && _currentTile == tile && !_isClosing) return; // 이미 열려있으면 무시
            
            _isClosing = false;
            _currentTile = tile;
            gameObject.SetActive(true);

            // DOTween 팝업 애니메이션
            transform.DOKill();
            transform.localScale = Vector3.zero;
            transform.DOScale(Vector3.one, 0.3f).SetEase(Ease.OutBack);
            
            // 1. 타일 좌표 기반 가짜 데이터 조립 (팩토리 패턴 적용) 및 타일 종류(이름) 설정
            SynthesizeFakeVehicleData(tile);

            // 2. 0.2초 스로틀링 루프 시작
            _currentWaitTime = 0f;
            if (_updateRoutine != null) StopCoroutine(_updateRoutine);
            _updateRoutine = StartCoroutine(UpdateCardRoutine());
        }

        public void CloseCard()
        {
            if (_isClosing || !gameObject.activeSelf) return;
            _isClosing = true;

            if (_updateRoutine != null) StopCoroutine(_updateRoutine);
            
            // DOTween 닫기 애니메이션
            transform.DOKill();
            transform.DOScale(Vector3.zero, 0.2f).SetEase(Ease.InBack).OnComplete(() => {
                gameObject.SetActive(false);
            });
        }

        private void SynthesizeFakeVehicleData(Vector2Int tile)
        {
            // 코어 엔진에서 타일 종류(Type) 가져오기
            string tileName = "Unknown";
            if (_services != null && _services.TileData != null)
            {
                TileType type = _services.TileData.GetTileType(tile);
                tileName = type.ToString();
            }
            
            // 피그마 기획 반영: 헤더 텍스트
            if (txtTitle != null) txtTitle.text = $"{tileName} (Lv.1)";

            // 같은 타일을 누르면 항상 같은 가짜 차량이 나오도록 Random Seed 고정
            Random.InitState(tile.x * 1000 + tile.y);
            
            if (txtTileCoord != null) txtTileCoord.text = $"Tile: {tile.x}, {tile.y}";
            
            string[] types = { "Sedan", "SUV", "Truck", "Bus", "Sports Car" };
            if (txtVehicleType != null) txtVehicleType.text = types[Random.Range(0, types.Length)];
            
            int idPrefix = Random.Range(10, 99);
            char idChar = (char)Random.Range(65, 90);
            int idSuffix = Random.Range(1000, 9999);
            if (txtVehicleId != null) txtVehicleId.text = $"{idPrefix}{idChar} {idSuffix}";
        }

        private IEnumerator UpdateCardRoutine()
        {
            WaitForSeconds wait = new WaitForSeconds(0.2f); // 200ms 주기로 강제 스로틀링

            while (true)
            {
                float density = GetTileDensity();

                // 요구사항 수식: += 0.2f * congestion(density)
                _currentWaitTime += 0.2f * density;

                // 타일 전용 로컬 혼잡도 바 갱신
                if (imgCongestionFill != null)
                {
                    imgCongestionFill.fillAmount = density;
                    imgCongestionFill.color = Color.Lerp(normalColor, warningColor, density);
                }

                if (txtWaitTime != null)
                {
                    string timeText = _currentWaitTime.ToString("F1") + "s";
                    
                    // 70% 돌파 시 크리티컬 경고 처리
                    if (density > 0.7f)
                    {
                        txtWaitTime.color = warningColor;
                        txtWaitTime.text = $"{timeText} ! JAM";
                    }
                    else
                    {
                        txtWaitTime.color = normalColor;
                        txtWaitTime.text = timeText;
                    }
                }

                yield return wait;
            }
        }

        private float GetTileDensity()
        {
            if (useFakeMode) return fakeDensity;
            
            if (_services != null && _services.TileData != null)
            {
                // 실제 코어 엔진에서 타일 혼잡도 수신
                return _services.TileData.GetDensity01(_currentTile);
            }
            return 0f;
        }
    }
}
