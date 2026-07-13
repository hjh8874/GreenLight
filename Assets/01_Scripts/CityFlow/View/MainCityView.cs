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

        private readonly Dictionary<Vector2Int, TileVisual> tileVisuals = new();
        private readonly Dictionary<Vector2Int, SignalVisual> signalVisuals = new();
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
        }

        private sealed class BurstVisual
        {
            public GameObject Object;
            public float HideAt;
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

            services.Events.Placed += OnPlaced;
            services.Events.CongestionChanged += OnCongestionChanged;
            services.Events.FlowBurst += OnFlowBurst;

            BuildRoots();
            BuildBoard();
            BuildGridLines();
            RefreshAllTiles();
            RefreshSignals();
            RefreshVehicles();
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
            RefreshVehicles();
            UpdateBursts();
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

            // 오버라이드 라인 가속: 이 차가 향하는 다음 신호가 오버라이드 중이면 시각 속도↑(순수 연출).
            // 주의: 전방 신호가 오버라이드여도 이 차의 축이 적색(수직축)일 수 있음 — 그 경우는
            // 바로 아래 blockedBySignal 체크가 speed=0으로 덮어 교정(이 순서가 깨지면 오판정).
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

            vehicle.Phase = Mathf.Repeat(vehicle.Phase + Time.deltaTime * speed, segmentCount * 2f);

            float folded = Fold(vehicle.Phase, segmentCount);
            int index = Mathf.Clamp(Mathf.FloorToInt(folded), 0, segmentCount - 1);
            float t = folded - index;
            Vector3 a = GridToLocal(route[index], vehicleZ);
            Vector3 b = GridToLocal(route[index + 1], vehicleZ);
            Vector3 direction = (b - a).normalized;

            vehicle.Object.transform.localPosition = Vector3.Lerp(a, b, t);

            if (direction.sqrMagnitude > 0.001f)
            {
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                vehicle.Object.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            }

            if (vehicle.Renderer != null)
            {
                Color routeColor = blockedBySignal ? Color.red : Color.HSVToRGB((routeIndex * 0.137f) % 1f, 0.7f, 0.95f);
                ApplyRendererColor(vehicle.Renderer, routeColor);
            }
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
