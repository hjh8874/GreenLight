using UnityEngine;

namespace CityFlow.Contracts
{
    [CreateAssetMenu(fileName = "NewTileData", menuName = "CityFlow/Data/Tile Data")]
    public class TileDataSO : ScriptableObject
    {
        [Header("Identity")]
        public string buildingId;           // e.g. house_001
        public string buildingName;         // e.g. 집
        public TileType category;           // e.g. Residential (using existing TileType enum)
        
        [Header("UI & Visuals")]
        public Sprite buildingIcon;         // 하단 건설 버튼용
        public GameObject buildingPrefab;   // 건설 고스트 및 맵 스폰용
        [TextArea]
        public string buildingDescription;  // 툴팁 설명 (추가 제안됨)

        [Header("Economy & Stats")]
        public int buildCost = 100;         // 건설 비용
        public int dailyCoinValue = 10;     // 분당 수입 통계용
        public int prosperityValue = 1;     // 안정도용
        
        // 2차 빌드용 변수들은 현재 기획에 맞춰 배제하거나,
        // 필요하다면 아래처럼 추가만 해두고 1차 빌드 로직에서는 무시합니다.
        // public bool canGenerateTraffic = true;
        // public bool canReceiveTraffic = true;
        // public float trafficGenerationAmount = 1f;
        // public float destinationRewardMultiplier = 0.5f;
        // public bool unlockedByDefault = true;
    }
}
