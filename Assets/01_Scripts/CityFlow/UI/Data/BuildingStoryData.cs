namespace CityFlow.UI.Data
{
    /// <summary>
    /// 건물 상세 카드에 바인딩할 온더플라이 합성 데이터 구조체.
    /// BuildingStoryDataFactory가 IReadOnlyTileData 시드를 기반으로 역산 조립하여 생성합니다.
    /// </summary>
    public readonly struct BuildingStoryData
    {
        /// <summary>건물/회사명 (예: "그린빌 아파트", "넥슨 판교 사옥")</summary>
        public readonly string BuildingName;

        /// <summary>스토리 한줄 코멘트 (예: "오늘도 8번 교차로 정체 때문에 11명이 지각했습니다!")</summary>
        public readonly string StoryComment;

        /// <summary>총 인원 수 (거주민 또는 직원)</summary>
        public readonly int TotalStaff;

        /// <summary>지각/정체 영향 인원 수</summary>
        public readonly int TardyStaff;

        /// <summary>분당 수입 (코인)</summary>

        /// <summary>평균 출근 지연 시간 (초 단위, 실시간 증가)</summary>
        public readonly float DelaySeconds;

        public BuildingStoryData(
            string buildingName,
            string storyComment,
            int totalStaff,
            int tardyStaff,
            float delaySeconds)
        {
            BuildingName = buildingName;
            StoryComment = storyComment;
            TotalStaff = totalStaff;
            TardyStaff = tardyStaff;
            DelaySeconds = delaySeconds;
        }
    }
}
