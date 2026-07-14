using System.Collections;
using System.Linq;
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

        [Header("Containers")]
        [SerializeField] private GameObject normalInfoContainer;
        [SerializeField] private GameObject signalControlContainer;

        [Header("Signal Control Elements")]
        [SerializeField] private Slider sliderOffset;
        [SerializeField] private Slider sliderGreen;
        [SerializeField] private Button btnOverrideH;
        [SerializeField] private Button btnOverrideV;

        [Header("Cooldown Overlay")]
        [SerializeField] private Image imgCooldownH;
        [SerializeField] private Image imgCooldownV;
        [SerializeField] private TMP_Text txtCooldownH;
        [SerializeField] private TMP_Text txtCooldownV;
        [Header("Footer Buttons")]
        [SerializeField] private Button btnResolveJam;
        [SerializeField] private Button btnUpgrade;

        [Header("Debug / Testing")]
        [SerializeField] private bool useFakeMode = false; // 코어 연동을 위해 끕니다.
        [SerializeField] [Range(0f, 1f)] private float fakeDensity = 0.8f; // 테스트용 80% 혼잡도

        private CityFlowServices _services;
        private Coroutine _updateRoutine;
        private Coroutine _signalCooldownRoutine;
        private float _currentWaitTime = 0f;
        private Vector2Int _currentTile;
        private bool _isClosing = false;

        public void Configure(
            TMP_Text title,
            TMP_Text tileCoord,
            TMP_Text vehicleId,
            TMP_Text vehicleType,
            TMP_Text waitTime,
            Button resolveJam,
            Button upgrade,
            bool fakeMode)
        {
            txtTitle = title;
            txtTileCoord = tileCoord;
            txtVehicleId = vehicleId;
            txtVehicleType = vehicleType;
            txtWaitTime = waitTime;

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
            
            if (sliderOffset != null) sliderOffset.onValueChanged.AddListener(OnOffsetChanged);
            if (sliderGreen != null) sliderGreen.onValueChanged.AddListener(OnGreenChanged);
            if (btnOverrideH != null) btnOverrideH.onClick.AddListener(() => OnOverrideClicked(true));
            if (btnOverrideV != null) btnOverrideV.onClick.AddListener(() => OnOverrideClicked(false));
        }

        private void OnOffsetChanged(float value)
        {
            var signalControl = _services?.Placement as ISignalControl;
            signalControl?.TrySetSignalOffsetSlots(_currentTile, (int)value);
        }

        private void OnGreenChanged(float value)
        {
            var signalControl = _services?.Placement as ISignalControl;
            signalControl?.TrySetSignalGreenSlots(_currentTile, (int)value);
        }

        private void OnOverrideClicked(bool horizontal)
        {
            var signalControl = _services?.Placement as ISignalControl;
            signalControl?.TryOverrideSignal(_currentTile, horizontal);
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
            
            // 모드 전환 전 기존 갱신 코루틴 정리 (신호 제어 타일로 넘어갈 때의 누수 방지)
            if (_updateRoutine != null)
            {
                StopCoroutine(_updateRoutine);
                _updateRoutine = null;
            }
            if (_signalCooldownRoutine != null)
            {
                StopCoroutine(_signalCooldownRoutine);
                _signalCooldownRoutine = null;
            }
            
            var signalControl = _services?.Placement as ISignalControl;
            if (signalControl != null && signalControl.SignalTiles.Contains(tile))
            {
                // 신호 제어 모드
                if (normalInfoContainer != null) normalInfoContainer.SetActive(false);
                if (signalControlContainer != null) signalControlContainer.SetActive(true);
                
                if (txtTitle != null) txtTitle.text = "교차로 신호 제어";
                
                int cycle = signalControl.GetSignalCycleSlots(tile);
                if (sliderOffset != null)
                {
                    sliderOffset.maxValue = Mathf.Max(0, cycle - 1);
                    sliderOffset.SetValueWithoutNotify(signalControl.GetSignalOffsetSlots(tile));
                }
                if (sliderGreen != null)
                {
                    sliderGreen.minValue = 1;
                    sliderGreen.maxValue = Mathf.Max(1, cycle - 1);
                    sliderGreen.SetValueWithoutNotify(signalControl.GetSignalGreenSlots(tile));
                }

                // 쿨다운 상태 즉시 반영 후 갱신 코루틴 시작
                ApplyCooldownVisuals(signalControl);
                _signalCooldownRoutine = StartCoroutine(UpdateSignalCooldownRoutine());
            }
            else
            {
                // 일반 정보 모드
                if (normalInfoContainer != null) normalInfoContainer.SetActive(true);
                if (signalControlContainer != null) signalControlContainer.SetActive(false);

                // 1. 타일 좌표 기반 가짜 데이터 조립 (팩토리 패턴 적용) 및 타일 종류(이름) 설정
                SynthesizeFakeVehicleData(tile);

                // 2. 0.2초 스로틀링 루프 시작
                _currentWaitTime = 0f;
                if (_updateRoutine != null) StopCoroutine(_updateRoutine);
                _updateRoutine = StartCoroutine(UpdateCardRoutine());
            }
        }

        public void CloseCard()
        {
            if (_isClosing || !gameObject.activeSelf) return;
            _isClosing = true;

            if (_updateRoutine != null) { StopCoroutine(_updateRoutine); _updateRoutine = null; }
            if (_signalCooldownRoutine != null) { StopCoroutine(_signalCooldownRoutine); _signalCooldownRoutine = null; }
            
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
            
            if (txtTileCoord != null) txtTileCoord.text = $"타일: {tile.x}, {tile.y}";
            
            string[] types = { "세단", "SUV", "트럭", "버스", "스포츠카" };
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



                if (txtWaitTime != null)
                {
                    string timeText = _currentWaitTime.ToString("F1") + "초";
                    
                    // 70% 돌파 시 크리티컬 경고 처리
                    if (density > 0.7f)
                    {
                        txtWaitTime.color = warningColor;
                        txtWaitTime.text = $"{timeText} ! 정체";
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

        // ─── 신호 제어 쿨다운 애니메이션 ───────────────────────────
        private IEnumerator UpdateSignalCooldownRoutine()
        {
            WaitForSeconds wait = new WaitForSeconds(0.2f); // 200ms 주기 스로틀링

            while (true)
            {
                var signalControl = _services?.Placement as ISignalControl;
                if (signalControl != null)
                {
                    ApplyCooldownVisuals(signalControl);
                }
                yield return wait;
            }
        }

        private void ApplyCooldownVisuals(ISignalControl signalControl)
        {
            float cooldownLeft = signalControl.GetOverrideCooldownLeft(_currentTile);
            bool onCooldown = cooldownLeft > 0f;

            // SimConfig 기본값 60초 기준 비율 계산
            float totalCooldown = 60f;
            float fillRatio = onCooldown ? Mathf.Clamp01(cooldownLeft / totalCooldown) : 0f;
            string timeLabel = onCooldown ? Mathf.CeilToInt(cooldownLeft) + "초" : "";

            // 가로 오버라이드 버튼
            if (btnOverrideH != null) btnOverrideH.interactable = !onCooldown;
            if (imgCooldownH != null)
            {
                imgCooldownH.gameObject.SetActive(onCooldown);
                imgCooldownH.fillAmount = fillRatio;
            }
            if (txtCooldownH != null)
            {
                txtCooldownH.gameObject.SetActive(onCooldown);
                txtCooldownH.text = timeLabel;
            }

            // 세로 오버라이드 버튼
            if (btnOverrideV != null) btnOverrideV.interactable = !onCooldown;
            if (imgCooldownV != null)
            {
                imgCooldownV.gameObject.SetActive(onCooldown);
                imgCooldownV.fillAmount = fillRatio;
            }
            if (txtCooldownV != null)
            {
                txtCooldownV.gameObject.SetActive(onCooldown);
                txtCooldownV.text = timeLabel;
            }
        }
    }
}
