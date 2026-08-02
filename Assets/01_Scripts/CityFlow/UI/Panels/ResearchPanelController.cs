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
    // 연구 패널 — 전제 깊이를 열, 루트 갈래를 행으로 하는 좌→우 트리.
    public sealed class ResearchPanelController : MonoBehaviour, ICityFlowServiceConsumer
    {
        private const float CellWidth = 220f;
        private const float CellHeight = 72f;
        private const float ColumnGap = 54f;
        private const float RowGap = 18f;
        private const float ConnectorThickness = 3f;
        private const float HeaderHeight = 96f;
        private const float PanelPadding = 24f;

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
        private bool warnedMissingResearch;

        internal IReadOnlyList<Row> RowsForTest => rows;

        public void Initialize(CityFlowServices cityServices)
        {
            AutoWireSceneIntegration();
            Unbind();
            services = cityServices;
            if (services == null) return;
            if (catalog == null) catalog = ResearchCatalogSO.LoadDefault();

            BindResearch(services.Research);
            BindEconomy(services.Economy);
            services.ResearchRegistered += BindResearch;
            services.EconomyRegistered += BindEconomy;
            if (research == null && !warnedMissingResearch)
            {
                warnedMissingResearch = true;
                Debug.LogWarning(
                    "[ResearchPanelController] Research 서비스가 아직 없다 — " +
                    "등록될 때까지 전부 잠김으로 표시한다.", this);
            }

            BuildRows();
            RefreshAll();
        }

        private void AutoWireSceneIntegration()
        {
            // 구 씬 패널은 현재 프리팹의 catalog/rowTemplate가 없는 이전 직렬화 형태다.
            // 해당 패널이 bootstrap 초기화 순서에서 새 연결을 되돌리지 않게 건너뛴다.
            if (catalog == null || rowTemplate == null) return;

            UIDockController dock = FindFirstObjectByType<UIDockController>(FindObjectsInactive.Include);
            if (dock != null)
            {
                dock.RebindResearchPanel(gameObject);
            }

            ResearchPanelController[] panels = FindObjectsByType<ResearchPanelController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
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

        private void OnEnable() => RefreshAll();
        private void OnDestroy() => Unbind();

        private void Unbind()
        {
            if (services != null)
            {
                services.ResearchRegistered -= BindResearch;
                services.EconomyRegistered -= BindEconomy;
            }
            if (research != null)
            {
                research.ResearchUnlocked -= OnResearchUnlocked;
                research.ResearchProgressChanged -= RefreshAll;
                research.ResearchStateRestored -= RefreshAll;
                research = null;
            }
            BindEconomy(null);
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
                Button button = instance.GetComponent<Button>() ?? instance.AddComponent<Button>();
                button.targetGraphic = card;
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
            LayoutRows(byId);
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
                CanvasGroup group = row.Instance.GetComponent<CanvasGroup>();
                if (group == null) group = row.Instance.AddComponent<CanvasGroup>();
                group.alpha =
                    row.IsUnlocked || row.IsReady || row.IsResearching
                        ? 1f
                        : 0.85f;
                Image card = row.Instance.GetComponent<Image>();
                if (card != null)
                    card.color = row.IsUnlocked ? new Color(0.13f, 0.27f, 0.17f)     // 완료 = 녹색톤
                        : row.IsResearching ? new Color(0.10f, 0.24f, 0.38f)         // 연구 중 = 청색톤
                        : row.IsReady ? new Color(0.36f, 0.31f, 0.12f)               // 해금 가능 = 황색톤
                        : new Color(0.21f, 0.22f, 0.26f);                            // 잠김 = 밝은 회색톤(배경과 구분)
                Button button = row.Instance.GetComponent<Button>();
                if (button != null)
                {
                    button.interactable =
                        row.IsReady &&
                        string.IsNullOrEmpty(research?.ActiveResearchId) &&
                        CanAfford(row.Entry);
                }
            }
            UpdateConnectorColors();
            if (yesterdayArrivalsText != null) yesterdayArrivalsText.text = $"어제 도착 {arrivals}";
            if (populationText != null) populationText.text = $"인구 {population}";
            if (unlockProgressText != null) unlockProgressText.text = $"해금 {unlockedCount}/{rows.Count}";
        }

        private string CreateProgressText(
            Row row,
            in ResearchConditionInputs inputs)
        {
            if (row.IsUnlocked)
            {
                return string.Empty;
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
            if (row.IsUnlocked) return "완료";
            if (row.IsResearching) return "연구 중";
            if (!row.IsReady) return "잠김";
            if (!string.IsNullOrEmpty(research?.ActiveResearchId))
                return "다른 연구 진행 중";
            if (!CanAfford(row.Entry))
                return $"재화 부족 · {Mathf.Max(0, row.Entry.researchCost):N0}";

            int cost = Mathf.Max(0, row.Entry.researchCost);
            int duration = Mathf.Max(
                0,
                row.Entry.researchDurationHours);
            if (cost == 0 && duration == 0)
            {
                return "즉시 해금";
            }

            return $"연구 시작 · {cost:N0} · {duration}시간";
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
                // 패널 좌상단 기준: 좌우 패딩 + 헤더 아래부터 그린다
                rect.anchoredPosition = new Vector2(
                    PanelPadding + rows[i].Depth * (CellWidth + ColumnGap),
                    -(HeaderHeight + PanelPadding) - rows[i].Branch * (CellHeight + RowGap));
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

            int columnCount = 0;
            int branchCount = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                columnCount = Mathf.Max(columnCount, rows[i].Depth + 1);
                branchCount = Mathf.Max(branchCount, rows[i].Branch + 1);
            }
            float gridWidth = columnCount * CellWidth + Mathf.Max(0, columnCount - 1) * ColumnGap;
            float gridHeight = branchCount * CellHeight + Mathf.Max(0, branchCount - 1) * RowGap;
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(
                gridWidth + PanelPadding * 2f,
                HeaderHeight + gridHeight + PanelPadding * 2f);
            // 부모(우측 독 서브패널)가 어디에 있든 화면 중앙 — 오버레이 캔버스는 픽셀 좌표
            panel.position = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);

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

        // 헤더 3줄(어제 도착·인구·해금)을 패널 좌상단 패딩 안에 고정한다.
        private void LayoutHeader(RectTransform panel)
        {
            TMP_Text[] headers = { yesterdayArrivalsText, populationText, unlockProgressText };
            for (int i = 0; i < headers.Length; i++)
            {
                if (headers[i] == null) continue;
                var rect = headers[i].rectTransform;
                rect.SetParent(panel, false);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(320f, 26f);
                rect.anchoredPosition = new Vector2(PanelPadding, -PanelPadding - i * 26f);
                headers[i].alignment = TextAlignmentOptions.TopLeft;
            }
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
            image.color = new Color(0.08f, 0.09f, 0.11f, 0.97f);
            image.raycastTarget = true;   // 뒤 맵 클릭 차단
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
            rect.pivot = new Vector2(0f, 1f);
            Vector2 start = GetRect(parent.Instance).anchoredPosition + new Vector2(CellWidth, -CellHeight * 0.5f);
            Vector2 end = GetRect(child.Instance).anchoredPosition + new Vector2(0f, -CellHeight * 0.5f);
            Vector2 delta = end - start;
            rect.anchoredPosition = start + new Vector2(0f, ConnectorThickness * 0.5f);
            rect.sizeDelta = new Vector2(delta.magnitude, ConnectorThickness);
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            connectors.Add(line);
            UpdateConnectorColor(line, parent);
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
            if (image != null) image.color = parent.IsUnlocked ? new Color(0.25f, 0.75f, 0.35f) : Color.gray;
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
