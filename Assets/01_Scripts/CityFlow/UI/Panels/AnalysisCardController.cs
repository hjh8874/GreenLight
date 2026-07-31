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
        [SerializeField] private SignalControlPanelView signalControlPanel;

        private SignalControlPanelView SafeSignalPanel
        {
            get
            {
                if (signalControlPanel == null)
                {
                    signalControlPanel = GetComponentInChildren<SignalControlPanelView>(true);
                }
                return signalControlPanel;
            }
        }

        [Header("Footer Buttons")]
        [SerializeField] private Button btnResolveJam;
        [SerializeField] private Button btnUpgrade;

        [Header("Building Info Card")]
        [Tooltip("건물 타일 클릭 시 표시되는 방치형 건물 정보 카드 (없으면 기존 도로 카드로 폴백)")]
        [SerializeField] private BuildingInfoCardController buildingInfoCard;

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

        // ── Minimap 내부 상태 ──
        private Camera _minimapCamera;
        private RenderTexture _minimapRT;
        private int _minimapFrameCounter;

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
            if (signalControlPanel == null)
            {
                signalControlPanel = GetComponentInChildren<SignalControlPanelView>(true);
            }

            // 호환성 유지: 기존 씬에서 Inspector 수동 조립을 요구하지 않도록,
            // 씬에 신호 제어 패널 프리팹이 없다면 Resources 폴더에서 동적 로드하여 자동 연동합니다.
            if (signalControlPanel == null && signalControlContainer != null)
            {
                var prefab = Resources.Load<GameObject>("CityFlow/UI/UI_SignalControlPanel");
                if (prefab != null)
                {
                    var instance = Instantiate(prefab, signalControlContainer.transform);
                    signalControlPanel = instance.GetComponent<SignalControlPanelView>();
                    Debug.Log($"[AnalysisCardController] UI_SignalControlPanel auto-loaded from Resources in scene {gameObject.scene.name}");
                }
            }

            if (btnResolveJam != null) btnResolveJam.onClick.AddListener(OnResolveJamClicked);
            if (btnUpgrade != null) btnUpgrade.onClick.AddListener(OnUpgradeClicked);

            if (SafeSignalPanel != null)
            {
                if (SafeSignalPanel.sliderOffset != null) SafeSignalPanel.sliderOffset.onValueChanged.AddListener(OnOffsetChanged);
                if (SafeSignalPanel.sliderGreen != null) SafeSignalPanel.sliderGreen.onValueChanged.AddListener(OnGreenChanged);
                if (SafeSignalPanel.btnOverrideH != null) SafeSignalPanel.btnOverrideH.onClick.AddListener(() => OnOverrideClicked(true));
                if (SafeSignalPanel.btnOverrideV != null) SafeSignalPanel.btnOverrideV.onClick.AddListener(() => OnOverrideClicked(false));
            }
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
                if (SafeSignalPanel == null)
                {
                    Debug.LogWarning("[CityFlow] SignalControlPanelView not found! Please run 'CityFlow/UI/Assemble Signal Control UI'.");
                    return;
                }

                // 신호 제어 모드
                if (normalInfoContainer != null) normalInfoContainer.SetActive(false);
                if (signalControlContainer != null) signalControlContainer.SetActive(true);

                if (txtTitle != null) txtTitle.text = "교차로 신호 제어";

                // 미니맵 카메라를 교차로 위치로 이동 및 활성화
                PositionMinimapCamera(tile);

                int cycle = signalControl.GetSignalCycleSlots(tile);
                if (SafeSignalPanel.sliderOffset != null)
                {
                    SafeSignalPanel.sliderOffset.maxValue = Mathf.Max(0, cycle - 1);
                    SafeSignalPanel.sliderOffset.SetValueWithoutNotify(signalControl.GetSignalOffsetSlots(tile));
                }
                if (SafeSignalPanel.sliderGreen != null)
                {
                    SafeSignalPanel.sliderGreen.minValue = 1;
                    SafeSignalPanel.sliderGreen.maxValue = Mathf.Max(1, cycle - 1);
                    SafeSignalPanel.sliderGreen.SetValueWithoutNotify(signalControl.GetSignalGreenSlots(tile));
                }

                // 쿨다운 상태 즉시 반영 후 갱신 코루틴 시작
                ApplyCooldownVisuals(signalControl);
                _signalCooldownRoutine = StartCoroutine(UpdateSignalCooldownRoutine());
            }
            else
            {
                // 일반 정보 모드 (도로 타일)
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

            // 미니맵 카메라 비활성화 (메모리 유지, 렌더링만 중단)
            if (_minimapCamera != null)
            {
                _minimapCamera.enabled = false;
                _minimapCamera.gameObject.SetActive(false);
            }

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
                        // 유형(사무실/공장/물류창고)과 출근창까지 — "회사"만으로는 3종이 구분이 안 된다
                        txtTitle.text = DescribeCompanyType(_currentTile);
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
                        string status =
                            hasStaffing &&
                            staffing.Filled >= staffing.Capacity
                                ? "구인 완료"
                                : "구인 중";
                        txtWaitTime.text = status + DescribeCommuterHomes(_currentTile);
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
            WaitForSeconds wait = new WaitForSeconds(0.05f); // 20fps for smooth gauge animation

            while (true)
            {
                var signalControl = _services?.Placement as ISignalControl;
                if (signalControl != null)
                {
                    ApplyCooldownVisuals(signalControl);
                    UpdateRealtimeGauge(signalControl);
                    UpdateRealtimeWaitCounts();
                }

                // 미니맵: 카메라 enabled=true로 URP가 자동 렌더 (Camera.Render()는 SRP 비호환)
                // 별도 수동 렌더 호출 불필요 — URP 파이프라인이 매 프레임 처리

                yield return wait;
            }
        }

        private void UpdateRealtimeGauge(ISignalControl signalControl)
        {
            if (SafeSignalPanel == null || SafeSignalPanel.cycleGaugeCursor == null) return;

            // 1. 커서 위치 (진행도) 업데이트 — 오버라이드 중이면 커서 숨김
            float fillRatio = signalControl.GetCurrentCycleProgress(_currentTile);
            if (fillRatio < 0f)
            {
                // 오버라이드(양축 강제 초록) 중 — 커서를 숨겨 혼란 방지
                SafeSignalPanel.cycleGaugeCursor.gameObject.SetActive(false);
                return;
            }
            SafeSignalPanel.cycleGaugeCursor.gameObject.SetActive(true);
            SafeSignalPanel.cycleGaugeCursor.anchorMin = new Vector2(fillRatio, 0f);
            SafeSignalPanel.cycleGaugeCursor.anchorMax = new Vector2(fillRatio, 1f);

            int cycleSlots = signalControl.GetSignalCycleSlots(_currentTile);
            if (cycleSlots <= 0) return;

            // SignalMath 단일 진실원에서 파생된 값 사용 — 하드코딩 금지
            float slotSec = signalControl.GetSlotSeconds();
            float yellowFrac = signalControl.GetYellowFraction();
            float clearFrac = signalControl.GetClearFraction();
            float greenFrac = 1f - yellowFrac - clearFrac;

            float cycle = cycleSlots * slotSec;
            int greenSlots = signalControl.GetSignalGreenSlots(_currentTile);

            // 게이지 세그먼트 폭 비율(FlexibleWidth) 업데이트
            if (SafeSignalPanel.leHG != null && SafeSignalPanel.leHY != null && SafeSignalPanel.leHC != null &&
                SafeSignalPanel.leVG != null && SafeSignalPanel.leVY != null && SafeSignalPanel.leVC != null)
            {
                float hSpan = greenSlots * slotSec;
                float vSpan = cycle - hSpan;

                SafeSignalPanel.leHG.flexibleWidth = hSpan * greenFrac;
                SafeSignalPanel.leHY.flexibleWidth = hSpan * yellowFrac;
                SafeSignalPanel.leHC.flexibleWidth = hSpan * clearFrac;
                SafeSignalPanel.leVG.flexibleWidth = vSpan * greenFrac;
                SafeSignalPanel.leVY.flexibleWidth = vSpan * yellowFrac;
                SafeSignalPanel.leVC.flexibleWidth = vSpan * clearFrac;
            }
        }

        private void UpdateRealtimeWaitCounts()
        {
            if (_services?.TileData == null || _minimapCamera == null) return;
            if (SafeSignalPanel == null) return;

            UpdateWaitText(SafeSignalPanel.txtWaitN, Dir.N);
            UpdateWaitText(SafeSignalPanel.txtWaitS, Dir.S);
            UpdateWaitText(SafeSignalPanel.txtWaitE, Dir.E);
            UpdateWaitText(SafeSignalPanel.txtWaitW, Dir.W);
        }

        private void UpdateWaitText(TMP_Text txtWait, Dir dir)
        {
            if (txtWait == null || txtWait.transform.parent == null) return;

            GameObject wrapperObj = txtWait.transform.parent.gameObject;
            RectTransform wrapperRT = wrapperObj.GetComponent<RectTransform>();

            // 텍스트 내용 업데이트
            int count = _services.TileData.GetQueueCount(_currentTile, dir);
            txtWait.text = count.ToString();

            // 대기 차량이 없으면 오버레이 숨기기 (옵션)
            if (count == 0)
            {
                wrapperObj.SetActive(false);
                return;
            }

            // --- AR Overlay (3D World to 2D UI) 추적 로직 ---
            Vector3 worldPos = GetRoadWorldPos(_currentTile, dir);
            Vector3 viewportPos = _minimapCamera.WorldToViewportPoint(worldPos);

            // 카메라 뒤에 있거나 시야 바깥이면 숨김
            if (viewportPos.z < 0 || viewportPos.x < -0.2f || viewportPos.x > 1.2f || viewportPos.y < -0.2f || viewportPos.y > 1.2f)
            {
                wrapperObj.SetActive(false);
                return;
            }

            wrapperObj.SetActive(true);

            // RawImage를 꽉 채우고 있는 RectTransform 내부에서의 Viewport(0~1) 비율을 Anchor로 설정하여 완벽히 추적
            wrapperRT.anchorMin = new Vector2(viewportPos.x, viewportPos.y);
            wrapperRT.anchorMax = new Vector2(viewportPos.x, viewportPos.y);
            wrapperRT.anchoredPosition = Vector2.zero;
        }

        private Vector3 GetRoadWorldPos(Vector2Int tile, Dir dir)
        {
            // 각 도로 끝 지점의 로컬 좌표 오프셋 (기본값: SafeSignalPanel.arRoadOffset = 0.4f)
            float dx = 0f, dy = 0f;
            float offset = SafeSignalPanel != null ? SafeSignalPanel.arRoadOffset : 0.4f;
            switch (dir)
            {
                case Dir.N: dy = offset; break;
                case Dir.S: dy = -offset; break;
                case Dir.E: dx = offset; break;
                case Dir.W: dx = -offset; break;
            }

            if (_services?.WorldCoordinates != null)
            {
                // 월드 좌표계 서비스가 존재하면 정확한 3D 투영 좌표 사용
                return _services.WorldCoordinates.GridPointToWorld(
                    new Vector2(tile.x + 0.5f + dx, tile.y + 0.5f + dy), 0f);
            }
            else
            {
                // Fallback: GridUtil 사용 (Z=0 평면)
                return GridUtil.GridToWorld(tile) + new Vector3(dx, dy, 0f);
            }
        }

        private void ApplyCooldownVisuals(ISignalControl signalControl)
        {
            if (SafeSignalPanel == null) return;

            float cooldownLeft = signalControl.GetOverrideCooldownLeft(_currentTile);
            bool onCooldown = cooldownLeft > 0f;

            // SimConfig 기준 전체 쿨다운을 인터페이스에서 동적으로 가져옴
            float totalCooldown = signalControl.GetTotalOverrideCooldown();
            float fillRatio = onCooldown ? Mathf.Clamp01(cooldownLeft / totalCooldown) : 0f;
            string timeLabel = onCooldown ? Mathf.CeilToInt(cooldownLeft) + "초" : "";

            // 가로 오버라이드 버튼
            if (SafeSignalPanel.btnOverrideH != null) SafeSignalPanel.btnOverrideH.interactable = !onCooldown;
            SetButtonLabelVisible(SafeSignalPanel.btnOverrideH, !onCooldown);
            if (SafeSignalPanel.imgCooldownH != null)
            {
                SafeSignalPanel.imgCooldownH.gameObject.SetActive(onCooldown);
                SafeSignalPanel.imgCooldownH.fillAmount = fillRatio;
            }
            if (SafeSignalPanel.txtCooldownH != null)
            {
                SafeSignalPanel.txtCooldownH.gameObject.SetActive(onCooldown);
                SafeSignalPanel.txtCooldownH.text = timeLabel;
            }

            // 세로 오버라이드 버튼
            if (SafeSignalPanel.btnOverrideV != null) SafeSignalPanel.btnOverrideV.interactable = !onCooldown;
            SetButtonLabelVisible(SafeSignalPanel.btnOverrideV, !onCooldown);
            if (SafeSignalPanel.imgCooldownV != null)
            {
                SafeSignalPanel.imgCooldownV.gameObject.SetActive(onCooldown);
                SafeSignalPanel.imgCooldownV.fillAmount = fillRatio;
            }
            if (SafeSignalPanel.txtCooldownV != null)
            {
                SafeSignalPanel.txtCooldownV.gameObject.SetActive(onCooldown);
                SafeSignalPanel.txtCooldownV.text = timeLabel;
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

        // ── 회사 카드 상세 (회사 3종) ──────────────────────────────────────

        private CompanyTypeCatalogSO _companyCatalog;
        private bool _companyCatalogLoaded;

        // "공장 (20~24시 출근)" — 유형 미지정·정보 없음이면 "회사"
        private string DescribeCompanyType(Vector2Int tile)
        {
            if (_services?.Stats == null ||
                !_services.Stats.TryGetCompanyTypeId(tile, out string typeId))
            {
                return "회사";
            }

            if (!_companyCatalogLoaded)
            {
                _companyCatalogLoaded = true;
                _companyCatalog = Resources.Load<CompanyTypeCatalogSO>(
                    "CityFlow/CompanyTypeCatalog");
            }
            if (_companyCatalog == null)
            {
                return "회사";
            }

            foreach (CompanyTypeSO so in _companyCatalog.Types)
            {
                if (so == null || so.companyTypeId?.Trim() != typeId) continue;
                string name = string.IsNullOrWhiteSpace(so.displayName)
                    ? typeId
                    : so.displayName;
                return $"{name} ({so.workStartHour:0}~{so.workStartHour + so.workStartWindow:0}시 출근)";
            }
            return "회사";
        }

        // " · 통근 (3,4)×2 (5,1)×1 외 2곳" — 통근자가 없으면 빈 문자열
        private string DescribeCommuterHomes(Vector2Int tile)
        {
            var homes = _services?.Stats?.GetCompanyCommuterHomes(tile);
            if (homes == null || homes.Count == 0)
            {
                return string.Empty;
            }

            var sb = new System.Text.StringBuilder(" · 통근 ");
            int shown = Mathf.Min(3, homes.Count);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append($"({homes[i].Home.x},{homes[i].Home.y})×{homes[i].Count}");
            }
            if (homes.Count > shown)
            {
                sb.Append($" 외 {homes.Count - shown}곳");
            }
            return sb.ToString();
        }

        // ─── Minimap Camera 관리 ─────────────────────────────────

        /// <summary>
        /// 미니맵 카메라를 교차로 타일 월드 좌표 위에 배치하고 RenderTexture를 연결합니다.
        /// 카메라는 enabled=false로 두고, 코루틴에서 수동으로 Render()를 호출합니다.
        /// </summary>
        private void PositionMinimapCamera(Vector2Int tile)
        {
            if (SafeSignalPanel == null || SafeSignalPanel.minimapRawImage == null) return;

            // RenderTexture 생성 (최초 1회)
            if (_minimapRT == null)
            {
                _minimapRT = new RenderTexture(SafeSignalPanel.minimapResolution, SafeSignalPanel.minimapResolution, 16, RenderTextureFormat.ARGB32);
                _minimapRT.antiAliasing = 1; // MSAA 끄기 (최적화)
                _minimapRT.name = "SignalMinimapRT";
            }

            // 카메라 생성 (최초 1회)
            if (_minimapCamera == null)
            {
                GameObject camObj = new GameObject("[MinimapCamera_Signal]");
                camObj.transform.SetParent(null, false); // Canvas 스케일 영향을 받지 않도록 씬 루트에 배치
                _minimapCamera = camObj.AddComponent<Camera>();
                _minimapCamera.enabled = false; // 카드 열릴 때 활성화
                _minimapCamera.orthographic = true;
                _minimapCamera.orthographicSize = SafeSignalPanel.minimapZoomSize;
                _minimapCamera.cullingMask = SafeSignalPanel.minimapCullingMask;
                _minimapCamera.clearFlags = CameraClearFlags.SolidColor;
                _minimapCamera.backgroundColor = SafeSignalPanel.minimapBackgroundColor;
                _minimapCamera.targetTexture = _minimapRT;
                _minimapCamera.depth = -10;
            }

            // 교차로 월드 좌표 계산 (GridUtil 규약 준수)
            Vector3 tileWorldPos;
            if (_services?.WorldCoordinates != null)
            {
                tileWorldPos = _services.WorldCoordinates.GridToWorld(tile, 0f);
            }
            else
            {
                tileWorldPos = GridUtil.GridToWorld(tile);
            }

            // 카메라를 교차로 바라보게 배치 (Main Camera와 동일한 각도 유지)
            if (Camera.main != null)
            {
                _minimapCamera.transform.rotation = Camera.main.transform.rotation;
                _minimapCamera.transform.position = tileWorldPos - Camera.main.transform.forward * SafeSignalPanel.minimapCameraHeight;
                _minimapCamera.orthographic = Camera.main.orthographic;
                if (_minimapCamera.orthographic)
                {
                    _minimapCamera.orthographicSize = SafeSignalPanel.minimapZoomSize;
                }
                else
                {
                    _minimapCamera.fieldOfView = SafeSignalPanel.minimapFov;
                }
            }
            else
            {
                _minimapCamera.transform.position = tileWorldPos + Vector3.up * SafeSignalPanel.minimapCameraHeight;
                _minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }

            _minimapCamera.gameObject.SetActive(true);
            _minimapCamera.enabled = true; // URP 호환: enabled=true로 파이프라인이 자동 렌더

            // RawImage에 RenderTexture 연결
            SafeSignalPanel.minimapRawImage.texture = _minimapRT;
            SafeSignalPanel.minimapRawImage.enabled = true;

            _minimapFrameCounter = 0;
        }

        private void OnDestroy()
        {
            // RenderTexture 메모리 해제
            if (_minimapCamera != null)
            {
                Destroy(_minimapCamera.gameObject);
                _minimapCamera = null;
            }
            if (_minimapRT != null)
            {
                _minimapRT.Release();
                Destroy(_minimapRT);
                _minimapRT = null;
            }
        }
    }
}
