using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.UI.Data
{
    [CreateAssetMenu(fileName = "InfrastructureData", menuName = "CityFlow/UI/InfrastructureData")]
    public class InfrastructureDataSO : ScriptableObject
    {
        [Header("기본 정보")]
        public InfrastructureKind Kind;
        public string InfrastructureName;
        public Sprite Icon;
        
        [TextArea(3, 5)]
        public string Description;
        
        [Min(0)]
        public int Cost;

        [Header("상세 설정 (해당하는 종류만 작성)")]
        [Tooltip("신호등 - 초록불 슬롯 길이")]
        public int GreenSlots = 5;
        
        [Tooltip("일방통행 - 방향 (예: 1,0 또는 0,1)")]
        public Vector2Int OnewayDir;
        
        [Tooltip("턴 제한 - 허용 모드")]
        public TurnMode TurnMode = TurnMode.LeftOnly;

        [Tooltip("우선도로 - 무정차 주축(곁길이 양보한다)")]
        public Axis PriorityAxis = Axis.Horizontal;
    }
}
