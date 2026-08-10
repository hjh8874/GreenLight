using System;
using System.Collections;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.UI.Data;
using CityFlow.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace CityFlow.UI
{
    /// <summary>
    /// 방치형 건물 정보 UI 카드 컨트롤러.
    /// 건물(House, Office, School, Hospital) 타일 클릭 시 좌측 하단에 카드를 팝업합니다.
    /// - 200ms 스로틀링 코루틴으로 실시간 수치 갱신 (GC/Canvas Rebuild 방지)
    /// - S 모드(480x270) 이하 해상도에서 자동 닫힘/생성 차단
    /// - BuildingStoryDataFactory를 통해 온더플라이 스토리 데이터 조립
    /// </summary>
    public sealed class BuildingInfoCardController : MonoBehaviour, ICityFlowServiceConsumer
    {
        // ─── UI 바인딩 필드 ──────────────────────────────────────────
        [Header("UI Text Elements")]
        [SerializeField] private TMP_Text txtBuildingName;
        [SerializeField] private TMP_Text txtStoryComment;
        [SerializeField] private TMP_Text txtTotalStaff;
        [SerializeField] private TMP_Text txtTardyStaff;
        [SerializeField] private TMP_Text txtIncomePerMin;
        [SerializeField] private TMP_Text txtDelaySeconds;

        [Header("Comment Data")]
        [SerializeField]
        private BuildingHoverCommentCatalogSO hoverCommentCatalog;

        [Header("Colors")]
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color warningColor = new Color(1f, 0.3f, 0.3f);
        [SerializeField] private Color positiveColor = new Color(0.4f, 0.9f, 0.4f);

        [Header("Resolution Guard")]
        [Tooltip("이 해상도 너비 이하에서는 카드가 자동으로 닫힙니다 (S 모드 = 480).")]
        [SerializeField] private int minimumScreenWidth = 960;
        [Tooltip("이 해상도 높이 이하에서는 카드가 자동으로 닫힙니다 (S 모드 = 270).")]
        [SerializeField] private int minimumScreenHeight = 540;

        [Header("World Position")]
        [SerializeField]
        [Min(0f)]
        private float worldHeightOffset = 1.5f;

        // ─── 내부 상태 ──────────────────────────────────────────────
        private CityFlowServices services;
        private PopulationSystem populationSystem;
        private Coroutine updateRoutine;
        private Vector2Int currentTile;
        private TileType currentType;
        private float accumulatedDelay;
        private bool isClosing;
        private TMP_Text labelTotalStaff;
        private TMP_Text labelTardyStaff;
        private TMP_Text labelIncomePerMin;
        private TMP_Text labelDelaySeconds;
        private string defaultTotalStaffLabel;
        private string defaultTardyStaffLabel;
        private string defaultIncomeLabel;
        private string defaultDelayLabel;
        private bool metricLabelsCached;
        private bool visibilityPublished;

        // UI 플로팅 좌표 변환용 캐싱
        private Canvas rootCanvas;
        private RectTransform parentRectTransform;
        private HUDDashboard hudDashboard;
        private readonly Vector3[] hudWorldCorners = new Vector3[4];

        /// <summary>현재 카드가 활성 상태인지 외부에서 확인할 수 있는 프로퍼티.</summary>
        public bool IsOpen => gameObject.activeSelf && !isClosing;
        public bool IsVisible => visibilityPublished;
        public Vector2Int DisplayedTile => currentTile;
        public event Action<Vector2Int, bool> VisibilityChanged;

        // ═══════════════════════════════════════════════════════════════
        // ICityFlowServiceConsumer 구현
        // ═══════════════════════════════════════════════════════════════

        public void Initialize(CityFlowServices cityFlowServices)
        {
            services = cityFlowServices;
            populationSystem = FindAnyObjectByType<PopulationSystem>();
            EnsureCommentCatalog();
            CacheMetricLabels();
            EnsureFloatingCanvas();
        }

        // ═══════════════════════════════════════════════════════════════
        // 카드 열기 / 닫기
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 건물 타일 클릭 시 호출. 해상도 가드를 검사한 후 카드를 팝업합니다.
        /// </summary>
        public void OpenCard(Vector2Int tile, TileType type)
        {
            // 해상도 가드: S 모드 이하에서는 카드 생성을 차단합니다.
            if (Screen.width < minimumScreenWidth || Screen.height < minimumScreenHeight)
            {
                return;
            }

            EnsureCommentCatalog();
            EnsureFloatingCanvas();

            Vector2Int displayAnchor = ResolveDisplayAnchor(tile, type);

            // 이미 같은 건물이 열려있으면 무시
            if (gameObject.activeSelf &&
                currentTile == displayAnchor &&
                !isClosing)
            {
                return;
            }

            if (visibilityPublished && currentTile != displayAnchor)
            {
                PublishVisibility(false);
            }

            isClosing = false;
            currentTile = displayAnchor;
            currentType = type;
            accumulatedDelay = 0f;

            // 말풍선 형태를 위해 Pivot을 하단 중앙(0.5, 0)으로 강제 설정
            RectTransform rect = transform as RectTransform;
            if (rect != null)
            {
                rect.pivot = new Vector2(0.5f, 0f);
            }

            gameObject.SetActive(true);
            PublishVisibility(true);
            UpdateFloatingPosition(); // 팝업 애니메이션 시작 전 초기 위치 세팅

            // DOTween 팝업 애니메이션
            transform.DOKill();
            transform.localScale = Vector3.zero;
            transform.DOScale(Vector3.one, 0.3f).SetEase(Ease.OutBack);

            // 기존 갱신 코루틴 정리 후 재시작
            StopUpdateRoutine();
            updateRoutine = StartCoroutine(UpdateCardRoutine());
        }

        private void LateUpdate()
        {
            if (IsOpen)
            {
                UpdateFloatingPosition();
            }
        }

        private void UpdateFloatingPosition()
        {
            EnsureFloatingCanvas();
            if (Camera.main == null) return;

            Vector2Int size = TileFootprint.GetSize(
                ResolveFootprintType());
            Vector2 center = new Vector2(
                currentTile.x + size.x * 0.5f,
                currentTile.y + size.y * 0.5f);
            IWorldCoordinateSpace coordinateSpace =
                services?.WorldCoordinates;
            Vector3 worldPos = coordinateSpace != null
                ? coordinateSpace.GridPointToWorld(
                    center,
                    worldHeightOffset)
                : new Vector3(
                    center.x,
                    center.y,
                    -worldHeightOffset);

            Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);

            // 카메라 뒤에 있는 경우 렌더링 무시
            if (screenPos.z < 0)
            {
                // 화면 바깥이거나 카메라 뒤에 있으면 캔버스 바깥으로 위치를 옮겨 숨김 처리
                RectTransform myRect = (RectTransform)transform;
                myRect.localPosition = new Vector3(-9999f, -9999f, 0f);
                return;
            }

            if (rootCanvas == null || parentRectTransform == null)
            {
                Debug.LogWarning($"[BuildingInfoCard] rootCanvas or parentRectTransform is null! Canvas: {rootCanvas}, Parent: {parentRectTransform}");
                return;
            }

            Camera uiCamera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRectTransform, screenPos, uiCamera, out Vector2 localPoint))
            {
                RectTransform myRect = (RectTransform)transform;
                Rect playableRect = ResolvePlayableLocalRect(uiCamera);

                // --- 캔버스 밖으로 벗어남 방지 (Clamping) ---
                // Pivot이 (0.5, 0) 이므로 좌/우 여백은 width/2, 상단 여백은 height 전체, 하단 여백은 0
                float pivotX = myRect.pivot.x;
                float pivotY = myRect.pivot.y;
                float width = myRect.rect.width;
                float height = myRect.rect.height;

                float minX = playableRect.xMin + (width * pivotX);
                float maxX = playableRect.xMax - (width * (1f - pivotX));
                float minY = playableRect.yMin + (height * pivotY);
                float maxY = playableRect.yMax - (height * (1f - pivotY));

                // 여백을 20픽셀 정도 두어 모서리에 너무 바짝 붙지 않게 조정
                float padding = 20f;
                localPoint.x = ClampToRange(
                    localPoint.x,
                    minX + padding,
                    maxX - padding);
                localPoint.y = ClampToRange(
                    localPoint.y,
                    minY + padding,
                    maxY - padding);

                myRect.localPosition = localPoint;
            }
        }

        private Rect ResolvePlayableLocalRect(Camera uiCamera)
        {
            Rect playableRect = parentRectTransform.rect;
            Rect safeArea = Screen.safeArea;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRectTransform,
                    safeArea.min,
                    uiCamera,
                    out Vector2 safeMin) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRectTransform,
                    safeArea.max,
                    uiCamera,
                    out Vector2 safeMax))
            {
                playableRect.xMin = Mathf.Max(
                    playableRect.xMin,
                    Mathf.Min(safeMin.x, safeMax.x));
                playableRect.xMax = Mathf.Min(
                    playableRect.xMax,
                    Mathf.Max(safeMin.x, safeMax.x));
                playableRect.yMin = Mathf.Max(
                    playableRect.yMin,
                    Mathf.Min(safeMin.y, safeMax.y));
                playableRect.yMax = Mathf.Min(
                    playableRect.yMax,
                    Mathf.Max(safeMin.y, safeMax.y));
            }

            hudDashboard ??= FindAnyObjectByType<HUDDashboard>(
                FindObjectsInactive.Include);
            RectTransform topBarRect =
                hudDashboard != null && hudDashboard.gameObject.activeInHierarchy
                    ? hudDashboard.transform as RectTransform
                    : null;
            if (topBarRect == null)
            {
                return playableRect;
            }

            topBarRect.GetWorldCorners(hudWorldCorners);
            Vector3 bottomLeft = parentRectTransform.InverseTransformPoint(
                hudWorldCorners[0]);
            Vector3 bottomRight = parentRectTransform.InverseTransformPoint(
                hudWorldCorners[3]);
            float topBarBottom = Mathf.Min(bottomLeft.y, bottomRight.y);
            playableRect.yMax = Mathf.Min(playableRect.yMax, topBarBottom);
            return playableRect;
        }

        private static float ClampToRange(
            float value,
            float minimum,
            float maximum)
        {
            return maximum >= minimum
                ? Mathf.Clamp(value, minimum, maximum)
                : (minimum + maximum) * 0.5f;
        }

        private Vector2Int ResolveDisplayAnchor(
            Vector2Int tile,
            TileType type)
        {
            if (!TileFootprint.IsBuilding(type) ||
                services?.TileData == null)
            {
                return tile;
            }

            return services.TileData.TryGetFootprintAnchor(
                tile,
                out Vector2Int anchor)
                ? anchor
                : tile;
        }

        private TileType ResolveFootprintType()
        {
            return currentType == TileType.UnderConstruction &&
                   services?.TileData != null &&
                   services.TileData.TryGetConstructionTargetType(
                       currentTile,
                       out TileType targetType)
                ? targetType
                : currentType;
        }

        private void EnsureFloatingCanvas()
        {
            if (rootCanvas == null)
            {
                rootCanvas = GetComponentInParent<Canvas>();
            }

            if (rootCanvas != null &&
                transform.parent != rootCanvas.transform)
            {
                transform.SetParent(rootCanvas.transform, false);
                transform.SetAsLastSibling();
            }

            parentRectTransform = transform.parent as RectTransform;
        }

        /// <summary>
        /// 카드를 닫습니다. DOTween 축소 애니메이션 후 비활성화.
        /// </summary>
        public void CloseCard()
        {
            if (isClosing || !gameObject.activeSelf) return;
            isClosing = true;

            StopUpdateRoutine();

            transform.DOKill();
            transform.DOScale(Vector3.zero, 0.2f).SetEase(Ease.InBack).OnComplete(() =>
            {
                gameObject.SetActive(false);
            });
        }

        private void PublishVisibility(bool visible)
        {
            if (visibilityPublished == visible)
            {
                return;
            }

            visibilityPublished = visible;
            VisibilityChanged?.Invoke(currentTile, visible);
        }

        // ═══════════════════════════════════════════════════════════════
        // 200ms 스로틀링 갱신 코루틴
        // ═══════════════════════════════════════════════════════════════

        private IEnumerator UpdateCardRoutine()
        {
            WaitForSeconds wait = new WaitForSeconds(0.2f); // 200ms 주기 강제 스로틀링

            while (true)
            {
                // 해상도 가드: 실시간으로 창 크기가 줄어들면 카드 자동 닫힘
                if (Screen.width < minimumScreenWidth || Screen.height < minimumScreenHeight)
                {
                    CloseCard();
                    yield break;
                }

                if (!RefreshCurrentTileState())
                {
                    yield break;
                }

                // 코어 엔진에서 density/congestion 시드 수신
                float density = GetTileDensity();
                CongestionLevel congestion = GetTileCongestion();

                if (currentType == TileType.UnderConstruction &&
                    TryBindConstruction(congestion))
                {
                    yield return wait;
                    continue;
                }

                if (currentType == TileType.SpecialBuilding &&
                    TryBindSpecialBuilding(congestion))
                {
                    yield return wait;
                    continue;
                }

                ApplySpecialMetricLabels(false);

                // 지연 시간 누적 (요구사항 수식: += 0.2f * density)
                accumulatedDelay += 0.2f * density;

                // 실제 인구/채용 데이터 가져오기
                int tilePopulation = GetTilePopulation();
                int staffingFilled = -1;
                int staffingCapacity = -1;
                TryGetStaffing(out staffingFilled, out staffingCapacity);

                // BuildingStoryDataFactory로 온더플라이 데이터 조립
                BuildingStoryData data = BuildingStoryDataFactory.Synthesize(
                    currentTile,
                    currentType,
                    density,
                    congestion,
                    accumulatedDelay,
                    staffingFilled,
                    staffingCapacity,
                    tilePopulation,
                    hoverCommentCatalog,
                    ResolveCompanyTypeId());

                // UI 텍스트 바인딩
                BindDataToUI(data, density, congestion);

                yield return wait;
            }
        }

        private bool RefreshCurrentTileState()
        {
            if (services?.TileData == null)
            {
                return true;
            }

            TileType observedType = services.TileData.GetTileType(currentTile);
            if (observedType == currentType)
            {
                return true;
            }

            if (!TileFootprint.IsBuilding(observedType))
            {
                CloseCard();
                return false;
            }

            currentType = observedType;
            accumulatedDelay = 0f;
            return true;
        }

        // ═══════════════════════════════════════════════════════════════
        // UI 바인딩
        // ═══════════════════════════════════════════════════════════════

        private void BindDataToUI(BuildingStoryData data, float density, CongestionLevel congestion)
        {
            if (txtBuildingName != null)
            {
                txtBuildingName.text = data.BuildingName;
            }

            if (txtStoryComment != null)
            {
                txtStoryComment.text = data.StoryComment;
                txtStoryComment.color = congestion == CongestionLevel.Jam
                    ? warningColor
                    : normalColor;
            }

            if (txtTotalStaff != null)
            {
                txtTotalStaff.text = $"{data.TotalStaff:N0}명";
            }

            if (txtTardyStaff != null)
            {
                txtTardyStaff.text = $"{data.TardyStaff:N0}명";
                txtTardyStaff.color = data.TardyStaff > 5
                    ? warningColor
                    : normalColor;
            }

            // 예상 수익 제거(2026-08-10). 이 값은 시뮬레이션이 아니라
            // 타일 좌표 해시로 만든 합성값이었다 —
            // BuildingStoryDataFactory: coinsPerPerson = 3 + (seed % 6)
            // 실제 경제(EconomyService·DistanceRewardService)와 연결이 없어서,
            // 플레이어가 "+120"을 보고 기대해도 그 돈은 들어오지 않는다.
            // 없는 정보를 보여주느니 행을 접는다. 슬롯 자체는 특수 건물(방문 수용량)과
            // 공사 중(주변 교통)이 재사용하므로 지우지 않는다.
            SetMetricRowVisible(txtIncomePerMin, false);

            if (txtDelaySeconds != null)
            {
                string delayText = $"(테스트) 지연: +{data.DelaySeconds:F1}초";

                if (congestion == CongestionLevel.Jam)
                {
                    txtDelaySeconds.color = warningColor;
                    txtDelaySeconds.text = $"{delayText} ! 정체";
                }
                else
                {
                    txtDelaySeconds.color = normalColor;
                    txtDelaySeconds.text = delayText;
                }
            }
        }

        private bool TryBindSpecialBuilding(CongestionLevel congestion)
        {
            if (services?.SpecialBuildings == null ||
                !services.SpecialBuildings.TryGetBuilding(
                    currentTile,
                    out SpecialBuildingInstance building) ||
                !services.SpecialBuildings.TryGetBuildOption(
                    building.BuildingId,
                    out SpecialBuildingBuildOption option))
            {
                return false;
            }

            ApplySpecialMetricLabels(true);

            SpecialBuildingVisitStatistics statistics = default;
            services.SpecialBuildingVisits?.TryGetStatistics(
                currentTile,
                out statistics);

            if (txtBuildingName != null)
            {
                txtBuildingName.text = option.DisplayName;
            }

            if (txtStoryComment != null)
            {
                txtStoryComment.text =
                    BuildingHoverCommentResolver.Resolve(
                        hoverCommentCatalog,
                        new BuildingHoverCommentContext(
                            currentTile,
                            currentType,
                            congestion,
                            option.DisplayName,
                            specialBuildingId: building.BuildingId));
                txtStoryComment.color = congestion == CongestionLevel.Jam
                    ? warningColor
                    : normalColor;
            }

            if (txtTotalStaff != null)
            {
                txtTotalStaff.text =
                    $"{statistics.PlannedToday:N0}명";
                txtTotalStaff.color = normalColor;
            }

            if (txtTardyStaff != null)
            {
                txtTardyStaff.text =
                    $"{statistics.TotalPlannedVisits:N0}명";
                txtTardyStaff.color = normalColor;
            }

            // 일반 건물 경로가 접어둔 행을 특수 건물에서는 다시 편다(방문 수용량).
            SetMetricRowVisible(txtIncomePerMin, true);
            if (txtIncomePerMin != null)
            {
                txtIncomePerMin.text = option.VisitorCapacity > 0
                    ? $"{option.VisitorCapacity:N0}명"
                    : "제한 없음";
                txtIncomePerMin.color = normalColor;
            }

            if (txtDelaySeconds != null)
            {
                txtDelaySeconds.text =
                    $"{option.VisitsPerPeriod}회 / " +
                    $"{option.PeriodDays}일";
                txtDelaySeconds.color = normalColor;
            }

            return true;
        }

        private bool TryBindConstruction(CongestionLevel congestion)
        {
            if (services?.TileData == null ||
                !services.TileData.TryGetConstructionTargetType(
                    currentTile,
                    out TileType targetType))
            {
                return false;
            }

            float progress01 = 0f;
            services.TileData.TryGetConstructionProgress01(
                currentTile,
                out progress01);

            string targetName = ResolveConstructionTargetName(targetType);
            ApplyConstructionMetricLabels();

            if (txtBuildingName != null)
            {
                txtBuildingName.text = $"{targetName} 공사 중";
            }

            if (txtStoryComment != null)
            {
                txtStoryComment.text = BuildingHoverCommentResolver.Resolve(
                    hoverCommentCatalog,
                    new BuildingHoverCommentContext(
                        currentTile,
                        currentType,
                        congestion,
                        targetName,
                        constructionProgress01: progress01));
                txtStoryComment.color = congestion == CongestionLevel.Jam
                    ? warningColor
                    : normalColor;
            }

            if (txtTotalStaff != null)
            {
                txtTotalStaff.text =
                    $"{Mathf.RoundToInt(progress01 * 100f)}%";
                txtTotalStaff.color = positiveColor;
            }

            if (txtTardyStaff != null)
            {
                txtTardyStaff.text = targetName;
                txtTardyStaff.color = normalColor;
            }

            if (txtIncomePerMin != null)
            {
                txtIncomePerMin.text = CongestionDisplayName(congestion);
                txtIncomePerMin.color = congestion == CongestionLevel.Jam
                    ? warningColor
                    : normalColor;
            }

            if (txtDelaySeconds != null)
            {
                txtDelaySeconds.text = "진행 중";
                txtDelaySeconds.color = normalColor;
            }

            return true;
        }

        private string ResolveConstructionTargetName(TileType targetType)
        {
            if (targetType == TileType.SpecialBuilding &&
                services?.SpecialBuildings != null &&
                services.SpecialBuildings.TryGetBuilding(
                    currentTile,
                    out SpecialBuildingInstance building) &&
                services.SpecialBuildings.TryGetBuildOption(
                    building.BuildingId,
                    out SpecialBuildingBuildOption option))
            {
                return option.DisplayName;
            }

            return BuildingStoryDataFactory.ResolveBuildingName(
                targetType,
                currentTile);
        }

        private static string CongestionDisplayName(
            CongestionLevel congestion) => congestion switch
        {
            CongestionLevel.Jam => "정체",
            CongestionLevel.Slow => "서행",
            _ => "원활"
        };

        private void CacheMetricLabels()
        {
            if (metricLabelsCached)
            {
                return;
            }

            labelTotalStaff = ResolveMetricLabel(txtTotalStaff);
            labelTardyStaff = ResolveMetricLabel(txtTardyStaff);
            labelIncomePerMin = ResolveMetricLabel(txtIncomePerMin);
            labelDelaySeconds = ResolveMetricLabel(txtDelaySeconds);
            defaultTotalStaffLabel = labelTotalStaff?.text;
            defaultTardyStaffLabel = labelTardyStaff?.text;
            defaultIncomeLabel = labelIncomePerMin?.text;
            defaultDelayLabel = labelDelaySeconds?.text;
            metricLabelsCached = true;
        }

        private void ApplySpecialMetricLabels(bool specialBuilding)
        {
            CacheMetricLabels();
            if (labelTotalStaff != null)
            {
                labelTotalStaff.text = specialBuilding
                    ? "오늘 방문 수요"
                    : defaultTotalStaffLabel;
            }

            if (labelTardyStaff != null)
            {
                labelTardyStaff.text = specialBuilding
                    ? "누적 방문 수요"
                    : defaultTardyStaffLabel;
            }

            if (labelIncomePerMin != null)
            {
                labelIncomePerMin.text = specialBuilding
                    ? "방문 수용량"
                    : defaultIncomeLabel;
            }

            if (labelDelaySeconds != null)
            {
                labelDelaySeconds.text = specialBuilding
                    ? "방문 주기"
                    : defaultDelayLabel;
            }
        }

        private void ApplyConstructionMetricLabels()
        {
            CacheMetricLabels();
            // 일반 건물 경로가 접어둔 행을 공사 중 표시에서는 다시 편다(주변 교통).
            SetMetricRowVisible(txtIncomePerMin, true);
            if (labelTotalStaff != null)
            {
                labelTotalStaff.text = "공사 진행률";
            }

            if (labelTardyStaff != null)
            {
                labelTardyStaff.text = "완공 대상";
            }

            if (labelIncomePerMin != null)
            {
                labelIncomePerMin.text = "주변 교통";
            }

            if (labelDelaySeconds != null)
            {
                labelDelaySeconds.text = "건설 상태";
            }
        }

        private void EnsureCommentCatalog()
        {
            if (hoverCommentCatalog != null)
            {
                return;
            }

            hoverCommentCatalog =
                BuildingHoverCommentCatalogSO.LoadDefault();
            if (hoverCommentCatalog == null)
            {
                Debug.LogWarning(
                    "[BuildingInfoCard] BuildingHoverCommentCatalog was not " +
                    "found. Built-in fallback comments will be used.",
                    this);
            }
        }

        private string ResolveCompanyTypeId()
        {
            return currentType == TileType.Office &&
                   services?.Stats != null &&
                   services.Stats.TryGetCompanyTypeId(
                       currentTile,
                       out string companyTypeId)
                ? companyTypeId
                : string.Empty;
        }

        // 값과 라벨은 같은 부모(행) 아래 있다 — 행 단위로 접고 편다.
        // 일반 건물에서 예상 수익을 숨기되, 같은 슬롯을 쓰는 특수 건물·공사 중
        // 표시는 살아 있어야 하므로 각 경로가 명시적으로 다시 켠다.
        private static void SetMetricRowVisible(TMP_Text valueText, bool visible)
        {
            if (valueText == null || valueText.transform.parent == null)
            {
                return;
            }

            GameObject row = valueText.transform.parent.gameObject;
            if (row.activeSelf != visible)
            {
                row.SetActive(visible);
            }
        }

        private static TMP_Text ResolveMetricLabel(TMP_Text valueText)
        {
            if (valueText == null || valueText.transform.parent == null)
            {
                return null;
            }

            TMP_Text[] texts = valueText.transform.parent
                .GetComponentsInChildren<TMP_Text>(true);
            for (int index = 0; index < texts.Length; index++)
            {
                if (texts[index] != valueText)
                {
                    return texts[index];
                }
            }

            return null;
        }

        // ═══════════════════════════════════════════════════════════════
        // 코어 엔진 데이터 접근 (Contracts 경유)
        // ═══════════════════════════════════════════════════════════════

        private float GetTileDensity()
        {
            if (services?.TileData == null) return 0f;

            // 건물 타일은 Density가 0일 수 있으므로, 인접 도로 타일의 density를 참조
            float selfDensity = services.TileData.GetDensity01(currentTile);
            if (selfDensity > 0f) return selfDensity;

            // 건물 크기에 따른 외곽 타일 순회
            float maxNeighborDensity = 0f;
            Vector2Int size = TileFootprint.GetSize(
                ResolveFootprintType());

            // 상, 하 외곽 도로 검사
            for (int x = 0; x < size.x; x++)
            {
                CheckDensity(currentTile + new Vector2Int(x, size.y), ref maxNeighborDensity);
                CheckDensity(currentTile + new Vector2Int(x, -1), ref maxNeighborDensity);
            }

            // 좌, 우 외곽 도로 검사
            for (int y = 0; y < size.y; y++)
            {
                CheckDensity(currentTile + new Vector2Int(size.x, y), ref maxNeighborDensity);
                CheckDensity(currentTile + new Vector2Int(-1, y), ref maxNeighborDensity);
            }

            return maxNeighborDensity;
        }

        private CongestionLevel GetTileCongestion()
        {
            if (services?.TileData == null) return CongestionLevel.Free;

            CongestionLevel selfLevel = services.TileData.GetCongestion(currentTile);
            if (selfLevel != CongestionLevel.Free) return selfLevel;

            // 인접 도로 중 가장 심각한 혼잡도 채택
            CongestionLevel worstLevel = CongestionLevel.Free;
            Vector2Int size = TileFootprint.GetSize(
                ResolveFootprintType());

            for (int x = 0; x < size.x; x++)
            {
                CheckCongestion(currentTile + new Vector2Int(x, size.y), ref worstLevel);
                CheckCongestion(currentTile + new Vector2Int(x, -1), ref worstLevel);
            }
            for (int y = 0; y < size.y; y++)
            {
                CheckCongestion(currentTile + new Vector2Int(size.x, y), ref worstLevel);
                CheckCongestion(currentTile + new Vector2Int(-1, y), ref worstLevel);
            }

            return worstLevel;
        }

        private int GetTilePopulation()
        {
            if (populationSystem == null) return -1;
            if (populationSystem.TryGetTilePopulation(currentTile, out int population))
            {
                return population;
            }

            return -1;
        }

        private void TryGetStaffing(out int filled, out int capacity)
        {
            filled = -1;
            capacity = -1;

            if (services?.Stats == null) return;
            if (services.Stats.TryGetCompanyStaffing(currentTile, out CompanyStaffing staffing))
            {
                filled = staffing.Filled;
                capacity = staffing.Capacity;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 유틸리티
        // ═══════════════════════════════════════════════════════════════

        private void CheckDensity(Vector2Int neighbor, ref float maxNeighborDensity)
        {
            if (!IsInsideWorld(neighbor)) return;
            if (services.TileData.GetTileType(neighbor) == TileType.Road)
            {
                float neighborDensity = services.TileData.GetDensity01(neighbor);
                if (neighborDensity > maxNeighborDensity)
                {
                    maxNeighborDensity = neighborDensity;
                }
            }
        }

        private void CheckCongestion(Vector2Int neighbor, ref CongestionLevel worstLevel)
        {
            if (!IsInsideWorld(neighbor)) return;
            if (services.TileData.GetTileType(neighbor) == TileType.Road)
            {
                CongestionLevel level = services.TileData.GetCongestion(neighbor);
                if (level > worstLevel)
                {
                    worstLevel = level;
                }
            }
        }
        // ═══════════════════════════════════════════════════════════════

        private bool IsInsideWorld(Vector2Int tile)
        {
            return services?.WorldGrid != null
                ? services.WorldGrid.IsInsideWorld(tile)
                : GridUtil.IsInside(tile);
        }

        private void StopUpdateRoutine()
        {
            if (updateRoutine != null)
            {
                StopCoroutine(updateRoutine);
                updateRoutine = null;
            }
        }

        private void OnDisable()
        {
            StopUpdateRoutine();
            PublishVisibility(false);
        }
    }
}
