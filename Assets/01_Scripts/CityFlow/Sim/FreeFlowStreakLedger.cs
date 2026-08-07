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

        private readonly int width;
        private readonly int height;
        private readonly float[] accumulated;

        internal FreeFlowStreakLedger(int width, int height)
        {
            this.width = Mathf.Max(0, width);
            this.height = Mathf.Max(0, height);
            accumulated = new float[this.width * this.height];
        }

        internal void RecordReset(Vector2Int tile)
        {
            if (!TryGetIndex(tile, out int index))
            {
                return;
            }

            accumulated[index] += 1f;
        }

        internal void Decay()
        {
            for (int index = 0; index < accumulated.Length; index++)
            {
                accumulated[index] *= DecayMultiplier;
            }
        }

        public float GetBottleneckIntensity(Vector2Int tile)
        {
            if (!TryGetIndex(tile, out int index))
            {
                return 0f;
            }

            return Mathf.Clamp01(accumulated[index] / IntensityScale);
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
