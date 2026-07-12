using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Sim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

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
        [SerializeField] private float vehicleZ = -0.35f;
        [SerializeField] private float signalZ = -0.45f;
        [SerializeField] private float burstSeconds = 0.8f;
        [SerializeField] private float gridLineThickness = 0.045f;
        [SerializeField] private float overrideSpeedMul = 2.2f;    // 오버라이드 라인 차량 속도 배율
        [SerializeField] private float overridePulseAmp = 0.25f;   // 신호 펄스 진폭
        [SerializeField] private float laneOffset = 0.18f;         // 우측통행 차선 오프셋(타일 비율)
        [SerializeField] private float followGap = 0.4f;           // 차간 유지 거리(타일 비율)
        [SerializeField] private float roundaboutOrbitRadius = 0.3f;   // 로터리 궤도 반경(타일 비율)

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

        private readonly Dictionary<Vector2Int, TileVisual> tileVisuals = new();
        private readonly Dictionary<Vector2Int, SignalVisual> signalVisuals = new();
        private readonly Dictionary<Vector2Int, GameObject> roundaboutVisuals = new();
        private readonly Dictionary<Vector2Int, GameObject> overpassVisuals = new();
        private readonly Dictionary<Vector2Int, GameObject> onewayVisuals = new();
        private readonly List<RouteVehicle> vehicles = new();
        private readonly List<BurstVisual> bursts = new();

        private CityFlowServices services;
        private IReadOnlyTileData tileData;
        private IPlacementService placement;
        private SimEngine simEngine;
        private ISignalControl signalControl;
        private Transform gridRoot;
        private Transform boardRoot;
        private Transform tileRoot;
        private Transform vehicleRoot;
        private Transform signalRoot;
        private Transform effectRoot;
        private int selectedSignalIndex;

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

        private sealed class RouteVehicle
        {
            public GameObject Object;
            public Renderer Renderer;
            public float Phase;
            public Vector3 Pos;   // 지난 프레임 위치·진행 방향 — 차간 유지 판정용(1프레임 지연 근사)
            public Vector3 Dir;
            public GameObject AngryMark;   // Jam 팝업(!) — vehicleRoot 소속(차량 자식 금지: 비균등 스케일)
            public GameObject SmokePuff;   // Jam 매연 퍼프 — 동일 소속
        }

        private sealed class BurstVisual
        {
            public GameObject Object;
            public float HideAt;
        }

        private sealed class CoinVisual
        {
            public GameObject Object;
            public Vector3 Velocity;
            public float DieAt;
        }

        private sealed class NoteVisual
        {
            public TextMesh Text;
            public float DieAt;
        }

        private readonly List<CoinVisual> coins = new();
        private readonly List<NoteVisual> notes = new();
        [SerializeField] private Color coinColor = new Color(1f, 0.84f, 0.2f);

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

            services.Events.Placed += OnPlaced;
            services.Events.CongestionChanged += OnCongestionChanged;
            services.Events.FlowBurst += OnFlowBurst;

            BuildRoots();
            BuildBoard();
            BuildGridLines();
            RefreshAllTiles();
            RefreshSignals();
            RefreshRoundabouts();
            RefreshOverpasses();
            RefreshOneways();
            RefreshVehicles();
            gameObject.AddComponent<DriveViewCamera>().Init(simEngine, transform, tileSize);
            gameObject.AddComponent<FloatingWindowService>().Init(width * tileSize, height * tileSize);
        }

        private void OnDestroy()
        {
            if (services == null)
            {
                return;
            }

            services.Events.Placed -= OnPlaced;
            services.Events.CongestionChanged -= OnCongestionChanged;
            services.Events.FlowBurst -= OnFlowBurst;
        }

        private void Update()
        {
            if (tileData == null)
            {
                return;
            }

            HandleSignalInput();
            RefreshSignals();
            RefreshRoundabouts();
            RefreshOverpasses();
            RefreshOneways();
            RefreshVehicles();
            UpdateBursts();
            UpdateCoins();
            UpdateNotes();
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
            visual.Object.transform.localPosition = GridToLocal(tile, 0f);
            visual.Object.transform.localScale = GetTileScale(type);
            ApplyTileColor(tile, visual);
        }

        private TileVisual CreateTileVisual(Vector2Int tile, TileType type)
        {
            GameObject instance = InstantiatePrefabOrPrimitive(GetPrefab(type), PrimitiveType.Cube);
            instance.name = $"{type}_{tile.x}_{tile.y}";
            instance.transform.SetParent(tileRoot, false);

            return new TileVisual
            {
                Object = instance,
                Renderer = PrepareRenderer(instance.GetComponentInChildren<Renderer>()),
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
            float size = type == TileType.Road ? tileSize : tileSize * 0.48f;
            float depth = type == TileType.Road ? 0.08f : 0.24f;
            return new Vector3(size, size, depth);
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
            if (simEngine == null)
            {
                return;
            }

            IReadOnlyList<Vector2Int> tiles = simEngine.RoundaboutTiles;

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

        private GameObject CreateRoundaboutVisual(Vector2Int tile)
        {
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = $"Roundabout_{tile.x}_{tile.y}";
            ring.transform.SetParent(signalRoot, false);
            ring.transform.localPosition = GridToLocal(tile, signalZ);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // 원반을 보드(XY)와 평행하게
            ring.transform.localScale = new Vector3(tileSize * 0.6f, 0.02f, tileSize * 0.6f);
            ApplyRendererColor(PrepareRenderer(ring.GetComponent<Renderer>()), roundaboutColor);
            return ring;
        }

        // 입체교차 마커: 위(가로)/아래(세로) 두 바로 "축 분리"를 암시 — 로터리와 동일 수명 규약.
        private void RefreshOverpasses()
        {
            if (simEngine == null)
            {
                return;
            }

            IReadOnlyList<Vector2Int> tiles = simEngine.OverpassTiles;

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
            if (simEngine == null)
            {
                return;
            }

            IReadOnlyList<Vector2Int> tiles = simEngine.OnewayTiles;

            for (int i = 0; i < tiles.Count; i++)
            {
                Vector2Int tile = tiles[i];

                if (!onewayVisuals.TryGetValue(tile, out GameObject visual))
                {
                    visual = CreateOnewayVisual(tile);
                    onewayVisuals.Add(tile, visual);
                }

                Vector2Int dir = simEngine.GetOnewayDir(tile);
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

        private SignalVisual CreateSignalVisual(Vector2Int tile)
        {
            GameObject root = signalPrefab != null
                ? Instantiate(signalPrefab, signalRoot)
                : new GameObject($"Signal_{tile.x}_{tile.y}");

            root.name = $"Signal_{tile.x}_{tile.y}";
            root.transform.SetParent(signalRoot, false);
            root.transform.localPosition = GridToLocal(tile, signalZ);

            Renderer horizontal = CreateSignalBar(root.transform, "Horizontal", new Vector3(tileSize * 0.42f, tileSize * 0.1f, 0.08f), Vector3.zero);
            Renderer vertical = CreateSignalBar(root.transform, "Vertical", new Vector3(tileSize * 0.1f, tileSize * 0.42f, 0.08f), Vector3.zero);
            Renderer selection = CreateSignalBar(root.transform, "Selection", new Vector3(tileSize * 0.72f, tileSize * 0.72f, 0.02f), new Vector3(0f, 0f, 0.02f));

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
            int visibleCount = Mathf.Min(maxMovingVehicles, routes.Count);
            EnsureVehicleCount(visibleCount);

            for (int i = 0; i < vehicles.Count; i++)
            {
                bool active = i < visibleCount && routes[i].Count > 1;
                vehicles[i].Object.SetActive(active);

                if (!active)
                {
                    if (vehicles[i].AngryMark != null)
                    {
                        vehicles[i].AngryMark.SetActive(false);
                        vehicles[i].SmokePuff.SetActive(false);
                    }
                    continue;
                }

                MoveVehicle(vehicles[i], routes[i], i);
            }
        }

        private void EnsureVehicleCount(int targetCount)
        {
            while (vehicles.Count < targetCount)
            {
                GameObject vehicle = InstantiatePrefabOrPrimitive(vehiclePrefab, PrimitiveType.Cube);
                vehicle.name = $"Vehicle_{vehicles.Count + 1}";
                vehicle.transform.SetParent(vehicleRoot, false);
                vehicle.transform.localScale = new Vector3(tileSize * 0.34f, tileSize * 0.16f, 0.12f);

                Renderer renderer = vehicle.GetComponentInChildren<Renderer>();
                PrepareRenderer(renderer);
                ApplyRendererColor(renderer, vehicleColor);

                vehicles.Add(new RouteVehicle
                {
                    Object = vehicle,
                    Renderer = renderer,
                    Phase = vehicles.Count * 0.618f
                });
            }
        }

        private void MoveVehicle(RouteVehicle vehicle, List<Vector2Int> route, int routeIndex)
        {
            int segmentCount = route.Count - 1;
            float speed = vehicleSpeed;

            if (segmentCount <= 0)
            {
                return;
            }

            Vector2Int currentTile = route[Mathf.Clamp(Mathf.FloorToInt(vehicle.Phase) % route.Count, 0, route.Count - 1)];
            speed *= Mathf.Lerp(1f, 0.25f, tileData.GetDensity01(currentTile));

            // 오버라이드 = 양축 초록이라 전방 신호가 오버라이드면 축 무관 가속이 정답(스펙 2026-07-11 §3).
            if (signalControl != null && TryGetNextSignalTile(route, vehicle.Phase, out _, out Vector2Int aheadSignal)
                && signalControl.GetOverrideSecondsLeft(aheadSignal) > 0f)
            {
                speed *= overrideSpeedMul;
            }

            bool blockedBySignal = IsRouteVehicleBlocked(route, vehicle.Phase);

            if (blockedBySignal)
            {
                speed = 0f;
            }

            // 차간 유지(MM식 추종): 같은 방향 바로 앞 차가 서 있으면 나도 정지 — 신호 앞 줄서기가 생김.
            if (speed > 0f && IsBlockedByLeader(vehicle))
            {
                speed = 0f;
            }

            vehicle.Phase = Mathf.Repeat(vehicle.Phase + Time.deltaTime * speed, segmentCount * 2f);

            float folded = Fold(vehicle.Phase, segmentCount);
            int index = Mathf.Clamp(Mathf.FloorToInt(folded), 0, segmentCount - 1);
            float t = folded - index;
            Vector3 a = GridToLocal(route[index], vehicleZ);
            Vector3 b = GridToLocal(route[index + 1], vehicleZ);
            Vector3 direction = (b - a).normalized;
            // 왕복 유령: 접힌 복귀 구간이면 실제 진행은 역방향 — 차선·바라보기 둘 다 이 방향 기준.
            bool forward = vehicle.Phase <= segmentCount;
            Vector3 travelDir = forward ? direction : -direction;

            // 우측통행 차선 오프셋: 진행 방향의 오른쪽으로 비껴 그림 → 왕복이 두 차선으로 갈라짐.
            Vector3 lane = new Vector3(travelDir.y, -travelDir.x, 0f) * (tileSize * laneOffset);
            Vector3 pos = Vector3.Lerp(a, b, t) + lane;

            // 로터리 연출(뷰 전용 — 엔진 무관): 타일 안에선 진행 방향 오른쪽으로 부풀어
            // 중앙 섬을 반시계로 돌아가는 궤적. 경계에서 0(직선과 연속), 중심에서 최대.
            Vector2Int insideTile = t < 0.5f ? route[index] : route[index + 1];
            if (IsRoundaboutTile(insideTile))
            {
                Vector3 center = GridToLocal(insideTile, vehicleZ);
                float along = Vector3.Dot(pos - center, travelDir);   // lane은 수직이라 영향 없음
                float bulge = Mathf.Cos(Mathf.PI * Mathf.Clamp(along / tileSize, -0.5f, 0.5f));
                float extra = Mathf.Max(0f, tileSize * (roundaboutOrbitRadius - laneOffset)) * bulge;
                pos += new Vector3(travelDir.y, -travelDir.x, 0f) * extra;
            }

            vehicle.Object.transform.localPosition = pos;

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
            if (!forward && simEngine != null)
            {
                Vector2Int onewayDir = simEngine.GetOnewayDir(insideTile);
                if (onewayDir != Vector2Int.zero)
                {
                    Vector3 onewayWorldDir = new Vector3(onewayDir.x, onewayDir.y, 0f);
                    hiddenAsGhost = Vector3.Dot(travelDir, onewayWorldDir) < -0.9f;
                }
            }

            if (vehicle.Renderer != null)
            {
                vehicle.Renderer.enabled = !hiddenAsGhost;
                Color routeColor = blockedBySignal ? Color.red : Color.HSVToRGB((routeIndex * 0.137f) % 1f, 0.7f, 0.95f);
                ApplyRendererColor(vehicle.Renderer, routeColor);
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
                    vehicle.AngryMark.transform.localPosition = basePos + new Vector3(0f, tileSize * 0.32f, -0.1f);
                    vehicle.AngryMark.transform.localScale = Vector3.one * pulse;
                    vehicle.SmokePuff.transform.localPosition = basePos - travelDir * (tileSize * 0.28f)
                        + new Vector3(0f, tileSize * 0.06f * Mathf.Sin(Time.time * 2f), 0f);
                }
            }

            vehicle.Pos = vehicle.Object.transform.localPosition;
            vehicle.Dir = travelDir;
        }

        // 반대 차선(마주 오는 차)은 무시 — 차선 오프셋으로 이미 분리. 같은 방향 추종만이라 순환 대기 없음
        // (SimDebug 렌더러의 데드락 근본수정과 같은 규약). 96대 전수 검사 = 프레임당 ~9천 회, 무해.
        private bool IsBlockedByLeader(RouteVehicle vehicle)
        {
            if (vehicle.Dir.sqrMagnitude < 0.001f)
            {
                return false;   // 첫 프레임(이력 없음)
            }

            float gap = tileSize * followGap;
            float gapSq = gap * gap;

            for (int i = 0; i < vehicles.Count; i++)
            {
                RouteVehicle other = vehicles[i];

                if (other == vehicle || !other.Object.activeSelf)
                {
                    continue;
                }

                Vector3 to = other.Pos - vehicle.Pos;

                if (to.sqrMagnitude > gapSq || to.sqrMagnitude < 0.0001f)
                {
                    continue;   // 멀거나, 완전 겹침(모호) — 겹침은 서로 못 막게 해 교착 방지
                }

                if (Vector3.Dot(vehicle.Dir, other.Dir) <= 0.5f)
                {
                    continue;   // 같은 방향만 추종
                }

                if (Vector3.Dot(vehicle.Dir, to.normalized) <= 0.6f)
                {
                    continue;   // 앞쪽에 있을 때만
                }

                return true;
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
            if (simEngine == null || !TryGetNextSignalTile(route, phase, out Vector2Int current, out Vector2Int next))
            {
                return false;
            }

            bool horizontal = current.y == next.y;
            return !simEngine.IsSignalGreen(next, horizontal);
        }

        // 차의 현재 위상에서 진행 방향의 현재/다음 타일. 다음 타일이 신호일 때만 true — 부스트·블록 판정 공용.
        private bool TryGetNextSignalTile(List<Vector2Int> route, float phase, out Vector2Int current, out Vector2Int next)
        {
            current = default;
            next = default;
            if (route == null || route.Count < 2) return false;
            int segmentCount = route.Count - 1;
            float cycle = segmentCount * 2f;
            float p = Mathf.Repeat(phase, cycle);
            bool forward = p <= segmentCount;
            float folded = forward ? p : cycle - p;
            int index = Mathf.Clamp(Mathf.FloorToInt(folded), 0, segmentCount - 1);
            current = forward ? route[index] : route[index + 1];
            next = forward ? route[index + 1] : route[index];
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
            if (simEngine == null)
            {
                return false;
            }

            return ContainsSignal(simEngine.RoundaboutTiles, tile);   // 선형 목록 검색 헬퍼 공용
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

                Vector3 world = Camera.main.ScreenToWorldPoint(mouse.position.ReadValue());
                Vector2Int clicked = WorldToGrid(world);

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

            if (keyboard.tabKey.wasPressedThisFrame)
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

        private void OnCongestionChanged(CongestionEvent e)
        {
            if (!tileVisuals.TryGetValue(e.Tile, out TileVisual visual))
            {
                return;
            }

            ApplyTileColor(e.Tile, visual);
        }

        private void OnFlowBurst(FlowBurstEvent e)
        {
            GameObject burst = InstantiatePrefabOrPrimitive(burstPrefab, PrimitiveType.Sphere);
            burst.name = $"FlowBurst_{e.Tile.x}_{e.Tile.y}";
            burst.transform.SetParent(effectRoot, false);
            burst.transform.localPosition = GridToLocal(e.Tile, -0.5f);
            burst.transform.localScale = Vector3.one * tileSize * 0.55f;
            ApplyRendererColor(burst.GetComponentInChildren<Renderer>(), flowBurstColor);

            bursts.Add(new BurstVisual
            {
                Object = burst,
                HideAt = Time.time + burstSeconds
            });

            // 동전 분수 + 음표(스펙 2026-07-12 §2): 길이 뚫리는 순간의 도파민 — 뷰 전용, Random 무방.
            Vector3 origin = GridToLocal(e.Tile, -0.5f);
            for (int i = 0; i < 6; i++)
            {
                GameObject coin = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                coin.name = "Coin";
                coin.transform.SetParent(effectRoot, false);
                coin.transform.localPosition = origin;
                coin.transform.localScale = Vector3.one * (tileSize * 0.1f);
                ApplyRendererColor(PrepareRenderer(coin.GetComponent<Renderer>()), coinColor);
                coins.Add(new CoinVisual
                {
                    Object = coin,
                    Velocity = new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(1.6f, 2.4f), 0f) * tileSize,
                    DieAt = Time.time + 0.9f,
                });
            }
            GameObject note = CreateTextMark(effectRoot, "♪", coinColor, tileSize * 0.16f);
            note.transform.localPosition = origin + new Vector3(0f, tileSize * 0.2f, 0f);
            notes.Add(new NoteVisual { Text = note.GetComponent<TextMesh>(), DieAt = Time.time + 1.1f });
        }

        private void UpdateBursts()
        {
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                BurstVisual burst = bursts[i];

                if (burst.Object == null)
                {
                    bursts.RemoveAt(i);
                    continue;
                }

                float remaining = Mathf.Clamp01((burst.HideAt - Time.time) / burstSeconds);
                burst.Object.transform.localScale = Vector3.one * Mathf.Lerp(tileSize * 1.1f, tileSize * 0.25f, remaining);

                if (Time.time < burst.HideAt)
                {
                    continue;
                }

                Destroy(burst.Object);
                bursts.RemoveAt(i);
            }
        }

        private void UpdateCoins()
        {
            for (int i = coins.Count - 1; i >= 0; i--)
            {
                CoinVisual coin = coins[i];
                if (coin.Object == null || Time.time >= coin.DieAt)
                {
                    if (coin.Object != null)
                    {
                        Destroy(coin.Object);
                    }
                    coins.RemoveAt(i);
                    continue;
                }
                coin.Velocity += Vector3.down * (6f * tileSize * Time.deltaTime);   // 간이 중력
                coin.Object.transform.localPosition += coin.Velocity * Time.deltaTime;
            }
        }

        private void UpdateNotes()
        {
            for (int i = notes.Count - 1; i >= 0; i--)
            {
                NoteVisual note = notes[i];
                if (note.Text == null || Time.time >= note.DieAt)
                {
                    if (note.Text != null)
                    {
                        Destroy(note.Text.gameObject);
                    }
                    notes.RemoveAt(i);
                    continue;
                }
                note.Text.transform.localPosition += Vector3.up * (0.8f * tileSize * Time.deltaTime);
                Color c = note.Text.color;
                c.a = Mathf.Clamp01((note.DieAt - Time.time) / 1.1f);
                note.Text.color = c;   // 폰트 머티리얼은 투명 지원
            }
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

            material.mainTexture = texture;

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
