namespace CityFlow.Contracts
{
    /// <summary>
    /// 장부가 불만과 해결을 짝지을 때 쓰는 구분자.
    /// CitizenFeedEventType의 복사본이 아니라 더 거친 개념이다 — Contracts의 asmdef
    /// references가 []라 Assembly-CSharp에 있는 CitizenFeedEventType을 볼 수 없다.
    /// 값이 둘뿐인 이유: 짝지을 수 있는 사건이 정체와 구급 둘뿐이다.
    /// </summary>
    public enum CitizenFeedConcernKind
    {
        Congestion = 0,
        Emergency = 1
    }
}
