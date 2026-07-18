using UnityEngine;

namespace CityFlow.ViewKit
{
    // MainCityView의 곡선 수학 이식(2026-07-16). 원본과 수치 동일 — 재발명 금지.
    public static class PolylineMath
    {
        public const float QuarterCircleHandle = 0.55228475f;

        // 모든 차량이 같은 고정 틱 위상을 사용하면 서로 다른 경로 테이블에서도
        // 이전/현재 Sim 스냅샷의 간격을 줄이지 않고 자연스럽게 이어진다.
        public static float InterpolateTickDistance(float previous, float current, float tickProgress01)
        {
            float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(tickProgress01));
            return Mathf.Lerp(previous, current, eased);
        }

        // 한 타일에 6대를 일렬로 놓으면 차폭보다 슬롯 간격이 좁아진다.
        // 최대 3열의 2차원 배치로 바꿔 차체 크기를 줄이지 않고 주차 간격을 확보한다.
        public static Vector2 ParkingSlotOffset(int slotIndex, int slotCount, float forwardInset)
        {
            int safeCount = Mathf.Max(1, slotCount);
            if (safeCount == 1) return new Vector2(forwardInset, 0f);

            int columns = Mathf.Min(3, safeCount);
            int rows = Mathf.CeilToInt(safeCount / (float)columns);
            int safeSlot = Mathf.Clamp(slotIndex, 0, safeCount - 1);
            int row = safeSlot / columns;
            int column = safeSlot % columns;
            float forward = forwardInset + (row - (rows - 1) * 0.5f) * 0.4f;
            float side = (column - (columns - 1) * 0.5f) * 0.3f;
            return new Vector2(forward, side);
        }

        public static float RemapBezierParameterByArcLength(
            Vector3 start,
            Vector3 controlIn,
            Vector3 controlOut,
            Vector3 end,
            float normalizedDistance)
        {
            const int samples = 12;
            float totalLength = 0f;
            Vector3 previous = start;

            for (int i = 1; i <= samples; i++)
            {
                float sampleT = i / (float)samples;
                Vector3 sample = EvaluateCubicBezier(start, controlIn, controlOut, end, sampleT);
                totalLength += Vector3.Distance(previous, sample);
                previous = sample;
            }

            float targetLength = totalLength * normalizedDistance;
            float accumulated = 0f;
            previous = start;

            for (int i = 1; i <= samples; i++)
            {
                float sampleT = i / (float)samples;
                Vector3 sample = EvaluateCubicBezier(start, controlIn, controlOut, end, sampleT);
                float segmentLength = Vector3.Distance(previous, sample);
                if (accumulated + segmentLength >= targetLength)
                {
                    float localT = segmentLength > 0.0001f
                        ? (targetLength - accumulated) / segmentLength
                        : 0f;
                    return Mathf.Lerp((i - 1) / (float)samples, sampleT, localT);
                }

                accumulated += segmentLength;
                previous = sample;
            }

            return 1f;
        }

        public static Vector3 EvaluateCubicBezier(
            Vector3 start,
            Vector3 controlIn,
            Vector3 controlOut,
            Vector3 end,
            float t)
        {
            float oneMinusT = 1f - t;
            return oneMinusT * oneMinusT * oneMinusT * start
                + 3f * oneMinusT * oneMinusT * t * controlIn
                + 3f * oneMinusT * t * t * controlOut
                + t * t * t * end;
        }

        public static Vector3 EvaluateCubicBezierTangent(
            Vector3 start,
            Vector3 controlIn,
            Vector3 controlOut,
            Vector3 end,
            float t)
        {
            float oneMinusT = 1f - t;
            return (3f * oneMinusT * oneMinusT * (controlIn - start)
                + 6f * oneMinusT * t * (controlOut - controlIn)
                + 3f * t * t * (end - controlOut)).normalized;
        }

        // 우측통행 로터리 CCW 스윕 — 실제 로터리 표준 mouth±α(QA G). incoming/outgoing = 진입·이탈 방향(정규화).
        //   θ_entry = 진입구 방위각(atan2(-in)) + α,  θ_exit = 출구 방위각(atan2(out)) − α.
        // α만큼 진입/이탈점을 링 둘레(CCW 진행쪽)로 밀어 진입 헤딩과 링 접선의 각차를 줄인다 —
        // 옛 mouth-정면 모델(α=0)은 진입구 정면에 합류해 접선이 헤딩과 90° 어긋나 섬 정면 돌진 후
        // 급선회했다(라이브 "섬 부딪힌 뒤 돌아감"). 기대 스윕(α=45°): 우회전≈0 / 직진 π/2 / 좌회전 π / U턴 3π/2.
        // sweep<0.1(우회전이 링을 스침) 링 생략 판정은 호출부(ApplyRoundaboutGeometry)가 담당한다.
        public static bool TryGetRoundaboutArc(
            Vector3 incoming, Vector3 outgoing, float entryExitOffsetRad,
            out float entryAngle, out float ccwSweep)
        {
            entryAngle = 0f; ccwSweep = 0f;
            if (incoming.sqrMagnitude < 0.5f || outgoing.sqrMagnitude < 0.5f) return false;
            entryAngle = Mathf.Atan2(-incoming.y, -incoming.x) + entryExitOffsetRad;
            float exitAngle = Mathf.Atan2(outgoing.y, outgoing.x) - entryExitOffsetRad;
            ccwSweep = Mathf.Repeat(exitAngle - entryAngle, 2f * Mathf.PI);
            return true;
        }
    }
}
