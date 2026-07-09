using UnityEngine;
using UnityEditor;
using CityFlow.Contracts;
using System.IO;

namespace CityFlow.Editor
{
    public static class TileDataGenerator
    {
        [MenuItem("CityFlow/Generate Initial Tile Data (1st Build)")]
        public static void GenerateTileData()
        {
            // 팀 공통 규칙에 따른 리소스 폴더 경로 (Assets/05_ScriptableObjects)
            string folderPath = "Assets/05_ScriptableObjects/CityFlow/TileData";
            
            // 규칙에 맞는 폴더가 없으면 안전하게 자동 생성
            if (!AssetDatabase.IsValidFolder("Assets/05_ScriptableObjects"))
                AssetDatabase.CreateFolder("Assets", "05_ScriptableObjects");
            if (!AssetDatabase.IsValidFolder("Assets/05_ScriptableObjects/CityFlow"))
                AssetDatabase.CreateFolder("Assets/05_ScriptableObjects", "CityFlow");
            if (!AssetDatabase.IsValidFolder(folderPath))
                AssetDatabase.CreateFolder("Assets/05_ScriptableObjects/CityFlow", "TileData");

            // 기획 명세에 맞춘 3가지 기본 타일 데이터 자동 생성
            CreateData(folderPath, "RoadData", "road_001", "도로", TileType.Road, 50, 0, 0, "도시의 혈관이 되는 튼튼한 도로입니다.");
            CreateData(folderPath, "HouseData", "house_001", "집", TileType.House, 100, 10, 1, "인구가 유입되고 세금(코인)을 창출하는 주거 구역입니다.");
            CreateData(folderPath, "OfficeData", "office_001", "상업 시설", TileType.Office, 200, 30, 2, "더 많은 일자리와 수익을 내는 상업/사무 시설입니다.");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            Debug.Log($"[CityFlow] 데이터 생성 완료! 생성 위치: {folderPath}");
        }

        private static void CreateData(string path, string assetName, string id, string name, TileType type, int cost, int coin, int prosperity, string desc)
        {
            string fullPath = $"{path}/{assetName}.asset";
            TileDataSO existing = AssetDatabase.LoadAssetAtPath<TileDataSO>(fullPath);
            
            if (existing != null)
            {
                Debug.LogWarning($"[CityFlow] {assetName} 파일이 이미 존재하여 덮어쓰지 않았습니다.");
                return;
            }

            TileDataSO newData = ScriptableObject.CreateInstance<TileDataSO>();
            newData.buildingId = id;
            newData.buildingName = name;
            newData.category = type;
            newData.buildCost = cost;
            newData.dailyCoinValue = coin;
            newData.prosperityValue = prosperity;
            newData.buildingDescription = desc;

            AssetDatabase.CreateAsset(newData, fullPath);
        }
    }
}
