using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Gameplay.Research;
using TMPro;
using UnityEngine;

namespace CityFlow.UI
{
    // 연구 패널 — 사다리 카탈로그를 행으로 그리고 해금 상태를 보여준다.
    // 잠긴 항목도 이름·필요 수치를 노출한다 — 숨기면 목표가 사라진다(설계 §5).
    // 서비스는 services.Research 경유로만 접근한다. 동봉 금지 —
    // ResearchUnlockService 는 SpecialBuildingSystem.prefab 에 이미 배선돼 있고,
    // 형제 컴포넌트를 직접 구독하면 등록 경쟁에서 진 죽은 인스턴스를 보게 된다.
    // Camera·Update 의존 없음 — 갱신은 이벤트 + OnEnable, EditMode 테스트가 직접 때린다.
    public sealed class ResearchPanelController : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private ResearchCatalogSO catalog;       // 비면 Resources 폴백
        [SerializeField] private GameObject rowTemplate;          // 비활성 템플릿(자식: Name·Progress·State TMP)
        [SerializeField] private TMP_Text yesterdayArrivalsText;  // "어제 도착 n" — '어제' 라벨 필수(설계 §8)
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
        }

        private readonly List<Row> rows = new();
        private CityFlowServices services;
        private IResearchUnlockService research;
        private bool warnedMissingResearch;

        internal IReadOnlyList<Row> RowsForTest => rows;

        public void Initialize(CityFlowServices cityServices)
        {
            Unbind();
            services = cityServices;
            if (services == null)
            {
                return;
            }

            if (catalog == null)
            {
                catalog = ResearchCatalogSO.LoadDefault();
            }

            BindResearch(services.Research);
            services.ResearchRegistered += BindResearch;
            if (research == null && !warnedMissingResearch)
            {
                warnedMissingResearch = true;
                Debug.LogWarning(
                    "[ResearchPanelController] Research 서비스가 아직 없다 — " +
                    "등록될 때까지 전부 잠김으로 표시한다.",
                    this);
            }

            BuildRows();
            RefreshAll();
        }

        private void OnEnable() => RefreshAll();

        private void OnDestroy() => Unbind();

        private void Unbind()
        {
            if (services != null)
            {
                services.ResearchRegistered -= BindResearch;
            }
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
            if (service == null || ReferenceEquals(research, service))
            {
                return;
            }

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
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Instance == null) continue;
                if (Application.isPlaying) Destroy(rows[i].Instance);
                else DestroyImmediate(rows[i].Instance);
            }
            rows.Clear();
            if (catalog == null || rowTemplate == null)
            {
                return;
            }

            List<ResearchEntry> entries = catalog.ValidEntries();
            for (int i = 0; i < entries.Count; i++)
            {
                GameObject instance = Instantiate(rowTemplate, rowTemplate.transform.parent);
                instance.name = entries[i].researchId;
                instance.SetActive(true);   // 잠긴 행도 노출
                rows.Add(new Row
                {
                    Entry = entries[i],
                    Instance = instance,
                    NameText = FindText(instance, "Name"),
                    ProgressText = FindText(instance, "Progress"),
                    StateText = FindText(instance, "State"),
                });
            }
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
                if (row.IsUnlocked) unlockedCount++;
                if (row.NameText != null)
                {
                    row.NameText.text = row.Entry.displayName;
                }
                if (row.ProgressText != null)
                {
                    row.ProgressText.text = row.IsUnlocked
                        ? string.Empty
                        : $"{ResearchConditionEvaluator.CurrentValue(row.Entry, inputs)}/{row.Entry.threshold}";
                }
                if (row.StateText != null)
                {
                    row.StateText.text = row.IsUnlocked ? "열림" : "진행중";
                }
            }

            if (yesterdayArrivalsText != null) yesterdayArrivalsText.text = $"어제 도착 {arrivals}";
            if (populationText != null) populationText.text = $"인구 {population}";
            if (unlockProgressText != null) unlockProgressText.text = $"해금 {unlockedCount}/{rows.Count}";
        }

        // 시설 개수 — ResearchUnlockService 와 같은 읽기 전용 앵커 스캔(패널 진행 표시용).
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
    }
}
