using UnityEngine;
using UnityEditor;
using CityFlow.UI.Data;
using CityFlow.Contracts;

namespace CityFlow.UI.Editor
{
    public static class InfrastructureDataGenerator
    {
        [MenuItem("CityFlow/UI/Generate Infrastructure Data SOs")]
        public static void Generate()
        {
            string folderPath = "Assets/05_ScriptableObjects/CityFlow/InfrastructureData";
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                if (!AssetDatabase.IsValidFolder("Assets/05_ScriptableObjects/CityFlow"))
                {
                    AssetDatabase.CreateFolder("Assets/05_ScriptableObjects", "CityFlow");
                }
                AssetDatabase.CreateFolder("Assets/05_ScriptableObjects/CityFlow", "InfrastructureData");
            }

            CreateAsset(InfrastructureKind.Signal, "SignalData", 100, 5, Vector2Int.zero, TurnMode.LeftOnly);
            CreateAsset(InfrastructureKind.Roundabout, "RoundaboutData", 150, 0, Vector2Int.zero, TurnMode.LeftOnly);
            CreateAsset(InfrastructureKind.Overpass, "OverpassData", 300, 0, Vector2Int.zero, TurnMode.LeftOnly);
            CreateAsset(InfrastructureKind.Oneway, "OnewayData", 50, 0, new Vector2Int(1, 0), TurnMode.LeftOnly);
            CreateAsset(InfrastructureKind.TurnRestriction, "TurnRestrictionData", 50, 0, Vector2Int.zero, TurnMode.LeftOnly);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[CityFlow] 5개의 인프라 데이터 SO가 성공적으로 생성되었습니다! (Assets/05_ScriptableObjects/CityFlow/InfrastructureData)");
        }

        private static void CreateAsset(InfrastructureKind kind, string name, int cost, int greenSlots, Vector2Int onewayDir, TurnMode turnMode)
        {
            string path = $"Assets/05_ScriptableObjects/CityFlow/InfrastructureData/{name}.asset";
            InfrastructureDataSO so = AssetDatabase.LoadAssetAtPath<InfrastructureDataSO>(path);
            
            bool isNew = false;
            if (so == null)
            {
                so = ScriptableObject.CreateInstance<InfrastructureDataSO>();
                isNew = true;
            }

            so.Kind = kind;
            so.InfrastructureName = kind.ToString();
            so.Cost = cost;
            so.GreenSlots = greenSlots;
            so.OnewayDir = onewayDir;
            so.TurnMode = turnMode;
            so.Description = $"{kind} 인프라 건설 데이터입니다.";

            if (isNew)
            {
                AssetDatabase.CreateAsset(so, path);
            }
            else
            {
                EditorUtility.SetDirty(so);
            }
        }
    }
}
