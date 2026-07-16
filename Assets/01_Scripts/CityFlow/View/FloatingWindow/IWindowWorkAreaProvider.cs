using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.View
{
    public readonly struct WindowWorkArea
    {
        public int MonitorIndex { get; }
        public Rect Bounds { get; }

        public WindowWorkArea(int monitorIndex, Rect bounds)
        {
            MonitorIndex = monitorIndex;
            Bounds = bounds;
        }
    }

    public interface IWindowWorkAreaProvider
    {
        IReadOnlyList<WindowWorkArea> GetWorkAreas();
    }
}
