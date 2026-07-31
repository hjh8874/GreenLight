using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Gameplay.Research
{
    [DisallowMultipleComponent]
    public sealed class ResearchUnlockService :
        MonoBehaviour,
        ICityFlowServiceConsumer,
        IResearchUnlockService,
        IResearchSaveSource
    {
        [SerializeField]
        private string playModeTestResearchId = "research_building_mall";

        [SerializeField]
        private ResearchCatalogSO catalog;   // 프리팹이 직렬화. 비면 Resources 폴백

        private readonly HashSet<string> unlockedResearchIds =
            new(StringComparer.Ordinal);
        private readonly HashSet<string> readyResearchIds =
            new(StringComparer.Ordinal);
        private readonly HashSet<string> purchasedUpgradeIds =
            new(StringComparer.Ordinal);
        private bool initialized;
        private CityFlowServices cityServices;
        private IReadOnlyPopulationData boundPopulation;
        private int lastSeenDayArrivals;
        internal Func<ResearchConditionInputs> inputsOverrideForTest;

        public int UnlockedCount => unlockedResearchIds.Count;

        public event Action<string> ResearchUnlocked;
        public event Action ResearchStateRestored;

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled || initialized)
            {
                return;
            }

            if (services == null)
            {
                Debug.LogWarning(
                    "[ResearchUnlockService] Research registration failed.",
                    this);
                return;
            }

            initialized = true;
            if (!services.RegisterResearch(this))
            {
                initialized = false;
                Debug.LogWarning(
                    "[ResearchUnlockService] Research registration failed.",
                    this);
                return;
            }

            Debug.Log("[ResearchUnlockService] Registered.", this);

            cityServices = services;
            lastSeenDayArrivals =
                cityServices.Stats?.LastDayArrivalCount ?? 0;
            if (catalog == null)
            {
                catalog = ResearchCatalogSO.LoadDefault();
            }

            services.Events.Placed += OnPlacedForResearch;
            if (services.Save != null)
            {
                services.Save.RestoreCompleted += OnRestoreForResearch;
            }
            BindPopulation(services.Population);
            services.PopulationRegistered += BindPopulation;
            EvaluatePendingResearch();                            // 초기 1회
        }

        private void OnDestroy()
        {
            if (cityServices != null)
            {
                if (cityServices.Events != null)
                {
                    cityServices.Events.Placed -= OnPlacedForResearch;
                }
                if (cityServices.Save != null)
                {
                    cityServices.Save.RestoreCompleted -= OnRestoreForResearch;
                }
                cityServices.PopulationRegistered -= BindPopulation;
            }
            if (boundPopulation != null)
            {
                boundPopulation.PopulationChanged -= OnPopulationChangedForResearch;
            }
        }

        private void Update()
        {
            IReadOnlyCityStats stats = cityServices?.Stats;
            if (stats == null)
            {
                return;
            }

            int currentDayArrivals = stats.LastDayArrivalCount;
            if (currentDayArrivals == lastSeenDayArrivals)
            {
                // 같은 확정 입력이면 판정 결과도 같으므로 생략해도 결과가 달라지지 않는다.
                return;
            }

            lastSeenDayArrivals = currentDayArrivals;
            EvaluatePendingResearch();
        }

        private void BindPopulation(IReadOnlyPopulationData population)
        {
            if (population == null || ReferenceEquals(boundPopulation, population))
            {
                return;
            }

            if (boundPopulation != null)
            {
                boundPopulation.PopulationChanged -= OnPopulationChangedForResearch;
            }
            boundPopulation = population;
            boundPopulation.PopulationChanged += OnPopulationChangedForResearch;
        }

        private void OnPlacedForResearch(PlacedEvent e)
        {
            if (e.IsRemove) return;
            EvaluatePendingResearch();     // 학교·병원 배치 즉시 시설 조건 반영
        }

        private void OnRestoreForResearch(RestoreCompletedEvent _) => EvaluatePendingResearch();
        private void OnPopulationChangedForResearch(int _) => EvaluatePendingResearch();

        internal void EvaluatePendingResearch()
        {
            if (!initialized || catalog == null) return;
            ResearchConditionInputs inputs = inputsOverrideForTest?.Invoke() ?? BuildInputs();
            List<ResearchEntry> entries = catalog.ValidEntries();
            var entriesById = new Dictionary<string, ResearchEntry>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                entriesById[NormalizeId(entries[i].researchId)] = entries[i];
            }

            readyResearchIds.Clear();
            for (int i = 0; i < entries.Count; i++)
            {
                ResearchEntry entry = entries[i];
                if (IsUnlocked(entry.researchId)) continue;               // §9: 다시 잠기지 않는다
                string prerequisiteId = NormalizeId(entry.prerequisiteId);
                if (prerequisiteId.Length > 0 &&
                    (!entriesById.ContainsKey(prerequisiteId) ||
                     !IsUnlocked(prerequisiteId)))
                {
                    continue;
                }
                if (!ResearchConditionEvaluator.IsSatisfied(entry, inputs)) continue;
                readyResearchIds.Add(NormalizeId(entry.researchId));
            }
        }

        private ResearchConditionInputs BuildInputs()
        {
            int arrivals = cityServices?.Stats?.LastDayArrivalCount ?? 0;
            int population = cityServices?.Population?.CurrentPopulation ?? 0;
            return new ResearchConditionInputs(arrivals, population, CountBuildings);
        }

        // 시설 개수: 평가 시점에만 그리드 전수(20×20=400칸, 앵커만 센다).
        // ponytail: 캐시 없음 — 배치·하루 경계에만 도는 스캔이라 프레임 비용이 아니다.
        private int CountBuildings(TileType type)
        {
            IReadOnlyTileData tiles = cityServices?.TileData;
            if (tiles == null) return 0;
            // WorldGridSystem 이 씬에 없으면 services.WorldGrid 는 null 이다 — 메인 씬 포함
            // 대부분의 씬이 그렇다(#169 미배선). null 이면 시설 조건이 영원히 0 이 되므로,
            // CityBootstrap 이 같은 상황에서 쓰는 기본 크기(GridUtil.Default*)로 폴백한다.
            // 라이브 스모크에서 실측한 결함(2026-07-30): 학교를 놓아도 약국이 안 열렸다.
            IWorldGridAccess grid = cityServices?.WorldGrid;
            int width = grid?.WorldWidth ?? GridUtil.DefaultWidth;
            int height = grid?.WorldHeight ?? GridUtil.DefaultHeight;
            int count = 0;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    var tile = new Vector2Int(x, y);
                    if (tiles.GetTileType(tile) == type && tiles.IsFootprintAnchor(tile)) count++;
                }
            return count;
        }

        public bool IsUnlocked(string researchId)
        {
            string normalizedId = NormalizeId(researchId);
            return normalizedId.Length > 0 &&
                   unlockedResearchIds.Contains(normalizedId);
        }

        public bool IsReady(string researchId)
        {
            string normalizedId = NormalizeId(researchId);
            return normalizedId.Length > 0 && readyResearchIds.Contains(normalizedId);
        }

        public bool TryUnlock(string researchId)
        {
            string normalizedId = NormalizeId(researchId);
            if (!initialized || normalizedId.Length == 0 || !IsReady(normalizedId) ||
                !unlockedResearchIds.Add(normalizedId))
            {
                return false;
            }

            readyResearchIds.Remove(normalizedId);

            Debug.Log(
                $"[ResearchUnlockService] Unlocked {normalizedId}.",
                this);
            EvaluatePendingResearch();
            ResearchUnlocked?.Invoke(normalizedId);
            return true;
        }

        public ResearchSaveData CreateSnapshot()
        {
            return new ResearchSaveData
            {
                UnlockedResearchIds = CreateSortedSnapshot(
                    unlockedResearchIds),
                PurchasedUpgradeIds = CreateSortedSnapshot(
                    purchasedUpgradeIds)
            };
        }

        public void RestoreSnapshot(ResearchSaveData snapshot)
        {
            RestoreSet(
                unlockedResearchIds,
                snapshot?.UnlockedResearchIds);
            RestoreSet(
                purchasedUpgradeIds,
                snapshot?.PurchasedUpgradeIds);
            EvaluatePendingResearch();
            ResearchStateRestored?.Invoke();
        }

#if UNITY_EDITOR
        [ContextMenu("Play Mode Test/Unlock Selected Research")]
        private void UnlockSelectedResearchForPlayModeTest()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning(
                    "[ResearchUnlockService] Enter Play Mode first.",
                    this);
                return;
            }

            if (!TryUnlock(playModeTestResearchId))
            {
                Debug.LogWarning(
                    "[ResearchUnlockService] The selected research is " +
                    "invalid or already unlocked.",
                    this);
            }
        }

        private void OnValidate()
        {
            playModeTestResearchId = NormalizeId(
                playModeTestResearchId);
        }
#endif

        private static string NormalizeId(string value) =>
            value?.Trim() ?? string.Empty;

        private static string[] CreateSortedSnapshot(
            HashSet<string> source)
        {
            var result = new string[source.Count];
            source.CopyTo(result);
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }

        private static void RestoreSet(
            HashSet<string> destination,
            string[] source)
        {
            destination.Clear();
            if (source == null)
            {
                return;
            }

            for (int index = 0; index < source.Length; index++)
            {
                string normalizedId = NormalizeId(source[index]);
                if (normalizedId.Length > 0)
                {
                    destination.Add(normalizedId);
                }
            }
        }
    }
}

// Unity setup: This component is prewired in SpecialBuildingSystem.prefab.
