using System.Collections.Generic;
using CityFlow.Sim;
using UnityEngine;

namespace CityFlow.Content
{
    // 회사 유형 카탈로그. 유형 추가가 에셋 편집만으로 끝나게 하는 목록이다.
    // 씬을 건드리지 않으려고 Resources 경로로 읽는다 — GameTimeSettingsSO 와 같은 방식.
    [CreateAssetMenu(
        fileName = "CompanyTypeCatalog",
        menuName = "CityFlow/Content/Company Type Catalog")]
    public sealed class CompanyTypeCatalogSO : ScriptableObject
    {
        public const string DefaultResourcePath = "CityFlow/CompanyTypeCatalog";

        [SerializeField] private List<CompanyTypeSO> types = new();

        public IReadOnlyList<CompanyTypeSO> Types => types;

        public static CompanyTypeCatalogSO LoadDefault() =>
            Resources.Load<CompanyTypeCatalogSO>(DefaultResourcePath);

        // Sim 에 넘길 표. 빈 id·중복 id 는 경고하고 건너뛴다 — 에셋 실수가 조용히 묻히지 않게.
        public List<CompanyTypeInfo> ToCompanyTypeInfos()
        {
            var result = new List<CompanyTypeInfo>(types?.Count ?? 0);
            if (types == null) return result;

            var seen = new HashSet<string>();
            for (int i = 0; i < types.Count; i++)
            {
                CompanyTypeSO definition = types[i];
                string id = definition?.companyTypeId?.Trim();
                if (definition == null || string.IsNullOrEmpty(id))
                {
                    Debug.LogWarning($"[CompanyTypeCatalogSO] {i}번 항목에 회사 유형 id 가 없다.", this);
                    continue;
                }
                if (!seen.Add(id))
                {
                    Debug.LogWarning($"[CompanyTypeCatalogSO] 중복된 회사 유형 id: {id}", this);
                    continue;
                }

                result.Add(new CompanyTypeInfo(
                    new CommuteWindow(
                        id,
                        definition.workStartHour, definition.workStartWindow,
                        definition.workEndHour, definition.workEndWindow),
                    definition.capacity));
            }

            return result;
        }
    }
}
