// Unity setup: This component is prewired in SpecialBuildingSystem.prefab.
using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Gameplay.Research;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    // 연구 패널 — 카테고리를 열, 연구 순서를 행으로 하는 상→하 목록.
    public sealed partial class ResearchPanelController : MonoBehaviour, ICityFlowServiceConsumer
    {
        private const float CellWidth = 220f;
        private const float CellHeight = 108f;
        private const float ColumnGap = 14f;
        private const float RowGap = 12f;
        private const float ConnectorThickness = 4f;
        // HeaderHeight includes the summary, category tabs, lane labels, and
        // breathing room before the first card. Keeping those regions inside
        // one anchored band prevents text and cards from overlapping when the
        // panel is shown at different Canvas scales.
        private const float HeaderHeight = 160f;
        private const float PanelPadding = 16f;

        [SerializeField] private ResearchCatalogSO catalog;
        [SerializeField] private GameObject rowTemplate;
        [SerializeField] private TMP_Text yesterdayArrivalsText;
        [SerializeField] private TMP_Text populationText;
        [SerializeField] private TMP_Text unlockProgressText;

        internal sealed class Row
        {
            public ResearchEntry Entry;
            public GameObject Instance;
            public TMP_Text NameText;
            public TMP_Text ProgressText;
            public TMP_Text StateText;
            public TMP_Text CategoryText;
            public GameObject IconBadge;
            public Image IconImage;
            public Image AccentImage;
            public Image StateBadgeImage;
            public bool IsUnlocked;
            public bool IsReady;
            public bool IsResearching;
            public int Depth;
            public int Branch;
        }

        private readonly List<Row> rows = new();
        private readonly List<GameObject> connectors = new();
        private CityFlowServices services;
        private IResearchUnlockService research;
        private IEconomyService economy;
        private ISpecialBuildingService specialBuildings;
        private UIDockController dockController;
        private bool warnedMissingResearch;

        internal IReadOnlyList<Row> RowsForTest => rows;

        public void Initialize(CityFlowServices cityServices)
        {
            EnsureInputRaycaster();
            AutoWireSceneIntegration();
            Unbind();
            services = cityServices;
            if (services == null) return;
            if (catalog == null) catalog = ResearchCatalogSO.LoadDefault();
            EnsureCatalogPresentation();

            BindResearch(services.Research);
            BindEconomy(services.Economy);
            BindSpecialBuildings(services.SpecialBuildings);
            services.ResearchRegistered += BindResearch;
            services.EconomyRegistered += BindEconomy;
            services.SpecialBuildingsRegistered += BindSpecialBuildings;
            if (research == null && !warnedMissingResearch)
            {
                warnedMissingResearch = true;
                Debug.LogWarning(
                    "[ResearchPanelController] Research 서비스가 아직 없다 — " +
                    "등록될 때까지 전부 잠김으로 표시한다.", this);
            }

            BuildRows();
            InitializeCatalogPresentation();
            RefreshAll();
        }

        private void AutoWireSceneIntegration()
        {
            // 구 씬 패널은 현재 프리팹의 catalog/rowTemplate가 없는 이전 직렬화 형태다.
            // 해당 패널이 bootstrap 초기화 순서에서 새 연결을 되돌리지 않게 건너뛴다.
            if (catalog == null || rowTemplate == null) return;

            dockController = FindAnyObjectByType<UIDockController>(FindObjectsInactive.Include);
            if (dockController != null)
            {
                dockController.RebindResearchPanel(gameObject);
            }

            ResearchPanelController[] panels = FindObjectsByType<ResearchPanelController>(
                FindObjectsInactive.Include);
            int disabledCount = 0;
            for (int i = 0; i < panels.Length; i++)
            {
                ResearchPanelController panel = panels[i];
                if (panel == this || panel == null) continue;
                panel.gameObject.SetActive(false);
                disabledCount++;
            }

            if (disabledCount > 0)
            {
                Debug.LogWarning(
                    $"[ResearchPanelController] 구 연구 패널 {disabledCount}개를 비활성화했다.",
                    this);
            }
        }

        private void OnEnable()
        {
            EnsureInputRaycaster();
            RefreshAll();
        }

        private void EnsureInputRaycaster()
        {
            Canvas canvas = GetComponentInParent<Canvas>(true);
            if (canvas != null && canvas.GetComponent<GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        private void OnDestroy()
        {
            ReleaseCatalogPresentation();
            ReleaseCatalogStyleResources();
            Unbind();
        }

        private void Unbind()
        {
            if (services != null)
            {
                services.ResearchRegistered -= BindResearch;
                services.EconomyRegistered -= BindEconomy;
                services.SpecialBuildingsRegistered -= BindSpecialBuildings;
            }
            if (research != null)
            {
                research.ResearchUnlocked -= OnResearchUnlocked;
                research.ResearchProgressChanged -= RefreshAll;
                research.ResearchStateRestored -= RefreshAll;
                research = null;
            }
            BindEconomy(null);
            BindSpecialBuildings(null);
            services = null;
        }

        private void BindResearch(IResearchUnlockService service)
        {
            if (service == null || ReferenceEquals(research, service)) return;
            if (research != null)
            {
                research.ResearchUnlocked -= OnResearchUnlocked;
                research.ResearchProgressChanged -= RefreshAll;
                research.ResearchStateRestored -= RefreshAll;
            }
            research = service;
            research.ResearchUnlocked += OnResearchUnlocked;
            research.ResearchProgressChanged += RefreshAll;
            research.ResearchStateRestored += RefreshAll;
            RefreshAll();
        }

        private void BindEconomy(IEconomyService service)
        {
            if (ReferenceEquals(economy, service)) return;
            if (economy != null) economy.CoinsChanged -= OnCoinsChanged;
            economy = service;
            if (economy != null) economy.CoinsChanged += OnCoinsChanged;
            RefreshAll();
        }

        private void OnCoinsChanged(long _) => RefreshAll();
        private void OnResearchUnlocked(string _) => RefreshAll();

        private void BindSpecialBuildings(ISpecialBuildingService service)
        {
            if (ReferenceEquals(specialBuildings, service)) return;
            if (specialBuildings != null)
            {
                specialBuildings.BuildOptionsChanged -=
                    RefreshResearchIcons;
            }

            specialBuildings = service;
            if (specialBuildings != null)
            {
                specialBuildings.BuildOptionsChanged +=
                    RefreshResearchIcons;
            }
            RefreshResearchIcons();
        }

        private void BuildRows()
        {
            DisableParentLayoutGroup();
            for (int i = 0; i < rows.Count; i++) DestroyObject(rows[i].Instance);
            for (int i = 0; i < connectors.Count; i++) DestroyObject(connectors[i]);
            rows.Clear();
            connectors.Clear();
            if (catalog == null || rowTemplate == null) return;

            List<ResearchEntry> entries = catalog.ValidEntries();
            var byId = new Dictionary<string, ResearchEntry>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
                byId[Normalize(entries[i].researchId)] = entries[i];

            var roots = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                int branch = GetBranch(entries[i], byId, roots);
                int depth = GetDepth(entries[i], byId, new HashSet<string>(StringComparer.Ordinal));
                GameObject instance = Instantiate(rowTemplate, rowTemplate.transform.parent);
                instance.name = entries[i].researchId;
                instance.SetActive(true);
                Row row = new Row
                {
                    Entry = entries[i],
                    Instance = instance,
                    NameText = FindText(instance, "Name"),
                    ProgressText = FindText(instance, "Progress"),
                    StateText = FindText(instance, "State"),
                    Depth = depth,
                    Branch = branch,
                };
                // 노드 카드 배경 — 상태색은 RefreshAll이 칠한다
                Image card = instance.GetComponent<Image>() ?? instance.AddComponent<Image>();
                ApplyRoundedSurface(card);
                Button button = instance.GetComponent<Button>() ?? instance.AddComponent<Button>();
                button.targetGraphic = card;
                Outline outline = instance.GetComponent<Outline>() ?? instance.AddComponent<Outline>();
                outline.effectColor = new Color(0.55f, 0.62f, 0.72f, 0.85f);
                outline.effectDistance = new Vector2(2f, -2f);
                outline.useGraphicAlpha = true;
                ConfigureResearchCard(row);
                string id = entries[i].researchId;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() =>
                {
                    if (research != null &&
                        research.TryStartResearch(id))
                    {
                        RefreshAll();
                    }
                });
                rows.Add(row);
            }
            RefreshResearchIcons();
            LayoutRows(byId);
            ApplyCatalogSelection();
        }

        private void RefreshAll()
        {
            int arrivals = services?.Stats?.LastDayArrivalCount ?? 0;
            int population = services?.Population?.CurrentPopulation ?? 0;
            var inputs = new ResearchConditionInputs(arrivals, population, CountBuildings);
            int unlockedCount = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                row.IsUnlocked = research?.IsUnlocked(row.Entry.researchId) == true;
                row.IsReady = !row.IsUnlocked && research?.IsReady(row.Entry.researchId) == true;
                row.IsResearching =
                    !row.IsUnlocked &&
                    research?.IsResearching(row.Entry.researchId) == true;
                if (row.IsUnlocked) unlockedCount++;
                if (row.NameText != null) row.NameText.text = row.Entry.displayName;
                if (row.ProgressText != null)
                    row.ProgressText.text = CreateProgressText(row, inputs);
                if (row.StateText != null)
                    row.StateText.text = CreateStateText(row);
                ApplyReadableTextColors(row);
                RefreshResearchCardVisual(row);
                CanvasGroup group = row.Instance.GetComponent<CanvasGroup>();
                if (group == null) group = row.Instance.AddComponent<CanvasGroup>();
                group.alpha =
                    row.IsUnlocked || row.IsReady || row.IsResearching
                        ? 1f
                        : 0.92f;
                Image card = row.Instance.GetComponent<Image>();
                if (card != null)
                    card.color = row.IsUnlocked ? new Color(0.16f, 0.38f, 0.22f)     // 완료 = 녹색톤
                        : row.IsResearching ? new Color(0.14f, 0.34f, 0.54f)         // 연구 중 = 청색톤
                        : row.IsReady ? new Color(0.58f, 0.43f, 0.10f)               // 해금 가능 = 황색톤
                        : new Color(0.22f, 0.27f, 0.35f);                            // 잠김 = 배경과 구분되는 회색톤
                Outline outline = row.Instance.GetComponent<Outline>();
                if (outline != null)
                {
                    outline.effectColor = row.IsUnlocked
                        ? new Color(0.32f, 0.84f, 0.44f, 0.90f)
                        : row.IsResearching
                            ? new Color(0.28f, 0.70f, 1f, 0.90f)
                            : row.IsReady
                                ? new Color(1f, 0.78f, 0.18f, 0.95f)
                                : new Color(0.58f, 0.65f, 0.76f, 0.80f);
                }
                Button button = row.Instance.GetComponent<Button>();
                if (button != null)
                {
                    button.interactable =
                        row.IsReady &&
                        string.IsNullOrEmpty(research?.ActiveResearchId) &&
                        CanAfford(row.Entry);
                }
            }
            RefreshCatalogPresentation();
            UpdateConnectorColors();
            if (yesterdayArrivalsText != null) yesterdayArrivalsText.text = $"어제 도착 {arrivals}";
            RefreshCatalogSummary(unlockedCount, population);
        }

        private static void ApplyReadableTextColors(Row row)
        {
            if (row.NameText != null)
                row.NameText.color = Color.white;
            if (row.ProgressText != null)
                row.ProgressText.color = new Color(0.86f, 0.90f, 0.96f, 1f);
            if (row.StateText == null) return;

            row.StateText.color = row.IsUnlocked
                ? new Color(0.48f, 1f, 0.60f, 1f)
                : row.IsResearching
                    ? new Color(0.48f, 0.84f, 1f, 1f)
                    : row.IsReady
                        ? new Color(1f, 0.88f, 0.34f, 1f)
                        : new Color(0.76f, 0.80f, 0.88f, 1f);
        }

        private string CreateProgressText(
            Row row,
            in ResearchConditionInputs inputs)
        {
            if (row.IsUnlocked)
            {
                return row.Entry.category == ResearchCategory.Expansion
                    ? "모든 지역 개척 완료"
                    : "연구 완료 · 건설 가능";
            }

            if (row.IsResearching)
            {
                return $"남은 시간 " +
                       $"{research.GetRemainingResearchHours(row.Entry.researchId)}시간";
            }

            var parts = new List<string>();
            if (row.Entry.requirements != null &&
                row.Entry.requirements.Count > 0)
            {
                for (int index = 0;
                     index < row.Entry.requirements.Count;
                     index++)
                {
                    ResearchRequirement requirement =
                        row.Entry.requirements[index];
                    if (requirement == null) continue;
                    parts.Add(
                        $"{GetConditionLabel(requirement.conditionKind, requirement.targetTileType)} " +
                        $"{ResearchConditionEvaluator.CurrentValue(requirement, inputs)}/" +
                        $"{Mathf.Max(0, requirement.threshold)}");
                }
            }
            else
            {
                parts.Add(
                    $"{GetConditionLabel(row.Entry.conditionKind, row.Entry.targetTileType)} " +
                    $"{ResearchConditionEvaluator.CurrentValue(row.Entry, inputs)}/" +
                    $"{Mathf.Max(0, row.Entry.threshold)}");
            }

            return string.Join(" · ", parts);
        }

        private string CreateStateText(Row row)
        {
            int cost = Mathf.Max(0, row.Entry.researchCost);
            string priceText = cost == 0
                ? "무료"
                : $"비용 {cost:N0}";

            if (row.IsUnlocked) return $"{priceText} · 완료";
            if (row.IsResearching) return $"{priceText} · 연구 중";
            if (!row.IsReady) return $"{priceText} · 잠김";
            if (!string.IsNullOrEmpty(research?.ActiveResearchId))
                return $"{priceText} · 다른 연구 진행 중";
            if (!CanAfford(row.Entry))
                return $"{priceText} · 재화 부족";

            int duration = Mathf.Max(
                0,
                row.Entry.researchDurationHours);
            if (duration == 0)
            {
                return $"{priceText} · 연구 가능";
            }

            return $"{priceText} · 연구 가능 · {duration}시간";
        }

        private bool CanAfford(ResearchEntry entry)
        {
            int cost = Mathf.Max(0, entry?.researchCost ?? 0);
            return cost == 0 ||
                   (economy != null && economy.Coins >= cost);
        }

        private static string GetConditionLabel(
            ResearchConditionKind kind,
            TileType targetTileType) =>
            kind switch
            {
                ResearchConditionKind.DailyArrivals => "통행",
                ResearchConditionKind.Population => "인구",
                ResearchConditionKind.BuildingCount =>
                    targetTileType switch
                    {
                        TileType.House => "주거",
                        TileType.Office => "회사",
                        TileType.School => "학교",
                        TileType.Hospital => "병원",
                        _ => targetTileType.ToString()
                    },
                _ => "조건"
            };

        private void LayoutRows(Dictionary<string, ResearchEntry> byId)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                RectTransform rect = GetRect(rows[i].Instance);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(CellWidth, CellHeight);
                // 패널 좌상단 기준: 카테고리는 가로, 연구 순서는 아래로 배치한다.
                rect.anchoredPosition = new Vector2(
                    PanelPadding +
                    GetOverallCategoryColumn(rows[i].Entry.category) *
                    (CellWidth + ColumnGap),
                    -(HeaderHeight + PanelPadding) -
                    GetOverallCategoryListIndex(rows[i]) *
                    (CellHeight + RowGap));
            }

            var rowById = new Dictionary<string, Row>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++) rowById[Normalize(rows[i].Entry.researchId)] = rows[i];
            for (int i = 0; i < rows.Count; i++)
            {
                string prerequisite = Normalize(rows[i].Entry.prerequisiteId);
                if (prerequisite.Length == 0 || !byId.ContainsKey(prerequisite) || !rowById.ContainsKey(prerequisite)) continue;
                CreateConnector(rowById[prerequisite], rows[i]);
            }
            ResizePanelToGrid();
        }

        private void DisableParentLayoutGroup()
        {
            Transform parent = rowTemplate != null ? rowTemplate.transform.parent : null;
            if (parent == null) return;
            LayoutGroup[] layoutGroups = parent.GetComponents<LayoutGroup>();
            for (int i = 0; i < layoutGroups.Length; i++) layoutGroups[i].enabled = false;
            ContentSizeFitter[] fitters = parent.GetComponents<ContentSizeFitter>();
            for (int i = 0; i < fitters.Length; i++) fitters[i].enabled = false;
        }

        private void ResizePanelToGrid()
        {
            RectTransform panel = GetComponent<RectTransform>();
            if (panel == null) return;

            int columnCount = rows.Count > 0 ? 3 : 0;
            int[] categoryCounts = new int[3];
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Entry.category ==
                    ResearchCategory.Expansion)
                {
                    continue;
                }

                int categoryColumn =
                    GetOverallCategoryColumn(rows[i].Entry.category);
                categoryCounts[categoryColumn]++;
            }
            // 탭을 바꿀 때 창 크기가 튀지 않도록 전체 보기의 행 수를
            // 모든 카테고리에서 공통 기준으로 사용한다.
            int rowCount = Mathf.Max(
                categoryCounts[0],
                Mathf.Max(categoryCounts[1], categoryCounts[2]));
            float gridWidth = columnCount * CellWidth + Mathf.Max(0, columnCount - 1) * ColumnGap;
            float gridHeight = rowCount * CellHeight + Mathf.Max(0, rowCount - 1) * RowGap;
            bool useGeonSubPanelLayout =
                panel.parent != null &&
                panel.parent.name == "SubPanels_Right";
            Vector2 anchor = useGeonSubPanelLayout
                ? new Vector2(1f, 0f)
                : new Vector2(0.5f, 0.5f);
            panel.anchorMin = anchor;
            panel.anchorMax = anchor;
            panel.pivot = anchor;
            panel.anchoredPosition = Vector2.zero;
            panel.localScale = Vector3.one;
            panel.sizeDelta = new Vector2(
                Mathf.Max(720f, gridWidth + PanelPadding * 2f),
                HeaderHeight + gridHeight + PanelPadding * 2f);
            // Geon 우측 서브패널 아래에서는 우하단 독에 맞추고,
            // 독립 오버레이로 사용할 때만 기존 화면 중앙 배치를 유지한다.

            // 행 컨테이너~패널 사이의 모든 중간 부모를 패널에 꽉 채워 정렬한다 —
            // 프리팹의 중간 오프셋(스크롤 뷰포트 등)이 남으면 그리드가 배경 밖으로 밀린다.
            if (rowTemplate != null)
            {
                Transform cursor = rowTemplate.transform.parent;
                while (cursor is RectTransform stretch && stretch != panel)
                {
                    stretch.anchorMin = Vector2.zero;
                    stretch.anchorMax = Vector2.one;
                    stretch.pivot = new Vector2(0.5f, 0.5f);
                    stretch.offsetMin = Vector2.zero;
                    stretch.offsetMax = Vector2.zero;
                    cursor = cursor.parent;
                }
            }
            LayoutHeader(panel);
            EnsureBackground(panel);
        }

        // 기존 프리팹 헤더를 새 카탈로그 요약 헤더 안에 재배치한다.
        private void LayoutHeader(RectTransform panel)
        {
            if (yesterdayArrivalsText != null)
                yesterdayArrivalsText.gameObject.SetActive(false);
            LayoutCatalogHeader(panel);
        }

        // 패널 루트에는 프리팹상 배경 Image가 없다 — 맵 위에 텍스트가 떠 보이는 원인.
        // 코드로 불투명 배경을 첫 자식으로 깔아 모달처럼 읽히게 한다.
        private void EnsureBackground(RectTransform panel)
        {
            Transform existing = panel.Find("TreeBackground");
            GameObject bg = existing != null ? existing.gameObject
                : new GameObject("TreeBackground", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(panel, false);
            bg.transform.SetAsFirstSibling();
            var rect = (RectTransform)bg.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = bg.GetComponent<Image>();
            image.color = new Color(0.07f, 0.10f, 0.13f, 0.97f);
            image.raycastTarget = true;   // 뒤 맵 클릭 차단
            ApplyRoundedSurface(image);
            ApplySoftShadow(
                image,
                0.30f,
                new Vector2(0f, -5f));
        }

        private void CreateConnector(Row parent, Row child)
        {
            Transform parentTransform = parent.Instance.transform.parent;
            GameObject line = new GameObject($"{parent.Entry.researchId}_to_{child.Entry.researchId}", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(parentTransform, false);
            line.transform.SetAsFirstSibling();
            RectTransform rect = line.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            PositionConnector(rect, parent, child);
            connectors.Add(line);
            UpdateConnectorColor(line, parent);
        }

        private static void PositionConnector(
            RectTransform rect,
            Row parent,
            Row child)
        {
            RectTransform parentRect = GetRect(parent.Instance);
            RectTransform childRect = GetRect(child.Instance);
            Vector2 start = parentRect.anchoredPosition +
                            new Vector2(
                                parentRect.rect.width * 0.5f,
                                -parentRect.rect.height);
            Vector2 end = childRect.anchoredPosition +
                          new Vector2(childRect.rect.width * 0.5f, 0f);
            Vector2 delta = end - start;
            rect.anchoredPosition = start;
            rect.sizeDelta = new Vector2(delta.magnitude, ConnectorThickness);
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        private void UpdateConnectorGeometry()
        {
            int connectorIndex = 0;
            var rowById = new Dictionary<string, Row>(StringComparer.Ordinal);
            for (int index = 0; index < rows.Count; index++)
            {
                rowById[Normalize(rows[index].Entry.researchId)] = rows[index];
            }

            for (int index = 0; index < rows.Count; index++)
            {
                Row child = rows[index];
                string prerequisite = Normalize(child.Entry.prerequisiteId);
                if (prerequisite.Length == 0 ||
                    !rowById.TryGetValue(prerequisite, out Row parent))
                {
                    continue;
                }

                if (connectorIndex >= connectors.Count) break;
                RectTransform connector =
                    connectors[connectorIndex++].GetComponent<RectTransform>();
                PositionConnector(connector, parent, child);
            }
        }

        private void UpdateConnectorColors()
        {
            int connectorIndex = 0;
            var rowById = new Dictionary<string, Row>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++) rowById[Normalize(rows[i].Entry.researchId)] = rows[i];
            for (int i = 0; i < rows.Count; i++)
            {
                string prerequisite = Normalize(rows[i].Entry.prerequisiteId);
                if (prerequisite.Length == 0 || !rowById.ContainsKey(prerequisite)) continue;
                if (connectorIndex < connectors.Count) UpdateConnectorColor(connectors[connectorIndex++], rowById[prerequisite]);
            }
        }

        private void UpdateConnectorColor(GameObject line, Row parent)
        {
            Image image = line.GetComponent<Image>();
            if (image != null)
            {
                image.color = parent.IsUnlocked
                    ? new Color(0.30f, 0.88f, 0.52f, 1f)
                    : new Color(0.42f, 0.50f, 0.62f, 1f);
            }
        }

        private static int GetBranch(ResearchEntry entry, Dictionary<string, ResearchEntry> byId, Dictionary<string, int> roots)
        {
            string root = Normalize(entry.researchId);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            ResearchEntry current = entry;
            while (current != null && visited.Add(Normalize(current.researchId)))
            {
                string prerequisite = Normalize(current.prerequisiteId);
                if (prerequisite.Length == 0 || !byId.TryGetValue(prerequisite, out current)) break;
                root = prerequisite;
            }
            if (!roots.TryGetValue(root, out int branch)) branch = roots[root] = roots.Count;
            return branch;
        }

        private static int GetDepth(ResearchEntry entry, Dictionary<string, ResearchEntry> byId, HashSet<string> visited)
        {
            string id = Normalize(entry.researchId);
            if (!visited.Add(id)) return 0;
            string prerequisite = Normalize(entry.prerequisiteId);
            return prerequisite.Length > 0 && byId.TryGetValue(prerequisite, out ResearchEntry parent)
                ? 1 + GetDepth(parent, byId, visited) : 0;
        }

        private int CountBuildings(TileType type)
        {
            IReadOnlyTileData tiles = services?.TileData;
            IWorldGridAccess grid = services?.WorldGrid;
            if (tiles == null || grid == null) return 0;
            int count = 0;
            for (int y = 0; y < grid.WorldHeight; y++)
                for (int x = 0; x < grid.WorldWidth; x++)
                {
                    var tile = new Vector2Int(x, y);
                    if (tiles.GetTileType(tile) == type && tiles.IsFootprintAnchor(tile)) count++;
                }
            return count;
        }

        private static TMP_Text FindText(GameObject instance, string childName)
        {
            Transform child = instance.transform.Find(childName);
            return child != null ? child.GetComponent<TMP_Text>() : null;
        }

        private static RectTransform GetRect(GameObject instance) =>
            instance.GetComponent<RectTransform>();

        private static string Normalize(string value) => value?.Trim() ?? string.Empty;

        private static void DestroyObject(GameObject value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
