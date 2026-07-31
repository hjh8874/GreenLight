using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Configs
{
    [CreateAssetMenu(fileName = "NewTileData", menuName = "CityFlow/Data/Tile Data")]
    public class TileDataSO : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string buildingId;           // e.g. house_001
        [SerializeField] private string buildingName;         // e.g. 집
        [SerializeField] private TileType category;           // e.g. Residential

        public string BuildingId => buildingId;
        public string BuildingName => buildingName;
        public TileType Category => category;
        
        [Header("UI & Visuals")]
        [SerializeField] private Sprite buildingIcon;         // 하단 건설 버튼용
        [SerializeField] private GameObject buildingPrefab;   // 건설 고스트 및 맵 스폰용
        [TextArea]
        [SerializeField] private string buildingDescription;  // 툴팁 설명

        public Sprite BuildingIcon => buildingIcon;
        public GameObject BuildingPrefab => buildingPrefab;
        public string BuildingDescription => buildingDescription;

        [Header("Economy & Stats")]
        [SerializeField] private int buildCost = 100;         // 건설 비용
        [SerializeField] private int dailyCoinValue = 10;     // 분당 수입 통계용
        [SerializeField] private int prosperityValue = 1;     // 안정도용

        public int BuildCost => buildCost;
        public int DailyCoinValue => dailyCoinValue;
        public int ProsperityValue => prosperityValue;

        [Header("Research")]
        [SerializeField]
        [Tooltip("비어 있으면 기본 해금. 값이 있으면 해당 연구 완료 후 건설 가능")]
        private string requiredResearchId;

        public string RequiredResearchId => requiredResearchId;
        
        // 에디터/제너레이터용 데이터 세팅 메서드 (캡슐화 유지)
        public void Initialize(string id, string name, TileType type, int cost, int coin, int prosperity, string desc)
        {
            buildingId = id;
            buildingName = name;
            category = type;
            buildCost = cost;
            dailyCoinValue = coin;
            prosperityValue = prosperity;
            buildingDescription = desc;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            buildingId = buildingId?.Trim();
            buildingName = buildingName?.Trim();
            requiredResearchId = requiredResearchId?.Trim();
            buildCost = Mathf.Max(0, buildCost);
            dailyCoinValue = Mathf.Max(0, dailyCoinValue);
            prosperityValue = Mathf.Max(0, prosperityValue);
        }
#endif
    }
}
