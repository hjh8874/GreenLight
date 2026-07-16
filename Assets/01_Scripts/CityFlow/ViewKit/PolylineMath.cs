using UnityEngine;

namespace CityFlow.ViewKit
{
    // MainCityView의 곡선 수학 이식(2026-07-16). 원본과 수치 동일 — 재발명 금지.
    public static class PolylineMath
    {
        public const float QuarterCircleHandle = 0.55228475f;

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

        // 우측통행 로터리 CCW 스윕. incoming/outgoing = 진입·이탈 방향(정규화).
        // laneShift(δ=asin(laneOffset/R))로 진입/이탈 접선 연속 — 왼쪽 끌림 수정(a982075) 보존.
        public static bool TryGetRoundaboutArc(
            Vector3 incoming, Vector3 outgoing, float laneOffset, float orbitRadius,
            out float entryAngle, out float ccwSweep)
        {
            entryAngle = 0f; ccwSweep = 0f;
            if (incoming.sqrMagnitude < 0.5f || outgoing.sqrMagnitude < 0.5f) return false;
            float laneShift = Mathf.Asin(Mathf.Clamp01(laneOffset / Mathf.Max(0.01f, orbitRadius)));
            entryAngle = Mathf.Atan2(-incoming.y, -incoming.x) + laneShift;
            float exitAngle = Mathf.Atan2(outgoing.y, outgoing.x) - laneShift;
            ccwSweep = Mathf.Repeat(exitAngle - entryAngle, 2f * Mathf.PI);
            if (ccwSweep < 0.05f) ccwSweep = 2f * Mathf.PI;
            return true;
        }
    }
}
