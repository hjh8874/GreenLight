using UnityEngine;
using UnityEditor;
using CityFlow.Contracts;
using CityFlow.Configs;
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

            // Generate 3 basic tile data based on design specifications
            CreateData(folderPath, "RoadData", "road_001", "Road", TileType.Road, 10, 0, 0, "Basic road for vehicles to travel.");
            CreateData(folderPath, "HouseData", "house_001", "House", TileType.House, 50, 5, 1, "Provides housing and generates traffic.");
            CreateData(folderPath, "OfficeData", "office_001", "Office", TileType.Office, 100, 20, 2, "Acts as a destination and generates coins.");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            Debug.Log($"[CityFlow] Data generation complete! Location: {folderPath}");
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
            
            // 캡슐화 규칙(1차 빌드 피드백)에 따라 퍼블릭 변수 직접 접근 대신 Initialize 메서드를 사용합니다.
            newData.Initialize(id, name, type, cost, coin, prosperity, desc);

            AssetDatabase.CreateAsset(newData, fullPath);
        }
    }
}
