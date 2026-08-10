using System.Collections.Generic;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Sim
{
    internal sealed class FreeFlowStreakLedger : IFreeFlowStreakLedger
    {
        // 플레이테스트 전 임시값: 감쇠율은 이후 밸런스 조정 대상이다.
        private const float DecayMultiplier = 0.995f;
        // 플레이테스트 전 임시값: 강도 포화 누적량은 이후 밸런스 조정 대상이다.
        private const float IntensityScale = 8f;
        // 이 값 밑으로 내려간 누적은 사전에서 빼서 감쇠 대상에서 제외한다.
        // ⚠️ 안전 마진이 뷰의 표시 임계에 의존한다. 표시 임계는 코드 상수가 아니라
        // FreeFlowStreakVfxProfileSO.bottleneckMarkerThreshold(기본 0.1) 이므로
        // 디자이너가 바꿀 수 있다. 기본값이면 누적 0.8 에서 보이기 시작해 80배 여유지만,
        // 임계를 0.00125(= PruneThreshold / IntensityScale) 미만으로 내리면
        // 가장 옅은 병목이 영영 안 보인다 — 그때는 이 값도 같이 내려야 한다.
        private const float PruneThreshold = 0.01f;

        private readonly int width;
        private readonly int height;
        // 병목이 생긴 타일만 담는다. 밀집 배열(200×200=40,000)을 매 틱 도는 것은
        // 병목이 하나도 없어도 40,000회를 쓰는 낭비다 — 대형 그리드 방향과 정면 충돌한다.
        private readonly Dictionary<int, float> accumulated = new Dictionary<int, float>();
        // 감쇠 중 제거할 키를 모으는 재사용 버퍼(틱 중 할당 0).
        private readonly List<int> pruneBuffer = new List<int>();

        internal FreeFlowStreakLedger(int width, int height)
        {
            this.width = Mathf.Max(0, width);
            this.height = Mathf.Max(0, height);
        }

        internal int TrackedTileCountForTest => accumulated.Count;

        internal void RecordReset(Vector2Int tile)
        {
            if (!TryGetIndex(tile, out int index))
            {
                return;
            }

            accumulated.TryGetValue(index, out float current);
            accumulated[index] = current + 1f;
        }

        internal void Decay()
        {
            if (accumulated.Count == 0)
            {
                return;
            }

            pruneBuffer.Clear();
            // Dictionary 는 순회 중 값 수정이 안 되므로 키를 먼저 훑고 되쓴다.
            foreach (KeyValuePair<int, float> entry in accumulated)
            {
                pruneBuffer.Add(entry.Key);
            }

            for (int i = 0; i < pruneBuffer.Count; i++)
            {
                int key = pruneBuffer[i];
                float next = accumulated[key] * DecayMultiplier;
                if (next < PruneThreshold)
                {
                    accumulated.Remove(key);
                    continue;
                }

                accumulated[key] = next;
            }
        }

        public float GetBottleneckIntensity(Vector2Int tile)
        {
            if (!TryGetIndex(tile, out int index) ||
                !accumulated.TryGetValue(index, out float value))
            {
                return 0f;
            }

            return Mathf.Clamp01(value / IntensityScale);
        }

        private bool TryGetIndex(Vector2Int tile, out int index)
        {
            if (tile.x < 0 || tile.x >= width ||
                tile.y < 0 || tile.y >= height)
            {
                index = -1;
                return false;
            }

            index = tile.y * width + tile.x;
            return true;
        }
    }
}
