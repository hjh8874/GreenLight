using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.Sim;
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
        [SerializeField] private float vehicleSpeed = 2f;
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
        [SerializeField, Range(0f, 1f)] private float slowSpeedMul = 0.55f;
        [SerializeField, Range(0f, 1f)] private float jamSpeedMul = 0.25f;
        [SerializeField, Range(0.02f, 1f)] private float vehicleStreamScale = 0.15f;   // 뷰 차량 스트림 배율 — 심 rate의 몇 %만 그릴지(화면 가독성). 심 수치·수익 불변
        [SerializeField, Range(0.6f, 0.85f)] private float cornerTurnRadius = 0.75f;   // 일반 교차로 회전 반경(타일 비율)
        [SerializeField] private float roundaboutOrbitRadius = 0.68f;  // 로터리 궤도 반경(타일 비율)
        [SerializeField] private float turnSignZ = -0.5f;           // 표지판 마커 z(신호와 분리 — 공존 타일 겹침 회피)

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
        private readonly List<RouteSpawnState> routeSpawns = new();
        private readonly List<FlowBurstSpeedZone> flowBurstSpeedZones = new();

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
            public float Phase;
            public float CurrentSpeed;
            public readonly List<Vector2Int> DisplayRoute = new();
            public int DisplayRouteHash;
            public Vector3 Pos;   // 지난 프레임 위치·진행 방향 — 차간 유지 판정용(1프레임 지연 근사)
            public Vector3 Dir;
            public Vector2Int CurrentTile;
            public bool HasCurrentTile;
            public GameObject AngryMark;   // Jam 팝업(!) — vehicleRoot 소속(차량 자식 금지: 비균등 스케일)
            public GameObject SmokePuff;   // Jam 매연 퍼프 — 동일 소속
            public int RouteIndex = -1;
            public int RouteHash;
        }

        private sealed class RouteSpawnState
        {
            public int RouteHash;
            public float Countdown;
            public int Target;        // 이 경로의 목표 대수(rate×왕복시간 근사) — 차는 순환 유지, 대수만 조절
            public int ActiveCount;   // 현재 이 경로에서 도는 대수
        }

        private struct FlowBurstSpeedZone
        {
            public Vector2Int Tile;
            public float Until;
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
                float nextZoomDistance = Mathf.Clamp(
                    zoomDistance - scrollY * zoomScrollSensitivity,
                    minimumZoomDistance,
                    minimumZoomDistance + zoomDistanceRange);
                if (!Mathf.Approximately(nextZoomDistance, zoomDistance))
                {
                    zoomDistance = nextZoomDistance;
                    cameraViewChanged = true;
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
            routeSpawns.Clear();

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
            if (simEngine == null)
            {
                return;
            }

            IReadOnlyList<List<Vector2Int>> routes = simEngine.ActiveRoutes;
            EnsureVehicleCount(maxMovingVehicles);
            SyncRouteSpawnStates(routes);
            SpawnRouteVehicles(routes);

            for (int i = 0; i < vehicles.Count; i++)
            {
                RouteVehicle vehicle = vehicles[i];
                if (!vehicle.Object.activeSelf)
                {
                    continue;
                }

                if (vehicle.RouteIndex < 0
                    || vehicle.RouteIndex >= routes.Count
                    || routes[vehicle.RouteIndex].Count <= 1
                    || vehicle.RouteHash != routeSpawns[vehicle.RouteIndex].RouteHash)
                {
                    DeactivateVehicle(vehicle);
                }
            }

            for (int i = 0; i < vehicles.Count; i++)
            {
                if (!vehicles[i].Object.activeSelf)
                {
                    continue;
                }

                MoveVehicle(vehicles[i], routes[vehicles[i].RouteIndex]);
            }
        }

        private void SyncRouteSpawnStates(IReadOnlyList<List<Vector2Int>> routes)
        {
            while (routeSpawns.Count < routes.Count)
            {
                routeSpawns.Add(new RouteSpawnState());
            }

            if (routeSpawns.Count > routes.Count)
            {
                routeSpawns.RemoveRange(routes.Count, routeSpawns.Count - routes.Count);
            }

            for (int i = 0; i < routes.Count; i++)
            {
                int routeHash = routes[i].Count > 1 ? ComputeDisplayRouteHash(routes[i]) : 0;
                RouteSpawnState spawn = routeSpawns[i];
                if (spawn.RouteHash == routeHash)
                {
                    continue;
                }

                spawn.RouteHash = routeHash;
                spawn.Countdown = 0f;
                spawn.ActiveCount = 0;   // 구 해시 차량은 Deactivate에서 해시 불일치로 감산 안 됨(이중감산 방지)
            }
        }

        private void SpawnRouteVehicles(IReadOnlyList<List<Vector2Int>> routes)
        {
            // 뷰 전용 배율: 심 rate 그대로면 화면이 차로 도배됨(경로당 rate×왕복시간 대).
            // 심 수치는 불변 — 그리는 밀도만 줄이는 번역 배율(원칙①). 인스펙터 라이브 튜닝.
            float demandRate = simEngine != null ? simEngine.DemandRate * vehicleStreamScale : 0f;
            if (demandRate <= 0f)
            {
                return;
            }

            float spawnInterval = 1f / demandRate;
            for (int routeIndex = 0; routeIndex < routes.Count && routeIndex < routeSpawns.Count; routeIndex++)
            {
                if (routes[routeIndex].Count <= 1)
                {
                    continue;
                }

                RouteSpawnState spawn = routeSpawns[routeIndex];

                // 목표 대수 = rate × 왕복시간 근사(긴 경로일수록 많이). 차는 순환 유지 —
                // 수요가 늘면 서서히 합류, 줄면 왕복 마친 차만 은퇴(생겼다 사라졌다 처닝 방지).
                float cycleTime = (routes[routeIndex].Count - 1) * 2f / Mathf.Max(0.01f, vehicleSpeed);
                spawn.Target = Mathf.Max(1, Mathf.CeilToInt(demandRate * cycleTime));

                if (spawn.ActiveCount >= spawn.Target)
                {
                    spawn.Countdown = 0f;   // 충원 필요 없음 — 다음 부족 시 즉시 1대부터
                    continue;
                }

                spawn.Countdown -= Time.deltaTime;
                if (spawn.Countdown <= 0f && TrySpawnVehicle(routeIndex, routes))
                {
                    spawn.ActiveCount++;
                    spawn.Countdown = spawnInterval;   // 간격당 1대씩만 합류(뿅 러시 방지)
                }
            }
        }

        private bool TrySpawnVehicle(int routeIndex, IReadOnlyList<List<Vector2Int>> routes)
        {
            RouteVehicle vehicle = null;
            for (int i = 0; i < vehicles.Count; i++)
            {
                if (!vehicles[i].Object.activeSelf)
                {
                    vehicle = vehicles[i];
                    break;
                }
            }

            if (vehicle == null)
            {
                return false;
            }

            List<Vector2Int> route = routes[routeIndex];
            vehicle.RouteIndex = routeIndex;
            vehicle.RouteHash = routeSpawns[routeIndex].RouteHash;
            vehicle.Phase = 0f;
            vehicle.CurrentSpeed = 0f;
            vehicle.HasCurrentTile = false;
            vehicle.DisplayRouteHash = 0;
            vehicle.DisplayRoute.Clear();

            if (vehicle.AngryMark != null)
            {
                vehicle.AngryMark.SetActive(false);
                vehicle.SmokePuff.SetActive(false);
            }

            List<Vector2Int> displayRoute = GetDisplayRoute(vehicle, route);
            if (displayRoute.Count > 1)
            {
                EvaluateVehiclePose(displayRoute, 0, 0f, true, out Vector3 pos, out Vector3 travelDir, out _);
                vehicle.Object.transform.localPosition = pos;
                vehicle.Pos = pos;
                vehicle.Dir = travelDir;
                vehicle.CurrentTile = displayRoute[0];
                vehicle.HasCurrentTile = true;
            }

            vehicle.Object.SetActive(true);
            return true;
        }

        private void DeactivateVehicle(RouteVehicle vehicle)
        {
            // 현 해시로 카운트된 차만 감산(위상 변경 직후 구 해시 차량 이중감산 방지)
            if (vehicle.RouteIndex >= 0 && vehicle.RouteIndex < routeSpawns.Count
                && routeSpawns[vehicle.RouteIndex].RouteHash == vehicle.RouteHash)
            {
                RouteSpawnState spawn = routeSpawns[vehicle.RouteIndex];
                spawn.ActiveCount = Mathf.Max(0, spawn.ActiveCount - 1);
            }

            vehicle.Object.SetActive(false);
            vehicle.RouteIndex = -1;
            vehicle.RouteHash = 0;
            vehicle.HasCurrentTile = false;
            vehicle.CurrentSpeed = 0f;
            if (vehicle.AngryMark != null)
            {
                vehicle.AngryMark.SetActive(false);
                vehicle.SmokePuff.SetActive(false);
            }
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
                if (vehiclePrefab == null)
                {
                    detailRenderer = CreateDetailCube(vehicle.transform, "Cabin",
                        new Vector3(0.55f, 0.72f, 0.42f), new Vector3(-0.05f, 0f, -0.65f));
                    ApplyRendererColor(detailRenderer, Color.Lerp(vehicleColor, Color.white, 0.3f));
                }

                vehicle.SetActive(false);
                vehicles.Add(new RouteVehicle
                {
                    Object = vehicle,
                    Renderer = renderer,
                    DetailRenderer = detailRenderer,
                    Phase = vehicles.Count * 0.618f
                });
            }
        }

        private void MoveVehicle(RouteVehicle vehicle, List<Vector2Int> sourceRoute)
        {
            List<Vector2Int> route = GetDisplayRoute(vehicle, sourceRoute);
            int segmentCount = route.Count - 1;
            float targetSpeed = vehicleSpeed;

            if (segmentCount <= 0)
            {
                return;
            }

            // 실제 렌더 위치와 같은 Fold 인덱스로 현재 타일을 잡는다. Phase%route.Count는
            // 복귀 구간에서 접힌 위치와 어긋나 정체 벗어난 차가 엉뚱한(정체) 타일을 읽어
            // !표·저속이 남던 버그(정체 연출과 속도 모두 이 타일 사용).
            Vector2Int currentTile = route[Mathf.Clamp(Mathf.FloorToInt(Fold(vehicle.Phase, segmentCount)), 0, route.Count - 1)];
            CongestionLevel congestion = tileData.GetCongestion(currentTile);
            if (congestion == CongestionLevel.Slow)
            {
                targetSpeed *= slowSpeedMul;
            }
            else if (congestion == CongestionLevel.Jam)
            {
                targetSpeed *= jamSpeedMul;
            }

            // 정체 해소 직후에는 팀 디버그 뷰와 같은 반경 가속을 적용한다.
            // 신호 정지는 아래에서 우선하고, 앞차 간격은 속도 상한으로 부드럽게 반영한다.
            bool boostedByFlowBurst = IsInFlowBurstSpeedZone(currentTile);
            if (boostedByFlowBurst)
            {
                targetSpeed *= flowBurstSpeedMultiplier;
            }

            // 오버라이드 = 양축 초록이라 전방 신호가 오버라이드면 축 무관 가속이 정답(스펙 2026-07-11 §3).
            if (signalControl != null && TryGetNextSignalTile(route, vehicle.Phase, out _, out Vector2Int aheadSignal, out _)
                && signalControl.GetOverrideSecondsLeft(aheadSignal) > 0f)
            {
                targetSpeed *= overrideSpeedMul;
            }

            targetSpeed = Mathf.Min(targetSpeed, LeaderSpeedCap(vehicle, targetSpeed));
            bool mustStop = false;
            if (IsRouteVehicleBlocked(route, vehicle.Phase))
            {
                targetSpeed = 0f;
                mustStop = true;
            }

            // 90도 원호 길이는 같은 반경의 두 직선 합보다 짧으므로, phase 진행량을 보정해
            // 직선과 원호에서 차량의 실제 화면 이동속도가 같게 유지되도록 한다.
            if (targetSpeed > 0f)
            {
                targetSpeed *= GetTurnPhaseSpeedMultiplier(route, vehicle.Phase);
            }

            if (mustStop)
            {
                // 신호 판정은 연출보다 우선한다. 감속 중 Phase가 전진하면
                // 정지선을 넘을 수 있으므로 논리 이동을 즉시 멈춘다.
                vehicle.CurrentSpeed = 0f;
            }
            else
            {
                float acceleration = targetSpeed < vehicle.CurrentSpeed
                    ? vehicleDeceleration
                    : vehicleAcceleration * (boostedByFlowBurst ? flowBurstAccelerationMultiplier : 1f);
                vehicle.CurrentSpeed = Mathf.MoveTowards(
                    vehicle.CurrentSpeed,
                    targetSpeed,
                    acceleration * Time.deltaTime);
            }
            float nextPhase = vehicle.Phase + Time.deltaTime * vehicle.CurrentSpeed;
            float cycleLength = segmentCount * 2f;
            if (nextPhase >= cycleLength)
            {
                // 왕복 완료 = 집 도착. 목표 초과분만 이 지점에서 자연 은퇴,
                // 아니면 다음 왕복 이어감 — 차가 화면에서 사라지지 않는다(처닝 방지).
                RouteSpawnState spawn = vehicle.RouteIndex >= 0 && vehicle.RouteIndex < routeSpawns.Count
                    ? routeSpawns[vehicle.RouteIndex]
                    : null;
                if (spawn == null || spawn.ActiveCount > spawn.Target)
                {
                    DeactivateVehicle(vehicle);
                    return;
                }

                nextPhase -= cycleLength;
            }

            vehicle.Phase = nextPhase;

            float folded = Fold(vehicle.Phase, segmentCount);
            int index = Mathf.Clamp(Mathf.FloorToInt(folded), 0, segmentCount - 1);
            float t = folded - index;
            // 왕복 유령: 접힌 복귀 구간이면 실제 진행은 역방향 — 차선·바라보기 둘 다 이 방향 기준.
            bool forward = vehicle.Phase <= segmentCount;
            EvaluateVehiclePose(route, index, t, forward, out Vector3 pos, out Vector3 travelDir, out Vector2Int insideTile);

            // 로터리 경계에서는 차선 포즈를 유지하고, 안쪽에서만 CCW 링 포즈로 전환한다.
            if (IsRoundaboutTile(insideTile))
            {
                int ci = t < 0.5f ? index : index + 1;
                if (TryRoundaboutOrbit(route, ci, folded, forward, out Vector3 ringPos, out Vector3 ringDir))
                {
                    float arcU = Mathf.Clamp01(forward ? folded - ci + 0.5f : ci + 0.5f - folded);
                    const float edge = 0.35f;
                    float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(arcU / edge))
                                * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - arcU) / edge));
                    pos = Vector3.Lerp(pos, ringPos, blend);
                    if (ringDir.sqrMagnitude > 0.0001f)
                    {
                        Vector3 blendedDir = Vector3.Lerp(travelDir.normalized, ringDir.normalized, blend);
                        if (blendedDir.sqrMagnitude > 0.0001f)
                        {
                            travelDir = blendedDir.normalized;
                        }
                    }
                }
            }

            vehicle.Object.transform.localPosition = pos;
            vehicle.CurrentTile = currentTile;
            vehicle.HasCurrentTile = true;

            if (travelDir.sqrMagnitude > 0.001f)
            {
                // 복귀 구간도 진짜 진행 방향을 바라봄(뒷걸음 유령 제거).
                float angle = Mathf.Atan2(travelDir.y, travelDir.x) * Mathf.Rad2Deg;
                vehicle.Object.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            }

            // 역주행 유령 숨김(스펙 §3 해법①): 복귀 구간(!forward)에서 지금 서 있는 타일이 일방이고
            // 실제 진행 방향이 그 일방 방향과 거의 정반대(역주행)면 렌더러를 숨긴다. 조건이 매 프레임
            // 재평가되므로 조건 해제(순방향 복귀 등) 시 별도 상태 없이 자연히 복원됨.
            bool hiddenAsGhost = false;
            if (!forward && trafficRule != null)
            {
                Vector2Int onewayDir = trafficRule.GetOnewayDir(insideTile);
                if (onewayDir != Vector2Int.zero)
                {
                    Vector3 onewayWorldDir = new Vector3(onewayDir.x, onewayDir.y, 0f);
                    hiddenAsGhost = Vector3.Dot(travelDir, onewayWorldDir) < -0.9f;
                }
            }

            if (vehicle.Renderer != null)
            {
                vehicle.Renderer.enabled = !hiddenAsGhost;
                Color routeColor = Color.HSVToRGB((vehicle.RouteIndex * 0.137f) % 1f, 0.7f, 0.95f);
                ApplyRendererColor(vehicle.Renderer, routeColor);

                if (vehicle.DetailRenderer != null)
                {
                    vehicle.DetailRenderer.enabled = !hiddenAsGhost;
                    ApplyRendererColor(vehicle.DetailRenderer, Color.Lerp(routeColor, Color.white, 0.3f));
                }
            }

            // Jam 분노 팝업(스펙 2026-07-12 §1): 내가 서 있는 타일이 Jam이면 ! + 매연 — 가짜 디테일.
            bool jammed = !hiddenAsGhost && tileData.GetCongestion(currentTile) == CongestionLevel.Jam;
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
                    vehicle.SmokePuff.transform.localPosition = basePos - travelDir * (tileSize * 0.28f)
                        + Vector3.back * (tileSize * (0.12f + 0.06f * Mathf.Sin(Time.time * 2f)));
                }
            }

            vehicle.Pos = vehicle.Object.transform.localPosition;
            vehicle.Dir = travelDir;
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

        // 시뮬레이션의 대각 지름길은 유지하되, 실제 직각 회전으로 보이는 구간은 표시 경로에
        // 교차로 타일을 삽입한다. 한 대각 구간에 두 타일 이동을 압축하지 않아 직선과 같은 속도를 유지한다.
        private List<Vector2Int> GetDisplayRoute(RouteVehicle vehicle, List<Vector2Int> sourceRoute)
        {
            int routeHash = ComputeDisplayRouteHash(sourceRoute);
            if (vehicle.DisplayRoute.Count > 0 && vehicle.DisplayRouteHash == routeHash)
            {
                return vehicle.DisplayRoute;
            }

            int previousSegmentCount = Mathf.Max(0, vehicle.DisplayRoute.Count - 1);
            float normalizedPhase = previousSegmentCount > 0
                ? vehicle.Phase / (previousSegmentCount * 2f)
                : 0f;

            vehicle.DisplayRoute.Clear();
            if (sourceRoute.Count == 0)
            {
                vehicle.DisplayRouteHash = routeHash;
                return vehicle.DisplayRoute;
            }

            vehicle.DisplayRoute.Add(sourceRoute[0]);
            for (int i = 0; i < sourceRoute.Count - 1; i++)
            {
                if (TryGetDiagonalTurnBridge(sourceRoute, i, out Vector2Int bridge)
                    && vehicle.DisplayRoute[vehicle.DisplayRoute.Count - 1] != bridge)
                {
                    vehicle.DisplayRoute.Add(bridge);
                }

                Vector2Int next = sourceRoute[i + 1];
                if (vehicle.DisplayRoute[vehicle.DisplayRoute.Count - 1] != next)
                {
                    vehicle.DisplayRoute.Add(next);
                }
            }

            int newSegmentCount = Mathf.Max(0, vehicle.DisplayRoute.Count - 1);
            if (previousSegmentCount > 0 && newSegmentCount > 0)
            {
                vehicle.Phase = normalizedPhase * newSegmentCount * 2f;
            }
            else if (newSegmentCount > 0)
            {
                vehicle.Phase = Mathf.Repeat(vehicle.Phase, newSegmentCount * 2f);
            }

            vehicle.DisplayRouteHash = routeHash;
            return vehicle.DisplayRoute;
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

        // 일반 교차로 회전 연출(뷰 전용 — 엔진 무관): 진입선과 이탈선에 접하는 90도 원호로
        // 연결해 교차로 중심에서 방향이 한 번에 꺾이지 않도록 한다.
        private void EvaluateVehiclePose(
            List<Vector2Int> route,
            int segmentIndex,
            float segmentT,
            bool forward,
            out Vector3 pos,
            out Vector3 travelDir,
            out Vector2Int insideTile)
        {
            Vector3 a = GridToLocal(route[segmentIndex], vehicleZ);
            Vector3 b = GridToLocal(route[segmentIndex + 1], vehicleZ);
            Vector3 centerline = Vector3.Lerp(a, b, segmentT);
            Vector3 routeTangent = (b - a).normalized;
            insideTile = segmentT < 0.5f ? route[segmentIndex] : route[segmentIndex + 1];

            float radiusFraction = GetCornerTurnRadiusFraction();

            int cornerIndex = -1;
            float curveT = 0f;

            if (segmentT >= 1f - radiusFraction && segmentIndex + 2 < route.Count)
            {
                cornerIndex = segmentIndex + 1;
                curveT = (segmentT - (1f - radiusFraction)) / (radiusFraction * 2f);
            }
            else if (segmentT < radiusFraction && segmentIndex > 0)
            {
                cornerIndex = segmentIndex;
                curveT = 0.5f + segmentT / (radiusFraction * 2f);
            }

            if (cornerIndex >= 0 && TryEvaluateTurnBezier(route, cornerIndex, curveT, radiusFraction, out Vector3 curvePosition, out Vector3 curveTangent))
            {
                centerline = curvePosition;
                routeTangent = curveTangent;
                insideTile = route[cornerIndex];
            }

            travelDir = forward ? routeTangent : -routeTangent;

            // 곡선의 접선을 기준으로 오른쪽 차선을 계산해야 회전 중에도 차선이 끊기지 않는다.
            Vector3 laneRight = new Vector3(travelDir.y, -travelDir.x, 0f);
            pos = centerline + laneRight * (tileSize * laneOffset);
        }

        private float GetTurnPhaseSpeedMultiplier(List<Vector2Int> route, float phase)
        {
            int segmentCount = route.Count - 1;
            if (segmentCount <= 0)
            {
                return 1f;
            }

            float folded = Fold(phase, segmentCount);
            int segmentIndex = Mathf.Clamp(Mathf.FloorToInt(folded), 0, segmentCount - 1);
            float segmentT = folded - segmentIndex;
            float radiusFraction = GetCornerTurnRadiusFraction();
            int cornerIndex = -1;

            int orbitIndex = segmentT < 0.5f ? segmentIndex : segmentIndex + 1;
            bool fwd = Mathf.Repeat(phase, segmentCount * 2f) <= segmentCount;
            if (orbitIndex > 0
                && orbitIndex < route.Count - 1
                && Mathf.Abs(folded - orbitIndex) <= 0.5f
                && IsRoundaboutTile(route[orbitIndex]))
            {
                if (roundaboutOrbitRadius > 0.01f
                    && TryGetRoundaboutArc(route, orbitIndex, fwd, out _, out float ccwSweep)
                    && ccwSweep > 0.01f)
                {
                    return 1f / (roundaboutOrbitRadius * ccwSweep);
                }

                return 1f;
            }

            if (segmentT >= 1f - radiusFraction && segmentIndex + 2 < route.Count)
            {
                cornerIndex = segmentIndex + 1;
            }
            else if (segmentT < radiusFraction && segmentIndex > 0)
            {
                cornerIndex = segmentIndex;
            }

            if (cornerIndex < 0 || !TryGetTurnDirections(route, cornerIndex, out Vector3 incoming, out Vector3 outgoing))
            {
                return 1f;
            }

            // 우측 차선 오프셋으로 실제 차량 궤도의 반경은 중심선 반경과 달라진다.
            // 좌회전은 바깥쪽, 우회전은 안쪽 반경을 사용하며 복귀 구간에서는 회전 방향도 반전된다.
            bool forward = phase <= segmentCount;
            float routeCross = incoming.x * outgoing.y - incoming.y * outgoing.x;
            float travelCross = forward ? routeCross : -routeCross;
            float centerRadius = GetCornerTurnRadiusFraction();
            float actualRadius = centerRadius + (travelCross > 0f ? laneOffset : -laneOffset);
            actualRadius = Mathf.Max(actualRadius, 0.05f);

            // phase상 곡선 길이(2×중심선 반경)를 실제 차량 원호 길이(π/2×차선 반경)에 맞춘다.
            return 4f * centerRadius / (Mathf.PI * actualRadius);
        }

        // 진행 방향 기준 진입·이탈을 잡고 우측통행 로터리의 CCW 스윕을 계산한다.
        private bool TryGetRoundaboutArc(
            List<Vector2Int> route,
            int centerIndex,
            bool forward,
            out float entryAngle,
            out float ccwSweep)
        {
            entryAngle = 0f;
            ccwSweep = 0f;
            if (centerIndex <= 0 || centerIndex >= route.Count - 1)
            {
                return false;
            }

            Vector2Int previous = forward ? route[centerIndex - 1] : route[centerIndex + 1];
            Vector2Int next = forward ? route[centerIndex + 1] : route[centerIndex - 1];
            Vector3 incoming = new Vector3(
                route[centerIndex].x - previous.x,
                route[centerIndex].y - previous.y,
                0f).normalized;
            Vector3 outgoing = new Vector3(
                next.x - route[centerIndex].x,
                next.y - route[centerIndex].y,
                0f).normalized;
            if (incoming.sqrMagnitude < 0.5f || outgoing.sqrMagnitude < 0.5f)
            {
                return false;
            }

            entryAngle = Mathf.Atan2(-incoming.y, -incoming.x);
            float exitAngle = Mathf.Atan2(outgoing.y, outgoing.x);
            ccwSweep = Mathf.Repeat(exitAngle - entryAngle, 2f * Mathf.PI);
            if (ccwSweep < 0.05f)
            {
                ccwSweep = 2f * Mathf.PI;
            }

            return true;
        }

        private bool TryRoundaboutOrbit(
            List<Vector2Int> route,
            int centerIndex,
            float folded,
            bool forward,
            out Vector3 position,
            out Vector3 tangent)
        {
            position = default;
            tangent = default;
            if (!TryGetRoundaboutArc(route, centerIndex, forward, out float entryAngle, out float ccwSweep))
            {
                return false;
            }

            float arcU = Mathf.Clamp01(
                forward ? folded - centerIndex + 0.5f : centerIndex + 0.5f - folded);
            float angle = entryAngle + arcU * ccwSweep;
            float radius = tileSize * roundaboutOrbitRadius;
            Vector3 center = GridToLocal(route[centerIndex], vehicleZ);
            position = center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
            tangent = new Vector3(-Mathf.Sin(angle), Mathf.Cos(angle), 0f);
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

        private bool TryEvaluateTurnBezier(
            List<Vector2Int> route,
            int cornerIndex,
            float curveT,
            float radiusFraction,
            out Vector3 position,
            out Vector3 tangent)
        {
            position = default;
            tangent = default;

            if (!TryGetTurnDirections(route, cornerIndex, out Vector3 incoming, out Vector3 outgoing))
            {
                return false;
            }

            Vector3 corner = GridToLocal(route[cornerIndex], vehicleZ);
            float radius = tileSize * radiusFraction;
            Vector3 entry = corner - incoming * radius;
            Vector3 exit = corner + outgoing * radius;
            const float quarterCircleHandle = 0.55228475f;
            Vector3 controlIn = entry + incoming * (radius * quarterCircleHandle);
            Vector3 controlOut = exit - outgoing * (radius * quarterCircleHandle);
            float u = RemapBezierParameterByArcLength(
                entry, controlIn, controlOut, exit, Mathf.Clamp01(curveT));

            position = EvaluateCubicBezier(entry, controlIn, controlOut, exit, u);
            tangent = EvaluateCubicBezierTangent(entry, controlIn, controlOut, exit, u);
            return tangent.sqrMagnitude > 0.0001f;
        }

        private static float RemapBezierParameterByArcLength(
            Vector3 start,
            Vector3 controlIn,
            Vector3 controlOut,
            Vector3 end,
            float normalizedDistance)
        {
            const int samples = 12;
            float totalLength = 0f;
            Vector3 previous = start;

            for (int i = 1; i <= samples; i++)
            {
                float sampleT = i / (float)samples;
                Vector3 sample = EvaluateCubicBezier(start, controlIn, controlOut, end, sampleT);
                totalLength += Vector3.Distance(previous, sample);
                previous = sample;
            }

            float targetLength = totalLength * normalizedDistance;
            float accumulated = 0f;
            previous = start;

            for (int i = 1; i <= samples; i++)
            {
                float sampleT = i / (float)samples;
                Vector3 sample = EvaluateCubicBezier(start, controlIn, controlOut, end, sampleT);
                float segmentLength = Vector3.Distance(previous, sample);
                if (accumulated + segmentLength >= targetLength)
                {
                    float localT = segmentLength > 0.0001f
                        ? (targetLength - accumulated) / segmentLength
                        : 0f;
                    return Mathf.Lerp((i - 1) / (float)samples, sampleT, localT);
                }

                accumulated += segmentLength;
                previous = sample;
            }

            return 1f;
        }

        private static Vector3 EvaluateCubicBezier(
            Vector3 start,
            Vector3 controlIn,
            Vector3 controlOut,
            Vector3 end,
            float t)
        {
            float oneMinusT = 1f - t;
            return oneMinusT * oneMinusT * oneMinusT * start
                + 3f * oneMinusT * oneMinusT * t * controlIn
                + 3f * oneMinusT * t * t * controlOut
                + t * t * t * end;
        }

        private static Vector3 EvaluateCubicBezierTangent(
            Vector3 start,
            Vector3 controlIn,
            Vector3 controlOut,
            Vector3 end,
            float t)
        {
            float oneMinusT = 1f - t;
            return (3f * oneMinusT * oneMinusT * (controlIn - start)
                + 6f * oneMinusT * t * (controlOut - controlIn)
                + 3f * t * t * (end - controlOut)).normalized;
        }

        private bool TryGetTurnDirections(
            List<Vector2Int> route,
            int cornerIndex,
            out Vector3 incoming,
            out Vector3 outgoing)
        {
            incoming = default;
            outgoing = default;

            // 로터리는 아래의 기존 전용 궤도 연출을 그대로 사용해 원호를 중복 적용하지 않는다.
            if (cornerIndex <= 0 || cornerIndex >= route.Count - 1 || IsRoundaboutTile(route[cornerIndex]))
            {
                return false;
            }

            Vector3 previous = GridToLocal(route[cornerIndex - 1], vehicleZ);
            Vector3 corner = GridToLocal(route[cornerIndex], vehicleZ);
            Vector3 next = GridToLocal(route[cornerIndex + 1], vehicleZ);
            incoming = corner - previous;
            outgoing = next - corner;

            if (incoming.sqrMagnitude < 0.0001f || outgoing.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            incoming.Normalize();
            outgoing.Normalize();
            return Mathf.Abs(Vector3.Dot(incoming, outgoing)) < 0.001f;
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

        private static float Fold(float phase, int segmentCount)
        {
            float cycle = segmentCount * 2f;
            float p = Mathf.Repeat(phase, cycle);
            return p <= segmentCount ? p : cycle - p;
        }

        private bool IsRouteVehicleBlocked(List<Vector2Int> route, float phase)
        {
            if (simEngine == null || !TryGetNextSignalTile(route, phase, out Vector2Int current, out Vector2Int next, out float progress))
            {
                return false;
            }

            // 타일 중앙 사이 절반이 교차로 경계. 이미 경계를 넘은 차는 노란불의
            // "진입 금지, 정리 준비" 규칙에 따라 멈추지 않고 교차로를 빠져나간다.
            if (progress >= 0.5f)
            {
                return false;
            }

            bool horizontal = current.y == next.y;
            return !simEngine.IsSignalGreen(next, horizontal);
        }

        // 차의 현재 위상에서 진행 방향의 현재/다음 타일. 다음 타일이 신호일 때만 true — 부스트·블록 판정 공용.
        private bool TryGetNextSignalTile(List<Vector2Int> route, float phase, out Vector2Int current, out Vector2Int next, out float progress)
        {
            current = default;
            next = default;
            progress = 0f;
            if (route == null || route.Count < 2) return false;
            int segmentCount = route.Count - 1;
            float cycle = segmentCount * 2f;
            float p = Mathf.Repeat(phase, cycle);
            bool forward = p <= segmentCount;
            float folded = forward ? p : cycle - p;
            int index = Mathf.Clamp(Mathf.FloorToInt(folded), 0, segmentCount - 1);
            current = forward ? route[index] : route[index + 1];
            next = forward ? route[index + 1] : route[index];
            progress = forward ? folded - index : index + 1f - folded;
            if (current == next || !IsSignalTile(next)) return false;
            return true;
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
