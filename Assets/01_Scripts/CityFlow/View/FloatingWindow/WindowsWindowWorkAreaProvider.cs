using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Kirurobo;
using UnityEngine;

namespace CityFlow.View
{
    public sealed class WindowsWindowWorkAreaProvider : IWindowWorkAreaProvider
    {
        private const uint MonitorDefaultToNearest = 2;

        public IReadOnlyList<WindowWorkArea> GetWorkAreas()
        {
            int monitorCount = Mathf.Max(0, UniWindowController.GetMonitorCount());
            List<WindowWorkArea> workAreas = new List<WindowWorkArea>(monitorCount);
            Rect primaryMonitor = monitorCount > 0
                ? UniWindowController.GetMonitorRect(0)
                : new Rect(0f, 0f, Screen.currentResolution.width, Screen.currentResolution.height);
            float primaryTop = primaryMonitor.yMax;

            for (int index = 0; index < monitorCount; index++)
            {
                Rect monitorBounds = UniWindowController.GetMonitorRect(index);
                Rect workBounds = TryGetWindowsWorkArea(monitorBounds, primaryTop, out Rect windowsWorkArea)
                    ? windowsWorkArea
                    : monitorBounds;

                workAreas.Add(new WindowWorkArea(index, workBounds));
            }

            if (workAreas.Count == 0)
            {
                workAreas.Add(new WindowWorkArea(0, primaryMonitor));
            }

            return workAreas;
        }

        private static bool TryGetWindowsWorkArea(
            Rect monitorBounds,
            float primaryTop,
            out Rect workArea)
        {
            workArea = monitorBounds;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            NativePoint monitorCenter = new NativePoint
            {
                X = Mathf.RoundToInt(monitorBounds.center.x),
                Y = Mathf.RoundToInt(primaryTop - monitorBounds.center.y),
            };
            IntPtr monitorHandle = MonitorFromPoint(monitorCenter, MonitorDefaultToNearest);
            if (monitorHandle == IntPtr.Zero)
            {
                return false;
            }

            MonitorInfo monitorInfo = new MonitorInfo
            {
                Size = Marshal.SizeOf<MonitorInfo>(),
            };
            if (!GetMonitorInfo(monitorHandle, ref monitorInfo))
            {
                return false;
            }

            NativeRect nativeWork = monitorInfo.Work;
            workArea = new Rect(
                nativeWork.Left,
                primaryTop - nativeWork.Bottom,
                nativeWork.Right - nativeWork.Left,
                nativeWork.Bottom - nativeWork.Top);
            return workArea.width > 0f && workArea.height > 0f;
#else
            return false;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(
            NativePoint point,
            uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(
            IntPtr monitorHandle,
            ref MonitorInfo monitorInfo);
#endif
    }
}
