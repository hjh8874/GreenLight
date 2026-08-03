using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.Sim;
using CityFlow.ViewKit;
using CityFlow.UI;
using CityFlow.UI.Controllers;
using CityFlow.UI.Data;
using CityFlow.UI.Feed;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace CityFlow.View
{
    public sealed partial class MainCityView :
        MonoBehaviour,
        ICityFlowServiceConsumer,
        IWorldCoordinateRoot
    {
        [Header("Grid")]
        [SerializeField] private int width = GridUtil.DefaultWidth;
        [SerializeField] private int height = GridUtil.DefaultHeight;
        [SerializeField] private float tileSize = GridUtil.TileSize;

        [Header("Optional Prefabs")]
        [SerializeField] private GameObject roadPrefab;
        [SerializeField] private SimpleTownRoadVisualSetSO simpleTownRoadVisualSet;
        [SerializeField] private BuildingVisualCatalogSO buildingVisualCatalog;
        [SerializeField] private GameObject housePrefab;
        [SerializeField] private GameObject officePrefab;
        [SerializeField] private GameObject schoolPrefab;
        [SerializeField] private GameObject hospitalPrefab;
        [SerializeField] private GameObject vehiclePrefab;
        [SerializeField] private VehicleVisualCatalogSO vehicleVisualCatalog;
        [SerializeField] private GameObject signalPrefab;
        [SerializeField] private GameObject burstPrefab;
        [SerializeField] private GameObject fieldTilePrefab;
        [SerializeField] private float fieldTileZ = 0.14f;

        [Header("Runtime Visuals")]
        [SerializeField] private float vehicleSpeed = 1.6f;   // 개성 패스 라이브 튜닝(환 2026-07-17): 2.0에서 20% 감속
        [SerializeField] private float vehicleZ = -0.18f;
        [SerializeField] private float signalZ = -0.45f;
        [SerializeField] private float burstSeconds = 0.8f;
        [SerializeField, Min(0)] private int flowBurstAnchorRadius = 3;
        [SerializeField] private float gridLineThickness = 0.045f;
        [SerializeField] private float overridePulseAmp = 0.25f;   // 신호 펄스 진폭
        // 우측통행 차선 오프셋(타일 비율). 교차 차량의 분리 거리 = laneOffset × √2 이므로
        // 이 값이 교차로 겹침을 직접 지배한다. 정규화 스윕(2026-07-20, 교대 3라운드):
        //   0.18 → 교차겹침 17.24/1k차프레임 | 0.22 → 11.79 | 0.26 → 8.94 | 0.30 → 15.75
        // 0.30에서 다시 나빠지는 건 차가 중앙선에서 너무 벗어나 코너 곡선과 어긋나기 때문.
        // 도로 반쪽 폭의 중앙은 0.25이므로 차량 중심을 중앙선과 외곽선 사이 정중앙에 둔다.
        // ⚠️ 씬에 직렬화된 값이 이 기본값을 덮는다 — 씬 인스펙터에서도 0.25로 맞출 것.
        [SerializeField] private float laneOffset = 0.25f;
        // 주행 가감속(월드유닛/초²). 정지 1회당 쌓이는 지연 = 순항²/(2·가속도)이므로
        // 가속이 느리면 최고속을 안 올려도 뷰가 계속 뒤처진다(2.5 = 정지당 1.25타일,
        // 6.0 = 0.52타일). 최고속이 아니라 여기를 올려야 지연이 준다. 라이브 튜닝 노브.
        [SerializeField] private float vehicleDriveAccel = 6.0f;
        [SerializeField] private float vehicleBrakeAccel = 5.0f;
        // 따라잡기: 지연이 이 문턱을 넘은 뒤부터 ramp 구간에 걸쳐 서서히 여유를 준다.
        // 문턱을 낮추거나 여유를 키우면 대기 후 교차로를 튀어나가듯 통과한다(환 라이브).
        [SerializeField] private float vehicleCatchUpRange = 0.3f;    // 순항 대비 최대 여유(+30%)
        [SerializeField] private float vehicleCatchUpStart = 1.0f;    // 여유 시작 지연(타일)
        [SerializeField] private float vehicleCatchUpRamp = 2.0f;     // 최대치까지의 지연 폭(타일)
        // MM 전환 Phase 2. 뷰 차가 프레임 단위로 스스로 달리고, Sim 위치는 '상한'이 된다.
        // 정지 원인은 딱 둘 — (같은 차선 앞차 간격)과 (코리도 상한). 둘 다 선형 순서라
        // 사이클(=데드락)을 만들 수 없다(계획서 2026-07-20-mm-continuous-motion §2).
        // 교차 차선 차량은 절대 정지 사유가 되지 않는다 — 겹침은 기하로만 막는다(dev-log-17).
        [SerializeField, Range(0f, 2f)] private float vehicleCorridorTiles = 1.0f;   // Sim 위치보다 얼마나 앞설 수 있나
        [SerializeField, Range(0.3f, 1f)] private float vehicleMinHeadway = 0.55f;   // 앞차와 유지할 최소 간격(타일). 최대 차 길이 0.437 + 여유
        [SerializeField] private float parkingApproachSpeedRatio = 0.9f;   // 주차 진입 속도 상한(순항 대비)
        // 교차로 정지선 후퇴량. 이 값은 '틱 목표'에서 빼지므로 통과 차의 틱당 이동거리를
        // 진입 1-inset / 이탈 1+inset 으로 갈라 속도 계단을 만든다(0.25면 1.66배).
        // 0.12로 낮춰 계단을 1.27배로 완화 — 레인 오프셋(0.18)이 있어 교차 차량 분리는 유지된다.
        // 완전 해소는 틱 위상 보간 대신 등속 리쉬로 가야 함(설계 과제, 감사 2026-07-18 Rank 1).
        [SerializeField, Range(0f, 0.45f)] private float intersectionQueueInset = 0.12f;
        [SerializeField, Range(0f, 0.35f)] private float intersectionVisualLagRatio = 0.15f;
        [SerializeField, Range(0.6f, 0.85f)] private float cornerTurnRadius = 0.75f;   // 일반 교차로 회전 반경(타일 비율)
        [SerializeField] private float turnSignZ = -0.5f;           // 표지판 마커 z(신호와 분리 — 공존 타일 겹침 회피)

        [Header("Roundabout Tuning")]   // 재생 중 슬라이더 조정 → 통근 폴리라인 즉시 리베이크(QA G)
        // 궤도 반경(타일 비율) = 차도 정중앙. 차도는 섬 0.45(Island scale 0.9)와 링 1.1(Ring scale 2.2)
        // 사이의 고리이므로 중앙은 (0.45+1.1)/2 = 0.775 — 섬 스침·바깥 이탈 양쪽에 여유 0.325로 균등.
        // 2026-07-21 전 씬 통일. 씬에 0.3(섬 내부 = 관통)이 5개 있었다. Range는 인스펙터만 막고
        // 직렬화 값은 그대로 쓰이므로, 값을 바꿀 때는 반드시 7개 씬을 함께 맞출 것.
        [SerializeField, Range(0.5f, 1.1f)] private float roundaboutOrbitRadius = 0.775f;
        [SerializeField, Range(10f, 80f)] private float roundaboutEntryExitDeg = 45f;      // α — 진입/이탈을 링 둘레로 미는 각. 클수록 링 체류 짧아짐
        [SerializeField, Range(RoutePolyline.MinTransitionSpan, RoutePolyline.MaxTransitionSpan)]
        private float roundaboutTransitionTiles = RoutePolyline.MinTransitionSpan; // 전이 곡선 길이(타일). R=0.775·α=45°·λ=0.26 재측정에서 비우회전 최소 이격 0.654(섬 하한 0.62)

        [Header("Commute (2차 빌드)")]
        [SerializeField] private float parkingSlotInset = 0.32f;   // 건물 타일 내 칸 오프셋(타일 비율)
        [SerializeField, Range(0f, 1f)] private float parkingSettleSeconds = 0.3f;   // 도착 후 슬롯 정착 정지 안무(초)
        [SerializeField, Min(0.5f)] private float coinPopFlushSeconds = 5f;   // 코인 팝 버퍼 타임아웃 — 차 도착이 이만큼 없으면 타일 팝으로 방출

        [Header("Camera View")]
        [SerializeField, Range(1f, 89f)] private float angledViewDegrees = 35.264f;
        [Tooltip("지면에서 가장 가까운 A 줌 지점까지의 거리")]
        [SerializeField, Min(0.5f)] private float minimumZoomDistance = 2f;
        [Tooltip("A-B 줌 지점 사이에 적용할 거리")]
        [FormerlySerializedAs("zoomStepDistance")]
        [SerializeField, Min(0.1f)] private float zoomDistanceRange = 10f;
        [Tooltip("마우스 휠 입력 1단위당 변경할 카메라 거리")]
        [SerializeField, Min(0.001f)] private float zoomScrollSensitivity = 1f;
        [Tooltip("카메라가 목표 방향으로 회전하는 속도(초당 각도)")]
        [SerializeField, Min(1f)] private float cameraRotationSpeed = 500f;
        [Tooltip("카메라 회전이 목표 방향에 도달할 때까지의 감속 시간")]
        [SerializeField, Min(0.01f)] private float cameraRotationSmoothTime = 0.15f;
        [Tooltip("마우스 측면 버튼의 카메라 회전 방향을 반대로 적용")]
        [SerializeField] private bool invertCameraRotationDirection;

        private const float OrthographicSizePerDistance = 0.9375f;
        private const float CameraYawStepDegrees = 90f;
        private const int VehicleRenderQueue = (int)RenderQueue.Geometry + 10;
        private const float VehicleBodyLengthTiles = 0.38f;
        private const float VehicleBodyWidthTiles = 0.2f;
        private const float VehicleBodyHeightTiles = 0.28f;

        [Header("Colors")]
        [SerializeField] private Color boardColor = new Color(0.78f, 0.82f, 0.78f);
        [SerializeField] private Color gridLineColor = new Color(0.28f, 0.36f, 0.38f, 1f);
        [SerializeField] private Color roadFreeColor = new Color(0.32f, 0.36f, 0.43f);
        [SerializeField] private Color roadSlowColor = new Color(0.95f, 0.72f, 0.25f);
        [SerializeField] private Color roadJamColor = new Color(0.9f, 0.22f, 0.18f);
        [SerializeField] private Color roadPerimeterColor = new Color(0.72f, 0.72f, 0.7f);
        [SerializeField] private Color roadCenterLineColor = new Color(0.82f, 0.82f, 0.8f);
        [SerializeField] private Color houseColor = new Color(0.35f, 0.6f, 0.86f);
        [SerializeField] private Color officeColor = new Color(0.92f, 0.59f, 0.24f);
        [SerializeField] private Color schoolColor = new Color(0.66f, 0.42f, 0.82f);
        [SerializeField] private Color hospitalColor = new Color(0.88f, 0.3f, 0.34f);
        [SerializeField] private Color vehicleColor = new Color(0.12f, 0.12f, 0.16f);
        [SerializeField] private Color flowBurstColor = new Color(1f, 0.78f, 0.12f);
        [SerializeField] private Color roundaboutColor = new Color(0.35f, 0.78f, 0.45f);
        [SerializeField] private Color overpassColor = new Color(0.55f, 0.62f, 0.75f);
        [SerializeField] private Color onewayColor = new Color(0.95f, 0.85f, 0.15f);
        [SerializeField] private Color turnSignColor = new Color(0.95f, 0.35f, 0.75f);

        private readonly Dictionary<Vector2Int, TileVisual> tileVisuals = new();
        private readonly Dictionary<Vector2Int, GridCellView> gridCells = new();
        private readonly Dictionary<Vector2Int, SignalVisual> signalVisuals = new();
        private readonly Dictionary<Vector2Int, GameObject> roundaboutVisuals = new();
        private readonly Dictionary<Vector2Int, GameObject> overpassVisuals = new();
        private readonly Dictionary<Vector2Int, GameObject> onewayVisuals = new();
        private readonly Dictionary<Vector2Int, TurnSignVisual> turnSignVisuals = new();
        private readonly Dictionary<Vector2Int, GameObject> priorityRoadVisuals = new();
        private readonly Dictionary<Vector2Int, GameObject> highwayVisuals = new();
        private readonly List<RouteVehicle> vehicles = new();
        private readonly List<ExternalSelectableVehicle>
            externalSelectableVehicles = new();

        // 통근 상태(유일 경로). 위상 리빌드 시 재구성된다.
        private readonly List<CommuteCar> carSimMirrors = new();
        private readonly Dictionary<int, BakedRoutePair> bakedRoutes = new();   // 키 = RouteIndex, 해시 변경 시 재베이크
        private readonly Dictionary<CommuteCar, RouteVehicle> carVehicles = new();
        private Transform parkingRoot;
        private int commuteRoutesHash;
        private int commuteTuningHash;   // 로터리 노브(반경/α/전이) 해시 — 변경 시 지오메트리만 리베이크(QA G)
        private bool commuteRoutesBuilt;

        private CityFlowServices services;
        private IReadOnlyTileData tileData;
        private IPlacementService placement;
        private SimEngine simEngine;
        private float lastTickProgress;   // 틱 경계 검출용 직전 프레임 위상
        private bool tickEdge;            // 이번 프레임에 Sim이 한 틱 넘어갔나
        private ISignalControl signalControl;
        private IIntersectionFacilityService intersectionFacility;
        private ITrafficRuleService trafficRule;
        private IHighwayService highwayService;
        private Transform gridRoot;
        private Transform boardRoot;
        private Transform fieldTileRoot;
        private Transform tileRoot;
        private Transform vehicleRoot;
        private Transform signalRoot;
        private Transform highwayRoot;
        private Transform effectRoot;
        private int selectedSignalIndex;
        private Camera mainCamera;
        private DriveViewCamera driveViewCamera;
        private UIDockController dockController;
        private PlacementController placementController;
        private InfrastructurePlacementCoordinator infrastructurePlacementCoordinator;
        private Transform selectedVehicleTarget;
        private Vector3 cameraTarget;
        private Vector3 cameraUpDirection;
        private float zoomDistance;
        private float currentCameraYawDegrees;
        private float targetCameraYawDegrees;
        private float cameraYawVelocity;
        private bool isIsometricView;
        private int visualGridWidth;
        private int visualGridHeight;
        private Vector2Int gridOrigin;

        // 도착 코인 팝(항목 A): 풀 고정 크기 — 전부 사용 중이면 가장 오래된 슬롯을 라운드로빈으로 재사용.
        private const int CoinPopPoolSize = 12;
        private const float CoinPopDuration = 0.8f;
        private static readonly Color CoinPopColor = new Color(1f, 0.85f, 0.25f);
        private readonly CoinPop[] coinPops = new CoinPop[CoinPopPoolSize];
        private bool coinPopPoolReady;
        private int coinPopCursor;

        // 코인 팝 버퍼링(라이브 피드백 2026-07-17): ArrivalEvent는 Sim 연속 적분 리듬(누산기 1 돌파마다)이라
        // 차 도착과 무관하게 발화 — 즉시 팝은 "틱마다 그냥 올라가는 +1"로 보인다. 그래서 sink 타일별로
        // 코인을 적립만 하고, 방출은 (1) 통근 차 도착 순간 그 차 위에서, (2) 타임아웃 시 타일 중심에서,
        // (3) 위상 리빌드 시 전액 타일 팝으로. 금액 보존 불변식: 발생한 Coins 합 = 팝 표시 합.
        // 키는 제거하지 않고 Coins=0으로 리셋(sink 수 상한의 소형 딕셔너리 — 프레임 순회 중 구조 변경 회피).
        private struct PendingCoinPop
        {
            public int Coins;
            public float FirstQueuedTime;   // 마지막 방출 이후 첫 적립 시각 — 타임아웃 기준
        }

        private readonly Dictionary<Vector2Int, PendingCoinPop> pendingCoinPops = new();
        private readonly List<Vector2Int> coinPopFlushBuffer = new();   // 타임아웃/리빌드 방출용 재사용 버퍼(순회 중 딕셔너리 쓰기 회피)

        public GameObject FlowBurstPrefab => burstPrefab;
        public Transform EffectRoot => effectRoot;
        public int GridWidth => width;
        public int GridHeight => height;
        public Vector2Int GridOrigin => gridOrigin;
        public float TileSize => tileSize;
        public float LaneOffset => Mathf.Max(0f, laneOffset);
        public float VehicleMinHeadway =>
            simEngine != null
                ? simEngine.StandardVehicleFootprint.HeadwayTiles
                : Mathf.Max(0.05f, vehicleMinHeadway);
        public float IntersectionQueueInsetTiles =>
            Mathf.Max(0f, intersectionQueueInset);
        public float CornerTurnRadiusFraction =>
            GetCornerTurnRadiusFraction();
        public float RoundaboutOrbitRadiusTiles =>
            Mathf.Max(0f, roundaboutOrbitRadius);
        public float RoundaboutEntryExitRadians =>
            roundaboutEntryExitDeg * Mathf.Deg2Rad;
        public float RoundaboutTransitionSpanTiles =>
            RoundaboutTransitionSpan();
        public float VehicleGroundZ =>
            GetRoadSurfaceZ() - tileSize * 0.05f;
        public float RoadSurfaceZ => GetRoadSurfaceZ();
        public float FieldTileZ => fieldTileZ;
        public GameObject FieldTilePrefab => fieldTilePrefab;
        public float GridLineThickness => gridLineThickness;
        public Color GridLineColor => gridLineColor;
        public Material GridLineMaterial { get; private set; }
        public float FlowBurstSeconds => burstSeconds;
        public Color FlowBurstColor => flowBurstColor;
        public bool IsDriveViewActive => driveViewCamera != null && driveViewCamera.IsFollowing;
        public event System.Action GridCellsBuilt;

        public bool IsRoundaboutRoadTile(Vector2Int tile) =>
            IsRoundaboutTile(tile);

        public bool IsSharedIntersectionTile(Vector2Int tile) =>
            simEngine != null &&
            simEngine.IsSharedCarIntersection(tile);
        public event System.Action CoordinateSpaceChanged;

        public bool TryGetGridCell(Vector2Int coordinate, out GridCellView cell)
        {
            return gridCells.TryGetValue(coordinate, out cell);
        }

        public bool TryConfigureFieldTiles(GameObject prefab, float surfaceZ)
        {
            if (prefab == null || prefab.GetComponent<GridCellView>() == null)
            {
                Debug.LogWarning(
                    "[MainCityView] Field tile configuration requires a prefab " +
                    "with GridCellView.",
                    this);
                return false;
            }

            bool changed = fieldTilePrefab != prefab ||
                           !Mathf.Approximately(fieldTileZ, surfaceZ);
            fieldTilePrefab = prefab;
            fieldTileZ = surfaceZ;

            if (changed && services != null)
            {
                BuildBoard();
                BuildGridLines();
                RefreshAllTiles();
            }

            return true;
        }

        public void SetVisualGridExtent(
            int visualWidth,
            int visualHeight,
            bool frameCamera)
        {
            visualGridWidth = Mathf.Max(width, visualWidth);
            visualGridHeight = Mathf.Max(height, visualHeight);

            float requiredDistance =
                Mathf.Max(visualGridWidth, visualGridHeight) * 0.8f;
            zoomDistanceRange = Mathf.Max(
                zoomDistanceRange,
                requiredDistance - minimumZoomDistance);

            if (frameCamera)
            {
                zoomDistance = Mathf.Max(
                    minimumZoomDistance,
                    requiredDistance);
            }

            if (mainCamera != null)
            {
                ApplyCameraView();
            }
        }

        public void SetBaseGridLinesVisible(bool isVisible)
        {
            if (gridRoot != null)
            {
                gridRoot.gameObject.SetActive(isVisible);
            }
        }

        public string SelectedSignalSummary
        {
            get
            {
                if (!TryGetSelectedSignal(out Vector2Int signal))
                {
                    return "Signal -";
                }

                int offset = signalControl.GetSignalOffsetSlots(signal);
                int green = signalControl.GetSignalGreenSlots(signal);
                return $"Signal {selectedSignalIndex + 1}/{signalControl.SignalTiles.Count} ({signal.x}, {signal.y})\nOffset: {offset}  Green: {green}";
            }
        }

        private sealed class TileVisual
        {
            public GameObject Object;
            public Renderer Renderer;
            public MaterialPropertyBlock Block;
            public TileType Type;
            public bool UsesAuthoredLayout;
            public bool UsesAuthoredMaterial;
            public Quaternion LocalRotation = Quaternion.identity;
        }

        private sealed class SignalVisual
        {
            public GameObject Root;
            public Renderer HorizontalRenderer;
            public Renderer VerticalRenderer;
            public TrafficLightLensView[] LensViews;
            public MaterialPropertyBlock HorizontalBlock;
            public MaterialPropertyBlock VerticalBlock;
            public float GroundedZ;
        }

        // 턴 제한 표지판 마커: 몸통 바(Shaft) + 꺾인 촉(Tip) — Tip의 위치/회전만 모드에 따라 매 폴링 갱신
        // (같은 타일에서 Left↔Right 회전이 배치물 재생성 없이 그대로 반영되도록 — 오프셋 폴링 규약).
        private sealed class TurnSignVisual
        {
            public GameObject Root;
            public Transform Tip;
        }

        private sealed class RouteVehicle
        {
            public GameObject Object;
            public Renderer Renderer;
            public Renderer[] Renderers;
            public Renderer DetailRenderer;
            public bool UsesAuthoredVisual;
            public float CurrentSpeed;
            public float TargetDistance;
            public int TargetTileIndex;
            public int TargetRouteIndex;
            public bool HasTickTarget;
            public bool TargetAdvancing;   // 직전 틱에 코리도 상한이 전진했나(=천장이 움직이는가)
            public Vector3 Pos;   // 지난 프레임 위치·진행 방향 — 차간 유지 판정용(1프레임 지연 근사)
            public Vector3 Dir;
            public Vector2Int CurrentTile;
            public bool HasCurrentTile;
            public GameObject AngryMark;   // Jam 팝업(!) — vehicleRoot 소속(차량 자식 금지: 비균등 스케일)
            public int RouteIndex = -1;

            // 차량 개성(개성 패스 2/2): 바인딩 시 (Home, HomeSlot) 해시로 캐시. 판단 없음 — 연출용.
            public CarStyle Style;         // 스케일·팔레트·속도/가속 배수·출발 지연 프로파일
            public CarState LastState;     // 상태 전환 감지(출발 지연·정착 안무 트리거)
            public bool HasLastState;
            public bool Settling;          // 정착 안무 진행 플래그(도착 프레임 재진입 게이트)
            public float SettleRate;       // 정착 등속 속도(유닛/초) — 정착 시작 시 남은거리/시간으로 1회 산출
            public float TravelSpeed;      // 현재 주행 속도(월드유닛/초) — 가감속으로 수렴시킨다
            public GameObject BrakeLight;  // 후방 제동등(기본 off) — CreateDetailCube 패턴
            public bool BrakeOn;           // 제동등 상태 캐시(매 프레임 SetActive 금지)
        }

        private sealed class ExternalSelectableVehicle
        {
            public Object Owner;
            public Transform VisualRoot;
            public Vector3 LocalTravelAxis;
        }

        // 도착 코인 팝: 소형 텍스트 마크 풀(고정 크기, 러시아워 다발 대비 — 매 도착마다 Instantiate 금지).
        private sealed class CoinPop
        {
            public GameObject Object;
            public TextMesh Text;
            public Vector3 StartPos;
            public float StartTime;
        }

        // 방향별 별도 베이크(외부 리뷰 치명 판정): Inbound는 브리지 타일을 뒤집고 앵커를 스왑해 다시 베이크.
        // 이동은 항상 정방향 SampleAt — 역샘플 절대 금지.
        private sealed class BakedRoutePair
        {
            public RoutePolyline Outbound;
            public RoutePolyline Inbound;
        }

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            this.services = services;
            gridOrigin = services.WorldGrid?.InitialPlayableOrigin
                ?? Vector2Int.zero;
            if (!services.RegisterWorldCoordinateRoot(this))
            {
                Debug.LogWarning(
                    "[MainCityView] Another world coordinate root is already " +
                    "registered.",
                    this);
            }

            tileData = services.TileData;
            placement = services.Placement;
            simEngine = services.Placement as SimEngine;
            signalControl = services.Placement as ISignalControl;
            intersectionFacility = services.Placement as IIntersectionFacilityService;
            trafficRule = services.Placement as ITrafficRuleService;
            highwayService = services.Placement as IHighwayService;
            ResolveSimpleTownRoadVisualSet();

            services.Events.Placed += OnPlaced;
            services.Events.Arrival += OnArrival;

            if (services.Save != null)
            {
                services.Save.RestoreCompleted += OnRestoreCompleted;
            }

            BuildRoots();
            EnsureFlowBurstViews();
            BuildBoard();
            BuildGridLines();
            RefreshAllTiles();
            HospitalAmbulanceParkingView ambulanceView =
                GetComponent<HospitalAmbulanceParkingView>() ??
                gameObject.AddComponent<HospitalAmbulanceParkingView>();
            ambulanceView.Initialize(services);
            RefreshSignals();
            RefreshRoundabouts();
            RefreshOverpasses();
            RefreshOneways();
            RefreshTurnSigns();
            RefreshPriorityRoads();
            RefreshHighways();
            RefreshVehicles();
            InitializeCameraView();
            driveViewCamera = gameObject.AddComponent<DriveViewCamera>();
            driveViewCamera.Init(transform, mainCamera);
            dockController = FindAnyObjectByType<UIDockController>(FindObjectsInactive.Include);
            placementController = FindAnyObjectByType<PlacementController>(FindObjectsInactive.Include);
            infrastructurePlacementCoordinator = FindAnyObjectByType<InfrastructurePlacementCoordinator>(FindObjectsInactive.Include);

            if (dockController != null)
            {
                if (placementController != null) placementController.IsBuildMenuOpen = () => dockController.CurrentMenu == UIDockController.MenuType.Build;
                if (infrastructurePlacementCoordinator != null) infrastructurePlacementCoordinator.IsBuildMenuOpen = () => dockController.CurrentMenu == UIDockController.MenuType.Build;
            }

            gameObject.AddComponent<FloatingWindowService>().Init(width * tileSize, height * tileSize, false);
        }

        public void ApplyCoordinateSpace(
            IWorldCoordinateSpace coordinateSpace)
        {
            if (coordinateSpace == null)
            {
                return;
            }

            bool changed =
                Vector3.SqrMagnitude(
                    transform.position - coordinateSpace.Origin) > 0.000001f ||
                Quaternion.Angle(
                    transform.rotation,
                    coordinateSpace.CoordinateRotation) > 0.001f;
            if (!changed)
            {
                return;
            }

            transform.SetPositionAndRotation(
                coordinateSpace.Origin,
                coordinateSpace.CoordinateRotation);

            if (mainCamera != null)
            {
                InitializeCameraView();
            }

            CoordinateSpaceChanged?.Invoke();
            Debug.Log(
                $"[MainCityView] Applied {coordinateSpace.Plane} world " +
                "coordinate space.",
                this);
        }

        private void OnDestroy()
        {
            if (driveViewCamera != null)
            {
                ExitDriveView();
            }

            if (services == null)
            {
                return;
            }

            services.Events.Placed -= OnPlaced;
            services.Events.Arrival -= OnArrival;

            if (services.Save != null)
            {
                services.Save.RestoreCompleted -= OnRestoreCompleted;
            }

        }

        private void InitializeCameraView()
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                return;
            }

            visualGridWidth = Mathf.Max(width, visualGridWidth);
            visualGridHeight = Mathf.Max(height, visualGridHeight);

            cameraTarget = transform.TransformPoint(new Vector3(
                width * tileSize * 0.5f,
                height * tileSize * 0.5f,
                0f));
            cameraUpDirection = (transform.up - transform.right).normalized;
            zoomDistance = minimumZoomDistance;
            currentCameraYawDegrees = 0f;
            targetCameraYawDegrees = 0f;
            cameraYawVelocity = 0f;
            isIsometricView = true;
            ApplyCameraView();
        }

        private bool HandleCameraViewInput()
        {
            if (mainCamera == null)
            {
                return false;
            }

            bool cameraViewChanged = false;
            bool cameraModeChanged = false;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.tabKey.wasPressedThisFrame)
            {
                isIsometricView = !isIsometricView;
                cameraViewChanged = true;
                cameraModeChanged = true;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                float scrollY = mouse.scroll.ReadValue().y;
                bool isOverUI = GreenFeedInputGuard.IsPointerCaptured ||
                                (UnityEngine.EventSystems.EventSystem.current != null &&
                                 UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject());
                if (!isOverUI)
                {
                    int yawStep = 0;
                    if (mouse.backButton.wasPressedThisFrame)
                    {
                        yawStep--;
                    }

                    if (mouse.forwardButton.wasPressedThisFrame)
                    {
                        yawStep++;
                    }

                    if (yawStep != 0)
                    {
                        float direction = invertCameraRotationDirection ? -1f : 1f;
                        targetCameraYawDegrees += yawStep * CameraYawStepDegrees * direction;
                    }

                    float nextZoomDistance = Mathf.Clamp(
                        zoomDistance - scrollY * zoomScrollSensitivity,
                        minimumZoomDistance,
                        minimumZoomDistance + zoomDistanceRange);
                    if (!Mathf.Approximately(nextZoomDistance, zoomDistance))
                    {
                        zoomDistance = nextZoomDistance;
                        cameraViewChanged = true;
                    }
                }

                if (mouse.middleButton.isPressed)
                {
                    Vector2 pointerDelta = mouse.delta.ReadValue();
                    if (pointerDelta.sqrMagnitude > 0f)
                    {
                        float worldUnitsPerPixel = mainCamera.orthographicSize * 2f / Mathf.Max(1, Screen.height);
                        Vector3 boardRight = Vector3.ProjectOnPlane(mainCamera.transform.right, transform.forward).normalized;
                        Vector3 boardUp = Vector3.ProjectOnPlane(mainCamera.transform.up, transform.forward).normalized;
                        cameraTarget -= (boardRight * pointerDelta.x + boardUp * pointerDelta.y) * worldUnitsPerPixel;
                        cameraViewChanged = true;
                    }
                }
            }

            if (cameraViewChanged)
            {
                if (cameraModeChanged && tileData != null)
                {
                    RefreshAllTiles();
                }
            }

            return cameraViewChanged;
        }

        private bool UpdateCameraRotation()
        {
            float previousYawDegrees = currentCameraYawDegrees;
            currentCameraYawDegrees = Mathf.SmoothDamp(
                currentCameraYawDegrees,
                targetCameraYawDegrees,
                ref cameraYawVelocity,
                cameraRotationSmoothTime,
                cameraRotationSpeed,
                Time.deltaTime);

            if (Mathf.Abs(targetCameraYawDegrees - currentCameraYawDegrees) < 0.01f
                && Mathf.Abs(cameraYawVelocity) < 0.01f)
            {
                currentCameraYawDegrees = targetCameraYawDegrees;
                cameraYawVelocity = 0f;
            }

            return !Mathf.Approximately(previousYawDegrees, currentCameraYawDegrees);
        }

        private void ApplyCameraView()
        {
            float viewDistance = Mathf.Clamp(
                zoomDistance,
                minimumZoomDistance,
                minimumZoomDistance + zoomDistanceRange);
            mainCamera.useOcclusionCulling = false;
            Vector3 cameraPosition = cameraTarget - transform.forward * viewDistance;
            Quaternion yawRotation = Quaternion.AngleAxis(
                currentCameraYawDegrees,
                transform.forward);
            Vector3 rotatedCameraUpDirection = yawRotation * cameraUpDirection;

            if (isIsometricView)
            {
                Vector3 southEastDirection = yawRotation * (transform.right - transform.up).normalized;
                float angleRadians = angledViewDegrees * Mathf.Deg2Rad;
                Vector3 angledOffsetDirection = southEastDirection * Mathf.Cos(angleRadians)
                    - transform.forward * Mathf.Sin(angleRadians);
                Vector3 cameraForward = -angledOffsetDirection;
                int cameraGridWidth = Mathf.Max(width, visualGridWidth);
                int cameraGridHeight = Mathf.Max(height, visualGridHeight);
                float projectedBoardHalfDepth = cameraGridWidth * tileSize * 0.5f
                    * Mathf.Abs(Vector3.Dot(transform.right, cameraForward))
                    + cameraGridHeight * tileSize * 0.5f
                    * Mathf.Abs(Vector3.Dot(transform.up, cameraForward));
                float safeCameraDistance = projectedBoardHalfDepth
                    + mainCamera.nearClipPlane
                    + 1f;
                Vector3 angledOffset = angledOffsetDirection * safeCameraDistance;
                cameraPosition = cameraTarget + angledOffset;
            }

            Quaternion cameraRotation = Quaternion.LookRotation(
                cameraTarget - cameraPosition,
                rotatedCameraUpDirection);
            mainCamera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            mainCamera.orthographicSize = viewDistance * OrthographicSizePerDistance;
        }

        private void Update()
        {
            if (selectedVehicleTarget != null && !IsDriveViewActive)
            {
                ExitDriveView();
            }

            bool cameraViewChanged = false;
            if (!OfflineSettlementPopup.IsInteractionBlocked)
            {
                HandleVehicleSelectionInput();
                if (!IsDriveViewActive)
                {
                    cameraViewChanged = HandleCameraViewInput();
                }
            }

            if (!IsDriveViewActive)
            {
                cameraViewChanged |= UpdateCameraRotation();
                if (cameraViewChanged)
                {
                    ApplyCameraView();
                }
            }

            if (tileData == null)
            {
                return;
            }

            if (!IsDriveViewActive && !OfflineSettlementPopup.IsInteractionBlocked)
            {
                HandleSignalInput();
            }
            RefreshSignals();
            RefreshRoundabouts();
            RefreshOverpasses();
            RefreshOneways();
            RefreshTurnSigns();
            RefreshPriorityRoads();
            RefreshHighways();
            RefreshVehicles();
            RefreshCoinPops();
        }

        private void HandleVehicleSelectionInput()
        {
            if (IsDriveViewActive || IsVehicleSelectionBlocked() || mainCamera == null || driveViewCamera == null)
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            {
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            if (TryGetVehicleAtScreenPosition(
                    mouse.position.ReadValue(),
                    out Transform vehicleTarget,
                    out Vector3 localTravelAxis))
            {
                selectedVehicleTarget = vehicleTarget;
                driveViewCamera.Follow(
                    vehicleTarget,
                    localTravelAxis);
                if (IsDriveViewActive)
                {
                    dockController?.SetDriveViewActive(true);
                }
                else
                {
                    selectedVehicleTarget = null;
                }
            }
        }

        private bool IsVehicleSelectionBlocked()
        {
            if (dockController == null)
            {
                dockController = FindAnyObjectByType<UIDockController>(FindObjectsInactive.Include);
            }

            if (placementController == null)
            {
                placementController = FindAnyObjectByType<PlacementController>(FindObjectsInactive.Include);
            }

            if (infrastructurePlacementCoordinator == null)
            {
                infrastructurePlacementCoordinator = FindAnyObjectByType<InfrastructurePlacementCoordinator>(FindObjectsInactive.Include);
            }

            return (dockController != null && dockController.IsAnyMenuOpen)
                || (placementController != null && placementController.IsBuildingMode)
                || (infrastructurePlacementCoordinator != null && infrastructurePlacementCoordinator.IsBuildingMode);
        }

        private void ExitDriveView()
        {
            selectedVehicleTarget = null;
            if (driveViewCamera != null)
            {
                driveViewCamera.StopFollowing();
            }
            if (dockController != null)
            {
                dockController.SetDriveViewActive(false);
            }
        }

        public bool IsPointerOverVehicle(Vector2 screenPosition)
        {
            return TryGetVehicleAtScreenPosition(
                screenPosition,
                out _,
                out _);
        }

        private bool TryGetVehicleAtScreenPosition(
            Vector2 screenPosition,
            out Transform vehicleTarget,
            out Vector3 localTravelAxis)
        {
            vehicleTarget = null;
            localTravelAxis = Vector3.right;
            if (mainCamera == null || !mainCamera.enabled)
            {
                return false;
            }

            Ray ray = mainCamera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(
                ray,
                out RaycastHit hit,
                mainCamera.farClipPlane,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            return TryResolveSelectableVehicle(
                hit.collider.transform,
                out vehicleTarget,
                out localTravelAxis);
        }

        internal bool TryResolveSelectableVehicle(
            Transform hitTransform,
            out Transform vehicleTarget,
            out Vector3 localTravelAxis)
        {
            vehicleTarget = null;
            localTravelAxis = Vector3.right;
            if (hitTransform == null)
            {
                return false;
            }

            for (int vehicleIndex = 0; vehicleIndex < vehicles.Count; vehicleIndex++)
            {
                RouteVehicle candidate = vehicles[vehicleIndex];
                if (!candidate.Object.activeSelf
                    || candidate.Renderer == null
                    || !candidate.Renderer.enabled)
                {
                    continue;
                }

                Transform vehicleTransform = candidate.Object.transform;
                if (hitTransform == vehicleTransform || hitTransform.IsChildOf(vehicleTransform))
                {
                    vehicleTarget = vehicleTransform;
                    return true;
                }
            }

            for (int vehicleIndex =
                     externalSelectableVehicles.Count - 1;
                 vehicleIndex >= 0;
                 vehicleIndex--)
            {
                ExternalSelectableVehicle candidate =
                    externalSelectableVehicles[vehicleIndex];
                if (candidate.Owner == null ||
                    candidate.VisualRoot == null)
                {
                    externalSelectableVehicles.RemoveAt(
                        vehicleIndex);
                    continue;
                }

                if (!candidate.VisualRoot.gameObject
                        .activeInHierarchy)
                {
                    continue;
                }

                if (hitTransform != candidate.VisualRoot &&
                    !hitTransform.IsChildOf(
                        candidate.VisualRoot))
                {
                    continue;
                }

                vehicleTarget = candidate.VisualRoot;
                localTravelAxis =
                    candidate.LocalTravelAxis;
                return true;
            }

            return false;
        }

        public void RegisterExternalSelectableVehicle(
            Object owner,
            Transform visualRoot,
            Vector3 localTravelAxis)
        {
            if (owner == null || visualRoot == null)
            {
                return;
            }

            EnsureVehicleSelectionCollider(visualRoot);
            Vector3 normalizedTravelAxis =
                localTravelAxis.sqrMagnitude > 0.0001f
                    ? localTravelAxis.normalized
                    : Vector3.right;

            for (int i =
                     externalSelectableVehicles.Count - 1;
                 i >= 0;
                 i--)
            {
                ExternalSelectableVehicle candidate =
                    externalSelectableVehicles[i];
                if (candidate.Owner == null ||
                    candidate.VisualRoot == null)
                {
                    externalSelectableVehicles.RemoveAt(i);
                    continue;
                }

                if (candidate.Owner != owner)
                {
                    continue;
                }

                candidate.VisualRoot = visualRoot;
                candidate.LocalTravelAxis =
                    normalizedTravelAxis;
                return;
            }

            externalSelectableVehicles.Add(
                new ExternalSelectableVehicle
                {
                    Owner = owner,
                    VisualRoot = visualRoot,
                    LocalTravelAxis =
                        normalizedTravelAxis
                });
        }

        public void UnregisterExternalSelectableVehicle(
            Object owner)
        {
            for (int i =
                     externalSelectableVehicles.Count - 1;
                 i >= 0;
                 i--)
            {
                ExternalSelectableVehicle candidate =
                    externalSelectableVehicles[i];
                if (candidate.Owner == null ||
                    candidate.VisualRoot == null ||
                    candidate.Owner == owner)
                {
                    if (candidate.VisualRoot ==
                        selectedVehicleTarget)
                    {
                        ExitDriveView();
                    }

                    externalSelectableVehicles.RemoveAt(i);
                }
            }
        }

        private static void EnsureVehicleSelectionCollider(
            Transform visualRoot)
        {
            if (visualRoot.GetComponentInChildren<
                    Collider>(true) != null)
            {
                return;
            }

            Renderer[] renderers =
                visualRoot.GetComponentsInChildren<
                    Renderer>(true);
            Bounds localBounds = default;
            bool hasBounds = false;
            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                Renderer renderer =
                    renderers[rendererIndex];
                Bounds rendererBounds =
                    renderer.localBounds;
                Vector3 min = rendererBounds.min;
                Vector3 max = rendererBounds.max;

                for (int corner = 0;
                     corner < 8;
                     corner++)
                {
                    Vector3 localCorner = new(
                        (corner & 1) == 0
                            ? min.x
                            : max.x,
                        (corner & 2) == 0
                            ? min.y
                            : max.y,
                        (corner & 4) == 0
                            ? min.z
                            : max.z);
                    Vector3 rootCorner =
                        visualRoot.InverseTransformPoint(
                            renderer.transform
                                .TransformPoint(
                                    localCorner));

                    if (!hasBounds)
                    {
                        localBounds = new Bounds(
                            rootCorner,
                            Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(
                            rootCorner);
                    }
                }
            }

            if (!hasBounds)
            {
                return;
            }

            BoxCollider selectionCollider =
                visualRoot.gameObject
                    .AddComponent<BoxCollider>();
            selectionCollider.center =
                localBounds.center;
            selectionCollider.size =
                localBounds.size;
        }

        private void BuildRoots()
        {
            boardRoot = CreateChildRoot("Board");
            fieldTileRoot = CreateChildRoot("GridCells");
            gridRoot = CreateChildRoot("GridLines");
            tileRoot = CreateChildRoot("Tiles");
            vehicleRoot = CreateChildRoot("Vehicles");
            signalRoot = CreateChildRoot("Signals");
            highwayRoot = CreateChildRoot("Highways");
            effectRoot = CreateChildRoot("Effects");
        }

        private Transform CreateChildRoot(string rootName)
        {
            Transform existing = transform.Find(rootName);

            if (existing == null)
            {
                GameObject root = new GameObject(rootName);
                root.transform.SetParent(transform, false);
                existing = root.transform;
            }

            existing.localPosition = Vector3.zero;
            existing.localRotation = Quaternion.identity;
            existing.localScale = Vector3.one;
            return existing;
        }

        private void EnsureFlowBurstViews()
        {
            FlowBurstView burstView = GetComponent<FlowBurstView>();
            if (burstView == null)
            {
                burstView = gameObject.AddComponent<FlowBurstView>();
            }
            burstView.Configure(this);
            burstView.Initialize(services);

            FlowBurstJuice burstJuice = GetComponent<FlowBurstJuice>();
            if (burstJuice == null)
            {
                burstJuice = gameObject.AddComponent<FlowBurstJuice>();
            }
            burstJuice.Initialize(services);
        }

        private void BuildBoard()
        {
            if (boardRoot == null)
            {
                return;
            }

            ClearChildren(boardRoot);

            GameObject board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "GridBoard";
            board.transform.SetParent(boardRoot, false);
            board.transform.localPosition = new Vector3(width * tileSize * 0.5f, height * tileSize * 0.5f, 0.15f);
            board.transform.localScale = new Vector3(width * tileSize, height * tileSize, 0.04f);
            Renderer boardRenderer = board.GetComponent<Renderer>();
            bool usesFieldTiles = BuildFieldTiles();

            if (usesFieldTiles)
            {
                boardRenderer.enabled = false;
            }
            else
            {
                boardRenderer.sharedMaterial = CreateGridMaterial(boardRenderer.sharedMaterial);
            }

            boardRenderer.allowOcclusionWhenDynamic = false;
            boardRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            boardRenderer.receiveShadows = false;
        }

        private bool BuildFieldTiles()
        {
            if (fieldTileRoot == null)
            {
                return false;
            }

            gridCells.Clear();
            ClearChildren(fieldTileRoot);

            if (fieldTilePrefab == null)
            {
                return false;
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    GameObject fieldTile = Instantiate(fieldTilePrefab, fieldTileRoot, false);
                    Vector2Int coordinate =
                        gridOrigin + new Vector2Int(x, y);
                    GridCellView gridCell = fieldTile.GetComponent<GridCellView>();

                    if (gridCell == null)
                    {
                        Debug.LogError(
                            $"[MainCityView] Field tile prefab is missing GridCellView at {coordinate}.",
                            fieldTile);
                        Destroy(fieldTile);
                        continue;
                    }

                    fieldTile.name = $"GridCell_{x}_{y}";
                    fieldTile.transform.localPosition = new Vector3(
                        (x + 0.5f) * tileSize,
                        (y + 0.5f) * tileSize,
                        fieldTileZ);
                    fieldTile.transform.localRotation = Quaternion.identity;
                    fieldTile.transform.localScale = Vector3.one;
                    gridCell.Initialize(coordinate);
                    gridCells.Add(coordinate, gridCell);
                }
            }

            GridCellsBuilt?.Invoke();
            Debug.Log($"[MainCityView] Built {gridCells.Count} grid cell visuals.");
            return gridCells.Count > 0;
        }

        private void BuildGridLines()
        {
            if (gridRoot == null)
            {
                return;
            }

            ClearChildren(gridRoot);

            if (fieldTilePrefab == null)
            {
                return;
            }

            const float lineDepth = 0.005f;
            const float surfaceOffset = 0.01f;
            float lineZ = fieldTileZ - surfaceOffset;
            GridLineMaterial = CreateUnlitMaterial(renderQueue: 1900);

            for (int x = 0; x <= width; x++)
            {
                CreateGridLine(
                    $"GridLine_V_{x}",
                    new Vector3(x * tileSize, height * tileSize * 0.5f, lineZ),
                    new Vector3(gridLineThickness, height * tileSize, lineDepth),
                    GridLineMaterial);
            }

            for (int y = 0; y <= height; y++)
            {
                CreateGridLine(
                    $"GridLine_H_{y}",
                    new Vector3(width * tileSize * 0.5f, y * tileSize, lineZ),
                    new Vector3(width * tileSize, gridLineThickness, lineDepth),
                    GridLineMaterial);
            }
        }

        private void CreateGridLine(
            string lineName,
            Vector3 localPosition,
            Vector3 localScale,
            Material sharedMaterial)
        {
            GameObject line =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            line.name = lineName;
            line.transform.SetParent(gridRoot, false);
            line.transform.localPosition = localPosition;
            line.transform.localScale = localScale;

            Collider collider = line.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(collider);
                }
                else
                {
                    DestroyImmediate(collider);
                }
            }

            Renderer renderer = line.GetComponent<Renderer>();
            renderer.sharedMaterial = sharedMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ApplyRendererColor(renderer, gridLineColor);
        }

        private static void ClearChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Destroy(root.GetChild(i).gameObject);
            }
        }

        private void RefreshAllTiles()
        {
            int minX = services?.WorldGrid != null ? 0 : gridOrigin.x;
            int minY = services?.WorldGrid != null ? 0 : gridOrigin.y;
            int maxX = services?.WorldGrid?.WorldWidth ?? gridOrigin.x + width;
            int maxY = services?.WorldGrid?.WorldHeight ?? gridOrigin.y + height;

            for (int y = minY; y < maxY; y++)
            {
                for (int x = minX; x < maxX; x++)
                {
                    Vector2Int tile = new Vector2Int(x, y);
                    if (services?.WorldGrid != null &&
                        !services.WorldGrid.IsTileUnlocked(tile))
                    {
                        continue;
                    }

                    TileType type = tileData.GetTileType(tile);
                    if (TileFootprint.IsBuilding(type) &&
                        !tileData.IsFootprintAnchor(tile))
                    {
                        RemoveTileVisual(tile);
                        continue;
                    }
                    RefreshTile(tile, type);
                }
            }
        }

        private void RebuildRestoredVisuals()
        {
            ClearChildren(tileRoot);
            tileVisuals.Clear();

            ClearChildren(signalRoot);
            signalVisuals.Clear();
            roundaboutVisuals.Clear();
            overpassVisuals.Clear();
            onewayVisuals.Clear();
            turnSignVisuals.Clear();
            priorityRoadVisuals.Clear();
            ClearChildren(highwayRoot);
            highwayVisuals.Clear();

            ClearChildren(vehicleRoot);
            vehicles.Clear();
            ResetCommuteState();   // 위상 리빌드: 통근 베이크/차량 매핑/주차칸 폐기 → RefreshVehicles가 재구성

            selectedSignalIndex = 0;

            RefreshAllTiles();
            RefreshSignals();
            RefreshRoundabouts();
            RefreshOverpasses();
            RefreshOneways();
            RefreshTurnSigns();
            RefreshPriorityRoads();
            RefreshHighways();
            RefreshVehicles();

            Debug.Log("[MainCityView] Restored city visuals rebuilt.");
        }

        private void RefreshTile(Vector2Int tile, TileType type)
        {
            if (type == TileType.Empty)
            {
                RemoveTileVisual(tile);
                return;
            }

            if (type == TileType.SpecialBuilding)
            {
                RemoveTileVisual(tile);
                return;
            }

            if (!tileVisuals.TryGetValue(tile, out TileVisual visual))
            {
                visual = CreateTileVisual(
                    tile,
                    type,
                    tileRoot,
                    includeRoadNetworkDetails: true);
                tileVisuals.Add(tile, visual);
            }
            else if (visual.Type != type)
            {
                RemoveTileVisual(tile);
                visual = CreateTileVisual(
                    tile,
                    type,
                    tileRoot,
                    includeRoadNetworkDetails: true);
                tileVisuals.Add(tile, visual);
            }

            visual.Type = type;
            Vector3 tileScale = GetTileScale(type);
            float tileZ = type == TileType.Road
                ? visual.UsesAuthoredLayout
                    ? fieldTileZ
                    : 0f
                : -tileScale.z * 0.5f;
            visual.Object.SetActive(true);
            Renderer[] renderers = visual.UsesAuthoredLayout
                ? visual.Object.GetComponentsInChildren<Renderer>(true)
                : new[] { visual.Renderer };
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = true;
                renderers[i].forceRenderingOff = false;
                renderers[i].allowOcclusionWhenDynamic = false;
            }
            if (TileFootprint.IsBuilding(type))
            {
                visual.Object.transform.localPosition = FootprintToLocal(tile, type);
                visual.Object.transform.localRotation =
                    GetBuildingRotation(tile, type);
                visual.Object.transform.localScale = Vector3.one;
            }
            else
            {
                visual.Object.transform.localPosition = GridToLocal(tile, tileZ);
                visual.Object.transform.localRotation =
                    visual.UsesAuthoredLayout
                        ? visual.LocalRotation
                        : Quaternion.identity;
                visual.Object.transform.localScale =
                    visual.UsesAuthoredLayout
                        ? Vector3.one
                        : tileScale;
            }
            ApplyTileColor(tile, visual);
            ConfigureRoadCongestionView(tile, visual);
        }

        private void ConfigureRoadCongestionView(Vector2Int tile, TileVisual visual)
        {
            RoadCongestionView[] congestionViews =
                visual.Object.GetComponentsInChildren<RoadCongestionView>(true);

            if (visual.Type != TileType.Road)
            {
                for (int i = 0; i < congestionViews.Length; i++)
                {
                    Destroy(congestionViews[i]);
                }
                return;
            }

            Renderer[] renderers = visual.UsesAuthoredLayout
                ? visual.Object.GetComponentsInChildren<Renderer>(true)
                : new[] { visual.Renderer };
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (IsGeneratedRoadDecoration(renderer))
                {
                    RoadCongestionView decorationView =
                        renderer.GetComponent<RoadCongestionView>();
                    if (decorationView != null)
                    {
                        Destroy(decorationView);
                    }
                    continue;
                }

                RoadCongestionView congestionView =
                    renderer.GetComponent<RoadCongestionView>();
                if (congestionView == null)
                {
                    congestionView =
                        renderer.gameObject.AddComponent<RoadCongestionView>();
                }

                congestionView.Configure(
                    tile,
                    renderer,
                    visual.UsesAuthoredMaterial
                        ? Color.white
                        : roadFreeColor,
                    roadSlowColor,
                    roadJamColor);
                congestionView.Initialize(services);
            }
        }

        private TileVisual CreateTileVisual(
            Vector2Int tile,
            TileType type,
            Transform parent,
            bool includeRoadNetworkDetails)
        {
            if (TileFootprint.IsBuilding(type))
            {
                return CreateBuildingVisual(tile, type, parent);
            }

            if (type == TileType.Road &&
                TryCreateSimpleTownRoadVisual(
                    tile,
                    parent,
                    includeRoadNetworkDetails,
                    out TileVisual roadVisual))
            {
                return roadVisual;
            }

            GameObject prefab = GetPrefab(type);
            GameObject instance = InstantiatePrefabOrPrimitive(prefab, PrimitiveType.Cube);
            instance.name = $"{type}_{tile.x}_{tile.y}";
            instance.transform.SetParent(parent, false);

            Renderer renderer = PrepareRenderer(instance.GetComponentInChildren<Renderer>());
            if (type == TileType.Road &&
                includeRoadNetworkDetails)
            {
                AddRoadCenterLines(instance.transform, tile);
            }
            else if (prefab == null)
            {
                AddFallbackBuildingDetails(instance.transform, type);
            }

            return new TileVisual
            {
                Object = instance,
                Renderer = renderer,
                Block = new MaterialPropertyBlock(),
                Type = type
            };
        }

        private bool TryCreateSimpleTownRoadVisual(
            Vector2Int tile,
            Transform parent,
            bool includeRoadNetworkDetails,
            out TileVisual visual)
        {
            visual = null;
            SimpleTownRoadVisualSetSO visualSet =
                ResolveSimpleTownRoadVisualSet();
            if (visualSet == null)
            {
                return false;
            }

            SimpleTownRoadConnections connections =
                includeRoadNetworkDetails
                    ? GetRoadConnections(tile)
                    : SimpleTownRoadConnections.None;
            SimpleTownRoadSelection selection =
                SimpleTownRoadTopology.Resolve(connections);
            GameObject prefab =
                visualSet.GetRoadPrefab(selection.Shape);
            if (prefab == null)
            {
                return false;
            }

            GameObject root =
                new GameObject($"Road_{tile.x}_{tile.y}");
            root.transform.SetParent(parent, false);

            GameObject model = Instantiate(prefab, root.transform, false);
            model.name = "RoadModel";
            float roadVisualSize = tileSize;
            FitSimpleTownPrefab(
                model.transform,
                root.transform,
                new Vector2(roadVisualSize, roadVisualSize));
            AlignRendererTopToSurface(
                model.transform,
                root.transform,
                GetRoadSurfaceZ() - fieldTileZ);

            Renderer renderer =
                PrepareAuthoredRenderers(model);
            AddConnectedRoadDetails(
                root.transform,
                tile,
                connections,
                roadVisualSize);

            visual = new TileVisual
            {
                Object = root,
                Renderer = renderer,
                Block = new MaterialPropertyBlock(),
                Type = TileType.Road,
                UsesAuthoredLayout = true,
                UsesAuthoredMaterial = true,
                LocalRotation = Quaternion.identity
            };
            return true;
        }

        private void AddConnectedRoadDetails(
            Transform road,
            Vector2Int tile,
            SimpleTownRoadConnections connections,
            float roadVisualSize)
        {
            GameObject perimeter =
                new GameObject("RoadPerimeter");
            perimeter.transform.SetParent(road, false);

            SimpleTownRoadConnections perimeterSides =
                SimpleTownRoadTopology.GetPerimeterSides(connections);
            float borderWidth = roadVisualSize * 0.075f;
            float borderOffset =
                roadVisualSize * 0.5f - borderWidth * 0.5f;
            float detailDepth = roadVisualSize * 0.018f;
            float roadSurfaceZ = -roadVisualSize * 0.01f;
            float detailZ =
                roadSurfaceZ - detailDepth * 0.5f;

            AddRoadPerimeterSide(
                perimeter.transform,
                perimeterSides,
                SimpleTownRoadConnections.North,
                new Vector3(0f, borderOffset, detailZ),
                new Vector3(
                    roadVisualSize,
                    borderWidth,
                    detailDepth));
            AddRoadPerimeterSide(
                perimeter.transform,
                perimeterSides,
                SimpleTownRoadConnections.East,
                new Vector3(borderOffset, 0f, detailZ),
                new Vector3(
                    borderWidth,
                    roadVisualSize,
                    detailDepth));
            AddRoadPerimeterCorner(
                perimeter.transform,
                tile,
                connections,
                SimpleTownRoadConnections.North,
                SimpleTownRoadConnections.East,
                Vector2Int.up + Vector2Int.right,
                new Vector3(
                    borderOffset,
                    borderOffset,
                    detailZ),
                borderWidth,
                detailDepth);
            AddRoadPerimeterCorner(
                perimeter.transform,
                tile,
                connections,
                SimpleTownRoadConnections.East,
                SimpleTownRoadConnections.South,
                Vector2Int.right + Vector2Int.down,
                new Vector3(
                    borderOffset,
                    -borderOffset,
                    detailZ),
                borderWidth,
                detailDepth);
            AddRoadPerimeterCorner(
                perimeter.transform,
                tile,
                connections,
                SimpleTownRoadConnections.South,
                SimpleTownRoadConnections.West,
                Vector2Int.down + Vector2Int.left,
                new Vector3(
                    -borderOffset,
                    -borderOffset,
                    detailZ),
                borderWidth,
                detailDepth);
            AddRoadPerimeterCorner(
                perimeter.transform,
                tile,
                connections,
                SimpleTownRoadConnections.West,
                SimpleTownRoadConnections.North,
                Vector2Int.left + Vector2Int.up,
                new Vector3(
                    -borderOffset,
                    borderOffset,
                    detailZ),
                borderWidth,
                detailDepth);
            AddRoadPerimeterSide(
                perimeter.transform,
                perimeterSides,
                SimpleTownRoadConnections.South,
                new Vector3(0f, -borderOffset, detailZ),
                new Vector3(
                    roadVisualSize,
                    borderWidth,
                    detailDepth));
            AddRoadPerimeterSide(
                perimeter.transform,
                perimeterSides,
                SimpleTownRoadConnections.West,
                new Vector3(-borderOffset, 0f, detailZ),
                new Vector3(
                    borderWidth,
                    roadVisualSize,
                    detailDepth));

            GameObject centerLines =
                new GameObject("RoadCenterLines");
            centerLines.transform.SetParent(road, false);
            if (!SimpleTownRoadTopology.ShouldDrawCenterLines(
                    connections))
            {
                return;
            }

            AddCenteredRoadLine(
                centerLines.transform,
                connections,
                roadVisualSize);
        }

        private void AddRoadPerimeterSide(
            Transform parent,
            SimpleTownRoadConnections perimeterSides,
            SimpleTownRoadConnections side,
            Vector3 position,
            Vector3 scale)
        {
            if ((perimeterSides & side) == 0)
            {
                return;
            }

            Renderer border = CreateRoadDecoration(
                parent,
                $"Perimeter_{side}",
                position,
                scale);
            ApplyRendererColor(border, roadPerimeterColor);
        }

        private void AddRoadPerimeterCorner(
            Transform parent,
            Vector2Int tile,
            SimpleTownRoadConnections connections,
            SimpleTownRoadConnections firstSide,
            SimpleTownRoadConnections secondSide,
            Vector2Int diagonalDirection,
            Vector3 position,
            float borderWidth,
            float detailDepth)
        {
            if (!SimpleTownRoadTopology.ShouldDrawPerimeterCorner(
                    connections,
                    firstSide,
                    secondSide,
                    IsRoadTile(tile + diagonalDirection)))
            {
                return;
            }

            Renderer corner = CreateRoadDecoration(
                parent,
                $"PerimeterCorner_{firstSide}_{secondSide}",
                position,
                new Vector3(
                    borderWidth,
                    borderWidth,
                    detailDepth));
            ApplyRendererColor(corner, roadPerimeterColor);
        }

        private void AddCenteredRoadLine(
            Transform parent,
            SimpleTownRoadConnections connections,
            float roadVisualSize)
        {
            bool horizontal =
                SimpleTownRoadTopology
                    .IsCenterLineHorizontal(connections);
            float lineLength = roadVisualSize * 0.22f;
            float lineWidth = roadVisualSize * 0.025f;
            float lineDepth = roadVisualSize * 0.012f;
            float roadSurfaceZ = -roadVisualSize * 0.01f;
            Vector3 scale = horizontal
                ? new Vector3(lineLength, lineWidth, lineDepth)
                : new Vector3(lineWidth, lineLength, lineDepth);
            Vector3 position = new Vector3(
                0f,
                0f,
                roadSurfaceZ - lineDepth * 0.5f);
            Renderer line = CreateRoadDecoration(
                parent,
                "CenterLine",
                position,
                scale);
            ApplyRendererColor(line, roadCenterLineColor);
        }

        private static Renderer CreateRoadDecoration(
            Transform parent,
            string name,
            Vector3 position,
            Vector3 scale)
        {
            GameObject decoration =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            decoration.name = name;
            decoration.transform.SetParent(parent, false);
            decoration.transform.localPosition = position;
            decoration.transform.localScale = scale;

            Collider collider = decoration.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(collider);
                }
                else
                {
                    DestroyImmediate(collider);
                }
            }

            Renderer renderer =
                PrepareRenderer(decoration.GetComponent<Renderer>());
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private static Renderer CreateFlatRoadDecoration(
            Transform parent,
            string name,
            Vector3 position,
            Vector2 size)
        {
            GameObject decoration =
                GameObject.CreatePrimitive(PrimitiveType.Quad);
            decoration.name = name;
            decoration.transform.SetParent(parent, false);
            decoration.transform.localPosition = position;
            decoration.transform.localScale =
                new Vector3(size.x, size.y, 1f);

            Collider collider = decoration.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(collider);
                }
                else
                {
                    DestroyImmediate(collider);
                }
            }

            Renderer renderer =
                PrepareRenderer(decoration.GetComponent<Renderer>());
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private static bool IsGeneratedRoadDecoration(
            Renderer renderer)
        {
            Transform current = renderer != null
                ? renderer.transform
                : null;
            while (current != null)
            {
                if (current.name == "RoadPerimeter" ||
                    current.name == "RoadCenterLines")
                {
                    return true;
                }
                current = current.parent;
            }

            return false;
        }

        private SimpleTownRoadVisualSetSO
            ResolveSimpleTownRoadVisualSet()
        {
            simpleTownRoadVisualSet ??=
                Resources.Load<SimpleTownRoadVisualSetSO>(
                    "CityFlow/SimpleTownRoadVisualSet");
            return simpleTownRoadVisualSet;
        }

        private SimpleTownRoadConnections GetRoadConnections(
            Vector2Int tile)
        {
            SimpleTownRoadConnections connections =
                SimpleTownRoadConnections.None;

            if (IsRoadTile(tile + Vector2Int.up))
            {
                connections |= SimpleTownRoadConnections.North;
            }
            if (IsRoadTile(tile + Vector2Int.right))
            {
                connections |= SimpleTownRoadConnections.East;
            }
            if (IsRoadTile(tile + Vector2Int.down))
            {
                connections |= SimpleTownRoadConnections.South;
            }
            if (IsRoadTile(tile + Vector2Int.left))
            {
                connections |= SimpleTownRoadConnections.West;
            }

            return connections;
        }

        private static Renderer PrepareAuthoredRenderers(
            GameObject root)
        {
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                renderer.enabled = true;
                renderer.forceRenderingOff = false;
                renderer.allowOcclusionWhenDynamic = false;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return renderers.Length > 0
                ? renderers[0]
                : null;
        }

        private static void FitSimpleTownPrefab(
            Transform model,
            Transform relativeTo,
            Vector2 targetSize,
            float modelYawDegrees = 0f)
        {
            model.localPosition = Vector3.zero;
            model.localRotation =
                Quaternion.Euler(-90f, 0f, 0f) *
                Quaternion.Euler(0f, modelYawDegrees, 0f);
            model.localScale = Vector3.one;

            if (!TryGetRendererBounds(
                    model.gameObject,
                    relativeTo,
                    out Bounds sourceBounds))
            {
                return;
            }

            float scaleX =
                targetSize.x /
                Mathf.Max(0.0001f, sourceBounds.size.x);
            float scaleY =
                targetSize.y /
                Mathf.Max(0.0001f, sourceBounds.size.y);
            model.localScale = new Vector3(
                scaleX,
                Mathf.Min(scaleX, scaleY),
                scaleY);

            if (!TryGetRendererBounds(
                    model.gameObject,
                    relativeTo,
                    out Bounds fittedBounds))
            {
                return;
            }

            model.localPosition = new Vector3(
                -fittedBounds.center.x,
                -fittedBounds.center.y,
                -fittedBounds.max.z - targetSize.y * 0.01f);
        }

        private static bool TryGetRendererBounds(
            GameObject root,
            Transform relativeTo,
            out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                Bounds localBounds = renderer.localBounds;
                Vector3 min = localBounds.min;
                Vector3 max = localBounds.max;

                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = new Vector3(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    Vector3 worldPoint =
                        renderer.transform.TransformPoint(
                            point);
                    Vector3 relativePoint =
                        relativeTo != null
                            ? relativeTo
                                .InverseTransformPoint(
                                    worldPoint)
                            : worldPoint;

                    if (!hasBounds)
                    {
                        bounds = new Bounds(
                            relativePoint,
                            Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(relativePoint);
                    }
                }
            }

            return hasBounds;
        }

        private static void AlignRendererBaseToSurface(
            Transform model,
            Transform relativeTo,
            float surfaceZ)
        {
            if (!TryGetRendererBounds(
                    model.gameObject,
                    relativeTo,
                    out Bounds bounds))
            {
                return;
            }

            Vector3 position = model.localPosition;
            position.z += surfaceZ - bounds.max.z;
            model.localPosition = position;
        }

        private static void AlignRendererTopToSurface(
            Transform model,
            Transform relativeTo,
            float surfaceZ)
        {
            if (!TryGetRendererBounds(
                    model.gameObject,
                    relativeTo,
                    out Bounds bounds))
            {
                return;
            }

            Vector3 position = model.localPosition;
            position.z += surfaceZ - bounds.min.z;
            model.localPosition = position;
        }

        public bool TryCreatePlacementPreview(
            TileType type,
            out GameObject preview)
        {
            return TryCreatePlacementPreview(
                Vector2Int.zero,
                type,
                out preview);
        }

        public bool TryCreatePlacementPreview(
            Vector2Int tile,
            TileType type,
            out GameObject preview)
        {
            preview = null;
            if (type == TileType.Empty ||
                type == TileType.SpecialBuilding)
            {
                return false;
            }

            TileVisual visual =
                CreateTileVisual(
                    tile,
                    type,
                    null,
                    includeRoadNetworkDetails: true);
            preview = visual.Object;
            preview.name = $"PlacementPreview_{type}";
            if (!TileFootprint.IsBuilding(type))
            {
                preview.transform.localPosition = Vector3.zero;
                preview.transform.localRotation =
                    visual.UsesAuthoredLayout
                        ? visual.LocalRotation
                        : Quaternion.identity;
                preview.transform.localScale =
                    visual.UsesAuthoredLayout
                        ? Vector3.one
                        : GetTileScale(type);
            }
            return true;
        }

        public Vector3 GetPlacementPreviewWorldPosition(
            Vector2Int tile,
            TileType type)
        {
            Vector3 localPosition;
            if (TileFootprint.IsBuilding(type))
            {
                localPosition = FootprintToLocal(tile, type);
            }
            else
            {
                float localZ =
                    type == TileType.Road
                        ? fieldTileZ
                        : 0f;
                localPosition = GridToLocal(tile, localZ);
            }

            return transform.TransformPoint(localPosition);
        }

        public Quaternion GetPlacementPreviewWorldRotation(
            Vector2Int tile,
            TileType type,
            PlacementDirection direction)
        {
            Quaternion localRotation =
                TileFootprint.IsBuilding(type)
                    ? GetBuildingRotation(
                        tile,
                        type,
                        direction)
                    : Quaternion.identity;
            return transform.rotation * localRotation;
        }

        public bool TryCreateInfrastructurePlacementPreview(
            InfrastructureDataSO data,
            Vector2Int? highwayStart,
            Vector2Int cursor,
            out GameObject preview)
        {
            preview = null;
            if (data == null ||
                data.Kind == InfrastructureKind.BusStop)
            {
                return false;
            }

            GameObject visual;
            switch (data.Kind)
            {
                case InfrastructureKind.Signal:
                    visual =
                        CreateSignalVisual(Vector2Int.zero).Root;
                    break;
                case InfrastructureKind.Roundabout:
                    visual =
                        CreateRoundaboutVisual(Vector2Int.zero);
                    break;
                case InfrastructureKind.Overpass:
                    visual =
                        CreateOverpassVisual(Vector2Int.zero);
                    break;
                case InfrastructureKind.Oneway:
                    visual =
                        CreateOnewayVisual(Vector2Int.zero);
                    visual.transform.localRotation =
                        Quaternion.Euler(
                            0f,
                            0f,
                            Mathf.Atan2(
                                data.OnewayDir.y,
                                data.OnewayDir.x) *
                            Mathf.Rad2Deg);
                    break;
                case InfrastructureKind.TurnRestriction:
                    TurnSignVisual turnVisual =
                        CreateTurnSignVisual(Vector2Int.zero);
                    ApplyTurnSignPreviewState(
                        turnVisual,
                        data.TurnMode);
                    visual = turnVisual.Root;
                    break;
                case InfrastructureKind.PriorityRoad:
                    visual =
                        CreatePriorityRoadVisual(Vector2Int.zero);
                    visual.transform.localRotation =
                        Quaternion.Euler(
                            0f,
                            0f,
                            data.PriorityAxis == Axis.Vertical
                                ? 90f
                                : 0f);
                    break;
                case InfrastructureKind.Highway:
                    Vector2Int start =
                        highwayStart ?? Vector2Int.zero;
                    Vector2Int end =
                        highwayStart.HasValue
                            ? cursor
                            : Vector2Int.right;
                    if (end == start)
                    {
                        end = start +
                              Vector2Int.right;
                    }
                    visual = CreateHighwayVisual(
                        new HighwayLink(start, end));
                    break;
                default:
                    return false;
            }

            preview = WrapInfrastructurePlacementPreview(
                visual,
                data.Kind);
            return preview != null;
        }

        private static GameObject
            WrapInfrastructurePlacementPreview(
                GameObject visual,
                InfrastructureKind kind)
        {
            if (visual == null)
            {
                return null;
            }

            var preview =
                new GameObject(
                    $"PlacementPreview_{kind}");
            visual.transform.SetParent(
                preview.transform,
                false);
            visual.transform.localPosition = Vector3.zero;
            return preview;
        }

        private TileVisual CreateBuildingVisual(
            Vector2Int tile,
            TileType type,
            Transform parent)
        {
            GameObject root = new GameObject($"{type}_{tile.x}_{tile.y}");
            root.transform.SetParent(parent, false);
            float buildingSurfaceZ =
                AddBuildingFoundation(
                    root.transform,
                    type);

            GameObject prefab = GetPrefab(type);
            GameObject body = InstantiatePrefabOrPrimitive(prefab, PrimitiveType.Cube);
            body.name = "BuildingBody";
            body.transform.SetParent(root.transform, false);
            bool usesAuthoredVisual = prefab != null &&
                                      (type == TileType.House ||
                                       type == TileType.Office ||
                                       type == TileType.School ||
                                       type == TileType.Hospital);
            Renderer renderer;
            if (usesAuthoredVisual)
            {
                Vector2 footprintSize =
                    GetBuildingBodyFootprintSize(type);
                FitSimpleTownPrefab(
                    body.transform,
                    root.transform,
                    footprintSize,
                    type == TileType.Hospital
                        ? 180f
                        : 0f);
                body.transform.localPosition +=
                    new Vector3(
                        0f,
                        tileSize * 0.5f,
                        0f);
                AlignRendererBaseToSurface(
                    body.transform,
                    root.transform,
                    buildingSurfaceZ);
                VehicleVisualUtility.PrepareUnlit(
                    body,
                    (int)RenderQueue.Geometry);
                renderer =
                    PrepareAuthoredRenderers(body);
            }
            else
            {
                body.transform.localScale =
                    GetBuildingBodyScale(type);
                body.transform.localPosition =
                    GetBuildingBodyPosition(type);
                renderer =
                    PrepareRenderer(
                        body.GetComponentInChildren<Renderer>());
                if (prefab == null)
                {
                    AddFallbackBuildingDetails(body.transform, type);
                }
                AlignRendererBaseToSurface(
                    body.transform,
                    root.transform,
                    buildingSurfaceZ);
            }

            // 공사장에는 주차 자리를 만들지 않는다. 차가 여기로 배정되지는 않지만
            // (타입이 House/Office 가 아니므로), ParkingSlot_{n} 자식을 남겨두면
            // CarMotion.GetParkingAnchor()가 Find 로 집어갈 여지가 생긴다 — 그 경로는
            // 에러 없이 조용히 폴백하므로 애초에 만들지 않는 편이 안전하다.
            if (type != TileType.UnderConstruction)
            {
                AddParkingDetails(root.transform, type);
            }

            return new TileVisual
            {
                Object = root,
                Renderer = renderer,
                Block = new MaterialPropertyBlock(),
                Type = type,
                UsesAuthoredMaterial = usesAuthoredVisual
            };
        }

        private float AddBuildingFoundation(
            Transform root,
            TileType type)
        {
            GameObject foundationPrefab =
                ResolveBuildingVisualCatalog()
                    ?.FoundationPrefab;
            float surfaceZ = GetRoadSurfaceZ();
            if (foundationPrefab == null)
            {
                return surfaceZ;
            }

            GameObject foundation =
                Instantiate(
                    foundationPrefab,
                    root,
                    false);
            foundation.name = "BuildingFoundation";
            FitSimpleTownPrefab(
                foundation.transform,
                root,
                GetBuildingFoundationFootprintSize(type));
            foundation.transform.localPosition +=
                new Vector3(
                    0f,
                    tileSize * 0.5f,
                    0f);
            AlignRendererTopToSurface(
                foundation.transform,
                root,
                surfaceZ);
            PrepareAuthoredRenderers(foundation);
            return surfaceZ;
        }

        private Vector2 GetBuildingBodyFootprintSize(
            TileType type)
        {
            return type switch
            {
                TileType.House or
                TileType.Office or
                TileType.School or
                TileType.Hospital =>
                    new Vector2(
                        tileSize * 1.9f,
                        tileSize * 0.9f),
                _ => new Vector2(
                    tileSize * 1.5f,
                    tileSize * 1.05f)
            };
        }

        private Vector2 GetBuildingFoundationFootprintSize(
            TileType type)
        {
            Vector2Int footprint =
                TileFootprint.GetSize(type);
            return new Vector2(
                footprint.x * tileSize,
                tileSize);
        }

        private Vector3 GetBuildingBodyScale(TileType type)
        {
            float height = type switch
            {
                TileType.Office => tileSize * 1.35f,
                TileType.School => tileSize * 0.65f,
                TileType.Hospital => tileSize * 0.85f,
                // 공사 중 = 낮게 깔린 기초. 한눈에 "아직 건물이 아니다"로 읽히는 게 목적이다.
                TileType.UnderConstruction => tileSize * 0.18f,
                _ => tileSize * 0.8f
            };

            if (type == TileType.House ||
                type == TileType.Office ||
                type == TileType.School ||
                type == TileType.Hospital)
            {
                return new Vector3(
                    tileSize * 1.9f,
                    tileSize * 0.9f,
                    height);
            }

            return new Vector3(
                tileSize * 1.5f,
                tileSize * 1.05f,
                height);
        }

        private Vector3 GetBuildingBodyPosition(TileType type)
        {
            float height = GetBuildingBodyScale(type).z;
            float forwardOffset = type switch
            {
                TileType.House => tileSize * 0.5f,
                TileType.Office => tileSize * 0.5f,
                TileType.School => tileSize * 0.5f,
                TileType.Hospital => tileSize * 0.5f,
                _ => tileSize * 0.32f
            };
            return new Vector3(
                0f,
                forwardOffset,
                GetRoadSurfaceZ() - height * 0.5f);
        }

        private void AddParkingDetails(Transform root, TileType type)
        {
            if (type == TileType.House ||
                type == TileType.Office ||
                type == TileType.School ||
                type == TileType.Hospital)
            {
                if (type == TileType.House)
                {
                    int parkingSlotCount =
                        GetHomeParkingSlotCount();
                    if (!TryAddDrivewayParkingDetails(
                            root,
                            TileType.House,
                            parkingSlotCount))
                    {
                        AddHouseParkingDetails(
                            root,
                            parkingSlotCount);
                    }
                }
                else if (type == TileType.Office)
                {
                    int parkingSlotCount =
                        GetOfficeParkingSlotCount();
                    if (!TryAddDrivewayParkingDetails(
                            root,
                            TileType.Office,
                            parkingSlotCount))
                    {
                        AddOfficeParkingDetails(
                            root,
                            parkingSlotCount);
                    }
                }
                else if (type == TileType.School)
                {
                    const int schoolParkingSlotCount = 6;
                    if (!TryAddDrivewayParkingDetails(
                            root,
                            TileType.School,
                            schoolParkingSlotCount))
                    {
                        AddOfficeParkingDetails(
                            root,
                            schoolParkingSlotCount);
                    }
                }
                else
                {
                    const int hospitalParkingSlotCount = 4;
                    if (!TryAddDrivewayParkingDetails(
                            root,
                            TileType.Hospital,
                            hospitalParkingSlotCount))
                    {
                        AddOfficeParkingDetails(
                            root,
                            hospitalParkingSlotCount);
                    }
                }
                return;
            }

            AddSurfaceParkingDetails(root);
        }

        private int GetHomeParkingSlotCount() =>
            simEngine != null
                ? simEngine.CarSimHomeParkingSlots
                : Mathf.Max(1, SimConfig.Default().CarsPerHouse);

        private int GetOfficeParkingSlotCount() =>
            simEngine != null
                ? simEngine.CarSimOfficeParkingSlots
                : Mathf.Max(1, SimConfig.Default().OfficeCapacity);

        private bool TryAddDrivewayParkingDetails(
            Transform root,
            TileType buildingType,
            int parkingSlotCount)
        {
            GameObject drivewayPrefab =
                ResolveSimpleTownRoadVisualSet()?.DrivewayPrefab;
            if (drivewayPrefab == null || parkingSlotCount <= 0)
            {
                return false;
            }

            const int parkingSlotsPerPrefab = 2;
            int drivewayCount = buildingType == TileType.House
                ? 2
                : Mathf.CeilToInt(
                    parkingSlotCount /
                    (float)parkingSlotsPerPrefab);
            float lotWidth = tileSize * 2f;
            float drivewayWidth =
                lotWidth / drivewayCount;
            int visibleParkingSlotCount =
                drivewayCount * parkingSlotsPerPrefab;
            float slotWidth =
                lotWidth / visibleParkingSlotCount;
            float drivewayFrontZ =
                float.PositiveInfinity;

            for (int drivewayIndex = 0;
                 drivewayIndex < drivewayCount;
                 drivewayIndex++)
            {
                float drivewayX =
                    lotWidth * 0.5f -
                    drivewayWidth *
                    (drivewayIndex + 0.5f);
                GameObject drivewayRoot =
                    new GameObject(
                        $"Driveway_{drivewayIndex}");
                drivewayRoot.transform.SetParent(root, false);
                drivewayRoot.transform.localPosition =
                    new Vector3(
                        drivewayX,
                        tileSize * -0.5f,
                        GetRoadSurfaceZ());
                drivewayRoot.transform.localRotation =
                    Quaternion.Euler(0f, 0f, 90f);

                GameObject driveway = Instantiate(
                    drivewayPrefab,
                    drivewayRoot.transform,
                    false);
                driveway.name = "PathDriveway";
                FitSimpleTownPrefab(
                    driveway.transform,
                    drivewayRoot.transform,
                    new Vector2(
                        tileSize,
                        drivewayWidth));
                AlignRendererTopToSurface(
                    driveway.transform,
                    root,
                    GetRoadSurfaceZ());
                PrepareAuthoredRenderers(driveway);

                if (TryGetRendererBounds(
                        driveway,
                        root,
                        out Bounds drivewayBounds))
                {
                    drivewayFrontZ = Mathf.Min(
                        drivewayFrontZ,
                        drivewayBounds.min.z);
                }
            }

            AddDrivewayPrefabBoundaries(
                root,
                drivewayCount,
                lotWidth,
                drivewayWidth,
                drivewayFrontZ);

            for (int slot = 0; slot < parkingSlotCount; slot++)
            {
                float slotX =
                    lotWidth * 0.5f -
                    slotWidth * (slot + 0.5f);
                GameObject anchor =
                    new GameObject($"ParkingSlot_{slot}");
                anchor.transform.SetParent(root, false);
                anchor.transform.localPosition =
                    new Vector3(
                        slotX,
                        tileSize * -0.5f,
                        VehicleGroundZ);
            }

            return true;
        }

        private void AddDrivewayPrefabBoundaries(
            Transform root,
            int drivewayCount,
            float lotWidth,
            float drivewayWidth,
            float drivewayFrontZ)
        {
            if (drivewayCount <= 1)
            {
                return;
            }

            float dividerWidth =
                tileSize * 0.015f;
            float dividerLength =
                tileSize * 0.9f;
            if (float.IsPositiveInfinity(
                    drivewayFrontZ))
            {
                drivewayFrontZ =
                    GetRoadSurfaceZ() -
                    tileSize * 0.02f;
            }
            Color dividerColor =
                new Color32(122, 118, 113, 255);

            for (int boundary = 1;
                 boundary < drivewayCount;
                 boundary++)
            {
                float boundaryX =
                    lotWidth * 0.5f -
                    drivewayWidth * boundary;
                Renderer divider =
                    CreateFlatRoadDecoration(
                        root,
                        $"DrivewayBoundary_{boundary}",
                        new Vector3(
                            boundaryX,
                            tileSize * -0.5f,
                            drivewayFrontZ -
                            tileSize * 0.001f),
                        new Vector2(
                            dividerWidth,
                            dividerLength));
                ApplyRendererColor(
                    divider,
                    dividerColor);
            }
        }

        private void AddHouseParkingDetails(Transform root, int parkingSlotCount)
        {
            Color lotColor = Color.Lerp(roadFreeColor, Color.black, 0.08f);
            Color lineColor = Color.Lerp(Color.white, roadFreeColor, 0.15f);
            float surfaceZ = GetRoadSurfaceZ();

            Renderer parking = CreateDetailCube(
                root,
                "ParkingLot",
                new Vector3(tileSize * 0.9f, tileSize * 0.9f, tileSize * 0.05f),
                new Vector3(
                    tileSize * -0.5f,
                    tileSize * -0.5f,
                    surfaceZ - tileSize * 0.025f));
            ApplyRendererColor(parking, lotColor);

            Renderer divider = CreateDetailCube(
                root,
                "ParkingLine_Center",
                new Vector3(tileSize * 0.045f, tileSize * 0.78f, tileSize * 0.018f),
                new Vector3(
                    tileSize * -0.5f,
                    tileSize * -0.5f,
                    surfaceZ - tileSize * 0.059f));
            ApplyRendererColor(divider, lineColor);

            float slotSpacing = parkingSlotCount > 1
                ? Mathf.Min(0.44f, 0.7f / (parkingSlotCount - 1))
                : 0f;
            float firstSlotX = -0.5f + slotSpacing * (parkingSlotCount - 1) * 0.5f;

            for (int slot = 0; slot < parkingSlotCount; slot++)
            {
                GameObject anchor = new GameObject($"ParkingSlot_{slot}");
                anchor.transform.SetParent(root, false);
                anchor.transform.localPosition = new Vector3(
                    tileSize * (firstSlotX - slotSpacing * slot),
                    tileSize * -0.5f,
                    VehicleGroundZ);
            }
        }

        private void AddOfficeParkingDetails(Transform root, int parkingSlotCount)
        {
            Color lotColor = Color.Lerp(roadFreeColor, Color.black, 0.08f);
            Color lineColor = Color.Lerp(Color.white, roadFreeColor, 0.15f);
            float lotWidth = tileSize * 1.8f;
            float slotWidth = lotWidth / parkingSlotCount;
            float surfaceZ = GetRoadSurfaceZ();

            Renderer parking = CreateDetailCube(
                root,
                "ParkingLot",
                new Vector3(lotWidth, tileSize * 0.9f, tileSize * 0.05f),
                new Vector3(
                    0f,
                    tileSize * -0.5f,
                    surfaceZ - tileSize * 0.025f));
            ApplyRendererColor(parking, lotColor);

            for (int line = 1; line < parkingSlotCount; line++)
            {
                float x = -lotWidth * 0.5f + slotWidth * line;
                Renderer divider = CreateDetailCube(
                    root,
                    $"ParkingLine_{line}",
                    new Vector3(tileSize * 0.025f, tileSize * 0.78f, tileSize * 0.018f),
                    new Vector3(
                        x,
                        tileSize * -0.5f,
                        surfaceZ - tileSize * 0.059f));
                ApplyRendererColor(divider, lineColor);
            }

            for (int slot = 0; slot < parkingSlotCount; slot++)
            {
                GameObject anchor = new GameObject($"ParkingSlot_{slot}");
                anchor.transform.SetParent(root, false);
                anchor.transform.localPosition = new Vector3(
                    lotWidth * 0.5f - slotWidth * (slot + 0.5f),
                    tileSize * -0.5f,
                    VehicleGroundZ);
            }
        }

        private void AddSurfaceParkingDetails(Transform root)
        {
            Color lotColor = Color.Lerp(roadFreeColor, Color.black, 0.08f);
            Color lineColor = Color.Lerp(Color.white, roadFreeColor, 0.15f);
            float surfaceZ = GetRoadSurfaceZ();

            Renderer parking = CreateDetailCube(
                root,
                "ParkingLot",
                new Vector3(tileSize * 1.8f, tileSize * 0.68f, tileSize * 0.05f),
                new Vector3(
                    0f,
                    tileSize * -0.61f,
                    surfaceZ - tileSize * 0.025f));
            ApplyRendererColor(parking, lotColor);

            for (int i = -1; i <= 1; i++)
            {
                Renderer line = CreateDetailCube(
                    root,
                    $"ParkingLine_{i + 1}",
                    new Vector3(tileSize * 0.045f, tileSize * 0.52f, tileSize * 0.018f),
                    new Vector3(
                        tileSize * i * 0.58f,
                        tileSize * -0.61f,
                        surfaceZ - tileSize * 0.059f));
                ApplyRendererColor(line, lineColor);
            }

            const int parkingSlotCount = 6;
            for (int slot = 0; slot < parkingSlotCount; slot++)
            {
                int column = 2 - slot % 3;
                int row = slot / 3;
                GameObject anchor = new GameObject($"ParkingSlot_{slot}");
                anchor.transform.SetParent(root, false);
                anchor.transform.localPosition = new Vector3(
                    tileSize * (column - 1) * 0.58f,
                    tileSize * (-0.76f + row * 0.3f),
                    VehicleGroundZ);
            }
        }

        private void RemoveTileVisual(Vector2Int tile)
        {
            if (!tileVisuals.TryGetValue(tile, out TileVisual visual))
            {
                return;
            }

            if (visual.Object != null)
            {
                Destroy(visual.Object);
            }

            tileVisuals.Remove(tile);
        }

        private void ApplyTileColor(Vector2Int tile, TileVisual visual)
        {
            if (visual.UsesAuthoredMaterial &&
                TileFootprint.IsBuilding(visual.Type))
            {
                return;
            }

            Color color = visual.Type switch
            {
                TileType.Road when visual.UsesAuthoredMaterial &&
                                   tileData.GetCongestion(tile) ==
                                   CongestionLevel.Free =>
                    Color.white,
                TileType.Road => GetRoadColor(
                    tileData.GetCongestion(tile)),
                TileType.House => houseColor,
                TileType.Office => officeColor,
                TileType.School => schoolColor,
                TileType.Hospital => hospitalColor,
                TileType.UnderConstruction => ConstructionColor,
                _ => Color.clear
            };

            Renderer[] renderers = visual.Type == TileType.Road &&
                                   visual.UsesAuthoredLayout
                ? visual.Object.GetComponentsInChildren<Renderer>(true)
                : new[] { visual.Renderer };
            for (int i = 0; i < renderers.Length; i++)
            {
                if (IsGeneratedRoadDecoration(renderers[i]))
                {
                    continue;
                }

                ApplyRendererColor(renderers[i], color, visual.Block);
            }
        }

        // 공사장 색. houseColor 등과 달리 [SerializeField]가 아니다 — MainCityView 에
        // 직렬화 필드를 추가하는 것은 금지(씬 7개를 함께 고쳐야 하므로 담당자가 몰아서 추가).
        // 튜닝이 필요해지면 그때 담당자에게 필드 추가를 요청한다.
        private static readonly Color ConstructionColor = new Color(0.62f, 0.55f, 0.38f);

        private Color GetRoadColor(CongestionLevel congestion)
        {
            return congestion switch
            {
                CongestionLevel.Jam => roadJamColor,
                CongestionLevel.Slow => roadSlowColor,
                _ => roadFreeColor
            };
        }

        private Vector3 GetTileScale(TileType type)
        {
            return type switch
            {
                TileType.Road => new Vector3(tileSize, tileSize, 0.08f),
                TileType.House => new Vector3(tileSize * 0.62f, tileSize * 0.62f, tileSize * 0.7f),
                TileType.Office => new Vector3(tileSize * 0.55f, tileSize * 0.55f, tileSize * 1.25f),
                TileType.School => new Vector3(tileSize * 0.76f, tileSize * 0.6f, tileSize * 0.55f),
                TileType.Hospital => new Vector3(tileSize * 0.78f, tileSize * 0.68f, tileSize * 0.8f),
                _ => Vector3.one * tileSize
            };
        }

        private float GetRoadSurfaceZ()
        {
            return fieldTileZ - tileSize * 0.02f;
        }

        private Vector3 FootprintToLocal(Vector2Int tile, TileType type)
        {
            Vector2Int size = TileFootprint.GetSize(type);
            Vector2Int localTile = tile - gridOrigin;
            return new Vector3(
                (localTile.x + size.x * 0.5f) * tileSize,
                (localTile.y + size.y * 0.5f) * tileSize,
                0f);
        }

        private Quaternion GetRoadFacingRotation(Vector2Int tile, TileType type)
        {
            Vector2Int size = TileFootprint.GetSize(type);
            if (type == TileType.House)
            {
                Quaternion parkingRotation = GetHouseParkingRotation(tile, size, out bool foundConnectedCorner);
                if (foundConnectedCorner)
                {
                    return parkingRotation;
                }
            }

            int south = CountRoadsAlongHorizontal(tile.x, tile.x + size.x, tile.y - 1);
            int east = CountRoadsAlongVertical(tile.y, tile.y + size.y, tile.x + size.x);
            int north = CountRoadsAlongHorizontal(tile.x, tile.x + size.x, tile.y + size.y);
            int west = CountRoadsAlongVertical(tile.y, tile.y + size.y, tile.x - 1);

            int best = Mathf.Max(south, east, north, west);
            if (best <= 0 || best == south) return Quaternion.identity;
            if (best == east) return Quaternion.Euler(0f, 0f, 90f);
            if (best == north) return Quaternion.Euler(0f, 0f, 180f);
            if (best == west) return Quaternion.Euler(0f, 0f, -90f);
            return Quaternion.identity;
        }

        private Quaternion GetBuildingRotation(
            Vector2Int tile,
            TileType type)
        {
            PlacementDirection direction = tileData.GetDirection(tile);
            return GetBuildingRotation(
                tile,
                type,
                direction);
        }

        private Quaternion GetBuildingRotation(
            Vector2Int tile,
            TileType type,
            PlacementDirection direction)
        {
            Vector2Int size = TileFootprint.GetRotatedSize(type, direction);
            if (direction != PlacementDirection.North ||
                CountRoadsAlongFront(tile, size, direction) > 0)
            {
                return Quaternion.Euler(
                    0f,
                    0f,
                    TileFootprint.ToAngle(direction));
            }

            // 구버전 저장에는 방향 선택 전에 배치한 건물이 많습니다.
            // 기본 North이며 선택 앞면에 도로가 없을 때만 기존 자동 정렬을 유지합니다.
            return GetRoadFacingRotation(tile, type);
        }

        private int CountRoadsAlongFront(
            Vector2Int tile,
            Vector2Int size,
            PlacementDirection direction)
        {
            Vector2Int front = TileFootprint.GetFrontOffset(direction);
            if (front.x != 0)
            {
                int x = front.x > 0
                    ? tile.x + size.x
                    : tile.x - 1;
                return CountRoadsAlongVertical(
                    tile.y,
                    tile.y + size.y,
                    x);
            }

            int y = front.y > 0
                ? tile.y + size.y
                : tile.y - 1;
            return CountRoadsAlongHorizontal(
                tile.x,
                tile.x + size.x,
                y);
        }

        private Quaternion GetHouseParkingRotation(
            Vector2Int anchor,
            Vector2Int footprintSize,
            out bool foundConnectedCorner)
        {
            int bestScore = 0;
            int bestIndex = 0;
            for (int i = 0; i < 4; i++)
            {
                Vector2Int corner = i switch
                {
                    1 => anchor + new Vector2Int(footprintSize.x - 1, 0),
                    2 => anchor + footprintSize - Vector2Int.one,
                    3 => anchor + new Vector2Int(0, footprintSize.y - 1),
                    _ => anchor
                };
                int score = 0;

                if (corner.y == anchor.y && IsRoadTile(corner + Vector2Int.down)) score++;
                if (corner.x == anchor.x + footprintSize.x - 1 && IsRoadTile(corner + Vector2Int.right)) score++;
                if (corner.y == anchor.y + footprintSize.y - 1 && IsRoadTile(corner + Vector2Int.up)) score++;
                if (corner.x == anchor.x && IsRoadTile(corner + Vector2Int.left)) score++;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            foundConnectedCorner = bestScore > 0;
            float rotation = bestIndex switch
            {
                1 => 90f,
                2 => 180f,
                3 => -90f,
                _ => 0f
            };
            return Quaternion.Euler(0f, 0f, rotation);
        }

        private int CountRoadsAlongHorizontal(int startX, int endX, int y)
        {
            int count = 0;
            for (int x = startX; x < endX; x++)
            {
                if (IsRoadTile(new Vector2Int(x, y))) count++;
            }
            return count;
        }

        private int CountRoadsAlongVertical(int startY, int endY, int x)
        {
            int count = 0;
            for (int y = startY; y < endY; y++)
            {
                if (IsRoadTile(new Vector2Int(x, y))) count++;
            }
            return count;
        }

        private void RefreshBuildingOrientations()
        {
            foreach (KeyValuePair<Vector2Int, TileVisual> pair in tileVisuals)
            {
                if (!TileFootprint.IsBuilding(pair.Value.Type)) continue;
                pair.Value.Object.transform.localRotation =
                    GetBuildingRotation(pair.Key, pair.Value.Type);
            }
        }

        private void AddFallbackBuildingDetails(Transform building, TileType type)
        {
            Color detailColor = type switch
            {
                TileType.House => Color.Lerp(houseColor, Color.white, 0.35f),
                TileType.Office => Color.Lerp(officeColor, Color.white, 0.2f),
                TileType.School => Color.Lerp(schoolColor, Color.black, 0.2f),
                TileType.Hospital => Color.white,
                _ => Color.white
            };

            Vector3 detailScale = type switch
            {
                TileType.House => new Vector3(0.78f, 0.78f, 0.28f),
                TileType.Office => new Vector3(0.72f, 0.72f, 0.18f),
                TileType.School => new Vector3(0.42f, 0.22f, 0.4f),
                TileType.Hospital => new Vector3(0.22f, 0.68f, 0.16f),
                _ => Vector3.one * 0.2f
            };
            Vector3 detailPosition = type switch
            {
                TileType.House => new Vector3(0f, 0f, -0.64f),
                TileType.Office => new Vector3(0f, 0f, -0.58f),
                TileType.School => new Vector3(0f, -0.48f, -0.42f),
                TileType.Hospital => new Vector3(0f, -0.56f, -0.52f),
                _ => Vector3.zero
            };

            Renderer detail = CreateDetailCube(building, "BuildingDetail", detailScale, detailPosition);
            ApplyRendererColor(detail, detailColor);
        }

        private Renderer CreateDetailCube(Transform parent, string name, Vector3 scale, Vector3 localPosition)
        {
            GameObject detail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            detail.name = name;
            detail.transform.SetParent(parent, false);
            detail.transform.localScale = scale;
            detail.transform.localPosition = localPosition;
            return PrepareRenderer(detail.GetComponent<Renderer>());
        }

        private void AddRoadCenterLines(
            Transform road,
            Vector2Int tile)
        {
            Transform existing = road.Find("RoadCenterLines");
            if (existing != null)
            {
                Destroy(existing.gameObject);
            }

            GameObject root = new GameObject("RoadCenterLines");
            root.transform.SetParent(road, false);

            SimpleTownRoadConnections connections =
                GetRoadConnections(tile);
            if (!SimpleTownRoadTopology.ShouldDrawCenterLines(
                    connections))
            {
                return;
            }

            float thickness =
                Mathf.Max(
                    0.025f,
                    gridLineThickness * 0.65f / tileSize);
            bool horizontal =
                SimpleTownRoadTopology
                    .IsCenterLineHorizontal(connections);
            Vector3 scale = horizontal
                ? new Vector3(0.22f, thickness, 0.2f)
                : new Vector3(thickness, 0.22f, 0.2f);
            Vector3 position =
                new Vector3(0f, 0f, -0.65f);
            Renderer line = CreateDetailCube(
                root.transform,
                "CenterLine",
                scale,
                position);
            Collider collider = line.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
            ApplyRendererColor(
                line,
                Color.Lerp(Color.yellow, Color.white, 0.15f));
        }

        private void RefreshRoadCenterLinesAround(Vector2Int tile)
        {
            RefreshRoadCenterLines(tile);
            RefreshRoadCenterLines(tile + Vector2Int.right);
            RefreshRoadCenterLines(tile + Vector2Int.left);
            RefreshRoadCenterLines(tile + Vector2Int.up);
            RefreshRoadCenterLines(tile + Vector2Int.down);
        }

        private void RefreshRoadCenterLines(Vector2Int tile)
        {
            if (tileVisuals.TryGetValue(tile, out TileVisual visual) && visual.Type == TileType.Road)
            {
                AddRoadCenterLines(visual.Object.transform, tile);
            }
        }

        private void RefreshRoadVisualsAround(Vector2Int tile)
        {
            if (ResolveSimpleTownRoadVisualSet() == null)
            {
                RefreshRoadCenterLinesAround(tile);
                return;
            }

            RefreshRoadVisual(tile);
            RefreshRoadVisual(tile + Vector2Int.right);
            RefreshRoadVisual(tile + Vector2Int.left);
            RefreshRoadVisual(tile + Vector2Int.up);
            RefreshRoadVisual(tile + Vector2Int.down);
        }

        private void RefreshRoadVisual(Vector2Int tile)
        {
            if (!IsRoadTile(tile))
            {
                return;
            }

            RemoveTileVisual(tile);
            RefreshTile(tile, TileType.Road);
        }

        private GameObject GetPrefab(TileType type)
        {
            BuildingVisualCatalogSO buildingCatalog =
                ResolveBuildingVisualCatalog();
            return type switch
            {
                TileType.Road => roadPrefab,
                TileType.House =>
                    buildingCatalog?.HousePrefab != null
                        ? buildingCatalog.HousePrefab
                        : housePrefab,
                TileType.Office =>
                    buildingCatalog?.OfficePrefab != null
                        ? buildingCatalog.OfficePrefab
                        : officePrefab,
                TileType.School =>
                    buildingCatalog?.SchoolPrefab != null
                        ? buildingCatalog.SchoolPrefab
                        : schoolPrefab,
                TileType.Hospital =>
                    buildingCatalog?.HospitalPrefab != null
                        ? buildingCatalog.HospitalPrefab
                        : hospitalPrefab,
                _ => null
            };
        }

        private BuildingVisualCatalogSO
            ResolveBuildingVisualCatalog()
        {
            if (buildingVisualCatalog == null)
            {
                buildingVisualCatalog =
                    Resources.Load<BuildingVisualCatalogSO>(
                        "CityFlow/BuildingVisualCatalog");
            }

            return buildingVisualCatalog;
        }

        private void RefreshSignals()
        {
            if (signalControl == null)
            {
                return;
            }

            IReadOnlyList<Vector2Int> signals = signalControl.SignalTiles;

            for (int i = 0; i < signals.Count; i++)
            {
                Vector2Int tile = signals[i];

                if (!signalVisuals.TryGetValue(tile, out SignalVisual visual))
                {
                    visual = CreateSignalVisual(tile);
                    signalVisuals.Add(tile, visual);
                }

                ApplySignalState(tile, visual);
            }

            foreach (Vector2Int tile in new List<Vector2Int>(signalVisuals.Keys))
            {
                if (!ContainsSignal(signals, tile))
                {
                    Destroy(signalVisuals[tile].Root);
                    signalVisuals.Remove(tile);
                }
            }
        }

        // 로터리 마커: RoundaboutTiles 폴링으로 생성/제거 — RefreshSignals와 동일 수명 규약.
        private void RefreshRoundabouts()
        {
            if (intersectionFacility == null)
            {
                return;
            }

            IReadOnlyList<Vector2Int> tiles = intersectionFacility.RoundaboutTiles;

            for (int i = 0; i < tiles.Count; i++)
            {
                if (!roundaboutVisuals.ContainsKey(tiles[i]))
                {
                    roundaboutVisuals.Add(tiles[i], CreateRoundaboutVisual(tiles[i]));
                }
            }

            foreach (Vector2Int tile in new List<Vector2Int>(roundaboutVisuals.Keys))
            {
                if (ContainsSignal(tiles, tile))
                {
                    continue;
                }

                Destroy(roundaboutVisuals[tile]);
                roundaboutVisuals.Remove(tile);
            }
        }

        // 풋프린트 로터리(스펙 2026-07-15): 도로색 회전 차도(링) + 초록 중앙 섬.
        //   접근 도로와 이어져 보이게 링을 도로색·도로 표면(z=0)에 깔고, 섬만 그 위로 살짝 띄운다.
        //   저장·흐름은 center 1타일 그대로 — 순수 뷰. v1은 차량이 center 직진 통과(도는 애니 = v2).
        private GameObject CreateRoundaboutVisual(Vector2Int tile)
        {
            GameObject root = new GameObject($"Roundabout_{tile.x}_{tile.y}");
            root.transform.SetParent(signalRoot, false);
            root.transform.localPosition = GridToLocal(tile, 0f);           // 도로 평면에 정렬

            // 회전 차도(링): 도로색 원형 판 — 반경 ~1.1타일로 상하좌우 접근 도로와 겹쳐 이어져 보인다.
            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "Ring";
            Destroy(pad.GetComponent<Collider>());                          // 장식 마커 — 물리 불필요
            pad.transform.SetParent(root.transform, false);
            pad.transform.localPosition = new Vector3(0f, 0f, -0.05f);      // 도로 슬래브 바로 앞(z-fight 방지)
            pad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);    // 원반을 보드(XY)와 평행하게
            pad.transform.localScale = new Vector3(tileSize * 2.2f, 0.02f, tileSize * 2.2f);
            ApplyRendererColor(PrepareRenderer(pad.GetComponent<Renderer>()), roadFreeColor);   // 도로색 = 이어짐

            // 중앙 섬(잔디): 링 위로 살짝 띄워 초록으로 — "차도 링 + 섬" 로터리 형태.
            GameObject island = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            island.name = "Island";
            Destroy(island.GetComponent<Collider>());
            island.transform.SetParent(root.transform, false);
            island.transform.localPosition = new Vector3(0f, 0f, -0.14f);
            island.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            island.transform.localScale = new Vector3(tileSize * 0.9f, 0.03f, tileSize * 0.9f);
            ApplyRendererColor(PrepareRenderer(island.GetComponent<Renderer>()), roundaboutColor);   // 초록 섬

            return root;
        }

        // 우선도로 마커: PriorityRoadTiles 폴링 — 로터리/입체/일방과 동일 수명 규약(생성/제거).
        // 메인축(가로/세로)에 맞춰 표지 막대를 회전(일방통행과 동일하게 회전은 매 프레임 갱신).
        private void RefreshPriorityRoads()
        {
            if (intersectionFacility == null)
            {
                return;
            }

            IReadOnlyList<Vector2Int> tiles = intersectionFacility.PriorityRoadTiles;

            for (int i = 0; i < tiles.Count; i++)
            {
                Vector2Int tile = tiles[i];

                if (!priorityRoadVisuals.TryGetValue(tile, out GameObject visual))
                {
                    visual = CreatePriorityRoadVisual(tile);
                    priorityRoadVisuals.Add(tile, visual);
                }

                float z = intersectionFacility.GetPriorityAxis(tile) == Axis.Vertical ? 90f : 0f;
                visual.transform.localRotation = Quaternion.Euler(0f, 0f, z);
            }

            foreach (Vector2Int tile in new List<Vector2Int>(priorityRoadVisuals.Keys))
            {
                if (ContainsSignal(tiles, tile))
                {
                    continue;
                }

                Destroy(priorityRoadVisuals[tile]);
                priorityRoadVisuals.Remove(tile);
            }
        }

        private GameObject CreatePriorityRoadVisual(Vector2Int tile)
        {
            // 임시 프리미티브 양보 표지(▽): 얇은 큐브 막대를 축 방향으로.
            // ponytail: 표지판 3D 에셋은 아트 단계
            GameObject root = new GameObject($"PriorityRoad_{tile.x}_{tile.y}");
            root.transform.SetParent(signalRoot, false);
            root.transform.localPosition = GridToLocal(tile, signalZ);

            Renderer bar = CreateSignalBar(root.transform, "Bar",
                new Vector3(tileSize * 0.5f, tileSize * 0.08f, 0.02f), Vector3.zero);
            ApplyRendererColor(bar, onewayColor);   // 기존 색 재사용(에셋 전)
            return root;
        }

        private void RefreshHighways()
        {
            if (highwayService == null || highwayRoot == null) return;
            IReadOnlyList<HighwayLink> links = highwayService.HighwayLinks;
            for (int i = 0; i < links.Count; i++)
                if (!highwayVisuals.ContainsKey(links[i].A))
                    highwayVisuals.Add(links[i].A, CreateHighwayVisual(links[i]));

            foreach (Vector2Int key in new List<Vector2Int>(highwayVisuals.Keys))
            {
                bool exists = false;
                for (int i = 0; i < links.Count; i++) if (links[i].A == key) { exists = true; break; }
                if (exists) continue;
                Destroy(highwayVisuals[key]);
                highwayVisuals.Remove(key);
            }
        }

        private GameObject CreateHighwayVisual(HighwayLink link)
        {
            var root = new GameObject($"Highway_{link.A.x}_{link.A.y}_{link.B.x}_{link.B.y}");
            root.transform.SetParent(highwayRoot, false);
            Vector3 a = GridToLocal(link.A, signalZ - 0.35f);
            Vector3 b = GridToLocal(link.B, signalZ - 0.35f);
            Vector3 delta = b - a;
            root.transform.localPosition = (a + b) * 0.5f;
            root.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);

            GameObject deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
            deck.name = "ElevatedDeck";
            Destroy(deck.GetComponent<Collider>());
            deck.transform.SetParent(root.transform, false);
            deck.transform.localScale = new Vector3(delta.magnitude, tileSize * 0.32f, 0.08f);
            ApplyRendererColor(PrepareRenderer(deck.GetComponent<Renderer>()), new Color(0.18f, 0.42f, 0.62f));

            for (int side = -1; side <= 1; side += 2)
            {
                GameObject ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ramp.name = side < 0 ? "RampA" : "RampB";
                Destroy(ramp.GetComponent<Collider>());
                ramp.transform.SetParent(root.transform, false);
                ramp.transform.localPosition = new Vector3(side * delta.magnitude * 0.5f, 0f, 0.12f);
                ramp.transform.localScale = new Vector3(tileSize * 0.7f, tileSize * 0.5f, 0.12f);
                ApplyRendererColor(PrepareRenderer(ramp.GetComponent<Renderer>()), new Color(0.25f, 0.65f, 0.9f));
            }
            return root;
        }

        // 입체교차 마커: 위(가로)/아래(세로) 두 바로 "축 분리"를 암시 — 로터리와 동일 수명 규약.
        private void RefreshOverpasses()
        {
            if (intersectionFacility == null)
            {
                return;
            }

            IReadOnlyList<Vector2Int> tiles = intersectionFacility.OverpassTiles;

            for (int i = 0; i < tiles.Count; i++)
            {
                if (!overpassVisuals.ContainsKey(tiles[i]))
                {
                    overpassVisuals.Add(tiles[i], CreateOverpassVisual(tiles[i]));
                }
            }

            foreach (Vector2Int tile in new List<Vector2Int>(overpassVisuals.Keys))
            {
                if (ContainsSignal(tiles, tile))
                {
                    continue;
                }

                Destroy(overpassVisuals[tile]);
                overpassVisuals.Remove(tile);
            }
        }

        private GameObject CreateOverpassVisual(Vector2Int tile)
        {
            GameObject root = new GameObject($"Overpass_{tile.x}_{tile.y}");
            root.transform.SetParent(signalRoot, false);
            root.transform.localPosition = GridToLocal(tile, signalZ);
            // 가로 바가 위층(z 앞), 세로 바가 아래층 — 입체를 z 차이로 암시.
            Renderer h = CreateSignalBar(root.transform, "Deck", new Vector3(tileSize * 0.8f, tileSize * 0.18f, 0.05f), new Vector3(0f, 0f, -0.04f));
            Renderer v = CreateSignalBar(root.transform, "Under", new Vector3(tileSize * 0.18f, tileSize * 0.8f, 0.05f), new Vector3(0f, 0f, 0.04f));
            ApplyRendererColor(h, overpassColor);
            ApplyRendererColor(v, overpassColor);
            return root;
        }

        // 일방통행 화살표 마커: OnewayTiles 폴링 — 로터리/입체와 동일 수명 규약(폴링 생성/제거).
        private void RefreshOneways()
        {
            if (trafficRule == null)
            {
                return;
            }

            IReadOnlyList<Vector2Int> tiles = trafficRule.OnewayTiles;

            for (int i = 0; i < tiles.Count; i++)
            {
                Vector2Int tile = tiles[i];

                if (!onewayVisuals.TryGetValue(tile, out GameObject visual))
                {
                    visual = CreateOnewayVisual(tile);
                    onewayVisuals.Add(tile, visual);
                }

                Vector2Int dir = trafficRule.GetOnewayDir(tile);
                visual.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            }

            foreach (Vector2Int tile in new List<Vector2Int>(onewayVisuals.Keys))
            {
                if (ContainsSignal(tiles, tile))
                {
                    continue;
                }

                Destroy(onewayVisuals[tile]);
                onewayVisuals.Remove(tile);
            }
        }

        // 화살표: 몸통 바(뒤쪽) + 촉 2개(사선 바, 앞쪽에서 V자) — 전부 동쪽(+x)을 기준으로 만들고
        // 루트 z회전으로 GetOnewayDir 방향을 표현(에셋 스왑 지점 1함수 수렴, 기존 규약).
        private GameObject CreateOnewayVisual(Vector2Int tile)
        {
            GameObject root = new GameObject($"Oneway_{tile.x}_{tile.y}");
            root.transform.SetParent(signalRoot, false);
            root.transform.localPosition = GridToLocal(tile, signalZ);

            Renderer shaft = CreateSignalBar(root.transform, "Shaft",
                new Vector3(tileSize * 0.5f, tileSize * 0.1f, 0.05f), new Vector3(-tileSize * 0.08f, 0f, 0f));
            Renderer headA = CreateSignalBar(root.transform, "HeadA",
                new Vector3(tileSize * 0.24f, tileSize * 0.1f, 0.05f), new Vector3(tileSize * 0.2f, tileSize * 0.1f, 0f));
            Renderer headB = CreateSignalBar(root.transform, "HeadB",
                new Vector3(tileSize * 0.24f, tileSize * 0.1f, 0.05f), new Vector3(tileSize * 0.2f, -tileSize * 0.1f, 0f));
            headA.transform.localRotation = Quaternion.Euler(0f, 0f, 40f);
            headB.transform.localRotation = Quaternion.Euler(0f, 0f, -40f);

            ApplyRendererColor(shaft, onewayColor);
            ApplyRendererColor(headA, onewayColor);
            ApplyRendererColor(headB, onewayColor);
            return root;
        }

        // 턴 제한 표지판 마커: TurnSignTiles 폴링 — 로터리/입체/일방과 동일 수명 규약(생성/제거).
        // 신호와 같은 타일에 공존할 수 있어(스펙 §핵심결정) turnSignZ로 signalZ와 z 분리(겹침 회피).
        private void RefreshTurnSigns()
        {
            if (trafficRule == null)
            {
                return;
            }

            IReadOnlyList<Vector2Int> tiles = trafficRule.TurnSignTiles;

            for (int i = 0; i < tiles.Count; i++)
            {
                Vector2Int tile = tiles[i];

                if (!turnSignVisuals.TryGetValue(tile, out TurnSignVisual visual))
                {
                    visual = CreateTurnSignVisual(tile);
                    turnSignVisuals.Add(tile, visual);
                }

                ApplyTurnSignState(tile, visual);
            }

            foreach (Vector2Int tile in new List<Vector2Int>(turnSignVisuals.Keys))
            {
                if (ContainsSignal(tiles, tile))
                {
                    continue;
                }

                Destroy(turnSignVisuals[tile].Root);
                turnSignVisuals.Remove(tile);
            }
        }

        // 굽은 화살: 몸통 바(북쪽 고정, 진입방향 무관 — 표지판은 특정 방향이 아니라 교차로 전체에 적용)
        // + 꺾인 촉. 촉의 위치/회전은 GetTurnMode로 매 폴링 갱신(에셋 스왑 지점 1함수 수렴, 기존 규약).
        private TurnSignVisual CreateTurnSignVisual(Vector2Int tile)
        {
            GameObject root = new GameObject($"TurnSign_{tile.x}_{tile.y}");
            root.transform.SetParent(signalRoot, false);
            root.transform.localPosition = GridToLocal(tile, turnSignZ);

            Renderer shaft = CreateSignalBar(root.transform, "Shaft",
                new Vector3(tileSize * 0.12f, tileSize * 0.3f, 0.05f), new Vector3(0f, -tileSize * 0.09f, 0f));
            Renderer tip = CreateSignalBar(root.transform, "Tip",
                new Vector3(tileSize * 0.26f, tileSize * 0.12f, 0.05f), Vector3.zero);

            ApplyRendererColor(shaft, turnSignColor);
            ApplyRendererColor(tip, turnSignColor);

            return new TurnSignVisual { Root = root, Tip = tip.transform };
        }

        // LeftOnly = 반시계 꺾임(촉이 왼쪽으로), RightOnly = 시계 꺾임(촉이 오른쪽으로) — 설계 §핵심결정
        // "회전 정의"와 같은 손감(왼쪽=+각도). null(표지판 소멸 경합 프레임)은 Left 형태로 방어.
        private void ApplyTurnSignState(Vector2Int tile, TurnSignVisual visual)
        {
            visual.Root.transform.localPosition = GridToLocal(tile, turnSignZ);

            TurnMode mode = trafficRule.GetTurnMode(tile) ?? TurnMode.LeftOnly;
            ApplyTurnSignPreviewState(visual, mode);
        }

        private void ApplyTurnSignPreviewState(
            TurnSignVisual visual,
            TurnMode mode)
        {
            bool leftOnly = mode == TurnMode.LeftOnly;
            float bendX = leftOnly ? -tileSize * 0.1f : tileSize * 0.1f;
            float bendAngle = leftOnly ? 45f : -45f;

            visual.Tip.localPosition = new Vector3(bendX, tileSize * 0.16f, 0f);
            visual.Tip.localRotation = Quaternion.Euler(0f, 0f, bendAngle);
        }

        private SignalVisual CreateSignalVisual(Vector2Int tile)
        {
            GameObject root = signalPrefab != null
                ? Instantiate(signalPrefab, signalRoot)
                : new GameObject($"Signal_{tile.x}_{tile.y}");

            root.name = $"Signal_{tile.x}_{tile.y}";
            root.transform.SetParent(signalRoot, false);
            TrafficLightLensView[] lensViews =
                root.GetComponentsInChildren<TrafficLightLensView>(includeInactive: true);
            root.transform.localPosition = GridToLocal(
                tile,
                lensViews.Length > 0 ? 0f : signalZ);

            Renderer horizontal = null;
            Renderer vertical = null;
            if (lensViews.Length == 0)
            {
                Renderer post = CreateSignalBar(root.transform, "Post",
                    new Vector3(tileSize * 0.08f, tileSize * 0.08f, tileSize * 0.6f), new Vector3(0f, 0f, tileSize * 0.15f));
                ApplyRendererColor(post, new Color(0.18f, 0.2f, 0.22f));

                horizontal = CreateSignalBar(root.transform, "Horizontal",
                    new Vector3(tileSize * 0.42f, tileSize * 0.1f, tileSize * 0.14f), new Vector3(0f, 0f, -tileSize * 0.18f));
                vertical = CreateSignalBar(root.transform, "Vertical",
                    new Vector3(tileSize * 0.1f, tileSize * 0.42f, tileSize * 0.14f), new Vector3(0f, 0f, -tileSize * 0.18f));
            }

            AlignRendererBaseToSurface(
                root.transform,
                signalRoot,
                GetRoadSurfaceZ());

            return new SignalVisual
            {
                Root = root,
                HorizontalRenderer = horizontal,
                VerticalRenderer = vertical,
                LensViews = lensViews,
                HorizontalBlock = new MaterialPropertyBlock(),
                VerticalBlock = new MaterialPropertyBlock(),
                GroundedZ = root.transform.localPosition.z
            };
        }

        private Renderer CreateSignalBar(Transform parent, string name, Vector3 scale, Vector3 localPosition)
        {
            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = name;
            bar.transform.SetParent(parent, false);
            bar.transform.localPosition = localPosition;
            bar.transform.localScale = scale;
            return PrepareRenderer(bar.GetComponent<Renderer>());
        }

        // 임시 텍스트 마커(에셋 스왑 전): 기본 폰트 TextMesh. 이모지는 tofu 위험 — 글리프 보장 문자만.
        private GameObject CreateTextMark(Transform parent, string text, Color color, float size)
        {
            GameObject go = new GameObject($"TextMark_{text}");
            go.transform.SetParent(parent, false);
            TextMesh tm = go.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            tm.text = text;
            tm.color = color;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.characterSize = size;
            tm.fontSize = 48;
            return go;
        }

        private void ApplySignalState(
            Vector2Int tile,
            SignalVisual visual)
        {
            Color horizontal = GetSignalColor(tile, horizontal: true);
            Color vertical = GetSignalColor(tile, horizontal: false);

            visual.Root.transform.localPosition = GridToLocal(
                tile,
                visual.GroundedZ);
            ApplyRendererColor(visual.HorizontalRenderer, horizontal, visual.HorizontalBlock);
            ApplyRendererColor(visual.VerticalRenderer, vertical, visual.VerticalBlock);

            if (visual.LensViews != null)
            {
                SignalPhase horizontalPhase = simEngine != null
                    ? simEngine.GetSignalPhase(tile, horizontal: true)
                    : SignalPhase.Green;
                SignalPhase verticalPhase = simEngine != null
                    ? simEngine.GetSignalPhase(tile, horizontal: false)
                    : SignalPhase.Green;

                foreach (TrafficLightLensView lensView in visual.LensViews)
                {
                    lensView.ApplyPhases(horizontalPhase, verticalPhase);
                }
            }

            // 오버라이드 특수효과: 코리도어 신호를 초록 방향으로 스케일 펄스(폴링 — 뷰가 매 프레임 갱신).
            bool overridden = signalControl != null && signalControl.GetOverrideSecondsLeft(tile) > 0f;
            float pulse = overridden
                ? 1f + overridePulseAmp * Mathf.Abs(Mathf.Sin(Time.time * 8f))
                : 1f;
            visual.Root.transform.localScale = Vector3.one * pulse;
        }

        private Color GetSignalColor(Vector2Int tile, bool horizontal)
        {
            if (simEngine == null)
            {
                return Color.green;
            }

            return simEngine.GetSignalPhase(tile, horizontal) switch
            {
                SignalPhase.Yellow => Color.yellow,
                SignalPhase.Red => Color.red,
                _ => Color.green
            };
        }

        private static bool ContainsSignal(IReadOnlyList<Vector2Int> signals, Vector2Int tile)
        {
            for (int i = 0; i < signals.Count; i++)
            {
                if (signals[i] == tile)
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshVehicles()
        {
            RefreshCommuteVehicles();
        }

        private void EnsureVehicleCount(int targetCount)
        {
            while (vehicles.Count < targetCount)
            {
                GameObject selectedPrefab = GetRandomNormalVehiclePrefab();
                GameObject vehicle = InstantiatePrefabOrPrimitive(
                    selectedPrefab,
                    PrimitiveType.Cube);
                vehicle.name = $"Vehicle_{vehicles.Count + 1}";
                vehicle.transform.SetParent(vehicleRoot, false);
                bool usesAuthoredVisual = selectedPrefab != null;
                vehicle.transform.localScale = usesAuthoredVisual
                    ? Vector3.one * (tileSize * VehicleBodyLengthTiles)
                    : new Vector3(
                        tileSize * VehicleBodyLengthTiles,
                        tileSize * VehicleBodyWidthTiles,
                        tileSize * VehicleBodyHeightTiles);

                if (vehicle.GetComponentInChildren<Collider>() == null)
                {
                    vehicle.AddComponent<BoxCollider>();
                }

                Renderer[] renderers =
                    vehicle.GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0;
                     rendererIndex < renderers.Length;
                     rendererIndex++)
                {
                    if (!usesAuthoredVisual)
                    {
                        PrepareRenderer(
                            renderers[rendererIndex],
                            VehicleRenderQueue);
                    }
                }
                if (usesAuthoredVisual)
                {
                    VehicleVisualUtility.PrepareUnlit(
                        vehicle,
                        VehicleRenderQueue);
                }
                Renderer renderer =
                    renderers.Length > 0
                        ? renderers[0]
                        : null;
                if (!usesAuthoredVisual)
                {
                    ApplyRendererColor(renderer, vehicleColor);
                }

                Renderer detailRenderer = null;
                GameObject brakeLight = null;
                if (!usesAuthoredVisual)
                {
                    detailRenderer = CreateDetailCube(vehicle.transform, "Cabin",
                        new Vector3(0.55f, 0.72f, 0.42f), new Vector3(-0.05f, 0f, -0.65f));
                    SetRendererRenderQueue(detailRenderer, VehicleRenderQueue);
                    ApplyRendererColor(detailRenderer, Color.Lerp(vehicleColor, Color.white, 0.3f));

                    // 제동등: 후방(-x = 진행 반대) 얇은 빨간 큐브, 기본 off. 감속 상태 진입 시에만 SetActive.
                    // vehiclePrefab == null 게이트 안 = 의도적 스코프(기존 Cabin 디테일 큐브 패턴과 동일 — 프리팹은 자체 룩 소유).
                    Renderer brakeRenderer = CreateDetailCube(vehicle.transform, "BrakeLight",
                        new Vector3(0.1f, 0.7f, 0.55f), new Vector3(-0.52f, 0f, 0f));
                    ApplyRendererColor(brakeRenderer, new Color(0.9f, 0.15f, 0.1f));
                    brakeLight = brakeRenderer.gameObject;
                    brakeLight.SetActive(false);
                }

                vehicle.SetActive(false);
                vehicles.Add(new RouteVehicle
                {
                    Object = vehicle,
                    Renderer = renderer,
                    Renderers = renderers,
                    DetailRenderer = detailRenderer,
                    BrakeLight = brakeLight,
                    UsesAuthoredVisual = usesAuthoredVisual
                });
            }
        }

        private GameObject GetRandomNormalVehiclePrefab()
        {
            VehicleVisualCatalogSO catalog =
                vehicleVisualCatalog != null
                    ? vehicleVisualCatalog
                    : Resources.Load<VehicleVisualCatalogSO>(
                        "CityFlow/VehicleVisualCatalog");
            GameObject[] prefabs = catalog?.NormalVehiclePrefabs;
            if (prefabs != null && prefabs.Length > 0)
            {
                int start = Random.Range(0, prefabs.Length);
                for (int offset = 0; offset < prefabs.Length; offset++)
                {
                    GameObject candidate =
                        prefabs[(start + offset) % prefabs.Length];
                    if (candidate != null)
                    {
                        return candidate;
                    }
                }
            }

            return vehiclePrefab;
        }

        private void AlignTextMarkPerpendicularToGround(Transform textMark)
        {
            Vector3 groundUp = -transform.forward;

            if (!isIsometricView && mainCamera != null)
            {
                // TextMesh 정면 규약: 글리프는 트랜스폼 -Z 쪽에서 볼 때 정상 판독(identity 회전 + 기본
                // 카메라(+Z 응시) 구도가 정독 구도). 즉 forward(+Z)는 카메라 "반대"를 향해야 한다 —
                // forward를 카메라로 향하게 하면 거울상("+1" 반전, "!"는 좌우대칭이라 안 보였음).
                Vector3 toCamera = mainCamera.transform.position - textMark.position;
                textMark.rotation = Quaternion.LookRotation(-toCamera.normalized, mainCamera.transform.up);
                return;
            }

            Vector3 facing = mainCamera != null
                ? Vector3.ProjectOnPlane(mainCamera.transform.position - textMark.position, groundUp)
                : transform.up;

            if (facing.sqrMagnitude < 0.001f)
            {
                facing = transform.up;
            }

            // 동일 규약: forward = 카메라 반대(-facing) — 지면 수직 세움은 up(groundUp)이 유지한다.
            textMark.rotation = Quaternion.LookRotation(-facing.normalized, groundUp);
        }

        private bool IsRoadTile(Vector2Int tile)
        {
            return tileData != null
                && (services?.WorldGrid?.IsInsideWorld(tile) ??
                    (tile.x >= gridOrigin.x &&
                     tile.x < gridOrigin.x + width &&
                     tile.y >= gridOrigin.y &&
                     tile.y < gridOrigin.y + height))
                && tileData.GetTileType(tile) == TileType.Road;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 통근 이동(유일 경로 — RefreshVehicles가 항상 진입).
        // 뷰는 번역기: CarSim 스냅샷의 타일/큐 슬롯을 폴리라인 위치로 보간한다.
        // 차를 세우는 자체 판단·Sim 역류 금지.
        // ─────────────────────────────────────────────────────────────────────────

        // 주차칸은 건물 주차선으로만 표시한다. 이전 배정 마크가 남아 있으면 제거한다.
        private void RebuildParkingVisuals()
        {
            DestroyParkingVisuals();
        }

        private void DestroyParkingVisuals()
        {
            if (parkingRoot == null)
            {
                parkingRoot = transform.Find("Parking");
            }

            if (parkingRoot != null)
            {
                ClearChildren(parkingRoot);
            }
        }

        // 개성 바인딩(Step 1): (Home, HomeSlot) 해시로 스케일·본색 적용. FromHash는 순수·저비용이라
        // 바인딩 시 1회 계산해 캐시(색은 여기서만 적용 — 이동 루프의 프레임당 색 재적용 제거).
        // 진행축 = 로컬 x(회전 Euler(0,0,angle), dir.x/dir.y) → 길이=x, 폭=y(지면 평면), 높이=z(불변).
        private void ApplyCarStyle(RouteVehicle vehicle, CommuteCar car)
        {
            CarStyle style = CarStyle.FromHash(car.Home, car.HomeSlot);
            vehicle.Style = style;

            vehicle.Object.transform.localScale =
                vehicle.UsesAuthoredVisual
                    ? Vector3.one *
                      (tileSize *
                       VehicleBodyLengthTiles)
                    : new Vector3(
                        tileSize *
                        VehicleBodyLengthTiles,
                        tileSize *
                        VehicleBodyWidthTiles *
                        style.WidthScale,
                        tileSize *
                        VehicleBodyHeightTiles);

            Color body = CarStyle.Palette[style.ColorIndex];
            if (!vehicle.UsesAuthoredVisual &&
                vehicle.Renderer != null)
            {
                ApplyRendererColor(vehicle.Renderer, body);
            }

            if (vehicle.DetailRenderer != null)
            {
                ApplyRendererColor(vehicle.DetailRenderer, Color.Lerp(body, Color.white, 0.3f));
            }
        }

        // 제동등 토글 — 상태 캐시로 매 프레임 SetActive 금지(진입/이탈 순간에만 호출).
        private static void SetBrakeLight(RouteVehicle vehicle, bool on)
        {
            if (vehicle.BrakeLight == null || vehicle.BrakeOn == on)
            {
                return;
            }

            vehicle.BrakeOn = on;
            vehicle.BrakeLight.SetActive(on);
        }

        private static void SetVehicleRenderersEnabled(RouteVehicle vehicle, bool enabled)
        {
            if (vehicle.Renderers != null)
            {
                for (int i = 0; i < vehicle.Renderers.Length; i++)
                {
                    if (vehicle.Renderers[i] != null)
                    {
                        vehicle.Renderers[i].enabled = enabled;
                    }
                }
                return;
            }

            if (vehicle.Renderer != null)
            {
                vehicle.Renderer.enabled = enabled;
            }

            if (vehicle.DetailRenderer != null)
            {
                vehicle.DetailRenderer.enabled = enabled;
            }
        }

        private static void HideJamMarks(RouteVehicle vehicle)
        {
            if (vehicle.AngryMark != null)
            {
                vehicle.AngryMark.SetActive(false);
            }
        }

        private void HandleSignalInput()
        {
            if (signalControl == null || signalControl.SignalTiles.Count == 0)
            {
                return;
            }

            IReadOnlyList<Vector2Int> signals = signalControl.SignalTiles;

            if (selectedSignalIndex >= signals.Count)
            {
                selectedSignalIndex = 0;
            }

            Mouse mouse = Mouse.current;

            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Camera.main != null)
            {
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                {
                    return;
                }

                Ray ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
                Plane boardPlane = new Plane(transform.forward, cameraTarget);
                if (!boardPlane.Raycast(ray, out float enter))
                {
                    return;
                }

                Vector2Int clicked = WorldToGrid(ray.GetPoint(enter));

                for (int i = 0; i < signals.Count; i++)
                {
                    if (signals[i] == clicked)
                    {
                        selectedSignalIndex = i;
                        break;
                    }
                }
            }

            Keyboard keyboard = Keyboard.current;

            if (keyboard == null)
            {
                return;
            }

            if (keyboard.nKey.wasPressedThisFrame)
            {
                SelectNextSignal();
            }

            if (keyboard.commaKey.wasPressedThisFrame)
            {
                AddSelectedSignalOffset(-1);
            }

            if (keyboard.periodKey.wasPressedThisFrame)
            {
                AddSelectedSignalOffset(1);
            }

            if (keyboard.leftBracketKey.wasPressedThisFrame)
            {
                AddSelectedSignalGreen(-1);
            }

            if (keyboard.rightBracketKey.wasPressedThisFrame)
            {
                AddSelectedSignalGreen(1);
            }

            if (keyboard.rKey.wasPressedThisFrame &&
                (placementController == null ||
                 !placementController.IsBuildingMode))
            {
                ResetSignalOffsets();
            }

            if (simEngine == null)
            {
                return;
            }

            if (keyboard.gKey.wasPressedThisFrame)
            {
                OverrideSelectedSignal(horizontal: true);
            }

            if (keyboard.vKey.wasPressedThisFrame)
            {
                OverrideSelectedSignal(horizontal: false);
            }
        }

        public void SelectNextSignal()
        {
            if (signalControl == null || signalControl.SignalTiles.Count == 0)
            {
                return;
            }

            selectedSignalIndex = (selectedSignalIndex + 1) % signalControl.SignalTiles.Count;
        }

        public void AddSelectedSignalOffset(int delta)
        {
            if (!TryGetSelectedSignal(out Vector2Int selected))
            {
                return;
            }

            signalControl.TrySetSignalOffsetSlots(selected, signalControl.GetSignalOffsetSlots(selected) + delta);
        }

        public void AddSelectedSignalGreen(int delta)
        {
            if (!TryGetSelectedSignal(out Vector2Int selected))
            {
                return;
            }

            signalControl.TrySetSignalGreenSlots(selected, signalControl.GetSignalGreenSlots(selected) + delta);
        }

        public void ResetSignalOffsets()
        {
            if (signalControl == null)
            {
                return;
            }

            IReadOnlyList<Vector2Int> signals = signalControl.SignalTiles;

            for (int i = 0; i < signals.Count; i++)
            {
                signalControl.TrySetSignalOffsetSlots(signals[i], 0);
            }
        }

        public void OverrideSelectedSignal(bool horizontal)
        {
            if (signalControl == null || !TryGetSelectedSignal(out Vector2Int selected))
            {
                return;
            }

            signalControl.TryOverrideSignal(selected, horizontal);
        }

        private bool TryGetSelectedSignal(out Vector2Int selected)
        {
            selected = default;

            if (signalControl == null || signalControl.SignalTiles.Count == 0)
            {
                return false;
            }

            if (selectedSignalIndex >= signalControl.SignalTiles.Count)
            {
                selectedSignalIndex = 0;
            }

            selected = signalControl.SignalTiles[selectedSignalIndex];
            return true;
        }

        private void OnPlaced(PlacedEvent e)
        {
            RefreshTile(e.Tile, e.IsRemove ? TileType.Empty : e.Type);
            if (e.Type == TileType.Road)
            {
                RefreshBuildingOrientations();
                RefreshRoadVisualsAround(e.Tile);
            }
            RefreshSignals();
        }

        // 도착 코인 팝(항목 A, 버퍼링 개정): Sim이 금액·타이밍을 결정(ArrivalEvent 그대로) — 뷰는 표시
        // 시점·위치만 고른다. 이벤트는 적립만 하고 방출은 차 도착/타임아웃/리빌드가 담당(HUD 숫자는 기존대로 틱 갱신).
        private void OnArrival(ArrivalEvent e)
        {
            pendingCoinPops.TryGetValue(e.Destination, out PendingCoinPop pending);
            pendingCoinPops[e.Destination] = new PendingCoinPop
            {
                Coins = pending.Coins + e.Coins,
                FirstQueuedTime = pending.Coins > 0 ? pending.FirstQueuedTime : Time.time
            };
        }

        // 타일의 적립 코인을 전액 팝으로 방출 후 0으로. pending 없으면 no-op(금액 보존 불변식의 방출 단일 경로).
        private void FlushPendingCoinPop(Vector2Int tile, Vector3 worldPos)
        {
            if (!pendingCoinPops.TryGetValue(tile, out PendingCoinPop pending) || pending.Coins <= 0)
            {
                return;
            }

            pendingCoinPops[tile] = default;
            SpawnCoinPop(worldPos, pending.Coins);
        }

        // 타임아웃 방출: 차 도착이 coinPopFlushSeconds 동안 없던 타일은 건물 중심 팝 —
        // 통근 정원(maxCars/슬롯)에서 제외된 수요의 수익도 가시성 유지. 재사용 버퍼로 순회 중 쓰기 회피(할당 0).
        private void FlushTimedOutCoinPops()
        {
            coinPopFlushBuffer.Clear();
            foreach (KeyValuePair<Vector2Int, PendingCoinPop> pair in pendingCoinPops)
            {
                if (pair.Value.Coins > 0 && Time.time - pair.Value.FirstQueuedTime >= coinPopFlushSeconds)
                {
                    coinPopFlushBuffer.Add(pair.Key);
                }
            }

            for (int i = 0; i < coinPopFlushBuffer.Count; i++)
            {
                Vector2Int tile = coinPopFlushBuffer[i];
                FlushPendingCoinPop(tile, transform.TransformPoint(GridToLocal(tile, VehicleGroundZ)));
            }
        }

        // 위상 리빌드 방출: 대기 중 전액을 타일 팝으로 비운다 — 리빌드로 차/경로가 갈려도 코인 유실 금지.
        private void FlushAllPendingCoinPops()
        {
            coinPopFlushBuffer.Clear();
            foreach (KeyValuePair<Vector2Int, PendingCoinPop> pair in pendingCoinPops)
            {
                if (pair.Value.Coins > 0)
                {
                    coinPopFlushBuffer.Add(pair.Key);
                }
            }

            for (int i = 0; i < coinPopFlushBuffer.Count; i++)
            {
                Vector2Int tile = coinPopFlushBuffer[i];
                FlushPendingCoinPop(tile, transform.TransformPoint(GridToLocal(tile, VehicleGroundZ)));
            }
        }

        private void SpawnCoinPop(Vector3 worldPos, int coins)
        {
            EnsureCoinPopPool();
            CoinPop pop = GetCoinPop();
            pop.Text.text = $"+{coins}";
            pop.StartPos = worldPos;
            pop.StartTime = Time.time;
            pop.Object.transform.position = worldPos;
            Color color = CoinPopColor;
            pop.Text.color = color;
            pop.Object.SetActive(true);
            AlignTextMarkPerpendicularToGround(pop.Object.transform);
        }

        private void EnsureCoinPopPool()
        {
            if (coinPopPoolReady)
            {
                return;
            }

            for (int i = 0; i < CoinPopPoolSize; i++)
            {
                GameObject go = CreateTextMark(vehicleRoot, "+0", CoinPopColor, tileSize * 0.16f);
                go.SetActive(false);
                coinPops[i] = new CoinPop { Object = go, Text = go.GetComponent<TextMesh>() };
            }

            coinPopPoolReady = true;
        }

        // 고정 크기 풀: 빈 슬롯 우선 재사용, 전부 사용 중이면 라운드로빈으로 가장 오래된 슬롯을 뺏는다
        // (러시아워 다발 대비 — Instantiate 없이 항상 12개 안에서 순환, 프레임당 힙 할당 0).
        private CoinPop GetCoinPop()
        {
            for (int i = 0; i < CoinPopPoolSize; i++)
            {
                if (!coinPops[i].Object.activeSelf)
                {
                    return coinPops[i];
                }
            }

            CoinPop stolen = coinPops[coinPopCursor];
            coinPopCursor = (coinPopCursor + 1) % CoinPopPoolSize;
            return stolen;
        }

        // 위로 떠오르며 페이드아웃(~0.8초). HUD로 날아가는 연출은 스코프 아웃(YAGNI).
        private void RefreshCoinPops()
        {
            FlushTimedOutCoinPops();

            if (!coinPopPoolReady)
            {
                return;
            }

            Vector3 groundUp = -transform.forward;
            for (int i = 0; i < CoinPopPoolSize; i++)
            {
                CoinPop pop = coinPops[i];
                if (pop == null || !pop.Object.activeSelf)
                {
                    continue;
                }

                float elapsed01 = Mathf.Clamp01((Time.time - pop.StartTime) / CoinPopDuration);
                if (elapsed01 >= 1f)
                {
                    pop.Object.SetActive(false);
                    continue;
                }

                pop.Object.transform.position = pop.StartPos + groundUp * (tileSize * 0.5f * elapsed01);
                AlignTextMarkPerpendicularToGround(pop.Object.transform);

                Color color = pop.Text.color;
                color.a = 1f - elapsed01;
                pop.Text.color = color;
            }
        }

        private void OnRestoreCompleted(RestoreCompletedEvent _)
        {
            RebuildRestoredVisuals();
        }

        public Vector3 GetFlowBurstAnchor(Vector2Int tile, out Transform targetVehicle)
        {
            Vector3 tileCenter = transform.TransformPoint(GridToLocal(tile, VehicleGroundZ));
            RouteVehicle bestVehicle = null;
            int bestPriority = int.MaxValue;
            float bestDistanceSqr = float.MaxValue;

            for (int i = 0; i < vehicles.Count; i++)
            {
                RouteVehicle vehicle = vehicles[i];
                if (!vehicle.HasCurrentTile || !vehicle.Object.activeInHierarchy
                    || (vehicle.Renderer != null && !vehicle.Renderer.enabled))
                {
                    continue;
                }

                bool sameTile = vehicle.CurrentTile == tile;
                Vector2Int tileDistance = vehicle.CurrentTile - tile;
                if (Mathf.Abs(tileDistance.x) > flowBurstAnchorRadius
                    || Mathf.Abs(tileDistance.y) > flowBurstAnchorRadius)
                {
                    continue;
                }

                bool wasJammed = vehicle.AngryMark != null && vehicle.AngryMark.activeSelf;
                int priority = sameTile
                    ? (wasJammed ? 0 : 1)
                    : (wasJammed ? 2 : 3);
                float distanceSqr = (vehicle.Object.transform.position - tileCenter).sqrMagnitude;

                if (priority < bestPriority || (priority == bestPriority && distanceSqr < bestDistanceSqr))
                {
                    bestVehicle = vehicle;
                    bestPriority = priority;
                    bestDistanceSqr = distanceSqr;
                }
            }

            Vector3 groundUp = -transform.forward;
            targetVehicle = bestVehicle != null ? bestVehicle.Object.transform : null;
            return bestVehicle != null
                ? bestVehicle.Object.transform.position + groundUp * (tileSize * 0.35f)
                : tileCenter + groundUp * (tileSize * 0.35f);
        }

        public Vector3 GridToLocal(Vector2Int tile, float z)
        {
            Vector2Int localTile = tile - gridOrigin;
            return new Vector3(
                (localTile.x + 0.5f) * tileSize,
                (localTile.y + 0.5f) * tileSize,
                z);
        }

        public Vector3 GetSpecialBuildingParkingPosition(
            Vector2Int building,
            float groundZ)
        {
            if (tileVisuals.TryGetValue(
                    building,
                    out TileVisual visual))
            {
                Transform slot =
                    visual.Object.transform.Find("ParkingSlot_0");
                if (slot != null)
                {
                    Vector3 local =
                        transform.InverseTransformPoint(
                            slot.position);
                    local.z = groundZ;
                    return local;
                }
            }

            Vector3 center =
                FootprintToLocal(
                    building,
                    TileType.Hospital);
            Vector3 forward =
                GetSpecialBuildingParkingRotation(building) *
                Vector3.right;
            center += forward * (tileSize * 0.28f);
            center.z = groundZ;
            return center;
        }

        public Quaternion GetSpecialBuildingParkingRotation(
            Vector2Int building)
        {
            return GetRoadFacingRotation(
                       building,
                       TileType.Hospital) *
                   Quaternion.Euler(0f, 0f, 90f);
        }

        private Vector2Int WorldToGrid(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            return gridOrigin + new Vector2Int(
                Mathf.FloorToInt(local.x / tileSize),
                Mathf.FloorToInt(local.y / tileSize));
        }

        private static GameObject InstantiatePrefabOrPrimitive(GameObject prefab, PrimitiveType primitive)
        {
            return prefab != null ? Instantiate(prefab) : GameObject.CreatePrimitive(primitive);
        }

        private static Renderer PrepareRenderer(
            Renderer renderer,
            int renderQueue = (int)RenderQueue.Geometry)
        {
            if (renderer != null)
            {
                renderer.sharedMaterial = CreateUnlitMaterial(renderer.sharedMaterial, renderQueue);
                renderer.enabled = true;
                renderer.forceRenderingOff = false;
                renderer.allowOcclusionWhenDynamic = false;
            }

            return renderer;
        }

        private static Material CreateUnlitMaterial(
            Material fallbackMaterial = null,
            int renderQueue = (int)RenderQueue.Geometry)
        {
            Shader shader = Resources.Load<Shader>("CityFlowOpaqueUnlit");
            shader ??= Shader.Find("GreenLight/CityFlow Opaque Unlit");
            shader ??= Shader.Find("Universal Render Pipeline/Unlit");
            shader ??= Shader.Find("Universal Render Pipeline/Simple Lit");
            shader ??= Shader.Find("Universal Render Pipeline/Lit");
            shader ??= Shader.Find("Unlit/Color");
            shader ??= Shader.Find("Standard");

            Material material = shader != null
                ? new Material(shader)
                : new Material(fallbackMaterial);
            ConfigureOpaqueMaterial(material, renderQueue);
            return material;
        }

        private static void ConfigureOpaqueMaterial(Material material, int renderQueue)
        {
            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = renderQueue;
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHATEST_ON");

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 0f);
            }

            if (material.HasProperty("_AlphaClip"))
            {
                material.SetFloat("_AlphaClip", 0f);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 1f);
            }

            if (material.HasProperty("_SrcBlend"))
            {
                material.SetFloat("_SrcBlend", (float)BlendMode.One);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            }
        }

        private static void SetRendererRenderQueue(Renderer renderer, int renderQueue)
        {
            if (renderer != null && renderer.sharedMaterial != null)
            {
                renderer.sharedMaterial.renderQueue = renderQueue;
            }
        }

        private Material CreateGridMaterial(Material fallbackMaterial)
        {
            Material material = CreateUnlitMaterial(fallbackMaterial, 1900);
            Texture2D texture = CreateGridTexture();

            material.mainTexture = texture;

            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 0f);
            }

            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.white);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", Color.white);
            }

            return material;
        }

        private Texture2D CreateGridTexture()
        {
            const int cellPixels = 48;
            int linePixels = Mathf.Max(2, Mathf.RoundToInt(cellPixels * Mathf.Clamp01(gridLineThickness)));
            int textureWidth = width * cellPixels + linePixels;
            int textureHeight = height * cellPixels + linePixels;

            Texture2D texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false)
            {
                name = "MainCityGridTexture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[textureWidth * textureHeight];

            for (int y = 0; y < textureHeight; y++)
            {
                bool horizontalLine = IsGridLinePixel(y, height, cellPixels, linePixels);

                for (int x = 0; x < textureWidth; x++)
                {
                    bool verticalLine = IsGridLinePixel(x, width, cellPixels, linePixels);
                    pixels[y * textureWidth + x] = horizontalLine || verticalLine ? gridLineColor : boardColor;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return texture;
        }

        private static bool IsGridLinePixel(int value, int cellCount, int cellPixels, int linePixels)
        {
            return value < linePixels
                || value >= cellCount * cellPixels
                || value % cellPixels < linePixels;
        }

        private static void ApplyRendererColor(Renderer renderer, Color color)
        {
            ApplyRendererColor(renderer, color, null);
        }

        private static void ApplyRendererColor(Renderer renderer, Color color, MaterialPropertyBlock block)
        {
            if (renderer == null)
            {
                return;
            }

            MaterialPropertyBlock targetBlock = block ?? new MaterialPropertyBlock();
            renderer.GetPropertyBlock(targetBlock);
            targetBlock.SetColor("_BaseColor", color);
            targetBlock.SetColor("_Color", color);
            renderer.SetPropertyBlock(targetBlock);
        }
    }
}
