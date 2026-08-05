using System;

namespace CityFlow.Contracts.Save
{
    [Serializable]
    public sealed class CitizenFeedSaveData
    {
        public CitizenConcernEntry[] OpenConcerns = Array.Empty<CitizenConcernEntry>();
    }

    /// <summary>
    /// 작성자를 이름으로 식별한다. FeedAuthorProfileSO에 안정적인 id가 없기 때문이다.
    /// 이름을 바꾸면 진행 중이던 연결만 조용히 끊기고 시스템은 멀쩡하다 —
    /// 장부는 24시간이면 만료되는 휘발성 데이터라 이 정도는 감수한다.
    /// </summary>
    [Serializable]
    public sealed class CitizenConcernEntry
    {
        public string AuthorName;
        public int TileX;
        public int TileY;
        public CitizenFeedConcernKind Kind;
        public double OpenedAtHour;
    }
}
