using System.Collections.Generic;
using CityFlow.Gameplay.Research;
using UnityEngine;

namespace CityFlow.Content
{
    // 연구 사다리 카탈로그. 항목 추가 = 에셋 한 줄(코드 0). Resources 경로로 읽어
    // 씬을 건드리지 않는다 — GameTimeSettingsSO(Resources/CityFlow/GameTimeSettings)와 같은 방식.
    [CreateAssetMenu(fileName = "ResearchCatalog", menuName = "CityFlow/Research/Catalog")]
    public sealed class ResearchCatalogSO : ScriptableObject
    {
        public const string DefaultResourcePath = "CityFlow/ResearchCatalog";

        [SerializeField] private List<ResearchEntry> entries = new();

        public static ResearchCatalogSO LoadDefault() =>
            Resources.Load<ResearchCatalogSO>(DefaultResourcePath);

        // 빈 id·중복 id 는 경고하고 건너뛴다 — 에셋 실수가 조용히 묻히지 않게.
        public List<ResearchEntry> ValidEntries()
        {
            var result = new List<ResearchEntry>(entries?.Count ?? 0);
            if (entries == null) return result;

            var seen = new HashSet<string>();
            for (int i = 0; i < entries.Count; i++)
            {
                string id = entries[i]?.researchId?.Trim();
                if (entries[i] == null || string.IsNullOrEmpty(id))
                {
                    Debug.LogWarning($"[ResearchCatalogSO] {i}번 항목에 연구 id 가 없다.", this);
                    continue;
                }
                if (!seen.Add(id))
                {
                    Debug.LogWarning($"[ResearchCatalogSO] 중복된 연구 id: {id}", this);
                    continue;
                }
                result.Add(entries[i]);
            }
            return result;
        }
    }
}
