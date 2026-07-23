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

        [Header("Colors")]
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color warningColor = new Color(1f, 0.3f, 0.3f);
        [SerializeField] private Color positiveColor = new Color(0.4f, 0.9f, 0.4f);

        [Header("Resolution Guard")]
        [Tooltip("이 해상도 너비 이하에서는 카드가 자동으로 닫힙니다 (S 모드 = 480).")]
        [SerializeField] private int minimumScreenWidth = 960;
        [Tooltip("이 해상도 높이 이하에서는 카드가 자동으로 닫힙니다 (S 모드 = 270).")]
        [SerializeField] private int minimumScreenHeight = 540;

        // ─── 내부 상태 ──────────────────────────────────────────────
        private CityFlowServices services;
        private PopulationSystem populationSystem;
        private Coroutine updateRoutine;
        private Vector2Int currentTile;
        private TileType currentType;
        private float accumulatedDelay;
        private bool isClosing;

        // UI 플로팅 좌표 변환용 캐싱
        private Canvas rootCanvas;
        private RectTransform parentRectTransform;

        /// <summary>현재 카드가 활성 상태인지 외부에서 확인할 수 있는 프로퍼티.</summary>
        public bool IsOpen => gameObject.activeSelf && !isClosing;

        // ═══════════════════════════════════════════════════════════════
        // ICityFlowServiceConsumer 구현
        // ═══════════════════════════════════════════════════════════════

        public void Initialize(CityFlowServices cityFlowServices)
        {
            services = cityFlowServices;
            populationSystem = FindAnyObjectByType<PopulationSystem>();

            // 캐싱 최적화
            rootCanvas = GetComponentInParent<Canvas>();
            
            // UI 레이아웃 그룹(AnalysisCard 하위 등)에 묶여있어 위치가 겹치는 현상 방지
            // 말풍선처럼 화면 전체를 자유롭게 날아다닐 수 있도록 최상위 캔버스로 독립시킵니다.
            if (rootCanvas != null && transform.parent != rootCanvas.transform)
            {
                transform.SetParent(rootCanvas.transform, false);
                transform.SetAsLastSibling(); // 항상 가장 위(맨 앞)에 렌더링되도록 보장
            }

            parentRectTransform = transform.parent as RectTransform;
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

            // 이미 같은 타일이 열려있으면 무시
            if (gameObject.activeSelf && currentTile == tile && !isClosing)
            {
                return;
            }

            isClosing = false;
            currentTile = tile;
            currentType = type;
            accumulatedDelay = 0f;

            // 말풍선 형태를 위해 Pivot을 하단 중앙(0.5, 0)으로 강제 설정
            RectTransform rect = transform as RectTransform;
            if (rect != null)
            {
                rect.pivot = new Vector2(0.5f, 0f);
            }

            gameObject.SetActive(true);
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
            if (Camera.main == null) return;

            // 1. 타일의 정중앙 바닥 좌표 (XY 평면 사용 시 x, y가 맵 바닥, z가 높이/깊이)
            Vector3 worldPos = new Vector3(currentTile.x + 0.5f, currentTile.y + 0.5f, 0f);
            
            // 건물 Footprint 크기에 따른 중앙 정렬 오프셋
            Vector2Int size = TileFootprint.GetSize(currentType);
            worldPos.x += (size.x - 1) * 0.5f;
            worldPos.y += (size.y - 1) * 0.5f;

            // 2. 높이 오프셋 적용 (아이소매트릭 뷰에서 Z축은 카메라 쪽으로 튀어나오는 높이)
            // 건물의 지붕 근처에 띄우기 위해 Z축을 카메라 방향(-)으로 이동 (유니티 2D/XY 평면 기준 -Z가 카메라 쪽)
            worldPos.z -= 1.5f; 

            Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);

            // [디버깅] 값 출력
            Debug.Log($"[BuildingInfoCard] Tile: {currentTile}, WorldPos: {worldPos}, ScreenPos: {screenPos}, Cam: {Camera.main.name}");

            // 카메라 뒤에 있는 경우 렌더링 무시 (Orthographic에서 z가 다르게 나올 수 있으므로 임시 해제하거나 확인)
            if (screenPos.z < 0) 
            {
                Debug.LogWarning("[BuildingInfoCard] screenPos.z < 0 이므로 위치 갱신 무시됨!");
                // return; // 임시로 무시 로직을 주석 처리해 봅니다.
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

                // --- 캔버스 밖으로 벗어남 방지 (Clamping) ---
                // Pivot이 (0.5, 0) 이므로 좌/우 여백은 width/2, 상단 여백은 height 전체, 하단 여백은 0
                float pivotX = myRect.pivot.x;
                float pivotY = myRect.pivot.y;
                float width = myRect.rect.width;
                float height = myRect.rect.height;

                float minX = parentRectTransform.rect.xMin + (width * pivotX);
                float maxX = parentRectTransform.rect.xMax - (width * (1f - pivotX));
                float minY = parentRectTransform.rect.yMin + (height * pivotY);
                float maxY = parentRectTransform.rect.yMax - (height * (1f - pivotY));

                // 여백을 20픽셀 정도 두어 모서리에 너무 바짝 붙지 않게 조정
                float padding = 20f;
                localPoint.x = Mathf.Clamp(localPoint.x, minX + padding, maxX - padding);
                localPoint.y = Mathf.Clamp(localPoint.y, minY + padding, maxY - padding);

                myRect.localPosition = localPoint;
            }
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

                // 코어 엔진에서 density/congestion 시드 수신
                float density = GetTileDensity();
                CongestionLevel congestion = GetTileCongestion();

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
                    tilePopulation);

                // UI 텍스트 바인딩
                BindDataToUI(data, density, congestion);

                yield return wait;
            }
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

            if (txtIncomePerMin != null)
            {
                txtIncomePerMin.text = $"+{data.IncomePerMin:N0}";
                txtIncomePerMin.color = positiveColor;
            }

            if (txtDelaySeconds != null)
            {
                string delayText = $"+{data.DelaySeconds:F1}초";

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

        // ═══════════════════════════════════════════════════════════════
        // 코어 엔진 데이터 접근 (Contracts 경유)
        // ═══════════════════════════════════════════════════════════════

        private float GetTileDensity()
        {
            if (services?.TileData == null) return 0f;

            // 건물 타일은 Density가 0일 수 있으므로, 인접 도로 타일의 density를 참조
            float selfDensity = services.TileData.GetDensity01(currentTile);
            if (selfDensity > 0f) return selfDensity;

            // 인접 4방향 도로 타일 중 가장 높은 density를 사용
            float maxNeighborDensity = 0f;
            Vector2Int[] offsets = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            foreach (Vector2Int offset in offsets)
            {
                Vector2Int neighbor = currentTile + offset;
                if (!GridUtil.IsInside(neighbor)) continue;
                if (services.TileData.GetTileType(neighbor) == TileType.Road)
                {
                    float neighborDensity = services.TileData.GetDensity01(neighbor);
                    if (neighborDensity > maxNeighborDensity)
                    {
                        maxNeighborDensity = neighborDensity;
                    }
                }
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
            Vector2Int[] offsets = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            foreach (Vector2Int offset in offsets)
            {
                Vector2Int neighbor = currentTile + offset;
                if (!GridUtil.IsInside(neighbor)) continue;
                if (services.TileData.GetTileType(neighbor) == TileType.Road)
                {
                    CongestionLevel level = services.TileData.GetCongestion(neighbor);
                    if (level > worstLevel)
                    {
                        worstLevel = level;
                    }
                }
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
        }
    }
}
