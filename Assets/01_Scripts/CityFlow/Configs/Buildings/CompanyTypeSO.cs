using UnityEngine;

namespace CityFlow.Content
{
    // 회사 유형 1종(사무실·공장·물류창고). 오서링 데이터다 — Sim 은 이 타입을 보지 않고
    // CompanyTypeInfo(평범한 구조체)만 받는다(CityFlow.Sim 은 이 어셈블리를 참조할 수 없다).
    // 시각은 게임시간 [0,24) 단위이며 하루 길이와 무관하다.
    // workEndHour 가 workStartHour 보다 이르면 자정을 넘는 근무다(공장 20시 출근 → 5시 퇴근).
    [CreateAssetMenu(
        fileName = "CompanyType",
        menuName = "CityFlow/Content/Company Type")]
    public sealed class CompanyTypeSO : ScriptableObject
    {
        public string companyTypeId;
        public string displayName;
        public int    capacity;

        public float  workStartHour;
        public float  workStartWindow;   // 출근 창 길이(시간). 개인 출근 시각이 이 안에 흩뿌려진다
        public float  workEndHour;
        public float  workEndWindow;     // 퇴근 창 길이(시간)
    }
}
