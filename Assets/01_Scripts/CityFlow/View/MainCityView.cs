using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.Sim;
using CityFlow.ViewKit;
using CityFlow.UI;
using CityFlow.UI.Controllers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace CityFlow.View
{
    public sealed partial class MainCityView : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("Grid")]
        [SerializeField] private int width = GridUtil.DefaultWidth;
        [SerializeField] private int height = GridUtil.DefaultHeight;
        [SerializeField] private float tileSize = GridUtil.TileSize;

        [Header("Optional Prefabs")]
        [SerializeField] private GameObject roadPrefab;
        [SerializeField] private GameObject housePrefab;
        [SerializeField] private GameObject officePrefab;
        [SerializeField] private GameObject schoolPrefab;
        [SerializeField] private GameObject vehiclePrefab;
        [SerializeField] private GameObject signalPrefab;
        [SerializeField] private GameObject burstPrefab;

        [Header("Runtime Visuals")]
        [SerializeField] private float vehicleSpeed = 1.6f;   // 개성 패스 라이브 튜닝(환 2026-07-17): 2.0에서 20% 감속
        [SerializeField] private float vehicleZ = -0.35f;
        [SerializeField] private float signalZ = -0.45f;
        [SerializeField] private float burstSeconds = 0.8f;
        [SerializeField, Min(0)] private int flowBurstAnchorRadius = 3;
        [SerializeField] private float gridLineThickness = 0.045f;
        [SerializeField] private float overridePulseAmp = 0.25f;   // 신호 펄스 진폭
        // 우측통행 차선 오프셋(타일 비율). 교차 차량의 분리 거리 = laneOffset × √2 이므로
        // 이 값이 교차로 겹침을 직접 지배한다. 정규화 스윕(2026-07-20, 교대 3라운드):
        //   0.18 → 교차겹침 17.24/1k차프레임 | 0.22 → 11.79 | **0.26 → 8.94** | 0.30 → 15.75
        // 0.30에서 다시 나빠지는 건 차가 중앙선에서 너무 벗어나 코너 곡선과 어긋나기 때문.
        // ⚠️ 씬에 직렬화된 값이 이 기본값을 덮는다 — 씬 인스펙터에서도 0.26으로 맞출 것.
        [SerializeField] private float laneOffset = 0.26f;
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
        [SerializeField, Range(0.6f, 0.85f)] private float cornerTurnRadius = 0.75f;   // 일반 교차로 회전 반경(타일 비율)
        [SerializeField] private float turnSignZ = -0.5f;           // 표지판 마커 z(신호와 분리 — 공존 타일 겹침 회피)

        [Header("Roundabout Tuning")]   // 재생 중 슬라이더 조정 → 통근 폴리라인 즉시 리베이크(QA G)
        // 궤도 반경(타일 비율) = 차도 정중앙. 차도는 섬 0.45(Island scale 0.9)와 링 1.1(Ring scale 2.2)
        // 사이의 고리이므로 중앙은 (0.45+1.1)/2 = 0.775 — 섬 스침·바깥 이탈 양쪽에 여유 0.325로 균등.
        // 2026-07-21 전 씬 통일. 씬에 0.3(섬 내부 = 관통)이 5개 있었다. Range는 인스펙터만 막고
        // 직렬화 값은 그대로 쓰이므로, 값을 바꿀 때는 반드시 7개 씬을 함께 맞출 것.
        [SerializeField, Range(0.5f, 1.1f)] private float roundaboutOrbitRadius = 0.775f;
        [SerializeField, Range(10f, 80f)] private float roundaboutEntryExitDeg = 45f;      // α — 진입/이탈을 링 둘레로 미는 각. 클수록 링 체류 짧아짐
        [SerializeField, Range(0.66f, 0.95f)] private float roundaboutTransitionTiles = 0.66f; // 전이 곡선 길이(타일) — 클수록 진입/이탈 완만. 하한 0.66 = 섬 스침 방지 실측(√(span²+λ²)>0.62, RoutePolyline.cs:316,392)

        [Header("Commute (2차 빌드)")]
        [SerializeField] private float parkingSlotInset = 0.32f;   // 건물 타일 내 칸 오프셋(타일 비율)
        [SerializeField, Range(0f, 1f)] private float parkingSettleSeconds = 0.3f;   // 도착 후 슬롯 정착 정지 안무(초)
        [SerializeField, Min(0.5f)] private float coinPopFlushSeconds = 5f;   // 코인 팝 버퍼 타임아웃 — 차 도착이 이만큼 없으면 타일 팝으로 방출

        [Header("Camera View")]
        [SerializeField, Range(1f, 89f)] private float angledViewDegrees = 35.264f;
        [Tooltip("지면에서 가장 가까운 A 줌 지점까지의 거리")]
        [SerializeField, Min(0.5f)] private float minimumZoomDistance = 5f;
        [Tooltip("A-B 줌 지점 사이에 적용할 거리")]
        [FormerlySerializedAs("zoomStepDistance")]
        [SerializeField, Min(0.1f)] private float zoomDistanceRange = 10f;
        [Tooltip("마우스 휠 입력 1단위당 변경할 카메라 거리")]
        [SerializeField, Min(0.001f)] private float zoomScrollSensitivity = 1f;

        private const float OrthographicSizePerDistance = 0.9375f;
        private const int VehicleRenderQueue = (int)RenderQueue.Geometry + 10;

        [Header("Colors")]
        [SerializeField] private Color boardColor = new Color(0.78f, 0.82f, 0.78f);
        [SerializeField] private Color gridLineColor = new Color(0.28f, 0.36f, 0.38f, 1f);
        [SerializeField] private Color roadFreeColor = new Color(0.32f, 0.36f, 0.43f);
        [SerializeField] private Color roadSlowColor = new Color(0.95f, 0.72f, 0.25f);
        [SerializeField] private Color roadJamColor = new Color(0.9f, 0.22f, 0.18f);
        [SerializeField] private Color houseColor = new Color(0.35f, 0.6f, 0.86f);
        [SerializeField] private Color officeColor = new Color(0.92f, 0.59f, 0.24f);
        [SerializeField] private Color schoolColor = new Color(0.66f, 0.42f, 0.82f);
        [SerializeField] private Color vehicleColor = new Color(0.12f, 0.12f, 0.16f);
        [SerializeField] private Color selectedSignalColor = Color.white;
        [SerializeField] private Color flowBurstColor = new Color(1f, 0.78f, 0.12f);
        [SerializeField] private Color roundaboutColor = new Color(0.35f, 0.78f, 0.45f);
        [SerializeField] private Color overpassColor = new Color(0.55f, 0.62f, 0.75f);
        [SerializeField] private Color onewayColor = new Color(0.95f, 0.85f, 0.15f);
        [SerializeField] private Color turnSignColor = new Color(0.95f, 0.35f, 0.75f);

        private readonly Dictionary<Vector2Int, TileVisual> tileVisuals = new();
        private readonly Dictionary<Vector2Int, SignalVisual> signalVisuals = new();
        private readonly Dictionary<Vector2Int, GameObject> roundaboutVisuals = new();
        private readonly Dictionary<Vector2Int, GameObject> overpassVisuals = new();
        private readonly Dictionary<Vector2Int, GameObject> onewayVisuals = new();
        private readonly Dictionary<Vector2Int, TurnSignVisual> turnSignVisuals = new();
        private readonly Dictionary<Vector2Int, GameObject> priorityRoadVisuals = new();
        private readonly Dictionary<Vector2Int, GameObject> highwayVisuals = new();
        private readonly List<RouteVehicle> vehicles = new();

        // 통근 상태(유일 경로). 위상 리빌드 시 재구성된다.
        private readonly List<CommuteCar> carSimMirrors = new();
        private readonly Dictionary<int, BakedRoutePair> bakedRoutes = new();   // 키 = RouteIndex, 해시 변경 시 재베이크
        private readonly Dictionary<CommuteCar, RouteVehicle> carVehicles = new();
        private readonly Dictionary<Vector2Int, List<GameObject>> parkingSlotVisuals = new();
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
        private RouteVehicle selectedVehicle;
        private Vector3 cameraTarget;
        private Vector3 cameraUpDirection;
        private float zoomDistance;
        private bool isIsometricView;

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
        public float TileSize => tileSize;
        public float FlowBurstSeconds => burstSeconds;
        public Color FlowBurstColor => flowBurstColor;
        public bool IsDriveViewActive => driveViewCamera != null && driveViewCamera.IsFollowing;

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
        }

        private sealed class SignalVisual
        {
            public GameObject Root;
            public Renderer HorizontalRenderer;
            public Renderer VerticalRenderer;
            public Renderer SelectionRenderer;
            public MaterialPropertyBlock HorizontalBlock;
            public MaterialPropertyBlock VerticalBlock;
            public MaterialPropertyBlock SelectionBlock;
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
            public Renderer DetailRenderer;
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
            tileData = services.TileData;
            placement = services.Placement;
            simEngine = services.Placement as SimEngine;
            signalControl = services.Placement as ISignalControl;
            intersectionFacility = services.Placement as IIntersectionFacilityService;
            trafficRule = services.Placement as ITrafficRuleService;
            highwayService = services.Placement as IHighwayService;

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
            gameObject.AddComponent<FloatingWindowService>().Init(width * tileSize, height * tileSize, false);
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

            cameraTarget = transform.TransformPoint(new Vector3(
                width * tileSize * 0.5f,
                height * tileSize * 0.5f,
                0f));
            cameraUpDirection = (transform.up - transform.right).normalized;
            zoomDistance = minimumZoomDistance;
            isIsometricView = true;
            ApplyCameraView();
        }

        private void HandleCameraViewInput()
        {
            if (mainCamera == null)
            {
                return;
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
                bool isOverUI = UnityEngine.EventSystems.EventSystem.current != null &&
                                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
                if (!isOverUI)
                {
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
                ApplyCameraView();

                if (cameraModeChanged && tileData != null)
                {
                    RefreshAllTiles();
                }
            }
        }

        private void ApplyCameraView()
        {
            float viewDistance = Mathf.Clamp(
                zoomDistance,
                minimumZoomDistance,
                minimumZoomDistance + zoomDistanceRange);
            mainCamera.useOcclusionCulling = false;
            Vector3 cameraPosition = cameraTarget - transform.forward * viewDistance;

            if (isIsometricView)
            {
                Vector3 southEastDirection = (transform.right - transform.up).normalized;
                float angleRadians = angledViewDegrees * Mathf.Deg2Rad;
                Vector3 angledOffsetDirection = southEastDirection * Mathf.Cos(angleRadians)
                    - transform.forward * Mathf.Sin(angleRadians);
                Vector3 cameraForward = -angledOffsetDirection;
                float projectedBoardHalfDepth = width * tileSize * 0.5f
                    * Mathf.Abs(Vector3.Dot(transform.right, cameraForward))
                    + height * tileSize * 0.5f
                    * Mathf.Abs(Vector3.Dot(transform.up, cameraForward));
                float safeCameraDistance = projectedBoardHalfDepth
                    + mainCamera.nearClipPlane
                    + 1f;
                Vector3 angledOffset = angledOffsetDirection * safeCameraDistance;
                cameraPosition = cameraTarget + angledOffset;
            }

            Quaternion cameraRotation = Quaternion.LookRotation(cameraTarget - cameraPosition, cameraUpDirection);
            mainCamera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            mainCamera.orthographicSize = viewDistance * OrthographicSizePerDistance;
        }

        private void Update()
        {
            if (selectedVehicle != null && !IsDriveViewActive)
            {
                ExitDriveView();
            }

            HandleVehicleSelectionInput();
            if (!IsDriveViewActive)
            {
                HandleCameraViewInput();
            }

            if (tileData == null)
            {
                return;
            }

            if (!IsDriveViewActive)
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

            if (TryGetVehicleAtScreenPosition(mouse.position.ReadValue(), out RouteVehicle vehicle))
            {
                selectedVehicle = vehicle;
                driveViewCamera.Follow(vehicle.Object.transform);
                if (IsDriveViewActive)
                {
                    dockController?.SetDriveViewActive(true);
                }
                else
                {
                    selectedVehicle = null;
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
            selectedVehicle = null;
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
            return TryGetVehicleAtScreenPosition(screenPosition, out _);
        }

        private bool TryGetVehicleAtScreenPosition(Vector2 screenPosition, out RouteVehicle vehicle)
        {
            vehicle = null;
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

            Transform hitTransform = hit.collider.transform;
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
                    vehicle = candidate;
                    return true;
                }
            }

            return false;
        }

        private void BuildRoots()
        {
            boardRoot = CreateChildRoot("Board");
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

            if (existing != null)
            {
                return existing;
            }

            GameObject root = new GameObject(rootName);
            root.transform.SetParent(transform, false);
            return root.transform;
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
            boardRenderer.sharedMaterial = CreateGridMaterial(boardRenderer.sharedMaterial);
            boardRenderer.allowOcclusionWhenDynamic = false;
            boardRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            boardRenderer.receiveShadows = false;
        }

        private void BuildGridLines()
        {
            if (gridRoot == null)
            {
                return;
            }

            ClearChildren(gridRoot);
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
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector2Int tile = new Vector2Int(x, y);
                    RefreshTile(tile, tileData.GetTileType(tile));
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

            if (!tileVisuals.TryGetValue(tile, out TileVisual visual))
            {
                visual = CreateTileVisual(tile, type);
                tileVisuals.Add(tile, visual);
            }

            visual.Type = type;
            Vector3 tileScale = GetTileScale(type);
            float tileZ = type == TileType.Road ? 0f : -tileScale.z * 0.5f;
            visual.Object.SetActive(true);
            visual.Renderer.enabled = true;
            visual.Renderer.forceRenderingOff = false;
            visual.Renderer.allowOcclusionWhenDynamic = false;
            visual.Object.transform.localPosition = GridToLocal(tile, tileZ);
            visual.Object.transform.localScale = tileScale;
            ApplyTileColor(tile, visual);
            ConfigureRoadCongestionView(tile, visual);
        }

        private void ConfigureRoadCongestionView(Vector2Int tile, TileVisual visual)
        {
            RoadCongestionView congestionView = visual.Object.GetComponent<RoadCongestionView>();

            if (visual.Type != TileType.Road)
            {
                if (congestionView != null)
                {
                    Destroy(congestionView);
                }
                return;
            }

            if (congestionView == null)
            {
                congestionView = visual.Object.AddComponent<RoadCongestionView>();
            }

            congestionView.Configure(tile, visual.Renderer, roadFreeColor, roadSlowColor, roadJamColor);
            congestionView.Initialize(services);
        }

        private TileVisual CreateTileVisual(Vector2Int tile, TileType type)
        {
            GameObject prefab = GetPrefab(type);
            GameObject instance = InstantiatePrefabOrPrimitive(prefab, PrimitiveType.Cube);
            instance.name = $"{type}_{tile.x}_{tile.y}";
            instance.transform.SetParent(tileRoot, false);

            Renderer renderer = PrepareRenderer(instance.GetComponentInChildren<Renderer>());
            if (prefab == null && type != TileType.Road)
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
            Color color = visual.Type switch
            {
                TileType.Road => GetRoadColor(tileData.GetCongestion(tile)),
                TileType.House => houseColor,
                TileType.Office => officeColor,
                TileType.School => schoolColor,
                _ => Color.clear
            };

            ApplyRendererColor(visual.Renderer, color, visual.Block);
        }

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
                _ => Vector3.one * tileSize
            };
        }

        private void AddFallbackBuildingDetails(Transform building, TileType type)
        {
            Color detailColor = type switch
            {
                TileType.House => Color.Lerp(houseColor, Color.white, 0.35f),
                TileType.Office => Color.Lerp(officeColor, Color.white, 0.2f),
                TileType.School => Color.Lerp(schoolColor, Color.black, 0.2f),
                _ => Color.white
            };

            Vector3 detailScale = type switch
            {
                TileType.House => new Vector3(0.78f, 0.78f, 0.28f),
                TileType.Office => new Vector3(0.72f, 0.72f, 0.18f),
                TileType.School => new Vector3(0.42f, 0.22f, 0.4f),
                _ => Vector3.one * 0.2f
            };
            Vector3 detailPosition = type switch
            {
                TileType.House => new Vector3(0f, 0f, -0.64f),
                TileType.Office => new Vector3(0f, 0f, -0.58f),
                TileType.School => new Vector3(0f, -0.48f, -0.42f),
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

        private GameObject GetPrefab(TileType type)
        {
            return type switch
            {
                TileType.Road => roadPrefab,
                TileType.House => housePrefab,
                TileType.Office => officePrefab,
                TileType.School => schoolPrefab,
                _ => null
            };
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

                ApplySignalState(tile, visual, i == selectedSignalIndex);
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
            root.transform.localPosition = GridToLocal(tile, signalZ);

            Renderer post = CreateSignalBar(root.transform, "Post",
                new Vector3(tileSize * 0.08f, tileSize * 0.08f, tileSize * 0.6f), new Vector3(0f, 0f, tileSize * 0.15f));
            ApplyRendererColor(post, new Color(0.18f, 0.2f, 0.22f));

            Renderer horizontal = CreateSignalBar(root.transform, "Horizontal",
                new Vector3(tileSize * 0.42f, tileSize * 0.1f, tileSize * 0.14f), new Vector3(0f, 0f, -tileSize * 0.18f));
            Renderer vertical = CreateSignalBar(root.transform, "Vertical",
                new Vector3(tileSize * 0.1f, tileSize * 0.42f, tileSize * 0.14f), new Vector3(0f, 0f, -tileSize * 0.18f));
            Renderer selection = CreateSignalBar(root.transform, "Selection",
                new Vector3(tileSize * 0.72f, tileSize * 0.72f, 0.03f), new Vector3(0f, 0f, tileSize * 0.42f));

            return new SignalVisual
            {
                Root = root,
                HorizontalRenderer = horizontal,
                VerticalRenderer = vertical,
                SelectionRenderer = selection,
                HorizontalBlock = new MaterialPropertyBlock(),
                VerticalBlock = new MaterialPropertyBlock(),
                SelectionBlock = new MaterialPropertyBlock()
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

        private void ApplySignalState(Vector2Int tile, SignalVisual visual, bool selected)
        {
            Color horizontal = GetSignalColor(tile, horizontal: true);
            Color vertical = GetSignalColor(tile, horizontal: false);

            visual.Root.transform.localPosition = GridToLocal(tile, signalZ);
            ApplyRendererColor(visual.HorizontalRenderer, horizontal, visual.HorizontalBlock);
            ApplyRendererColor(visual.VerticalRenderer, vertical, visual.VerticalBlock);

            if (visual.SelectionRenderer != null)
            {
                visual.SelectionRenderer.gameObject.SetActive(selected);
                ApplyRendererColor(visual.SelectionRenderer, selectedSignalColor, visual.SelectionBlock);
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
                GameObject vehicle = InstantiatePrefabOrPrimitive(vehiclePrefab, PrimitiveType.Cube);
                vehicle.name = $"Vehicle_{vehicles.Count + 1}";
                vehicle.transform.SetParent(vehicleRoot, false);
                vehicle.transform.localScale = new Vector3(tileSize * 0.38f, tileSize * 0.2f, tileSize * 0.28f);

                if (vehicle.GetComponentInChildren<Collider>() == null)
                {
                    vehicle.AddComponent<BoxCollider>();
                }

                Renderer renderer = vehicle.GetComponentInChildren<Renderer>();
                PrepareRenderer(renderer, VehicleRenderQueue);
                ApplyRendererColor(renderer, vehicleColor);

                Renderer detailRenderer = null;
                GameObject brakeLight = null;
                if (vehiclePrefab == null)
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
                    DetailRenderer = detailRenderer,
                    BrakeLight = brakeLight
                });
            }
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
                && tile.x >= 0 && tile.x < width
                && tile.y >= 0 && tile.y < height
                && tileData.GetTileType(tile) == TileType.Road;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 통근 이동(유일 경로 — RefreshVehicles가 항상 진입).
        // 뷰는 번역기: CarSim 스냅샷의 타일/큐 슬롯을 폴리라인 위치로 보간한다.
        // 차를 세우는 자체 판단·Sim 역류 금지.
        // ─────────────────────────────────────────────────────────────────────────

        // 주차칸 얇은 마크: 배정된 칸마다 1개(CreateDetailCube 패턴). 위상 리빌드 시 전부 재생성.
        private void RebuildParkingVisuals()
        {
            DestroyParkingVisuals();

            if (parkingRoot == null)
            {
                parkingRoot = CreateChildRoot("Parking");
            }

            Color slotColor = Color.Lerp(roadFreeColor, Color.white, 0.35f);   // 도로색 명도업
            HashSet<(Vector2Int, int)> seen = new HashSet<(Vector2Int, int)>();
            IReadOnlyList<CommuteCar> cars = CurrentCommuteCars();
            for (int i = 0; i < cars.Count; i++)
            {
                CommuteCar car = cars[i];
                if (!bakedRoutes.TryGetValue(car.RouteIndex, out BakedRoutePair pair))
                {
                    continue;
                }

                Vector3 homeAnchor = pair.Outbound.SampleAt(0f).Pos;
                Vector3 workAnchor = pair.Outbound.SampleAt(pair.Outbound.Length).Pos;
                AddParkingMark(car.Home, car.HomeSlot, homeAnchor, slotColor, seen);
                AddParkingMark(car.Work, car.WorkSlot, workAnchor, slotColor, seen);
            }
        }

        private void AddParkingMark(Vector2Int building, int slot, Vector3 anchor, Color color, HashSet<(Vector2Int, int)> seen)
        {
            if (!seen.Add((building, slot)))
            {
                return;
            }

            Renderer renderer = CreateDetailCube(
                parkingRoot,
                $"ParkingMark_{building.x}_{building.y}_{slot}",
                new Vector3(tileSize * 0.34f, tileSize * 0.34f, tileSize * 0.06f),
                anchor);
            ApplyRendererColor(renderer, color);

            if (!parkingSlotVisuals.TryGetValue(building, out List<GameObject> list))
            {
                list = new List<GameObject>();
                parkingSlotVisuals[building] = list;
            }

            list.Add(renderer.gameObject);
        }

        private void DestroyParkingVisuals()
        {
            foreach (KeyValuePair<Vector2Int, List<GameObject>> kv in parkingSlotVisuals)
            {
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    if (kv.Value[i] != null)
                    {
                        Destroy(kv.Value[i]);
                    }
                }
            }

            parkingSlotVisuals.Clear();
        }

        // 개성 바인딩(Step 1): (Home, HomeSlot) 해시로 스케일·본색 적용. FromHash는 순수·저비용이라
        // 바인딩 시 1회 계산해 캐시(색은 여기서만 적용 — 이동 루프의 프레임당 색 재적용 제거).
        // 진행축 = 로컬 x(회전 Euler(0,0,angle), dir.x/dir.y) → 길이=x, 폭=y(지면 평면), 높이=z(불변).
        private void ApplyCarStyle(RouteVehicle vehicle, CommuteCar car)
        {
            CarStyle style = CarStyle.FromHash(car.Home, car.HomeSlot);
            vehicle.Style = style;

            vehicle.Object.transform.localScale = new Vector3(
                tileSize * 0.38f * style.LengthScale,
                tileSize * 0.2f * style.WidthScale,
                tileSize * 0.28f);

            Color body = CarStyle.Palette[style.ColorIndex];
            if (vehicle.Renderer != null)
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

            if (keyboard.rKey.wasPressedThisFrame)
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
                FlushPendingCoinPop(tile, transform.TransformPoint(GridToLocal(tile, vehicleZ)));
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
                FlushPendingCoinPop(tile, transform.TransformPoint(GridToLocal(tile, vehicleZ)));
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
            Vector3 tileCenter = transform.TransformPoint(GridToLocal(tile, vehicleZ));
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

        private Vector3 GridToLocal(Vector2Int tile, float z)
        {
            return new Vector3((tile.x + 0.5f) * tileSize, (tile.y + 0.5f) * tileSize, z);
        }

        private Vector2Int WorldToGrid(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            return new Vector2Int(Mathf.FloorToInt(local.x / tileSize), Mathf.FloorToInt(local.y / tileSize));
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
