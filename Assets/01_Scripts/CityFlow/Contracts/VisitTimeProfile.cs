using System;

namespace CityFlow.Contracts
{
    // 특수건물 방문이 몰리는 시간대 프리셋. AllDay = 24시간 균등(도입 전과 동일).
    public enum VisitTimeProfile
    {
        AllDay = 0,
        Daytime = 1,    // 9~18시
        Afternoon = 2,  // 12~20시
        Evening = 3,    // 17~23시
        Rush = 4        // 6~10시 + 17~21시 쌍봉
    }

    // 결정론 분위수 샘플러: index번째 방문의 예정 시각을 프로파일 CDF 역함수로 뽑는다.
    // 난수 없음 — 같은 (profile, index, count)는 항상 같은 시각(세이브·테스트 안전).
    public static class VisitTimeProfileSampler
    {
        // 시간별 가중치 24칸. 창 밖 0 = 그 시간대 방문 없음. 창 안 균등.
        static readonly float[] AllDayWeights = Uniform();
        static readonly float[] DaytimeWeights = Window((9, 18));
        static readonly float[] AfternoonWeights = Window((12, 20));
        static readonly float[] EveningWeights = Window((17, 23));
        static readonly float[] RushWeights = Window((6, 10), (17, 21));

        public static float SampleHour(
            VisitTimeProfile profile, int index, int count)
        {
            float[] weights = WeightsFor(profile);
            float total = 0f;
            for (int h = 0; h < 24; h++) total += weights[h];
            float target = (index + 0.5f) / Math.Max(1, count) * total;
            float accumulated = 0f;
            for (int h = 0; h < 24; h++)
            {
                float w = weights[h];
                if (w <= 0f) continue;
                if (target < accumulated + w)
                    return h + (target - accumulated) / w;
                accumulated += w;
            }
            return 24f - 1e-3f; // 부동소수 누적 끝단 가드
        }

        static float[] WeightsFor(VisitTimeProfile profile) => profile switch
        {
            VisitTimeProfile.Daytime => DaytimeWeights,
            VisitTimeProfile.Afternoon => AfternoonWeights,
            VisitTimeProfile.Evening => EveningWeights,
            VisitTimeProfile.Rush => RushWeights,
            _ => AllDayWeights,
        };

        static float[] Uniform()
        {
            var w = new float[24];
            for (int h = 0; h < 24; h++) w[h] = 1f;
            return w;
        }

        static float[] Window(params (int start, int end)[] spans)
        {
            var w = new float[24];
            foreach ((int start, int end) in spans)
                for (int h = start; h < end; h++) w[h] = 1f;
            return w;
        }
    }
}
