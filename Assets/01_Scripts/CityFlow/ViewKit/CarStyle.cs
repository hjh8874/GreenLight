using UnityEngine;

namespace CityFlow.ViewKit
{
    // 해시 결정론 차량 개성 프로파일. 저장 불필요 — (Home, slot) 입력으로 매 프레임 재계산 가능.
    // 판단 없음(연출용 프로파일만) — Task 2(비주얼 적용)가 소비.
    public readonly struct CarStyle
    {
        public readonly float LengthScale, WidthScale, SpeedMul, AccelMul, DepartDelaySec;
        public readonly int ColorIndex;

        // 저채도 도시 톤 8색(채도 0.3~0.5, 명도 0.5~0.75 근방):
        // 짙은 청회 · 벽돌 · 올리브 · 머스터드 · 먹색(차콜 네이비) · 청록 · 자주 · 모래.
        public static readonly Color[] Palette =
        {
            new Color(0.34f, 0.45f, 0.55f), // 짙은 청회
            new Color(0.65f, 0.42f, 0.34f), // 벽돌
            new Color(0.56f, 0.60f, 0.35f), // 올리브
            new Color(0.72f, 0.64f, 0.40f), // 머스터드
            new Color(0.34f, 0.38f, 0.52f), // 먹색
            new Color(0.35f, 0.60f, 0.58f), // 청록
            new Color(0.60f, 0.36f, 0.52f), // 자주
            new Color(0.72f, 0.64f, 0.47f), // 모래
        };

        // 트럭 전용 도장(차급 시인성) — 개성 팔레트와 분리. SpeedFactorNumerator < 60
        // 차량에 뷰가 적용한다(M1-2).
        public static readonly Color TruckColor = new Color(0.42f, 0.40f, 0.38f); // 무광 강회

        CarStyle(float lengthScale, float widthScale, float speedMul, float accelMul, float departDelaySec, int colorIndex)
        {
            LengthScale = lengthScale;
            WidthScale = widthScale;
            SpeedMul = speedMul;
            AccelMul = accelMul;
            DepartDelaySec = departDelaySec;
            ColorIndex = colorIndex;
        }

        // 해시 = CommuteScheduler.StaggerHour 계열(73856093/19349663 XOR)에 슬롯 항 추가.
        // 32비트로는 6개 필드를 못 채우므로 2차 믹스(h2 = h1 * 2654435761, Knuth 곱셈 해시)로
        // 비트 풀을 8바이트로 확장 — 하위 8비트씩 분할해 필드별로 0..1 정규화 후 Lerp.
        public static CarStyle FromHash(Vector2Int home, int slot)
        {
            uint h1 = (uint)(home.x * 73856093) ^ (uint)(home.y * 19349663) ^ (uint)(slot * 83492791);
            uint h2 = h1 * 2654435761u;

            float t0 = (h1 & 0xFFu) / 255f;
            float t1 = ((h1 >> 8) & 0xFFu) / 255f;
            float t2 = ((h1 >> 16) & 0xFFu) / 255f;
            float t3 = ((h1 >> 24) & 0xFFu) / 255f;
            float t4 = (h2 & 0xFFu) / 255f;
            int colorByte = (int)((h2 >> 8) & 0xFFu);

            float lengthScale = Mathf.Lerp(0.85f, 1.15f, t0);
            float widthScale = Mathf.Lerp(0.9f, 1.1f, t1);
            // ±7%로 압축(M1-2): 차급 SpeedFactor(0.67)가 속도 차이의 주역이 되고,
            // 개성은 순항 위에 미세 편차만 얹는다(설계 Q4 — 교차로 권한 속도 미적용).
            float speedMul = Mathf.Lerp(0.93f, 1.07f, t2);
            float accelMul = Mathf.Lerp(0.9f, 1.1f, t3);
            float departDelaySec = Mathf.Lerp(0.1f, 0.4f, t4);
            int colorIndex = colorByte % Palette.Length;

            return new CarStyle(lengthScale, widthScale, speedMul, accelMul, departDelaySec, colorIndex);
        }
    }
}
