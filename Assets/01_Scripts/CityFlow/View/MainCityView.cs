using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.Sim;
using CityFlow.ViewKit;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace CityFlow.View
{
    public sealed class MainCityView : MonoBehaviour, ICityFlowServiceConsumer
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
        [SerializeField] private int maxMovingVehicles = 96;
        [SerializeField] private float vehicleSpeed = 1.6f;   // 개성 패스 라이브 튜닝(환 2026-07-17): 2.0에서 20% 감속
        [SerializeField, Min(0.1f)] private float vehicleAcceleration = 2.2f;
        [SerializeField, Min(0.1f)] private float vehicleDeceleration = 9f;
        [SerializeField] private float vehicleZ = -0.35f;
        [SerializeField] private float signalZ = -0.45f;
        [SerializeField] private float burstSeconds = 0.8f;
        [SerializeField, Min(1f)] private float flowBurstSpeedMultiplier = 1.6f;
        [SerializeField, Min(1f)] private float flowBurstAccelerationMultiplier = 3f;
        [SerializeField, Min(0.1f)] private float flowBurstSpeedDuration = 2.5f;
        [SerializeField, Min(0)] private int flowBurstSpeedRadius = 3;
        [SerializeField] private float gridLineThickness = 0.045f;
        [SerializeField] private float overrideSpeedMul = 2.2f;    // 오버라이드 라인 차량 속도 배율
        [SerializeField] private float overridePulseAmp = 0.25f;   // 신호 펄스 진폭
        [SerializeField] private float laneOffset = 0.18f;         // 우측통행 차선 오프셋(타일 비율)
        [SerializeField] private float followGap = 0.4f;           // 차간 유지 거리(타일 비율)
        [SerializeField, Range(0.3f, 2f)] private float crossYieldRange = 0.8f;   // 교차 양보 감지 반경(타일)
        [SerializeField, Range(0.1f, 1f)] private float crossYieldFloor = 0.3f;   // 양보 최저 속도 배율(0 금지)
        [SerializeField, Range(0f, 1f)] private float slowSpeedMul = 0.55f;
        [SerializeField, Range(0f, 1f)] private float jamSpeedMul = 0.25f;
        [SerializeField, Range(0.6f, 0.85f)] private float cornerTurnRadius = 0.75f;   // 일반 교차로 회전 반경(타일 비율)
        [SerializeField] private float turnSignZ = -0.5f;           // 표지판 마커 z(신호와 분리 — 공존 타일 겹침 회피)

        [Header("Roundabout Tuning")]   // 재생 중 슬라이더 조정 → 통근 폴리라인 즉시 리베이크(QA G)
        [SerializeField, Range(0.5f, 1.1f)] private float roundaboutOrbitRadius = 0.9f;    // 궤도 반경(타일 비율) — 풋프린트 차도 중앙(섬 0.45~판 1.1). 씬 직렬화 값 우선
        [SerializeField, Range(10f, 80f)] private float roundaboutEntryExitDeg = 45f;      // α — 진입/이탈을 링 둘레로 미는 각. 클수록 링 체류 짧아짐
        [SerializeField, Range(0.66f, 0.95f)] private float roundaboutTransitionTiles = 0.66f; // 전이 곡선 길이(타일) — 클수록 진입/이탈 완만. 하한 0.66 = 섬 스침 방지 실측(√(span²+λ²)>0.62, RoutePolyline.cs:316,392)

        [Header("Commute (2차 빌드)")]
        [SerializeField, Range(1, 12)] private int officeSlots = 6;
        [SerializeField, Range(1, 2)] private int homeSlots = 1;
        [SerializeField] private Vector2 morningWindow = new Vector2(6f, 10f);
        [SerializeField] private Vector2 eveningWindow = new Vector2(17f, 21f);
        [SerializeField, Range(0.1f, 1f)] private float parkingApproachSpeedMul = 0.4f;
        [SerializeField] private float parkingSlotInset = 0.32f;   // 건물 타일 내 칸 오프셋(타일 비율)
        [SerializeField, Range(0f, 1f)] private float parkingSettleSeconds = 0.3f;   // 도착 후 슬롯 정착 정지 안무(초)

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
        private readonly List<RouteVehicle> vehicles = new();
        private readonly List<FlowBurstSpeedZone> flowBurstSpeedZones = new();

        // 통근 상태(유일 경로). 위상 리빌드 시 재구성된다.
        private readonly CommuteScheduler commuteScheduler = new();
        private readonly Dictionary<int, BakedRoutePair> bakedRoutes = new();   // 키 = RouteIndex, 해시 변경 시 재베이크
        private readonly Dictionary<CommuteCar, RouteVehicle> carVehicles = new();
        private readonly Dictionary<Vector2Int, List<GameObject>> parkingSlotVisuals = new();
        private IGameCalendarService calendar;
        private Transform parkingRoot;
        private int commuteRoutesHash;
        private int commuteTuningHash;   // 로터리 노브(반경/α/전이) 해시 — 변경 시 지오메트리만 리베이크(QA G)
        private bool commuteRoutesBuilt;
        private float gameHourFraction;

        private CityFlowServices services;
        private IReadOnlyTileData tileData;
        private IPlacementService placement;
        private SimEngine simEngine;
        private ISignalControl signalControl;
        private IIntersectionFacilityService intersectionFacility;
        private ITrafficRuleService trafficRule;
        private Transform gridRoot;
        private Transform boardRoot;
        private Transform tileRoot;
        private Transform vehicleRoot;
        private Transform signalRoot;
        private Transform effectRoot;
        private int selectedSignalIndex;
        private Camera mainCamera;
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

        public GameObject FlowBurstPrefab => burstPrefab;
        public Transform EffectRoot => effectRoot;
        public float TileSize => tileSize;
        public float FlowBurstSeconds => burstSeconds;
        public Color FlowBurstColor => flowBurstColor;

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
            public Vector3 Pos;   // 지난 프레임 위치·진행 방향 — 차간 유지 판정용(1프레임 지연 근사)
            public Vector3 Dir;
            public Vector2Int CurrentTile;
            public bool HasCurrentTile;
            public GameObject AngryMark;   // Jam 팝업(!) — vehicleRoot 소속(차량 자식 금지: 비균등 스케일)
            public GameObject SmokePuff;   // Jam 매연 퍼프 — 동일 소속
            public int RouteIndex = -1;
            public bool OnRing;            // 통근 전용(Task 6R): 링 원 안(공유 링 레인 소속) — 핑퐁 무관
            public Vector2Int RingTile;    // OnRing일 때의 로터리 center 타일

            // 차량 개성(개성 패스 2/2): 바인딩 시 (Home, HomeSlot) 해시로 캐시. 판단 없음 — 연출용.
            public CarStyle Style;         // 스케일·팔레트·속도/가속 배수·출발 지연 프로파일
            public CarState LastState;     // 상태 전환 감지(출발 지연·정착 안무 트리거)
            public bool HasLastState;
            public float DepartHold;       // 출발 지연 잔여(초) — >0이면 출발 앵커 고정(적분 보류)
            public float SettleHold;       // 주차 정착 잔여(초) — >0이면 도착 앵커 정지 안무 중
            public bool Settling;          // 정착 안무 진행 플래그(도착 프레임 재진입 게이트)
            public GameObject BrakeLight;  // 후방 제동등(기본 off) — CreateDetailCube 패턴
            public bool BrakeOn;           // 제동등 상태 캐시(매 프레임 SetActive 금지)

            // 도착 코인 팝 앵커링(ArrivalEvent 시각 앵커링): 정착 진입 시(도착 순간) Time.time 스탬프.
            // 판단 없음 — OnArrival이 "최근 도착" 후보를 고를 때만 읽는다.
            public float LastArrivedTime = float.NegativeInfinity;
        }

        // 도착 코인 팝: 소형 텍스트 마크 풀(고정 크기, 러시아워 다발 대비 — 매 도착마다 Instantiate 금지).
        private sealed class CoinPop
        {
            public GameObject Object;
            public TextMesh Text;
            public Vector3 StartPos;
            public float StartTime;
        }

        private struct FlowBurstSpeedZone
        {
            public Vector2Int Tile;
            public float Until;
        }

        // 방향별 별도 베이크(외부 리뷰 치명 판정): Inbound는 브리지 타일을 뒤집고 앵커를 스왑해 다시 베이크.
        // 이동은 항상 정방향 SampleAt — 역샘플 절대 금지.
        private sealed class BakedRoutePair
        {
            public RoutePolyline Outbound;
            public RoutePolyline Inbound;
            public Vector3 OutboundMerge;   // 출발 스퍼가 도로 차선에 닿는 점(합류 양보 판정 — 라이브 QA B)
            public Vector3 InboundMerge;
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

            services.Events.Placed += OnPlaced;
            services.Events.FlowBurst += OnFlowBurstSpeedBoost;
            services.Events.Arrival += OnArrival;

            if (services.Save != null)
            {
                services.Save.RestoreCompleted += OnRestoreCompleted;
            }

            // 통근 캘린더 배선: 이미 등록됐으면 즉시, 아니면 지연 획득(CityFlowServices L56-70).
            // 구독은 fraction 리셋만 건드림 — 핑퐁 차량 동작에 무영향(스위치 off 시).
            if (services.GameCalendar != null)
            {
                BindGameCalendar(services.GameCalendar);
            }
            else
            {
                services.GameCalendarRegistered += BindGameCalendar;
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
            RefreshVehicles();
            gameObject.AddComponent<DriveViewCamera>().Init(simEngine, transform, tileSize);
            gameObject.AddComponent<FloatingWindowService>().Init(width * tileSize, height * tileSize, false);
            InitializeCameraView();
        }

        private void OnDestroy()
        {
            if (services == null)
            {
                return;
            }

            services.Events.Placed -= OnPlaced;
            services.Events.FlowBurst -= OnFlowBurstSpeedBoost;
            services.Events.Arrival -= OnArrival;

            if (services.Save != null)
            {
                services.Save.RestoreCompleted -= OnRestoreCompleted;
            }

            services.GameCalendarRegistered -= BindGameCalendar;
            if (calendar != null)
            {
                calendar.HourChanged -= OnGameHourChanged;
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
            HandleCameraViewInput();

            if (tileData == null)
            {
                return;
            }

            HandleSignalInput();
            RefreshSignals();
            RefreshRoundabouts();
            RefreshOverpasses();
            RefreshOneways();
            RefreshTurnSigns();
            RefreshPriorityRoads();
            RefreshVehicles();
            RefreshCoinPops();
        }

        private void BuildRoots()
        {
            boardRoot = CreateChildRoot("Board");
            gridRoot = CreateChildRoot("GridLines");
            tileRoot = CreateChildRoot("Tiles");
            vehicleRoot = CreateChildRoot("Vehicles");
            signalRoot = CreateChildRoot("Signals");
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
            boardRenderer.sharedMaterial = CreateGridMaterial();
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

        private GameObject CreateSmokePuff()
        {
            GameObject puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            puff.name = "SmokePuff";
            puff.transform.SetParent(vehicleRoot, false);
            puff.transform.localScale = Vector3.one * (tileSize * 0.12f);
            ApplyRendererColor(PrepareRenderer(puff.GetComponent<Renderer>()), new Color(0.45f, 0.45f, 0.45f));
            return puff;
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

                Renderer renderer = vehicle.GetComponentInChildren<Renderer>();
                PrepareRenderer(renderer);
                ApplyRendererColor(renderer, vehicleColor);

                Renderer detailRenderer = null;
                GameObject brakeLight = null;
                if (vehiclePrefab == null)
                {
                    detailRenderer = CreateDetailCube(vehicle.transform, "Cabin",
                        new Vector3(0.55f, 0.72f, 0.42f), new Vector3(-0.05f, 0f, -0.65f));
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
                Vector3 toCamera = mainCamera.transform.position - textMark.position;
                textMark.rotation = Quaternion.LookRotation(toCamera.normalized, mainCamera.transform.up);
                return;
            }

            Vector3 facing = mainCamera != null
                ? Vector3.ProjectOnPlane(mainCamera.transform.position - textMark.position, groundUp)
                : transform.up;

            if (facing.sqrMagnitude < 0.001f)
            {
                facing = transform.up;
            }

            textMark.rotation = Quaternion.LookRotation(facing.normalized, groundUp);
        }

        private int ComputeDisplayRouteHash(List<Vector2Int> route)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < route.Count; i++)
                {
                    hash = hash * 31 + route[i].GetHashCode();
                    if (i < route.Count - 1 && TryGetDiagonalTurnBridge(route, i, out Vector2Int bridge))
                    {
                        hash = hash * 31 + bridge.GetHashCode();
                    }
                }
                return hash;
            }
        }

        private bool TryGetDiagonalTurnBridge(List<Vector2Int> route, int segmentIndex, out Vector2Int bridge)
        {
            bridge = default;
            Vector2Int from = route[segmentIndex];
            Vector2Int to = route[segmentIndex + 1];
            int dx = to.x - from.x;
            int dy = to.y - from.y;
            if (Mathf.Abs(dx) != 1 || Mathf.Abs(dy) != 1)
            {
                return false;
            }

            Vector2Int horizontalBridge = new Vector2Int(to.x, from.y);
            Vector2Int verticalBridge = new Vector2Int(from.x, to.y);
            bool canTurnHorizontalFirst = IsRoadTile(horizontalBridge)
                && IsRoadTile(new Vector2Int(from.x - dx, from.y))
                && IsRoadTile(new Vector2Int(to.x, to.y + dy));
            bool canTurnVerticalFirst = IsRoadTile(verticalBridge)
                && IsRoadTile(new Vector2Int(from.x, from.y - dy))
                && IsRoadTile(new Vector2Int(to.x + dx, to.y));

            if (!canTurnHorizontalFirst && !canTurnVerticalFirst)
            {
                return false;
            }

            bool horizontalFirst = canTurnHorizontalFirst;
            if (canTurnHorizontalFirst && canTurnVerticalFirst)
            {
                if (segmentIndex > 0)
                {
                    Vector2Int incoming = from - route[segmentIndex - 1];
                    horizontalFirst = incoming.y == 0;
                }
                else if (segmentIndex + 2 < route.Count)
                {
                    Vector2Int outgoing = route[segmentIndex + 2] - to;
                    horizontalFirst = outgoing.x == 0;
                }
            }

            bridge = horizontalFirst ? horizontalBridge : verticalBridge;
            // 로터리 중심도 표시 경로에 넣어 꺾는 차가 대각선으로 섬을 가로지르지 않게 한다.
            return true;
        }

        private float GetCornerTurnRadiusFraction()
        {
            return cornerTurnRadius > 0f
                ? Mathf.Clamp(cornerTurnRadius, 0.6f, 0.85f)
                : 0.75f;
        }

        private bool IsRoadTile(Vector2Int tile)
        {
            return tileData != null
                && tile.x >= 0 && tile.x < width
                && tile.y >= 0 && tile.y < height
                && tileData.GetTileType(tile) == TileType.Road;
        }

        // 앞차와의 거리를 [gap, 2*gap]에서 [0, freeSpeed]로 매핑. 없으면 freeSpeed.
        // 차량 수가 수백 대 규모인 동안은 O(n) 스캔을 유지하고, 병목이 되면 공간 해시로 승급한다.
        private float LeaderSpeedCap(RouteVehicle vehicle, float freeSpeed)
        {
            if (vehicle.Dir.sqrMagnitude < 0.001f) return freeSpeed;
            float gap = tileSize * followGap;
            float nearest = float.MaxValue;
            for (int i = 0; i < vehicles.Count; i++)
            {
                RouteVehicle other = vehicles[i];
                if (other == vehicle || !other.Object.activeSelf) continue;
                Vector3 to = other.Pos - vehicle.Pos;
                float d = to.magnitude;
                if (d < 0.0001f || d > 2f * gap) continue;
                if (Vector3.Dot(vehicle.Dir, other.Dir) <= 0.5f) continue;
                if (Vector3.Dot(vehicle.Dir, to.normalized) <= 0.6f) continue;
                if (d < nearest) nearest = d;
            }
            if (nearest == float.MaxValue) return freeSpeed;
            return Mathf.Lerp(0f, freeSpeed, Mathf.Clamp01((nearest - gap) / gap));
        }

        private void OnFlowBurstSpeedBoost(FlowBurstEvent e)
        {
            flowBurstSpeedZones.Add(new FlowBurstSpeedZone
            {
                Tile = e.Tile,
                Until = Time.time + flowBurstSpeedDuration
            });
        }

        private bool IsInFlowBurstSpeedZone(Vector2Int tile)
        {
            for (int i = flowBurstSpeedZones.Count - 1; i >= 0; i--)
            {
                FlowBurstSpeedZone zone = flowBurstSpeedZones[i];
                if (Time.time > zone.Until)
                {
                    flowBurstSpeedZones.RemoveAt(i);
                    continue;
                }

                Vector2Int distance = tile - zone.Tile;
                if (Mathf.Abs(distance.x) <= flowBurstSpeedRadius
                    && Mathf.Abs(distance.y) <= flowBurstSpeedRadius)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsSignalTile(Vector2Int tile)
        {
            if (simEngine == null)
            {
                return false;
            }

            IReadOnlyList<Vector2Int> signals = simEngine.SignalTiles;

            for (int i = 0; i < signals.Count; i++)
            {
                if (signals[i] == tile)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsRoundaboutTile(Vector2Int tile)
        {
            if (intersectionFacility == null)
            {
                return false;
            }

            return ContainsSignal(intersectionFacility.RoundaboutTiles, tile);   // 선형 목록 검색 헬퍼 공용
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 통근 이동(유일 경로 — RefreshVehicles가 항상 진입).
        // 뷰는 번역기: 폴리라인 정방향 샘플 × 혼잡 3단 × 신호 × LeaderSpeedCap(+주차 감속)만.
        // 차를 세우는 자체 판단·Sim 역류 금지.
        // ─────────────────────────────────────────────────────────────────────────

        private void BindGameCalendar(IGameCalendarService gameCalendar)
        {
            if (gameCalendar == null || calendar == gameCalendar)
            {
                return;
            }

            if (calendar != null)
            {
                calendar.HourChanged -= OnGameHourChanged;
            }

            calendar = gameCalendar;
            calendar.HourChanged += OnGameHourChanged;
            gameHourFraction = 0f;
        }

        private void OnGameHourChanged(int _)
        {
            gameHourFraction = 0f;   // 시간 눈금 갱신 시 프레임 누적분 리셋(스펙 §시간)
        }

        // 매 프레임 fraction 누적(0..1 클램프). 이동 루프 전용 — 누적·hour 산출은 여기서만.
        private float AdvanceGameHour()
        {
            if (calendar == null)
            {
                return 0f;
            }

            float perHour = Mathf.Max(0.0001f, calendar.RealSecondsPerGameHour);
            gameHourFraction = Mathf.Clamp01(gameHourFraction + Time.deltaTime / perHour);
            return calendar.Hour + gameHourFraction;
        }

        // 읽기 전용 hour(누적하지 않음) — SnapToHour 등 리빌드 시점 스냅용.
        private float CurrentGameHour()
        {
            return calendar != null ? calendar.Hour + gameHourFraction : 0f;
        }

        private void RefreshCommuteVehicles()
        {
            if (simEngine == null)
            {
                return;
            }

            EnsureVehicleCount(maxMovingVehicles);
            SyncCommutePopulation();

            float hour = AdvanceGameHour();
            commuteScheduler.UpdateDepartures(hour);

            IReadOnlyList<CommuteCar> cars = commuteScheduler.Cars;
            for (int i = 0; i < cars.Count; i++)
            {
                CommuteCar car = cars[i];
                if (!carVehicles.TryGetValue(car, out RouteVehicle vehicle) || vehicle == null)
                {
                    continue;
                }

                if (!vehicle.Object.activeSelf)
                {
                    continue;
                }

                MoveCommuteVehicle(car, vehicle);
            }
        }

        // ActiveRoutes(브리지 포함) 또는 출/도착지가 하나라도 바뀌면 리빌드+재베이크.
        private void SyncCommutePopulation()
        {
            IReadOnlyList<List<Vector2Int>> routes = simEngine.ActiveRoutes;
            int hash = ComputeCommuteRoutesHash(routes, simEngine.ActiveRouteSources, simEngine.ActiveRouteSinks);
            int tuningHash = ComputeRoundaboutTuningHash();
            if (commuteRoutesBuilt && hash == commuteRoutesHash)
            {
                // 경로는 그대로 — 로터리 노브만 바뀌었으면 스케줄러/차량 진행 보존한 채 폴리라인만 재베이크(QA G).
                if (tuningHash != commuteTuningHash)
                {
                    commuteTuningHash = tuningHash;
                    RebakeCommuteGeometry(routes);
                }

                return;
            }

            commuteRoutesHash = hash;
            commuteTuningHash = tuningHash;
            commuteRoutesBuilt = true;
            RebuildCommute(routes);
        }

        // 로터리 라이브 노브 3종의 해시 — 재생 중 슬라이더 조정 감지용(QA G).
        private int ComputeRoundaboutTuningHash()
        {
            int hash = 17;
            hash = hash * 31 + roundaboutOrbitRadius.GetHashCode();
            hash = hash * 31 + roundaboutEntryExitDeg.GetHashCode();
            hash = hash * 31 + roundaboutTransitionTiles.GetHashCode();
            return hash;
        }

        private int ComputeCommuteRoutesHash(
            IReadOnlyList<List<Vector2Int>> routes,
            IReadOnlyList<Vector2Int> sources,
            IReadOnlyList<Vector2Int> sinks)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + routes.Count;
                for (int i = 0; i < routes.Count; i++)
                {
                    hash = hash * 31 + (routes[i].Count > 1 ? ComputeDisplayRouteHash(routes[i]) : routes[i].Count);
                    if (i < sources.Count)
                    {
                        hash = hash * 31 + sources[i].GetHashCode();
                    }

                    if (i < sinks.Count)
                    {
                        hash = hash * 31 + sinks[i].GetHashCode();
                    }
                }

                return hash;
            }
        }

        private void RebuildCommute(IReadOnlyList<List<Vector2Int>> routes)
        {
            // sticky(QA A): 리빌드 전 각 차의 구 폴리라인을 붙잡아 "경로 타일이 실제로 바뀐 차"만 골라낸다.
            var previousBakes = new Dictionary<CommuteCar, BakedRoutePair>(carVehicles.Count);
            foreach (KeyValuePair<CommuteCar, RouteVehicle> kv in carVehicles)
            {
                if (bakedRoutes.TryGetValue(kv.Key.RouteIndex, out BakedRoutePair old))
                {
                    previousBakes[kv.Key] = old;
                }
            }

            commuteScheduler.Rebuild(
                simEngine.ActiveRouteSources, simEngine.ActiveRouteSinks,
                officeSlots, homeSlots, maxMovingVehicles,
                morningWindow.x, morningWindow.y, eveningWindow.x, eveningWindow.y);

            BakeAllRoutes(routes);

            commuteScheduler.SnapNewToHour(CurrentGameHour());   // 신규 차만 현재 시각 수렴 — 생존 차 상태 보존(QA A)
            SyncCommuteVehicleBindings(previousBakes);
            RebuildParkingVisuals();
        }

        // 로터리 노브만 바뀐 경우: 스케줄러·차량 진행을 건드리지 않고 폴리라인만 재베이크(QA G).
        // 경로 타일은 동일하므로 sticky previousBakes로 각 차가 새 지오메트리에 이어붙는다(위상 보존).
        private void RebakeCommuteGeometry(IReadOnlyList<List<Vector2Int>> routes)
        {
            var previousBakes = new Dictionary<CommuteCar, BakedRoutePair>(carVehicles.Count);
            foreach (KeyValuePair<CommuteCar, RouteVehicle> kv in carVehicles)
            {
                if (bakedRoutes.TryGetValue(kv.Key.RouteIndex, out BakedRoutePair old))
                {
                    previousBakes[kv.Key] = old;
                }
            }

            BakeAllRoutes(routes);
            SyncCommuteVehicleBindings(previousBakes);
        }

        // bakedRoutes를 현재 스케줄러 차량·라이브 로터리 노브로 다시 채운다(RebuildCommute·RebakeCommuteGeometry 공용).
        private void BakeAllRoutes(IReadOnlyList<List<Vector2Int>> routes)
        {
            bakedRoutes.Clear();
            IReadOnlyList<CommuteCar> cars = commuteScheduler.Cars;
            for (int i = 0; i < cars.Count; i++)
            {
                CommuteCar car = cars[i];
                if (car.RouteIndex < 0 || car.RouteIndex >= routes.Count)
                {
                    continue;
                }

                List<Vector2Int> source = routes[car.RouteIndex];
                if (source.Count <= 1)
                {
                    continue;
                }

                // 각 폴리라인은 자기 타일 리스트를 참조 보관(RoutePolyline._tiles) — 공유·재사용 금지.
                List<Vector2Int> outboundTiles = new List<Vector2Int>(source.Count);
                BuildBridgedRoute(source, outboundTiles);
                if (outboundTiles.Count <= 1)
                {
                    continue;
                }

                Vector3 homeAnchor = GetParkingAnchor(car.Home, outboundTiles[0], car.HomeSlot, homeSlots);
                Vector3 workAnchor = GetParkingAnchor(car.Work, outboundTiles[outboundTiles.Count - 1], car.WorkSlot, officeSlots);

                List<Vector2Int> inboundTiles = new List<Vector2Int>(outboundTiles);
                inboundTiles.Reverse();   // 방향별 별도 베이크: 역순 타일 + 앵커 스왑(역샘플 금지)

                bakedRoutes[car.RouteIndex] = new BakedRoutePair
                {
                    Outbound = BakeCommuteRoute(outboundTiles, homeAnchor, workAnchor),
                    Inbound = BakeCommuteRoute(inboundTiles, workAnchor, homeAnchor),
                    OutboundMerge = ComputeMergePoint(outboundTiles),
                    InboundMerge = ComputeMergePoint(inboundTiles),
                };
            }
        }

        // 출발 스퍼가 도로 차선에 닿는 점 ≈ 첫 도로 세그먼트 시작의 차선 오프셋 위치(베이크 phase 0 포즈).
        private Vector3 ComputeMergePoint(List<Vector2Int> tiles)
        {
            Vector3 a = GridToLocal(tiles[0], vehicleZ);
            Vector3 b = GridToLocal(tiles[1], vehicleZ);
            Vector3 dir = (b - a).normalized;
            Vector3 laneRight = new Vector3(dir.y, -dir.x, 0f);
            return a + laneRight * (tileSize * laneOffset);
        }

        // 대각 브리지 삽입 로직을 차량 상태 없이 재현(베이크 입력용).
        private void BuildBridgedRoute(List<Vector2Int> source, List<Vector2Int> dest)
        {
            dest.Clear();
            if (source.Count == 0)
            {
                return;
            }

            dest.Add(source[0]);
            for (int i = 0; i < source.Count - 1; i++)
            {
                if (TryGetDiagonalTurnBridge(source, i, out Vector2Int bridge)
                    && dest[dest.Count - 1] != bridge)
                {
                    dest.Add(bridge);
                }

                Vector2Int next = source[i + 1];
                if (dest[dest.Count - 1] != next)
                {
                    dest.Add(next);
                }
            }
        }

        private RoutePolyline BakeCommuteRoute(IReadOnlyList<Vector2Int> tiles, Vector3 startAnchor, Vector3 endAnchor)
        {
            return RoutePolyline.Bake(new BakeInput
            {
                Tiles = tiles,
                TileSize = tileSize,
                LaneOffset = laneOffset,
                CornerRadiusFraction = GetCornerTurnRadiusFraction(),   // 베이커는 클램프 안 함 — 여기서 해석해 전달(리뷰 #2)
                OrbitRadius = roundaboutOrbitRadius,
                EntryExitOffsetRad = roundaboutEntryExitDeg * Mathf.Deg2Rad,   // α (QA G)
                TransitionLength = roundaboutTransitionTiles,
                Z = vehicleZ,
                IsRoundabout = IsRoundaboutTile,   // 항상 non-null(리뷰 #6)
                StartAnchor = startAnchor,
                EndAnchor = endAnchor,
                SamplesPerSegment = 8,
            });
        }

        // 건물 프리팹에 "ParkingSlot_N" 자식이 있으면 그 위치(뷰 로컬), 없으면 절차 폴백.
        // slotCount = 건물 타입별 칸 수(회사=officeSlots, 집=homeSlots) — 집 1칸은 중앙 정렬.
        private Vector3 GetParkingAnchor(Vector2Int building, Vector2Int frontageRoad, int slotIndex, int slotCount)
        {
            if (tileVisuals.TryGetValue(building, out TileVisual visual))
            {
                Transform named = visual.Object.transform.Find($"ParkingSlot_{slotIndex}");
                if (named != null)
                {
                    // 폴리라인/차량 로컬 좌표계(transform 기준)와 정합 — world→view-local 변환.
                    return transform.InverseTransformPoint(named.position);
                }
            }

            Vector3 center = GridToLocal(building, vehicleZ);
            Vector3 toRoad = (GridToLocal(frontageRoad, vehicleZ) - center).normalized;
            Vector3 side = new Vector3(toRoad.y, -toRoad.x, 0f);
            float spread = slotCount <= 1
                ? 0f
                : (slotIndex - (slotCount - 1) * 0.5f) / (slotCount - 1);
            return center + toRoad * (tileSize * parkingSlotInset)
                          + side * (tileSize * 0.6f * spread);
        }

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
            IReadOnlyList<CommuteCar> cars = commuteScheduler.Cars;
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

        // sticky 바인딩(QA A): 생존 차는 vehicle·위치 무접촉 유지, 사라진 짝만 풀 반납, 신규만 할당.
        // 경로 타일이 실제로 바뀐 이동 중 차만 페이드(렌더러 off) + 주차 상태로 개별 수렴(순간이동 금지).
        // 총 인구 상한 = 풀 크기 = maxMovingVehicles(리뷰 #7).
        private void SyncCommuteVehicleBindings(Dictionary<CommuteCar, BakedRoutePair> previousBakes)
        {
            IReadOnlyList<CommuteCar> cars = commuteScheduler.Cars;
            var alive = new HashSet<CommuteCar>();
            for (int i = 0; i < cars.Count; i++)
            {
                alive.Add(cars[i]);
            }

            List<CommuteCar> stale = null;
            foreach (KeyValuePair<CommuteCar, RouteVehicle> kv in carVehicles)
            {
                if (!alive.Contains(kv.Key))
                {
                    (stale ??= new List<CommuteCar>()).Add(kv.Key);
                }
            }

            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                {
                    DeactivateCommuteVehicle(carVehicles[stale[i]]);
                    carVehicles.Remove(stale[i]);
                }
            }

            for (int i = 0; i < cars.Count; i++)
            {
                CommuteCar car = cars[i];
                bool hasBake = bakedRoutes.TryGetValue(car.RouteIndex, out BakedRoutePair pair);
                if (carVehicles.TryGetValue(car, out RouteVehicle vehicle))
                {
                    vehicle.RouteIndex = car.RouteIndex;   // bakedRoutes 짝 키 동기화(색은 개성 팔레트로 분리됨)
                    if (!hasBake)
                    {
                        // 새 위상에서 경로 소실(미연결 등): 페이드 후 다음 리빌드에서 재배치.
                        SetVehicleRenderersEnabled(vehicle, false);
                        continue;
                    }

                    bool moving = car.State == CarState.Outbound || car.State == CarState.Inbound;
                    if (moving
                        && previousBakes.TryGetValue(car, out BakedRoutePair old)
                        && !SamePolylineTiles(old.Outbound, pair.Outbound))
                    {
                        // 이동 중 경로 변경: Distance가 새 폴리라인과 무의미 — 페이드 + 주차로 개별 수렴.
                        SetVehicleRenderersEnabled(vehicle, false);
                        CommuteScheduler.SnapCar(car, CurrentGameHour());
                    }

                    // 주차 차/경로 불변 이동 차: 무접촉 — 위치·상태 그대로(주차 앵커는 다음 프레임 재계산).
                }
                else
                {
                    if (!hasBake)
                    {
                        continue;   // 경로 너무 짧아 베이크 실패 → 이 car는 그리지 않음
                    }

                    RouteVehicle fresh = TakeFreeVehicle();
                    if (fresh == null)
                    {
                        continue;   // 풀 고갈 — 정원 초과분 미표시
                    }

                    ResetVehicleForCommute(fresh, car.RouteIndex);
                    // 리바인드 불변식: 언바운드 차는 Distance≈0(신규/스냅 직후)에서만 발생 — 바인딩 로직 변경 시 재검토.
                    ApplyCarStyle(fresh, car);
                    carVehicles[car] = fresh;
                }
            }

            // 어떤 car에도 안 묶인 활성 차 정리(통근 모드 런타임 전환 등 안전망).
            var bound = new HashSet<RouteVehicle>(carVehicles.Values);
            for (int i = 0; i < vehicles.Count; i++)
            {
                if (vehicles[i].Object.activeSelf && !bound.Contains(vehicles[i]))
                {
                    DeactivateCommuteVehicle(vehicles[i]);
                }
            }
        }

        private RouteVehicle TakeFreeVehicle()
        {
            for (int i = 0; i < vehicles.Count; i++)
            {
                if (!vehicles[i].Object.activeSelf)
                {
                    return vehicles[i];
                }
            }

            return null;
        }

        private static bool SamePolylineTiles(RoutePolyline a, RoutePolyline b)
        {
            if (a.TileCount != b.TileCount)
            {
                return false;
            }

            for (int i = 0; i < a.TileCount; i++)
            {
                if (a.TileAt(i) != b.TileAt(i))
                {
                    return false;
                }
            }

            return true;
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

        private void ResetVehicleForCommute(RouteVehicle vehicle, int routeIndex)
        {
            vehicle.RouteIndex = routeIndex;
            vehicle.CurrentSpeed = 0f;
            vehicle.Dir = Vector3.zero;
            vehicle.OnRing = false;
            vehicle.HasCurrentTile = false;
            vehicle.DepartHold = 0f;
            vehicle.SettleHold = 0f;
            vehicle.Settling = false;
            vehicle.HasLastState = false;
            vehicle.BrakeOn = false;
            if (vehicle.BrakeLight != null)
            {
                vehicle.BrakeLight.SetActive(false);   // 하드 리셋(캐시 가드 우회) — 풀 재사용 desync 방지
            }
            HideJamMarks(vehicle);
            if (vehicle.Renderer != null)
            {
                vehicle.Renderer.enabled = true;
            }

            if (vehicle.DetailRenderer != null)
            {
                vehicle.DetailRenderer.enabled = true;
            }

            vehicle.Object.SetActive(true);
        }

        private void DeactivateCommuteVehicle(RouteVehicle vehicle)
        {
            vehicle.Object.SetActive(false);
            vehicle.RouteIndex = -1;
            vehicle.HasCurrentTile = false;
            vehicle.CurrentSpeed = 0f;
            vehicle.Dir = Vector3.zero;
            vehicle.OnRing = false;
            vehicle.DepartHold = 0f;
            vehicle.SettleHold = 0f;
            vehicle.Settling = false;
            vehicle.HasLastState = false;
            vehicle.BrakeOn = false;
            if (vehicle.BrakeLight != null)
            {
                vehicle.BrakeLight.SetActive(false);
            }
            HideJamMarks(vehicle);
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

        private void MoveCommuteVehicle(CommuteCar car, RouteVehicle vehicle)
        {
            if (!bakedRoutes.TryGetValue(car.RouteIndex, out BakedRoutePair pair))
            {
                // 위상 변경으로 경로 소실: 페이드아웃 후 다음 리빌드에서 재배치(순간이동 금지).
                SetVehicleRenderersEnabled(vehicle, false);
                return;
            }

            bool inbound = car.State == CarState.Inbound;
            bool moving = car.State == CarState.Outbound || car.State == CarState.Inbound;

            // 상태 전환 감지(Step 3/5): 이동 상태 진입 시 출발 지연 시작, 정착 안무 리셋. 시간 기반 안무만 — 정지 판단 아님.
            if (!vehicle.HasLastState || vehicle.LastState != car.State)
            {
                if (moving)
                {
                    vehicle.DepartHold = vehicle.Style.DepartDelaySec;   // 전환 직후 출발 지연 적립
                }

                vehicle.SettleHold = 0f;
                vehicle.Settling = false;
                vehicle.LastState = car.State;
                vehicle.HasLastState = true;
            }

            if (!moving)
            {
                // 주차: 앵커 고정(비용 0). Outbound 양끝이 home(0)·work(Length) 앵커.
                float anchorDistance = car.State == CarState.ParkedWork ? pair.Outbound.Length : 0f;
                Vector3 anchorPos = pair.Outbound.SampleAt(anchorDistance).Pos;
                vehicle.Object.transform.localPosition = anchorPos;
                vehicle.Pos = anchorPos;
                vehicle.Dir = Vector3.zero;   // LeaderSpeedCap(Dot≤0.5)/CommuteCrossingYieldCap(sqr<0.001) 자동 제외(리뷰 #3)
                vehicle.OnRing = false;
                vehicle.CurrentSpeed = 0f;
                vehicle.CurrentTile = car.State == CarState.ParkedWork ? car.Work : car.Home;
                vehicle.HasCurrentTile = true;
                SetVehicleRenderersEnabled(vehicle, true);
                SetBrakeLight(vehicle, false);
                HideJamMarks(vehicle);
                return;
            }

            // 주차 정착 안무(Step 5 early-out): 정착 창 동안 파이프라인 전체 차단(혼잡/신호/캡 스캔·적분 없음)
            // — 주차 차와 동일한 "앵커 고정·비용 0" 패턴. 만료 시 슬롯 방향 정렬 후 NotifyArrived.
            if (vehicle.Settling)
            {
                RoutePolyline settlePoly = inbound ? pair.Inbound : pair.Outbound;
                Sample settleEnd = settlePoly.SampleAt(settlePoly.Length);
                vehicle.Object.transform.localPosition = settleEnd.Pos;
                vehicle.Pos = settleEnd.Pos;
                vehicle.Dir = Vector3.zero;   // 정지 → 타 차 Leader/Yield 판정에서 제외(주차와 동일)
                vehicle.OnRing = false;
                vehicle.CurrentSpeed = 0f;
                vehicle.CurrentTile = inbound ? car.Home : car.Work;
                vehicle.HasCurrentTile = true;
                SetVehicleRenderersEnabled(vehicle, true);
                SetBrakeLight(vehicle, false);
                HideJamMarks(vehicle);

                vehicle.SettleHold -= Time.deltaTime;
                if (vehicle.SettleHold <= 0f)
                {
                    if (settleEnd.Dir.sqrMagnitude > 0.001f)
                    {
                        float slotAngle = Mathf.Atan2(settleEnd.Dir.y, settleEnd.Dir.x) * Mathf.Rad2Deg;
                        vehicle.Object.transform.localRotation = Quaternion.Euler(0f, 0f, slotAngle);
                    }

                    vehicle.Settling = false;
                    commuteScheduler.NotifyArrived(car);   // Distance=0 리셋 + 상태 전이(Parked)
                }

                return;
            }

            // 출발 지연 안무(Step 3): 전환 직후 DepartDelaySec 동안 출발 앵커 고정 — 적분 보류(교통 판단 아님).
            if (vehicle.DepartHold > 0f)
            {
                vehicle.DepartHold -= Time.deltaTime;
                RoutePolyline startPoly = inbound ? pair.Inbound : pair.Outbound;
                Sample start = startPoly.SampleAt(car.Distance);
                vehicle.Object.transform.localPosition = start.Pos;
                if (start.Dir.sqrMagnitude > 0.001f)
                {
                    // 풀 재사용 차의 이전 회전 노출 방지(리뷰 픽스 2): 지연 창 동안 출발 방향으로 즉시 정렬.
                    float startAngle = Mathf.Atan2(start.Dir.y, start.Dir.x) * Mathf.Rad2Deg;
                    vehicle.Object.transform.localRotation = Quaternion.Euler(0f, 0f, startAngle);
                }

                vehicle.Pos = start.Pos;
                vehicle.Dir = Vector3.zero;
                vehicle.OnRing = false;
                vehicle.CurrentSpeed = 0f;
                vehicle.CurrentTile = inbound ? car.Work : car.Home;
                vehicle.HasCurrentTile = true;
                SetVehicleRenderersEnabled(vehicle, true);
                SetBrakeLight(vehicle, false);
                HideJamMarks(vehicle);
                return;
            }

            RoutePolyline poly = inbound ? pair.Inbound : pair.Outbound;
            Sample s = poly.SampleAt(car.Distance);
            Vector2Int currentTile = poly.TileAt(Mathf.Clamp(s.TileIndex, 0, poly.TileCount - 1));

            float targetSpeed = vehicleSpeed;
            bool boostedByFlowBurst = false;
            if (s.IsSpur)
            {
                targetSpeed *= parkingApproachSpeedMul;   // 주차 진입/이탈 감속(신호·혼잡 판정 제외 구간)
                if (s.SegT < 0.5f)   // 출발 스퍼(SegT=0)만 — 도착 스퍼(SegT=1)는 합류 아님
                {
                    // 합류 양보(QA B): 합류 지점 근방 이동 차에 바닥 있는 감속 — 하드스톱·점유 금지.
                    Vector3 merge = inbound ? pair.InboundMerge : pair.OutboundMerge;
                    targetSpeed = Mathf.Min(targetSpeed, MergeYieldCap(vehicle, merge, targetSpeed));
                }
            }
            else
            {
                CongestionLevel congestion = tileData.GetCongestion(currentTile);
                if (congestion == CongestionLevel.Slow)
                {
                    targetSpeed *= slowSpeedMul;
                }
                else if (congestion == CongestionLevel.Jam)
                {
                    targetSpeed *= jamSpeedMul;
                }

                boostedByFlowBurst = IsInFlowBurstSpeedZone(currentTile);
                if (boostedByFlowBurst)
                {
                    targetSpeed *= flowBurstSpeedMultiplier;
                }

                if (signalControl != null
                    && TryGetNextSignalTile(poly, s, out _, out Vector2Int aheadSignal, out _)
                    && signalControl.GetOverrideSecondsLeft(aheadSignal) > 0f)
                {
                    targetSpeed *= overrideSpeedMul;
                }
            }

            vehicle.Dir = s.Dir;   // Leader/Yield 캡이 읽음(이동차만 방향 보유)
            UpdateRingState(vehicle, poly, s);   // 다른 차의 OnRing은 지난 프레임 값 — Pos/Dir 관례와 동일한 1프레임 근사

            // 공유 링 레인(Task 6R): 우선권 = 링 절대·단방향. 링 차는 CCW 앞차 간격만 보고
            // (진입 차 때문에 절대 감속하지 않음 → 상호 대기 불가 = 데드락 원천 차단),
            // 진입 차는 합류점 상류 창 게이트로 대기(0 도달 허용 = 합류선 정지, 차선 의미론).
            if (vehicle.OnRing)
            {
                targetSpeed = Mathf.Min(targetSpeed, RingLaneLeaderCap(vehicle, targetSpeed));   // 링 차는 RingLane 경로만
            }
            else
            {
                targetSpeed = Mathf.Min(targetSpeed, LeaderSpeedCap(vehicle, targetSpeed));
                if (!s.IsSpur)
                {
                    // QA C: 교차 양보(도로 구간 계약). 로터리 타일은 스킵 — 링 레인과 이중 제동 방지.
                    if (!IsRoundaboutTile(currentTile))
                    {
                        targetSpeed = Mathf.Min(targetSpeed, CommuteCrossingYieldCap(vehicle, targetSpeed));
                    }

                    targetSpeed = Mathf.Min(targetSpeed, RingMergeGateCap(vehicle, poly, s, targetSpeed));
                }
            }

            // 개성 속도 배수(Step 2): 번역·캡 곱이 전부 끝난 뒤 적용(±10% 연출 편차 — 라이브 튜닝 전제).
            targetSpeed *= vehicle.Style.SpeedMul;

            bool mustStop = !s.IsSpur && IsRouteVehicleBlocked(poly, s);
            if (mustStop)
            {
                vehicle.CurrentSpeed = 0f;   // 신호 정지는 연출보다 우선 — 논리 이동 즉시 정지
            }
            else
            {
                float acceleration = (targetSpeed < vehicle.CurrentSpeed
                    ? vehicleDeceleration
                    : vehicleAcceleration * (boostedByFlowBurst ? flowBurstAccelerationMultiplier : 1f))
                    * vehicle.Style.AccelMul;   // 개성 가감속 배수(Step 2)
                vehicle.CurrentSpeed = Mathf.MoveTowards(vehicle.CurrentSpeed, targetSpeed, acceleration * Time.deltaTime);
            }

            // 아크렝스 적분: phase-속도(타일/초)를 월드거리로 환산(1세그먼트 ≈ tileSize).
            // 폴리라인이 이미 아크렝스 정규화라 코너 속도 보정 불필요.
            car.Distance += vehicle.CurrentSpeed * tileSize * Time.deltaTime;
            if (car.Distance >= poly.Length)
            {
                // 주차 정착 안무(Step 5): 즉시 스냅 대신 앵커 정지 + Settling 게이트 무장 —
                // 이후 프레임은 상단 early-out이 파이프라인 없이 처리(카운트다운·정렬·NotifyArrived).
                Vector3 endPos = poly.SampleAt(poly.Length).Pos;
                vehicle.Object.transform.localPosition = endPos;
                vehicle.Pos = endPos;
                vehicle.Dir = Vector3.zero;   // 정지 → 타 차 Leader/Yield 판정에서 제외(주차와 동일)
                vehicle.OnRing = false;
                vehicle.CurrentSpeed = 0f;
                vehicle.CurrentTile = currentTile;
                vehicle.HasCurrentTile = true;
                HideJamMarks(vehicle);
                SetBrakeLight(vehicle, false);
                vehicle.Settling = true;
                vehicle.SettleHold = parkingSettleSeconds;
                vehicle.LastArrivedTime = Time.time;   // 도착 코인 팝 앵커링용 스탬프(연출 전용, 판단 없음)
                return;
            }

            Vector3 pos = s.Pos;
            vehicle.Object.transform.localPosition = pos;
            vehicle.CurrentTile = currentTile;
            vehicle.HasCurrentTile = true;
            if (s.Dir.sqrMagnitude > 0.001f)
            {
                float angle = Mathf.Atan2(s.Dir.y, s.Dir.x) * Mathf.Rad2Deg;
                vehicle.Object.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            }

            // 역주행 유령 숨김(스펙 §3 해법①, !forward→Inbound 대입): Inbound가 일방 타일을
            // 방향 반대로 지나면 렌더러 off. 매 프레임 재평가 → 조건 해제 시 자연 복원.
            bool hiddenAsGhost = false;
            if (inbound && !s.IsSpur && trafficRule != null)
            {
                Vector2Int onewayDir = trafficRule.GetOnewayDir(currentTile);
                if (onewayDir != Vector2Int.zero)
                {
                    Vector3 onewayWorldDir = new Vector3(onewayDir.x, onewayDir.y, 0f);
                    hiddenAsGhost = Vector3.Dot(s.Dir, onewayWorldDir) < -0.9f;
                }
            }

            // 색은 바인딩 시(ApplyCarStyle) 팔레트로 1회 적용 — 여기선 유령 가시성만 토글(프레임당 색 재적용·MPB 할당 제거).
            if (vehicle.Renderer != null)
            {
                vehicle.Renderer.enabled = !hiddenAsGhost;
                if (vehicle.DetailRenderer != null)
                {
                    vehicle.DetailRenderer.enabled = !hiddenAsGhost;
                }
            }

            // 제동등(Step 4): 감속 의도(targetSpeed < CurrentSpeed - 0.05) 진입/이탈 시에만 토글. 유령 숨김 시 off.
            SetBrakeLight(vehicle, !hiddenAsGhost && targetSpeed < vehicle.CurrentSpeed - 0.05f);

            // Jam 분노 팝업(!)+매연 — 스퍼/유령 제외(기존 L1484-1505 패턴 재사용).
            bool jammed = !hiddenAsGhost && !s.IsSpur
                && tileData.GetCongestion(currentTile) == CongestionLevel.Jam;
            if (jammed && vehicle.AngryMark == null)
            {
                vehicle.AngryMark = CreateTextMark(vehicleRoot, "!", Color.red, tileSize * 0.14f);
                vehicle.SmokePuff = CreateSmokePuff();
            }

            if (vehicle.AngryMark != null)
            {
                vehicle.AngryMark.SetActive(jammed);
                vehicle.SmokePuff.SetActive(jammed);
                if (jammed)
                {
                    Vector3 basePos = vehicle.Object.transform.localPosition;
                    float pulse = 1f + 0.2f * Mathf.Abs(Mathf.Sin(Time.time * 6f));
                    vehicle.AngryMark.transform.localPosition = basePos + Vector3.back * (tileSize * 0.45f);
                    AlignTextMarkPerpendicularToGround(vehicle.AngryMark.transform);
                    vehicle.AngryMark.transform.localScale = Vector3.one * pulse;
                    vehicle.SmokePuff.transform.localPosition = basePos - s.Dir * (tileSize * 0.28f)
                        + Vector3.back * (tileSize * (0.12f + 0.06f * Mathf.Sin(Time.time * 2f)));
                }
            }

            vehicle.Pos = vehicle.Object.transform.localPosition;
            vehicle.Dir = s.Dir;
        }

        // 합류 양보 캡(라이브 QA B): 스퍼→도로 진입 시 합류 지점 근방의 이동 차가 있으면
        // 무신호 양보 패턴으로 바닥(crossYieldFloor)까지만 감속 — 하드스톱 없음(데드락 금지).
        // 방향 무관(합류는 어느 방향 차와도 겹칠 수 있음 — LeaderSpeedCap의 Dot>0.5 필터가 못 잡는 케이스),
        // 주차 차(Dir=zero)는 제외. 신규 튜닝 필드 없음 — followGap·crossYieldFloor 재사용(ponytail).
        private float MergeYieldCap(RouteVehicle vehicle, Vector3 mergePoint, float freeSpeed)
        {
            float range = tileSize * followGap;
            float nearest = float.MaxValue;
            for (int i = 0; i < vehicles.Count; i++)
            {
                RouteVehicle other = vehicles[i];
                if (other == vehicle || !other.Object.activeSelf || other.Dir.sqrMagnitude < 0.001f)
                {
                    continue;
                }

                float d = (other.Pos - mergePoint).magnitude;
                if (d < nearest)
                {
                    nearest = d;
                }
            }

            if (nearest >= range)
            {
                return freeSpeed;
            }

            float floor = Mathf.Clamp(crossYieldFloor, 0.1f, 1f);
            return Mathf.Lerp(freeSpeed * floor, freeSpeed, Mathf.Clamp01(nearest / range));
        }

        // ── 공유 링 레인(Task 6R) — QA D(EntryYieldCap)·E-2(RingLeaderCap) 대체 ─────────
        // 기하는 RingLane(순수 static)이 담당, 여기는 좌표 환산(atan2→호 길이)과 속도 매핑만.
        // 우선권 = 링 절대·단방향(진입만 대기) — 상호 대기 불가 = 데드락 원천 차단.
        // 합류 대기가 0까지 가는 건 의도된 차선 의미론(리더 갭)이다.

        private readonly List<float> ringArcScratch = new();   // 프레임 내 재사용 버퍼

        // 링 소속 판정: 링 원 안(중심거리 ≤ R+ε)이면 공유 레인 소속. 전이 베지어·원호 전 구간이
        // 원 안이라 이 판정 하나로 커버되고, 접근 차는 링/차선 교점 통과 순간부터 자동 편입.
        // 다른 차가 읽는 값은 지난 프레임 — Pos/Dir와 같은 1프레임 근사 관례.
        private void UpdateRingState(RouteVehicle vehicle, RoutePolyline poly, in Sample s)
        {
            vehicle.OnRing = false;
            if (s.IsSpur)
            {
                return;   // 주차 스퍼는 링일 수 없음(건물 근방 오검출 방지)
            }

            Vector2Int current = poly.TileAt(Mathf.Clamp(s.TileIndex, 0, poly.TileCount - 1));
            Vector2Int next = poly.TileAt(Mathf.Clamp(s.TileIndex + 1, 0, poly.TileCount - 1));
            Vector2Int candidate;
            if (IsRoundaboutTile(current))
            {
                candidate = current;
            }
            else if (IsRoundaboutTile(next))
            {
                candidate = next;
            }
            else
            {
                return;
            }

            Vector3 rel = s.Pos - GridToLocal(candidate, vehicleZ);
            rel.z = 0f;
            if (rel.magnitude <= RingRadius() + tileSize * 0.05f)
            {
                vehicle.OnRing = true;
                vehicle.RingTile = candidate;
            }
        }

        // 링 위 차: CCW 앞차와의 호 간격을 기존 [gap, 2gap]→[0, v] 매핑(LeaderSpeedCap 컨벤션,
        // 바닥 없음 — 차선 의미론). 링 차가 로드 정체로 출구에서 막히면 링도 서는 건 정상 동작.
        private float RingLaneLeaderCap(RouteVehicle vehicle, float freeSpeed)
        {
            float radius = RingRadius();
            Vector3 center = GridToLocal(vehicle.RingTile, vehicleZ);
            float circumference = 2f * Mathf.PI * radius;
            CollectRingArcs(vehicle, vehicle.RingTile, center, radius);
            float gap = RingLane.LeaderGap(ArcOf(vehicle.Pos, center, radius), ringArcScratch, circumference);

            float follow = tileSize * followGap;
            if (gap >= 2f * follow)
            {
                return freeSpeed;
            }

            return Mathf.Lerp(0f, freeSpeed, Mathf.Clamp01((gap - follow) / follow));
        }

        // 진입 차(링 전이 직전 ~0.3타일): 합류점 상류 2×followGap 창 안의 링 차가 리더 —
        // 같은 매핑으로 감속(0 도달 허용 = 합류선 대기), 창이 비면 진입. 합류점 arc는 자기 현재
        // 각도로 근사(접근 차선은 링/차선 교점으로 수렴 — 창 0.8타일 대비 오차 ~0.04타일, 무상태).
        private float RingMergeGateCap(RouteVehicle vehicle, RoutePolyline poly, in Sample s, float freeSpeed)
        {
            Vector2Int current = poly.TileAt(Mathf.Clamp(s.TileIndex, 0, poly.TileCount - 1));
            Vector2Int next = poly.TileAt(Mathf.Clamp(s.TileIndex + 1, 0, poly.TileCount - 1));
            Vector2Int ringTile;
            if (IsRoundaboutTile(current))
            {
                ringTile = current;
            }
            else if (IsRoundaboutTile(next))
            {
                ringTile = next;
            }
            else
            {
                return freeSpeed;
            }

            float radius = RingRadius();
            Vector3 center = GridToLocal(ringTile, vehicleZ);
            Vector3 rel = vehicle.Pos - center;
            rel.z = 0f;
            if (rel.magnitude > radius + tileSize * 0.3f)
            {
                return freeSpeed;   // 아직 멀다(전이 직전 아님). 원 안이면 OnRing이라 이 경로로 안 옴.
            }

            float circumference = 2f * Mathf.PI * radius;
            float follow = tileSize * followGap;
            CollectRingArcs(vehicle, ringTile, center, radius);
            float gap = RingLane.MergeGap(ArcOf(vehicle.Pos, center, radius), ringArcScratch, 2f * follow, circumference);
            if (gap >= 2f * follow)
            {
                return freeSpeed;   // 창 비어 있음(MaxValue 포함) — 진입
            }

            return Mathf.Lerp(0f, freeSpeed, Mathf.Clamp01((gap - follow) / follow));
        }

        // 무신호 교차로 양보(통근판, Task 6R): 링 소속(OnRing) 또는 로터리 타일 위 차는 교차
        // 상대에서 제외 — 링 레인이 전담하는 차에 이중 제동 금지.
        private float CommuteCrossingYieldCap(RouteVehicle vehicle, float freeSpeed)
        {
            if (vehicle.Dir.sqrMagnitude < 0.001f)
            {
                return freeSpeed;
            }

            Vector3 myDirection = vehicle.Dir.normalized;
            Vector3 rightDirection = new Vector3(myDirection.y, -myDirection.x, 0f);
            float range = tileSize * Mathf.Max(0.3f, crossYieldRange);
            float nearest = float.MaxValue;

            for (int i = 0; i < vehicles.Count; i++)
            {
                RouteVehicle other = vehicles[i];
                if (other == vehicle || !other.Object.activeSelf || other.Dir.sqrMagnitude < 0.001f)
                {
                    continue;
                }

                if (other.OnRing || (other.HasCurrentTile && IsRoundaboutTile(other.CurrentTile)))
                {
                    continue;   // 로터리 관할 차 제외(이중 제동 방지)
                }

                Vector3 to = other.Pos - vehicle.Pos;
                float distance = to.magnitude;
                if (distance < 0.0001f || distance >= range)
                {
                    continue;
                }

                Vector3 otherDirection = other.Dir.normalized;
                if (Mathf.Abs(Vector3.Dot(myDirection, otherDirection)) >= 0.5f)
                {
                    continue;
                }

                if (Vector3.Dot(rightDirection, to) <= 0f)
                {
                    continue;
                }

                if (Vector3.Dot(myDirection, to) <= 0f)
                {
                    continue;
                }

                if (distance < nearest)
                {
                    nearest = distance;
                }
            }

            if (nearest == float.MaxValue)
            {
                return freeSpeed;
            }

            float floor = Mathf.Clamp(crossYieldFloor, 0.1f, 1f);
            return Mathf.Lerp(freeSpeed * floor, freeSpeed, Mathf.Clamp01(nearest / range));
        }

        private float RingRadius()
        {
            return tileSize * Mathf.Max(0.05f, roundaboutOrbitRadius);
        }

        // 각도 좌표 → 호 길이(RingLane 규약). [0, 2πR).
        private static float ArcOf(Vector3 pos, Vector3 center, float radius)
        {
            Vector3 rel = pos - center;
            return Mathf.Repeat(Mathf.Atan2(rel.y, rel.x), 2f * Mathf.PI) * radius;
        }

        // 같은 링(RingTile)에 소속된 다른 차들의 호 좌표 수집 — 자기 자신 제외(RingLane 규약).
        private void CollectRingArcs(RouteVehicle exclude, Vector2Int ringTile, Vector3 center, float radius)
        {
            ringArcScratch.Clear();
            foreach (KeyValuePair<CommuteCar, RouteVehicle> kv in carVehicles)
            {
                RouteVehicle other = kv.Value;
                if (other == exclude || !other.OnRing || other.RingTile != ringTile || !other.Object.activeSelf)
                {
                    continue;
                }

                ringArcScratch.Add(ArcOf(other.Pos, center, radius));
            }
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

            if (vehicle.SmokePuff != null)
            {
                vehicle.SmokePuff.SetActive(false);
            }
        }

        // 폴리라인용 신호 오버로드(기존 List 버전 무접촉). Sample이 이미 current/progress를 들고 있어 재계산 없음.
        private bool TryGetNextSignalTile(RoutePolyline poly, in Sample sample, out Vector2Int current, out Vector2Int next, out float progress)
        {
            current = default;
            next = default;
            progress = 0f;
            if (poly == null || sample.IsSpur)
            {
                return false;
            }

            int index = sample.TileIndex;
            if (index < 0 || index + 1 >= poly.TileCount)
            {
                return false;
            }

            current = poly.TileAt(index);
            next = poly.TileAt(index + 1);
            progress = sample.SegT;
            if (current == next || !IsSignalTile(next))
            {
                return false;
            }

            return true;
        }

        private bool IsRouteVehicleBlocked(RoutePolyline poly, in Sample sample)
        {
            if (simEngine == null
                || !TryGetNextSignalTile(poly, sample, out Vector2Int current, out Vector2Int next, out float progress))
            {
                return false;
            }

            // 경계 0.5 이미 넘은 차는 노란불 정리 규칙으로 통과(기존 L2075 규칙 이항).
            if (progress >= 0.5f)
            {
                return false;
            }

            bool horizontal = current.y == next.y;
            return !simEngine.IsSignalGreen(next, horizontal);
        }

        private void ResetCommuteState()
        {
            bakedRoutes.Clear();
            carVehicles.Clear();
            DestroyParkingVisuals();
            commuteScheduler.Clear();   // 복원 = sticky 없이 전원 신규 → SnapNewToHour가 전체 수렴
            commuteRoutesBuilt = false;
            commuteRoutesHash = 0;
            commuteTuningHash = 0;
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

        // 도착 코인 팝(항목 A): Sim이 금액·타이밍을 결정(ArrivalEvent 그대로) — 뷰는 팝을 그릴 위치만 고른다.
        // 수익·이벤트에 뷰 상태 역류 없음(읽기 전용 앵커 선택).
        private void OnArrival(ArrivalEvent e)
        {
            Vector3 anchor = GetArrivalPopAnchor(e.Destination);
            SpawnCoinPop(anchor, e.Coins);
        }

        // 팝 위치 선택: e.Destination에 최근 도착(정착 중 포함)했거나 주차 중인 통근 차가 있으면 그 차 위,
        // 없으면 타일 중심 폴백. 경제 이벤트(FlowSolver)와 통근 연출(CommuteScheduler)은 독립 시스템이라
        // 항상 매칭되는 차가 있진 않다 — 폴백은 정상 경로다.
        private Vector3 GetArrivalPopAnchor(Vector2Int tile)
        {
            const float RecentArrivalWindowSeconds = 1f;

            RouteVehicle best = null;
            int bestPriority = int.MinValue;

            foreach (KeyValuePair<CommuteCar, RouteVehicle> pair in carVehicles)
            {
                CommuteCar car = pair.Key;
                RouteVehicle vehicle = pair.Value;
                if (!vehicle.HasCurrentTile || vehicle.CurrentTile != tile)
                {
                    continue;
                }

                bool settling = vehicle.Settling;
                bool recentlyArrived = Time.time - vehicle.LastArrivedTime <= RecentArrivalWindowSeconds;
                bool parkedHere = (car.State == CarState.ParkedHome && car.Home == tile)
                    || (car.State == CarState.ParkedWork && car.Work == tile);

                if (!settling && !recentlyArrived && !parkedHere)
                {
                    continue;
                }

                int priority = settling ? 2 : recentlyArrived ? 1 : 0;
                if (best == null || priority > bestPriority)
                {
                    best = vehicle;
                    bestPriority = priority;
                }
            }

            return best != null
                ? best.Object.transform.position
                : transform.TransformPoint(GridToLocal(tile, vehicleZ));
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
                if (Mathf.Abs(tileDistance.x) > flowBurstSpeedRadius
                    || Mathf.Abs(tileDistance.y) > flowBurstSpeedRadius)
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

        private static Renderer PrepareRenderer(Renderer renderer)
        {
            if (renderer != null)
            {
                renderer.sharedMaterial = CreateUnlitMaterial();
                renderer.enabled = true;
                renderer.forceRenderingOff = false;
                renderer.allowOcclusionWhenDynamic = false;
            }

            return renderer;
        }

        private static Material CreateUnlitMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            shader ??= Shader.Find("Unlit/Color");
            shader ??= Shader.Find("Sprites/Default");
            shader ??= Shader.Find("Standard");
            return new Material(shader);
        }

        private Material CreateGridMaterial()
        {
            Material material = CreateUnlitMaterial();
            Texture2D texture = CreateGridTexture();

            material.renderQueue = 1900;
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
