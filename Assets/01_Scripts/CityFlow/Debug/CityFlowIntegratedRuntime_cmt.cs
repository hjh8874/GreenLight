using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Sim;
using CityFlow.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CityFlow.DebugTools
{
    // CityFlowIntegrated_cmt ?꾩슜 ?듯빀 寃利??꾧뎄.
    // ?ㅼ젣 UI ?낅젰 -> PlacementController -> SimEngine -> ????쇱옟/李⑤웾/HUD ?쇰뱶諛??먮쫫?????ъ뿉???뺤씤?쒕떎.
    public sealed class CityFlowIntegratedRuntime_cmt : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("Board")]
        [SerializeField] private int width = GridUtil.DefaultWidth;
        [SerializeField] private int height = GridUtil.DefaultHeight;
        [SerializeField] private float tileSize = 1f;
        [SerializeField] private float tileThickness = 0.08f;
        [SerializeField] private int maxVehiclesPerRoad = 5;
        [SerializeField] private int maxMovingVehicles = 80;
        [SerializeField] private int movingVehicleStride = 2;
        [SerializeField] private float movingVehicleSpeed = 2f;

        [Header("Colors")]
        [SerializeField] private Color emptyColor = new Color(0.18f, 0.2f, 0.18f);
        [SerializeField] private Color roadFreeColor = new Color(0.2f, 0.55f, 0.28f);
        [SerializeField] private Color roadSlowColor = new Color(1f, 0.75f, 0.2f);
        [SerializeField] private Color roadJamColor = new Color(0.95f, 0.25f, 0.2f);
        [SerializeField] private Color houseColor = new Color(0.25f, 0.55f, 1f);
        [SerializeField] private Color officeColor = new Color(1f, 0.55f, 0.15f);
        [SerializeField] private Color schoolColor = new Color(0.65f, 0.42f, 1f);

        private readonly Dictionary<Vector2Int, TileVisual> visuals = new Dictionary<Vector2Int, TileVisual>();
        private CityFlowServices services;
        private SimEngine simEngine;
        private PlacementController placementController;
        private Text statusText;
        private Text walletText;
        private Text efficiencyText;
        private TextMeshProUGUI signalInfoText;
        private readonly List<RouteVehicle> movingVehicles = new List<RouteVehicle>();
        private long coins;
        private float stability01 = 1f;
        private float hudRefreshTimer;
        private int selectedSignalIndex;
        private string currentMode = "Road";
        private string lastEvent = "Ready";

        private sealed class TileVisual
        {
            public GameObject TileObject;
            public Renderer Renderer;
            public MaterialPropertyBlock Block;
            public TileType Type;
            public GameObject SignalMarker;
            public Renderer SignalRenderer;
            public MaterialPropertyBlock SignalBlock;
            public readonly List<GameObject> Vehicles = new List<GameObject>();
        }

        private sealed class RouteVehicle
        {
            public GameObject Object;
            public float Phase;
        }

        public void Initialize(CityFlowServices services)
        {
            this.services = services;
            simEngine = services.Placement as SimEngine;

            ConfigureCamera();
            CreateBoard();
            CreatePlacementController();
            CreateUi();

            services.Events.Placed += OnPlaced;
            services.Events.CongestionChanged += OnCongestionChanged;
            services.Events.Arrival += OnArrival;
            services.Events.FlowBurst += OnFlowBurst;
            services.Events.StabilityChanged += OnStabilityChanged;

            RefreshHud();
            Debug.Log("[INTEGRATED] CityFlowIntegrated_cmt ?곌껐 ?꾨즺 - UI 諛곗튂 ?낅젰?쇰줈 SimEngine/?붾㈃ ?쇰뱶諛깆쓣 ?뺤씤?섏꽭??");
        }

        private void OnDestroy()
        {
            if (services == null)
            {
                return;
            }

            services.Events.Placed -= OnPlaced;
            services.Events.CongestionChanged -= OnCongestionChanged;
            services.Events.Arrival -= OnArrival;
            services.Events.FlowBurst -= OnFlowBurst;
            services.Events.StabilityChanged -= OnStabilityChanged;
        }

        private void Update()
        {
            if (services == null)
            {
                return;
            }

            stability01 = services.TileData.Stability01;

            foreach (KeyValuePair<Vector2Int, TileVisual> pair in visuals)
            {
                TileVisual visual = pair.Value;
                if (visual.Type != TileType.Road)
                {
                    SetVehicleCount(visual, 0);
                    continue;
                }

                ApplyTileColor(pair.Key, visual, services.TileData.GetCongestion(pair.Key));
                SetVehicleCount(visual, 0);
            }

            RefreshMovingVehicles();
            RefreshSignalMarkers();

            hudRefreshTimer += Time.deltaTime;
            if (hudRefreshTimer >= 0.2f)
            {
                hudRefreshTimer = 0f;
                RefreshHud();
            }
        }

        private void ConfigureCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
            }

            camera.orthographic = true;
            camera.orthographicSize = 12f;
            camera.transform.position = new Vector3((width - 1) * 0.5f, 18f, (height - 1) * 0.5f);
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.24f, 0.33f);
        }

        private void CreateBoard()
        {
            Transform existing = transform.Find("IntegratedBoard");
            if (existing != null)
            {
                Destroy(existing.gameObject);
            }

            GameObject root = new GameObject("IntegratedBoard");
            root.transform.SetParent(transform, false);

            Material material = CreateRuntimeMaterial();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector2Int tile = new Vector2Int(x, y);
                    GameObject tileObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tileObject.name = $"Tile_{x:00}_{y:00}_Empty";
                    tileObject.transform.SetParent(root.transform, false);
                    tileObject.transform.position = GridToWorld(tile);
                    tileObject.transform.localScale = new Vector3(tileSize * 0.96f, tileThickness, tileSize * 0.96f);

                    Renderer renderer = tileObject.GetComponent<Renderer>();
                    renderer.sharedMaterial = material;

                    TileVisual visual = new TileVisual
                    {
                        TileObject = tileObject,
                        Renderer = renderer,
                        Block = new MaterialPropertyBlock(),
                        Type = TileType.Empty
                    };

                    visuals[tile] = visual;
                    ApplyTileColor(tile, visual, CongestionLevel.Free);
                }
            }
        }

        private void CreatePlacementController()
        {
            GameObject controllerObject = new GameObject("IntegratedPlacementController");
            controllerObject.transform.SetParent(transform, false);
            placementController = controllerObject.AddComponent<PlacementController>();
            placementController.SetFakeMode(false);
            placementController.ConfigureGhost(CreateGhostRenderer(controllerObject.transform));
            placementController.Initialize(services);
            placementController.SetBuildType(TileType.Road);
            placementController.ToggleBuildMode(true);
        }

        private SpriteRenderer CreateGhostRenderer(Transform parent)
        {
            GameObject ghostObject = new GameObject("PlacementGhost");
            ghostObject.transform.SetParent(parent, false);
            ghostObject.transform.localScale = Vector3.one * 0.9f;

            SpriteRenderer renderer = ghostObject.AddComponent<SpriteRenderer>();
            renderer.sprite = CreateWhiteSprite();
            renderer.color = new Color(0f, 1f, 0f, 0.45f);
            renderer.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            return renderer;
        }

        private void CreateUi()
        {
            EnsureEventSystem();

            GameObject canvasObject = new GameObject("IntegratedUI");
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();

            statusText = CreateText(canvasObject.transform, "StatusText", new Vector2(16f, -16f), new Vector2(560f, 84f), 18);
            walletText = CreateText(canvasObject.transform, "WalletText", new Vector2(16f, -104f), new Vector2(260f, 34f), 18);
            efficiencyText = CreateText(canvasObject.transform, "EfficiencyText", new Vector2(16f, -142f), new Vector2(260f, 34f), 18);

            CreateButton(canvasObject.transform, "Road", new Vector2(16f, 16f), () => SetMode(TileType.Road, "Road"));
            CreateButton(canvasObject.transform, "House", new Vector2(116f, 16f), () => SetMode(TileType.House, "House"));
            CreateButton(canvasObject.transform, "Office", new Vector2(216f, 16f), () => SetMode(TileType.Office, "Office"));
            CreateButton(canvasObject.transform, "School", new Vector2(316f, 16f), () => SetMode(TileType.School, "School"));
            CreateButton(canvasObject.transform, "Remove", new Vector2(416f, 16f), () => SetMode(TileType.Empty, "Remove"));
            CreateButton(canvasObject.transform, "Select", new Vector2(516f, 16f), SetSelectMode);
            CreateTeamUi(canvasObject.transform);
        }

        private void CreateTeamUi(Transform canvas)
        {
            GameObject hudPanel = CreatePanel(canvas, "Geon_HUDDashboard", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -16f), new Vector2(260f, 172f));
            TextMeshProUGUI timeText = CreateTmpText(hudPanel.transform, "TimeText", "00:00", new Vector2(12f, -10f), new Vector2(112f, 24f), 18);
            TextMeshProUGUI vehicleText = CreateTmpText(hudPanel.transform, "VehicleCountText", "0", new Vector2(132f, -10f), new Vector2(112f, 24f), 18);
            TextMeshProUGUI coinText = CreateTmpText(hudPanel.transform, "CoinText", "0", new Vector2(12f, -42f), new Vector2(112f, 24f), 18);
            TextMeshProUGUI efficiency = CreateTmpText(hudPanel.transform, "EfficiencyText", "100%", new Vector2(132f, -42f), new Vector2(112f, 24f), 18);
            Slider congestionSlider = CreateSlider(hudPanel.transform, "CongestionSlider", new Vector2(12f, -82f), new Vector2(232f, 18f));
            Image congestionFill = CreateFillImage(hudPanel.transform, "CongestionFill", new Vector2(12f, -112f), new Vector2(232f, 18f));
            GameObject burstEffect = CreatePanel(hudPanel.transform, "FlowBurstEffect", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(184f, 26f), new Color(1f, 0.7f, 0.12f, 0.9f));
            CreateTmpText(burstEffect.transform, "Label", "FLOW BURST", Vector2.zero, new Vector2(184f, 26f), 16, TextAlignmentOptions.Center);
            burstEffect.SetActive(false);

            HUDDashboard dashboard = hudPanel.AddComponent<HUDDashboard>();
            dashboard.Configure(timeText, vehicleText, coinText, efficiency, congestionSlider, congestionFill, burstEffect);
            dashboard.Initialize(services);

            GameObject dock = CreatePanel(canvas, "Geon_UIDock", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(132f, 188f));
            Button btnBuild = CreateTmpButton(dock.transform, "Build", new Vector2(12f, -12f));
            Button btnResearch = CreateTmpButton(dock.transform, "Research", new Vector2(12f, -56f));
            Button btnStats = CreateTmpButton(dock.transform, "Stats", new Vector2(12f, -100f));
            Button btnSettings = CreateTmpButton(dock.transform, "Settings", new Vector2(12f, -144f));

            GameObject buildPanel = CreatePanel(canvas, "BuildPanel", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-16f, 212f), new Vector2(360f, 96f));
            Button buildRoad = CreateTmpButton(buildPanel.transform, "Road", new Vector2(12f, -12f), new Vector2(76f, 32f));
            Button buildHouse = CreateTmpButton(buildPanel.transform, "House", new Vector2(96f, -12f), new Vector2(76f, 32f));
            Button buildOffice = CreateTmpButton(buildPanel.transform, "Office", new Vector2(180f, -12f), new Vector2(76f, 32f));
            Button buildRemove = CreateTmpButton(buildPanel.transform, "Remove", new Vector2(264f, -12f), new Vector2(76f, 32f));
            Button buildSchool = CreateTmpButton(buildPanel.transform, "School*", new Vector2(12f, -52f), new Vector2(76f, 32f));
            buildSchool.onClick.AddListener(() => SetMode(TileType.School, "School"));
            BuildPanelController buildController = buildPanel.AddComponent<BuildPanelController>();
            buildController.Configure(placementController, buildRoad, buildHouse, buildOffice, buildRemove);

            GameObject researchPanel = CreatePanel(canvas, "ResearchPanel", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-16f, 212f), new Vector2(360f, 96f));
            Button upgradeSpeed = CreateTmpButton(researchPanel.transform, "Speed Up", new Vector2(12f, -12f), new Vector2(156f, 32f));
            Button reduceCooldown = CreateTmpButton(researchPanel.transform, "Cooldown", new Vector2(180f, -12f), new Vector2(156f, 32f));
            researchPanel.AddComponent<ResearchPanelController>().Configure(upgradeSpeed, reduceCooldown);
            CreateTmpText(researchPanel.transform, "Note", "Button log test only", new Vector2(12f, -56f), new Vector2(320f, 24f), 14);

            GameObject statsPanel = CreatePanel(canvas, "StatsPanel", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-16f, 212f), new Vector2(360f, 96f));
            TextMeshProUGUI jamCount = CreateTmpText(statsPanel.transform, "JamCount", "Jam Zones: 0", new Vector2(12f, -12f), new Vector2(320f, 24f), 16);
            TextMeshProUGUI cpm = CreateTmpText(statsPanel.transform, "CoinsPerMinute", "Income: 0/min", new Vector2(12f, -44f), new Vector2(320f, 24f), 16);
            StatsPanelController statsController = statsPanel.AddComponent<StatsPanelController>();
            statsController.Configure(jamCount, cpm);
            statsController.Initialize(services);

            GameObject settingsPanel = CreatePanel(canvas, "SettingsPanel", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-16f, 212f), new Vector2(360f, 96f));
            Toggle muteToggle = CreateToggle(settingsPanel.transform, "Mute", new Vector2(12f, -12f));
            Button quitButton = CreateTmpButton(settingsPanel.transform, "Stop Play", new Vector2(180f, -12f), new Vector2(156f, 32f));
            muteToggle.onValueChanged.AddListener(isMuted => AudioListener.volume = isMuted ? 0f : 1f);
            quitButton.onClick.AddListener(StopPlayMode);
            settingsPanel.AddComponent<SettingsPanelController>().Configure(muteToggle, quitButton);

            GameObject analysisPanel = CreatePanel(canvas, "AnalysisCard", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, -60f), new Vector2(260f, 246f));
            TextMeshProUGUI title = CreateTmpText(analysisPanel.transform, "Title", "Tile", new Vector2(12f, -12f), new Vector2(232f, 24f), 18);
            TextMeshProUGUI coord = CreateTmpText(analysisPanel.transform, "Coord", "Tile: -", new Vector2(12f, -42f), new Vector2(232f, 22f), 15);
            TextMeshProUGUI vehicleId = CreateTmpText(analysisPanel.transform, "VehicleId", "ID", new Vector2(12f, -68f), new Vector2(232f, 22f), 15);
            TextMeshProUGUI vehicleType = CreateTmpText(analysisPanel.transform, "VehicleType", "Type", new Vector2(12f, -94f), new Vector2(232f, 22f), 15);
            TextMeshProUGUI wait = CreateTmpText(analysisPanel.transform, "Wait", "0.0s", new Vector2(12f, -120f), new Vector2(232f, 22f), 15);
            Image localCongestion = CreateFillImage(analysisPanel.transform, "LocalCongestionFill", new Vector2(12f, -150f), new Vector2(232f, 18f));
            Button resolve = CreateTmpButton(analysisPanel.transform, "Resolve Jam", new Vector2(12f, -182f), new Vector2(108f, 32f));
            Button upgrade = CreateTmpButton(analysisPanel.transform, "Upgrade", new Vector2(136f, -182f), new Vector2(108f, 32f));
            AnalysisCardController analysis = analysisPanel.AddComponent<AnalysisCardController>();
            analysis.Configure(title, coord, vehicleId, vehicleType, wait, localCongestion, resolve, upgrade, false);
            analysis.Initialize(services);

            GameObject highlight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            highlight.name = "SelectedTileHighlight";
            highlight.transform.SetParent(transform, false);
            highlight.transform.localScale = new Vector3(tileSize, 0.12f, tileSize);
            highlight.GetComponent<Renderer>().material.color = new Color(1f, 1f, 1f, 0.55f);

            TileSelectionController selection = gameObject.AddComponent<TileSelectionController>();
            selection.Configure(analysis, placementController, highlight);

            GameObject signalPanel = CreatePanel(canvas, "SignalPanel", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -188f), new Vector2(360f, 126f));
            signalInfoText = CreateTmpText(signalPanel.transform, "SignalInfo", "Signal: none", new Vector2(12f, -12f), new Vector2(336f, 42f), 15);
            CreateTmpButton(signalPanel.transform, "Next", new Vector2(12f, -66f), new Vector2(76f, 32f)).onClick.AddListener(SelectNextSignal);
            CreateTmpButton(signalPanel.transform, "Offset -", new Vector2(96f, -66f), new Vector2(76f, 32f)).onClick.AddListener(() => AddSignalOffset(-1));
            CreateTmpButton(signalPanel.transform, "Offset +", new Vector2(180f, -66f), new Vector2(76f, 32f)).onClick.AddListener(() => AddSignalOffset(1));
            CreateTmpButton(signalPanel.transform, "Reset", new Vector2(264f, -66f), new Vector2(76f, 32f)).onClick.AddListener(ResetSignalOffsets);

            UIDockController dockController = dock.AddComponent<UIDockController>();
            dockController.Configure(btnBuild, btnResearch, btnStats, btnSettings, buildPanel, researchPanel, statsPanel, settingsPanel, placementController);
            buildPanel.SetActive(false);
            researchPanel.SetActive(false);
            statsPanel.SetActive(false);
            settingsPanel.SetActive(false);
        }

        private Text CreateText(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, int fontSize)
        {
            GameObject textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);

            Text text = textObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (text.font == null)
            {
                text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;

            RectTransform rect = text.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return text;
        }

        private void CreateButton(Transform parent, string label, Vector2 anchoredPosition, UnityEngine.Events.UnityAction action)
        {
            GameObject buttonObject = new GameObject($"{label}Button");
            buttonObject.transform.SetParent(parent, false);

            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.12f, 0.14f, 0.16f, 0.92f);

            Button button = buttonObject.AddComponent<Button>();
            button.onClick.AddListener(action);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(88f, 36f);

            Text text = CreateText(buttonObject.transform, "Label", Vector2.zero, rect.sizeDelta, 16);
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = Vector2.zero;
            text.text = label;
        }

        private void SetMode(TileType type, string modeName)
        {
            placementController.SetBuildType(type);
            placementController.ToggleBuildMode(true);
            currentMode = modeName;
            lastEvent = $"Mode: {modeName}";
            RefreshHud();
        }

        private void SetSelectMode()
        {
            placementController.ToggleBuildMode(false);
            currentMode = "Select";
            lastEvent = "Mode: Select tile";
            RefreshHud();
        }

        private void StopPlayMode()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnPlaced(PlacedEvent placed)
        {
            if (!visuals.TryGetValue(placed.Tile, out TileVisual visual))
            {
                return;
            }

            visual.Type = placed.IsRemove ? TileType.Empty : placed.Type;
            visual.TileObject.name = $"Tile_{placed.Tile.x:00}_{placed.Tile.y:00}_{visual.Type}";
            SetVehicleCount(visual, visual.Type == TileType.Road ? visual.Vehicles.Count : 0);
            ApplyTileColor(placed.Tile, visual, services.TileData.GetCongestion(placed.Tile));
            lastEvent = placed.IsRemove
                ? $"Removed {placed.Tile}"
                : $"Placed {placed.Type} {placed.Tile}";
            RefreshHud();
        }

        private void OnCongestionChanged(CongestionEvent e)
        {
            if (visuals.TryGetValue(e.Tile, out TileVisual visual))
            {
                ApplyTileColor(e.Tile, visual, e.Level);
            }

            lastEvent = $"Congestion {e.Tile}: {e.Level}";
            RefreshHud();
        }

        private void OnArrival(ArrivalEvent e)
        {
            coins += e.Coins;
            lastEvent = $"Arrival {e.Destination} +{e.Coins}";
            RefreshHud();
        }

        private void OnFlowBurst(FlowBurstEvent e)
        {
            coins += e.Reward;
            lastEvent = $"FlowBurst {e.Tile} +{e.Reward}";
            RefreshHud();
        }

        private void OnStabilityChanged(StabilityEvent e)
        {
            stability01 = e.Stability01;
            RefreshHud();
        }

        private void RefreshHud()
        {
            if (statusText != null)
            {
                statusText.text =
                    $"CityFlowIntegrated_cmt\nMode: {currentMode}\nLast: {lastEvent}";
            }

            if (walletText != null)
            {
                walletText.text = $"Coins: {coins}";
            }

            if (efficiencyText != null)
            {
                efficiencyText.text = $"Stability: {Mathf.RoundToInt(stability01 * 100f)}%";
            }
        }

        private void ApplyTileColor(Vector2Int tile, TileVisual visual, CongestionLevel congestion)
        {
            Color color = visual.Type switch
            {
                TileType.Road => congestion switch
                {
                    CongestionLevel.Jam => roadJamColor,
                    CongestionLevel.Slow => roadSlowColor,
                    _ => roadFreeColor
                },
                TileType.House => houseColor,
                TileType.Office => officeColor,
                TileType.School => schoolColor,
                _ => emptyColor
            };

            visual.Renderer.GetPropertyBlock(visual.Block);
            visual.Block.SetColor("_BaseColor", color);
            visual.Block.SetColor("_Color", color);
            visual.Renderer.SetPropertyBlock(visual.Block);
        }

        private void SetVehicleCount(TileVisual visual, int count)
        {
            count = Mathf.Clamp(count, 0, maxVehiclesPerRoad);
            while (visual.Vehicles.Count < count)
            {
                GameObject vehicle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                vehicle.name = "DensityVehicle";
                vehicle.transform.SetParent(visual.TileObject.transform, false);
                vehicle.transform.localScale = new Vector3(0.14f, 0.18f, 0.28f);
                Renderer renderer = vehicle.GetComponent<Renderer>();
                renderer.sharedMaterial = CreateRuntimeMaterial();
                renderer.material.color = Color.black;
                visual.Vehicles.Add(vehicle);
            }

            for (int i = 0; i < visual.Vehicles.Count; i++)
            {
                bool active = i < count;
                GameObject vehicle = visual.Vehicles[i];
                vehicle.SetActive(active);
                if (active)
                {
                    float offset = (i - (count - 1) * 0.5f) * 0.18f;
                    vehicle.transform.localPosition = new Vector3(offset, 0.8f, 0f);
                }
            }
        }

        private void RefreshMovingVehicles()
        {
            if (simEngine == null)
            {
                SetActiveMovingVehicles(0);
                return;
            }

            IReadOnlyList<List<Vector2Int>> routes = simEngine.ActiveRoutes;
            int routeCount = routes.Count;
            int visibleCount = 0;

            for (int routeIndex = 0; routeIndex < routeCount && visibleCount < maxMovingVehicles; routeIndex++)
            {
                if (movingVehicleStride > 1 && routeIndex % movingVehicleStride != 0)
                {
                    continue;
                }

                List<Vector2Int> route = routes[routeIndex];
                if (route == null || route.Count < 2)
                {
                    continue;
                }

                EnsureMovingVehicle(visibleCount);
                RouteVehicle vehicle = movingVehicles[visibleCount];
                vehicle.Object.SetActive(true);

                MoveRouteVehicle(vehicle, route, routeIndex);
                visibleCount++;
            }

            SetActiveMovingVehicles(visibleCount);
        }

        private void EnsureMovingVehicle(int index)
        {
            while (movingVehicles.Count <= index)
            {
                GameObject vehicleObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                vehicleObject.name = "MovingRouteVehicle";
                vehicleObject.transform.SetParent(transform, false);
                vehicleObject.transform.localScale = new Vector3(0.18f, 0.16f, 0.38f);

                Renderer renderer = vehicleObject.GetComponent<Renderer>();
                renderer.sharedMaterial = CreateRuntimeMaterial();
                renderer.material.color = Color.black;

                movingVehicles.Add(new RouteVehicle
                {
                    Object = vehicleObject,
                    Phase = movingVehicles.Count * 0.618f
                });
            }
        }

        private void MoveRouteVehicle(RouteVehicle vehicle, List<Vector2Int> route, int routeIndex)
        {
            int segmentCount = route.Count - 1;
            int cycle = segmentCount * 2;
            float folded = Fold(vehicle.Phase, segmentCount, cycle);
            int tileIndex = Mathf.Clamp((int)folded, 0, segmentCount - 1);

            Vector2Int currentTile = route[tileIndex];
            float density = services.TileData.GetDensity01(currentTile);
            float speed = movingVehicleSpeed * Mathf.Lerp(1f, 0.25f, density);
            if (IsRouteVehicleBlocked(route, vehicle.Phase))
            {
                speed = 0f;
            }

            vehicle.Phase += speed * Time.deltaTime;

            RoutePose pose = GetRoutePose(route, vehicle.Phase, routeIndex);
            vehicle.Object.transform.position = new Vector3(pose.Position.x, 0.34f, pose.Position.y);
            if (pose.Direction.sqrMagnitude > 0.0001f)
            {
                float angle = Mathf.Atan2(pose.Direction.x, pose.Direction.y) * Mathf.Rad2Deg;
                vehicle.Object.transform.rotation = Quaternion.Euler(0f, angle, 0f);
            }
        }

        private bool IsRouteVehicleBlocked(List<Vector2Int> route, float phase)
        {
            if (simEngine == null || route == null || route.Count < 2)
            {
                return false;
            }

            int segmentCount = route.Count - 1;
            int cycle = segmentCount * 2;
            float folded = Fold(phase, segmentCount, cycle);
            int tileIndex = Mathf.Clamp((int)folded, 0, segmentCount - 1);
            Vector2Int current = route[tileIndex];
            Vector2Int next = route[Mathf.Min(tileIndex + 1, route.Count - 1)];

            if (current == next)
            {
                return false;
            }

            bool horizontal = current.y == next.y;
            if (!IsSignalTile(next))
            {
                return false;
            }

            return !simEngine.IsSignalGreen(next, horizontal);
        }

        private bool IsSignalTile(Vector2Int tile)
        {
            if (simEngine == null)
            {
                return false;
            }

            IReadOnlyList<Vector2Int> signalTiles = simEngine.SignalTiles;
            for (int i = 0; i < signalTiles.Count; i++)
            {
                if (signalTiles[i] == tile)
                {
                    return true;
                }
            }

            return false;
        }

        private void SetActiveMovingVehicles(int activeCount)
        {
            for (int i = activeCount; i < movingVehicles.Count; i++)
            {
                movingVehicles[i].Object.SetActive(false);
            }
        }

        private struct RoutePose
        {
            public Vector2 Position;
            public Vector2 Direction;
        }

        private RoutePose GetRoutePose(List<Vector2Int> route, float phase, int routeIndex)
        {
            int segmentCount = route.Count - 1;
            int cycle = segmentCount * 2;

            float q = phase % cycle;
            if (q < 0)
            {
                q += cycle;
            }

            bool forward = q <= segmentCount;
            float folded = Fold(phase, segmentCount, cycle);
            int index = Mathf.Clamp((int)folded, 0, segmentCount - 1);
            float t = folded - index;

            Vector2 a = new Vector2(route[index].x * tileSize, route[index].y * tileSize);
            Vector2 b = new Vector2(route[index + 1].x * tileSize, route[index + 1].y * tileSize);
            Vector2 direction = ((b - a) * (forward ? 1f : -1f)).normalized;
            Vector2 lane = new Vector2(direction.y, -direction.x) * (0.12f + (routeIndex % 2) * 0.08f);

            return new RoutePose
            {
                Position = Vector2.Lerp(a, b, t) + lane,
                Direction = direction
            };
        }

        private static float Fold(float phase, int segmentCount, int cycle)
        {
            float q = phase % cycle;
            if (q < 0)
            {
                q += cycle;
            }

            return q <= segmentCount ? q : cycle - q;
        }

        private void RefreshSignalMarkers()
        {
            if (simEngine == null)
            {
                return;
            }

            IReadOnlyList<Vector2Int> signalTiles = simEngine.SignalTiles;
            foreach (KeyValuePair<Vector2Int, TileVisual> pair in visuals)
            {
                if (pair.Value.SignalMarker != null)
                {
                    pair.Value.SignalMarker.SetActive(false);
                }
            }

            if (signalTiles.Count == 0)
            {
                selectedSignalIndex = 0;
                UpdateSignalInfo();
                return;
            }

            if (selectedSignalIndex >= signalTiles.Count)
            {
                selectedSignalIndex = 0;
            }

            for (int i = 0; i < signalTiles.Count; i++)
            {
                Vector2Int tile = signalTiles[i];
                if (!visuals.TryGetValue(tile, out TileVisual visual))
                {
                    continue;
                }

                EnsureSignalMarker(visual);
                visual.SignalMarker.SetActive(true);
                visual.SignalMarker.transform.localScale = i == selectedSignalIndex
                    ? new Vector3(0.34f, 0.34f, 0.34f)
                    : new Vector3(0.24f, 0.24f, 0.24f);

                SignalPhase horizontal = simEngine.GetSignalPhase(tile, true);
                SignalPhase vertical = simEngine.GetSignalPhase(tile, false);
                SignalPhase phase = horizontal == SignalPhase.Green || vertical == SignalPhase.Green
                    ? SignalPhase.Green
                    : horizontal == SignalPhase.Yellow || vertical == SignalPhase.Yellow
                        ? SignalPhase.Yellow
                        : SignalPhase.Red;

                Color color = phase switch
                {
                    SignalPhase.Green => Color.green,
                    SignalPhase.Yellow => Color.yellow,
                    _ => Color.red
                };

                visual.SignalRenderer.GetPropertyBlock(visual.SignalBlock);
                visual.SignalBlock.SetColor("_BaseColor", color);
                visual.SignalBlock.SetColor("_Color", color);
                visual.SignalRenderer.SetPropertyBlock(visual.SignalBlock);
            }

            UpdateSignalInfo();
        }

        private void EnsureSignalMarker(TileVisual visual)
        {
            if (visual.SignalMarker != null)
            {
                return;
            }

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "SignalMarker";
            marker.transform.SetParent(visual.TileObject.transform, false);
            marker.transform.localPosition = new Vector3(0f, 1.45f, 0f);
            marker.transform.localScale = new Vector3(0.24f, 0.24f, 0.24f);

            visual.SignalMarker = marker;
            visual.SignalRenderer = marker.GetComponent<Renderer>();
            visual.SignalRenderer.sharedMaterial = CreateRuntimeMaterial();
            visual.SignalBlock = new MaterialPropertyBlock();
        }

        private void SelectNextSignal()
        {
            if (simEngine == null || simEngine.SignalTiles.Count == 0)
            {
                lastEvent = "Signal: none";
                RefreshHud();
                return;
            }

            selectedSignalIndex = (selectedSignalIndex + 1) % simEngine.SignalTiles.Count;
            Vector2Int tile = simEngine.SignalTiles[selectedSignalIndex];
            lastEvent = $"Signal selected {tile}";
            RefreshHud();
        }

        private void AddSignalOffset(int delta)
        {
            if (simEngine == null || simEngine.SignalTiles.Count == 0)
            {
                return;
            }

            if (selectedSignalIndex >= simEngine.SignalTiles.Count)
            {
                selectedSignalIndex = 0;
            }

            Vector2Int tile = simEngine.SignalTiles[selectedSignalIndex];
            int offset = simEngine.GetSignalOffsetSlots(tile);
            simEngine.TrySetSignalOffsetSlots(tile, offset + delta);
            lastEvent = $"Signal offset {tile}: {simEngine.GetSignalOffsetSlots(tile)}";
            RefreshHud();
        }

        private void ResetSignalOffsets()
        {
            if (simEngine == null)
            {
                return;
            }

            foreach (Vector2Int tile in simEngine.SignalTiles)
            {
                simEngine.TrySetSignalOffsetSlots(tile, 0);
            }

            lastEvent = "Signal offsets reset";
            RefreshHud();
        }

        private void UpdateSignalInfo()
        {
            if (signalInfoText == null)
            {
                return;
            }

            if (simEngine == null)
            {
                signalInfoText.text = "Signal: real SimEngine only";
                return;
            }

            IReadOnlyList<Vector2Int> tiles = simEngine.SignalTiles;
            if (tiles.Count == 0)
            {
                signalInfoText.text = "Signal: create an intersection";
                return;
            }

            if (selectedSignalIndex >= tiles.Count)
            {
                selectedSignalIndex = 0;
            }

            Vector2Int tile = tiles[selectedSignalIndex];
            signalInfoText.text =
                $"Signal {selectedSignalIndex + 1}/{tiles.Count} {tile}\nOffset: {simEngine.GetSignalOffsetSlots(tile)}";
        }

        private Vector3 GridToWorld(Vector2Int tile)
        {
            return new Vector3(tile.x * tileSize, 0f, tile.y * tileSize);
        }

        private GameObject CreatePanel(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            return CreatePanel(parent, name, anchorMin, anchorMax, pivot, anchoredPosition, size, new Color(0.08f, 0.1f, 0.12f, 0.9f));
        }

        private GameObject CreatePanel(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 size,
            Color color)
        {
            GameObject panel = new GameObject(name);
            panel.transform.SetParent(parent, false);
            Image image = panel.AddComponent<Image>();
            image.color = color;

            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return panel;
        }

        private TextMeshProUGUI CreateTmpText(
            Transform parent,
            string name,
            string value,
            Vector2 anchoredPosition,
            Vector2 size,
            int fontSize,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left)
        {
            GameObject textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);

            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = alignment;
            text.raycastTarget = false;

            RectTransform rect = text.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return text;
        }

        private Button CreateTmpButton(Transform parent, string label, Vector2 anchoredPosition)
        {
            return CreateTmpButton(parent, label, anchoredPosition, new Vector2(108f, 32f));
        }

        private Button CreateTmpButton(Transform parent, string label, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject buttonObject = new GameObject($"{label}Button");
            buttonObject.transform.SetParent(parent, false);

            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.14f, 0.17f, 0.2f, 0.96f);

            Button button = buttonObject.AddComponent<Button>();

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            TextMeshProUGUI text = CreateTmpText(buttonObject.transform, "Label", label, Vector2.zero, size, 14, TextAlignmentOptions.Center);
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = Vector2.zero;
            return button;
        }

        private Slider CreateSlider(Transform parent, string name, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject root = CreatePanel(parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), anchoredPosition, size, new Color(0.08f, 0.09f, 0.1f, 0.95f));
            Slider slider = root.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.interactable = false;

            GameObject fill = CreatePanel(root.transform, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Vector2.zero, Color.red);
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            slider.fillRect = fillRect;
            slider.targetGraphic = fill.GetComponent<Image>();
            return slider;
        }

        private Image CreateFillImage(Transform parent, string name, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject background = CreatePanel(parent, $"{name}Background", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), anchoredPosition, size, new Color(0.08f, 0.09f, 0.1f, 0.95f));
            GameObject fill = CreatePanel(background.transform, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Vector2.zero, Color.green);
            RectTransform rect = fill.GetComponent<RectTransform>();
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = fill.GetComponent<Image>();
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillAmount = 0f;
            return image;
        }

        private Toggle CreateToggle(Transform parent, string label, Vector2 anchoredPosition)
        {
            GameObject toggleObject = CreatePanel(parent, $"{label}Toggle", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), anchoredPosition, new Vector2(144f, 32f), new Color(0.14f, 0.17f, 0.2f, 0.96f));
            Toggle toggle = toggleObject.AddComponent<Toggle>();

            GameObject checkmark = CreatePanel(toggleObject.transform, "Checkmark", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(18f, 18f), Color.green);
            toggle.graphic = checkmark.GetComponent<Image>();
            toggle.targetGraphic = toggleObject.GetComponent<Image>();
            CreateTmpText(toggleObject.transform, "Label", label, new Vector2(36f, -6f), new Vector2(96f, 22f), 14);
            return toggle;
        }

        private static Material CreateRuntimeMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            return new Material(shader);
        }

        private static Sprite CreateWhiteSprite()
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            GameObject eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
        }
    }
}
