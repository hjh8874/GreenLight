using System;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.Sim
{
    /// <summary>
    /// Road congestion duration by tile. The current day and the completed
    /// previous day are kept separately so consumers never observe a partial day.
    /// </summary>
    public sealed class CongestionLedger
    {
        private float[] _today;
        private float[] _lastDay;
        private bool[] _todayObserved;
        private bool[] _lastDayObserved;
        private int _width;
        private int _height;

        public int Width => _width;
        public int Height => _height;

        public void Configure(int width, int height)
        {
            _width = Mathf.Max(0, width);
            _height = Mathf.Max(0, height);
            int length = _width * _height;
            _today = new float[length];
            _lastDay = new float[length];
            _todayObserved = new bool[length];
            _lastDayObserved = new bool[length];
        }

        public void Record(int tileIndex, CongestionLevel level, float gameHourDelta)
        {
            if (_today == null || tileIndex < 0 || tileIndex >= _today.Length)
            {
                return;
            }

            _todayObserved[tileIndex] = true;
            if (level >= CongestionLevel.Slow && gameHourDelta > 0f)
            {
                _today[tileIndex] += gameHourDelta;
            }
        }

        public void OnDayWrap()
        {
            if (_today == null)
            {
                return;
            }

            float[] hours = _lastDay;
            _lastDay = _today;
            _today = hours;

            bool[] observed = _lastDayObserved;
            _lastDayObserved = _todayObserved;
            _todayObserved = observed;

            Array.Clear(_today, 0, _today.Length);
            Array.Clear(_todayObserved, 0, _todayObserved.Length);
        }

        public float LastDayJamRatio01(int tileIndex)
        {
            if (_lastDay == null || tileIndex < 0 || tileIndex >= _lastDay.Length)
            {
                return 0f;
            }

            return Mathf.Clamp01(_lastDay[tileIndex] / 24f);
        }

        public float TodayJamHours(int tileIndex)
        {
            if (_today == null || tileIndex < 0 || tileIndex >= _today.Length)
            {
                return 0f;
            }

            return _today[tileIndex];
        }

        public float AverageLastDayJamRatio01(Vector2Int tile, int radius)
        {
            if (_lastDay == null || _width <= 0 || _height <= 0)
            {
                return 0f;
            }

            int clampedRadius = Mathf.Max(0, radius);
            float total = 0f;
            int count = 0;
            int minX = Mathf.Max(0, tile.x - clampedRadius);
            int maxX = Mathf.Min(_width - 1, tile.x + clampedRadius);
            int minY = Mathf.Max(0, tile.y - clampedRadius);
            int maxY = Mathf.Min(_height - 1, tile.y + clampedRadius);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int index = x + y * _width;
                    if (!_lastDayObserved[index]) continue;
                    total += LastDayJamRatio01(index);
                    count++;
                }
            }

            return count == 0 ? 0f : total / count;
        }

        public void Clear()
        {
            if (_today == null) return;
            Array.Clear(_today, 0, _today.Length);
            Array.Clear(_lastDay, 0, _lastDay.Length);
            Array.Clear(_todayObserved, 0, _todayObserved.Length);
            Array.Clear(_lastDayObserved, 0, _lastDayObserved.Length);
        }
    }
}
