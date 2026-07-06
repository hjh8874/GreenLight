using UnityEngine;

namespace CityFlow
{
    // ponytail: 스텁 — 한주석의 진짜 Contracts 커밋되면 통째로 교체(enum이든 role 필드든).
    // House=수요원(통근 발생), Company·School=수요처(통근 흡수). design-doc-v2 §60 근거.
    public enum TileType
    {
        Empty,
        Road,
        House,
        Company,
        School,
    }

    // ponytail: 스텁 — 도착 이벤트 최소 형태. 실제 필드는 한주석 Contracts가 확정.
    // 통근 1건이 수요처(Sink)에 도착 → 보상 원료. 지금은 큐/Drain 검증용 최소 필드만.
    public struct ArrivalEvent
    {
        public Vector2Int Sink;
        public int Count;
    }

    // 킥오프 "약속 3개"의 좌표 규약: 1차원 flat 배열 인덱스 = y * width + x.
    public static class GridUtil
    {
        public static int Index(int x, int y, int width) => y * width + x;
    }
}
