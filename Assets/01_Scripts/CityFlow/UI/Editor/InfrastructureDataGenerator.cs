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

            // 가격 = 밸런스 시트 v1.1 확정값(진우). 이 제너레이터가 SO를 덮어쓰므로
            // 여기 값이 곧 진실원 — 시트가 바뀌면 여기도 같이 바꿀 것.
            CreateAsset(InfrastructureKind.Signal, "SignalData", 50, 5, Vector2Int.zero, TurnMode.LeftOnly, Axis.Horizontal);
            CreateAsset(InfrastructureKind.Roundabout, "RoundaboutData", 90, 0, Vector2Int.zero, TurnMode.LeftOnly, Axis.Horizontal);
            CreateAsset(InfrastructureKind.Overpass, "OverpassData", 2500, 0, Vector2Int.zero, TurnMode.LeftOnly, Axis.Horizontal);
            CreateAsset(InfrastructureKind.Oneway, "OnewayData", 50, 0, new Vector2Int(1, 0), TurnMode.LeftOnly, Axis.Horizontal);
            CreateAsset(InfrastructureKind.TurnRestriction, "TurnRestrictionData", 50, 0, Vector2Int.zero, TurnMode.LeftOnly, Axis.Horizontal);
            // 우선도로: 시트에 항목 없음 → 규칙표지판 가족(일방·턴제한=50)에 맞춘 임시값. 진우 확정 필요.
            CreateAsset(InfrastructureKind.PriorityRoad, "PriorityRoadData", 50, 0, Vector2Int.zero, TurnMode.LeftOnly, Axis.Horizontal);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[CityFlow] 6개의 인프라 데이터 SO가 성공적으로 생성되었습니다! (Assets/05_ScriptableObjects/CityFlow/InfrastructureData)");
        }

        private static void CreateAsset(InfrastructureKind kind, string name, int cost, int greenSlots, Vector2Int onewayDir, TurnMode turnMode, Axis priorityAxis)
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
            so.PriorityAxis = priorityAxis;
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
