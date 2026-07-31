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
            public int Depth;
            public int Branch;
        }

        private readonly List<Row> rows = new();
        private readonly List<GameObject> connectors = new();
        private CityFlowServices services;
        private IResearchUnlockService research;
        private bool warnedMissingResearch;

        internal IReadOnlyList<Row> RowsForTest => rows;

        public void Initialize(CityFlowServices cityServices)
        {
            Unbind();
            services = cityServices;
            if (services == null) return;
            if (catalog == null) catalog = ResearchCatalogSO.LoadDefault();

            BindResearch(services.Research);
            services.ResearchRegistered += BindResearch;
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

        private void OnEnable() => RefreshAll();
        private void OnDestroy() => Unbind();

        private void Unbind()
        {
            if (services != null) services.ResearchRegistered -= BindResearch;
            if (research != null)
            {
                research.ResearchUnlocked -= OnResearchUnlocked;
                research.ResearchStateRestored -= RefreshAll;
                research = null;
            }
            services = null;
        }

        private void BindResearch(IResearchUnlockService service)
        {
            if (service == null || ReferenceEquals(research, service)) return;
            if (research != null)
            {
                research.ResearchUnlocked -= OnResearchUnlocked;
                research.ResearchStateRestored -= RefreshAll;
            }
            research = service;
            research.ResearchUnlocked += OnResearchUnlocked;
            research.ResearchStateRestored += RefreshAll;
            RefreshAll();
        }

        private void OnResearchUnlocked(string _) => RefreshAll();

        private void BuildRows()
        {
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
                Button button = instance.GetComponent<Button>() ?? instance.AddComponent<Button>();
                string id = entries[i].researchId;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() =>
                {
                    if (research != null && research.TryUnlock(id)) RefreshAll();
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
                if (row.IsUnlocked) unlockedCount++;
                if (row.NameText != null) row.NameText.text = row.Entry.displayName;
                if (row.ProgressText != null)
                    row.ProgressText.text = row.IsUnlocked ? string.Empty :
                        $"{ResearchConditionEvaluator.CurrentValue(row.Entry, inputs)}/{row.Entry.threshold}";
                if (row.StateText != null)
                    row.StateText.text = row.IsUnlocked ? "완료" : row.IsReady ? "해금 가능" : "잠김";
                CanvasGroup group = row.Instance.GetComponent<CanvasGroup>();
                if (group == null) group = row.Instance.AddComponent<CanvasGroup>();
                group.alpha = row.IsUnlocked || row.IsReady ? 1f : 0.45f;
                Button button = row.Instance.GetComponent<Button>();
                if (button != null) button.interactable = row.IsReady;
            }
            UpdateConnectorColors();
            if (yesterdayArrivalsText != null) yesterdayArrivalsText.text = $"어제 도착 {arrivals}";
            if (populationText != null) populationText.text = $"인구 {population}";
            if (unlockProgressText != null) unlockProgressText.text = $"해금 {unlockedCount}/{rows.Count}";
        }

        private void LayoutRows(Dictionary<string, ResearchEntry> byId)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                RectTransform rect = GetRect(rows[i].Instance);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(CellWidth, CellHeight);
                rect.anchoredPosition = new Vector2(
                    rows[i].Depth * (CellWidth + ColumnGap),
                    -rows[i].Branch * (CellHeight + RowGap));
            }

            var rowById = new Dictionary<string, Row>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++) rowById[Normalize(rows[i].Entry.researchId)] = rows[i];
            for (int i = 0; i < rows.Count; i++)
            {
                string prerequisite = Normalize(rows[i].Entry.prerequisiteId);
                if (prerequisite.Length == 0 || !byId.ContainsKey(prerequisite) || !rowById.ContainsKey(prerequisite)) continue;
                CreateConnector(rowById[prerequisite], rows[i]);
            }
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
            rect.pivot = new Vector2(0.5f, 0.5f);
            Vector2 start = GetRect(parent.Instance).anchoredPosition + new Vector2(CellWidth, -CellHeight * 0.5f);
            Vector2 end = GetRect(child.Instance).anchoredPosition + new Vector2(0f, -CellHeight * 0.5f);
            Vector2 delta = end - start;
            rect.anchoredPosition = (start + end) * 0.5f;
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
