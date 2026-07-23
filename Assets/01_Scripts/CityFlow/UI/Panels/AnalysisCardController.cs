using System.Collections;
using System.Linq;
using CityFlow.Bootstrap;
using CityFlow.Content;
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
        private PopulationSystem _populationSystem;
        private PlacementController _placementController;

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
            _populationSystem =
                FindAnyObjectByType<PopulationSystem>();
            _placementController =
                FindAnyObjectByType<PlacementController>();
        }

        private void Start()
        {
            NormalizeSignalControlLayout();

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

            NormalizeSignalControlLayout();
            
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

        private void NormalizeSignalControlLayout()
        {
            RectTransform cardRect = transform as RectTransform;
            if (cardRect != null)
            {
                cardRect.anchorMin = Vector2.zero;
                cardRect.anchorMax = Vector2.zero;
                cardRect.pivot = Vector2.zero;
                cardRect.anchoredPosition = new Vector2(20f, 20f);
                cardRect.sizeDelta = new Vector2(460f, 280f);
            }

            if (txtTitle != null)
            {
                RectTransform titleRect = txtTitle.rectTransform;
                titleRect.anchorMin = new Vector2(0.5f, 1f);
                titleRect.anchorMax = new Vector2(0.5f, 1f);
                titleRect.pivot = new Vector2(0.5f, 1f);
                titleRect.anchoredPosition = new Vector2(0f, -14f);
                titleRect.sizeDelta = new Vector2(420f, 36f);
                txtTitle.fontSize = 22f;
                txtTitle.alignment = TextAlignmentOptions.TopLeft;
                txtTitle.textWrappingMode = TextWrappingModes.NoWrap;
            }

            if (signalControlContainer == null)
            {
                return;
            }

            RectTransform containerRect = signalControlContainer.transform as RectTransform;
            if (containerRect != null)
            {
                containerRect.anchorMin = Vector2.zero;
                containerRect.anchorMax = Vector2.one;
                containerRect.anchoredPosition = Vector2.zero;
                containerRect.sizeDelta = Vector2.zero;
            }

            NormalizeSignalLabel("LblOffset", new Vector2(-150f, 48f));
            NormalizeSignalLabel("LblGreen", new Vector2(-150f, -8f));
            NormalizeSlider(sliderOffset, new Vector2(35f, 48f));
            NormalizeSlider(sliderGreen, new Vector2(35f, -8f));
            NormalizeSignalButton(btnOverrideH, new Vector2(-65f, -82f));
            NormalizeSignalButton(btnOverrideV, new Vector2(65f, -82f));
        }

        private void NormalizeSignalLabel(string childName, Vector2 position)
        {
            Transform labelTransform = signalControlContainer.transform.Find(childName);
            if (labelTransform == null)
            {
                return;
            }

            RectTransform labelRect = labelTransform as RectTransform;
            if (labelRect != null)
            {
                labelRect.anchorMin = new Vector2(0.5f, 0.5f);
                labelRect.anchorMax = new Vector2(0.5f, 0.5f);
                labelRect.pivot = new Vector2(0.5f, 0.5f);
                labelRect.anchoredPosition = position;
                labelRect.sizeDelta = new Vector2(110f, 24f);
            }

            TMP_Text label = labelTransform.GetComponent<TMP_Text>();
            if (label != null)
            {
                label.fontSize = 16f;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.textWrappingMode = TextWrappingModes.NoWrap;
            }
        }

        private static void NormalizeSlider(Slider slider, Vector2 position)
        {
            if (slider == null)
            {
                return;
            }

            RectTransform sliderRect = slider.transform as RectTransform;
            if (sliderRect != null)
            {
                sliderRect.anchorMin = new Vector2(0.5f, 0.5f);
                sliderRect.anchorMax = new Vector2(0.5f, 0.5f);
                sliderRect.pivot = new Vector2(0.5f, 0.5f);
                sliderRect.anchoredPosition = position;
                sliderRect.sizeDelta = new Vector2(220f, 20f);
            }

            RectTransform background = slider.transform.Find("Background") as RectTransform;
            if (background != null)
            {
                background.anchorMin = new Vector2(0f, 0.3f);
                background.anchorMax = new Vector2(1f, 0.7f);
                background.offsetMin = Vector2.zero;
                background.offsetMax = Vector2.zero;
            }

            RectTransform fillArea = slider.transform.Find("Fill Area") as RectTransform;
            if (fillArea != null)
            {
                fillArea.anchorMin = new Vector2(0f, 0.25f);
                fillArea.anchorMax = new Vector2(1f, 0.75f);
                fillArea.offsetMin = new Vector2(10f, 0f);
                fillArea.offsetMax = new Vector2(-10f, 0f);
            }

            RectTransform handleArea = slider.transform.Find("Handle Slide Area") as RectTransform;
            if (handleArea != null)
            {
                handleArea.anchorMin = Vector2.zero;
                handleArea.anchorMax = Vector2.one;
                handleArea.offsetMin = new Vector2(10f, 0f);
                handleArea.offsetMax = new Vector2(-10f, 0f);
            }
        }

        private static void NormalizeSignalButton(Button button, Vector2 position)
        {
            if (button == null)
            {
                return;
            }

            RectTransform buttonRect = button.transform as RectTransform;
            if (buttonRect != null)
            {
                buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
                buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
                buttonRect.pivot = new Vector2(0.5f, 0.5f);
                buttonRect.anchoredPosition = position;
                buttonRect.sizeDelta = new Vector2(110f, 40f);
            }

            Transform labelTransform = button.transform.Find("Text");
            TMP_Text label = labelTransform != null ? labelTransform.GetComponent<TMP_Text>() : null;
            if (label != null)
            {
                label.fontSize = 16f;
                label.alignment = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.NoWrap;
            }

            Transform cooldownTextTransform = button.transform.Find("CooldownOverlay/CooldownText");
            TMP_Text cooldownText = cooldownTextTransform != null
                ? cooldownTextTransform.GetComponent<TMP_Text>()
                : null;
            if (cooldownText != null)
            {
                cooldownText.fontSize = 16f;
                cooldownText.alignment = TextAlignmentOptions.Center;
                cooldownText.textWrappingMode = TextWrappingModes.NoWrap;
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
            TileType type = TileType.Empty;
            if (_services != null && _services.TileData != null)
            {
                type = _services.TileData.GetTileType(tile);
                tileName = type.ToString();
            }
            
            // 피그마 기획 반영: 헤더 텍스트
            if (txtTitle != null) txtTitle.text = $"{tileName} (Lv.1)";

            // 같은 타일을 누르면 항상 같은 가짜 차량이 나오도록 Random Seed 고정
            Random.InitState(tile.x * 1000 + tile.y);
            
            if (txtTileCoord != null) txtTileCoord.text = $"타일: {tile.x}, {tile.y}";

            if (TryRefreshBuildingDetails(type))
            {
                return;
            }
            
            string[] types = { "세단", "SUV", "트럭", "버스", "스포츠카" };
            if (txtVehicleType != null) txtVehicleType.text = types[Random.Range(0, types.Length)];
            
            int idPrefix = Random.Range(10, 99);
            char idChar = (char)Random.Range(65, 90);
            int idSuffix = Random.Range(1000, 9999);
            if (txtVehicleId != null) txtVehicleId.text = $"{idPrefix}{idChar} {idSuffix}";
        }

        private bool TryRefreshBuildingDetails(TileType type)
        {
            switch (type)
            {
                case TileType.House:
                {
                    int population = 0;
                    _populationSystem?.TryGetTilePopulation(
                        _currentTile,
                        out population
                    );

                    int basePopulation =
                        _populationSystem?.PopulationConfig
                            ?.GetPopulationValue(TileType.House) ?? 0;

                    if (txtTitle != null)
                    {
                        txtTitle.text = "주거지";
                    }
                    if (txtVehicleType != null)
                    {
                        txtVehicleType.text = "거주 인구";
                    }
                    if (txtVehicleId != null)
                    {
                        txtVehicleId.text = $"{population:N0}명";
                    }
                    if (txtWaitTime != null)
                    {
                        txtWaitTime.color = normalColor;
                        txtWaitTime.text = population > basePopulation
                            ? "학교 혜택 적용"
                            : "기본 인구";
                    }

                    return true;
                }

                case TileType.Office:
                {
                    CompanyStaffing staffing = default;
                    bool hasStaffing =
                        _services?.Stats != null &&
                        _services.Stats.TryGetCompanyStaffing(
                            _currentTile,
                            out staffing
                        );

                    if (txtTitle != null)
                    {
                        txtTitle.text = "회사";
                    }
                    if (txtVehicleType != null)
                    {
                        txtVehicleType.text = "구인 현황";
                    }
                    if (txtVehicleId != null)
                    {
                        txtVehicleId.text = hasStaffing
                            ? $"{staffing.Filled:N0}/{staffing.Capacity:N0}명"
                            : "정보 없음";
                    }
                    if (txtWaitTime != null)
                    {
                        txtWaitTime.color = normalColor;
                        txtWaitTime.text =
                            hasStaffing &&
                            staffing.Filled >= staffing.Capacity
                                ? "구인 완료"
                                : "구인 중";
                    }

                    return true;
                }

                case TileType.School:
                {
                    PopulationConfigSO config =
                        _placementController?.PopulationConfig;

                    if (txtTitle != null)
                    {
                        txtTitle.text = "학교";
                    }
                    if (txtVehicleType != null)
                    {
                        txtVehicleType.text = "교육 영향";
                    }
                    if (txtVehicleId != null)
                    {
                        txtVehicleId.text = config != null
                            ? $"반경 {config.SchoolCoverageRadius}칸"
                            : "설정 없음";
                    }
                    if (txtWaitTime != null)
                    {
                        txtWaitTime.color = normalColor;
                        txtWaitTime.text = config != null
                            ? $"주거지 +{config.SchoolCoveragePopulationBonus}명"
                            : string.Empty;
                    }

                    return true;
                }

                case TileType.Hospital:
                {
                    BuildingDefinitionSO definition =
                        _placementController?.HospitalDefinition;

                    if (txtTitle != null)
                    {
                        txtTitle.text = "병원";
                    }
                    if (txtVehicleType != null)
                    {
                        txtVehicleType.text = "의료 영향";
                    }
                    if (txtVehicleId != null)
                    {
                        txtVehicleId.text = definition != null
                            ? $"반경 {definition.HospitalCoverageRadius}칸"
                            : "설정 없음";
                    }
                    if (txtWaitTime != null)
                    {
                        txtWaitTime.color = normalColor;
                        txtWaitTime.text = definition != null
                            ? $"최대 {definition.HospitalPatientCapacity}채"
                            : string.Empty;
                    }

                    return true;
                }

                default:
                    return false;
            }
        }

        private IEnumerator UpdateCardRoutine()
        {
            WaitForSeconds wait = new WaitForSeconds(0.2f); // 200ms 주기로 강제 스로틀링

            while (true)
            {
                TileType currentType =
                    _services?.TileData?.GetTileType(
                        _currentTile
                    ) ?? TileType.Empty;

                if (TryRefreshBuildingDetails(currentType))
                {
                    yield return wait;
                    continue;
                }

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

            // SimConfig 기준 전체 쿨다운을 인터페이스에서 동적으로 가져옴
            float totalCooldown = signalControl.GetTotalOverrideCooldown();
            float fillRatio = onCooldown ? Mathf.Clamp01(cooldownLeft / totalCooldown) : 0f;
            string timeLabel = onCooldown ? Mathf.CeilToInt(cooldownLeft) + "초" : "";

            // 가로 오버라이드 버튼
            if (btnOverrideH != null) btnOverrideH.interactable = !onCooldown;
            SetButtonLabelVisible(btnOverrideH, !onCooldown);
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
            SetButtonLabelVisible(btnOverrideV, !onCooldown);
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

        private static void SetButtonLabelVisible(Button button, bool isVisible)
        {
            if (button == null)
            {
                return;
            }

            Transform label = button.transform.Find("Text");
            if (label != null)
            {
                label.gameObject.SetActive(isVisible);
            }
        }
    }
}
