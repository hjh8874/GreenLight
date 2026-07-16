using System;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;
using Process = System.Diagnostics.Process;

namespace CityFlow.View
{
    public sealed class WindowsFloatingWindowHost : IDisposable
    {
        private const int WindowStyleIndex = -16;
        private const int WindowProcedureIndex = -4;

        private const int WindowStyleCaption = 0x00C00000;
        private const int WindowStyleThickFrame = 0x00040000;
        private const int WindowStylePopup = unchecked((int)0x80000000);
        private const int WindowStyleVisible = 0x10000000;

        private const uint SetWindowPositionNoSize = 0x0001;
        private const uint SetWindowPositionNoMove = 0x0002;
        private const uint SetWindowPositionNoZOrder = 0x0004;
        private const uint SetWindowPositionFrameChanged = 0x0020;
        private const uint MonitorDefaultToNearest = 0x00000002;

        private const int ShowWindowHide = 0;
        private const int ShowWindowRestore = 9;
        private const int ShowWindowMinimize = 6;

        private const uint NotifyIconAdd = 0x00000000;
        private const uint NotifyIconDelete = 0x00000002;
        private const uint NotifyIconMessage = 0x00000001;
        private const uint NotifyIconIcon = 0x00000002;
        private const uint NotifyIconTip = 0x00000004;

        private const uint WindowMessageApplication = 0x8000;
        private const uint TrayCallbackMessage = WindowMessageApplication + 0x51;
        private const int WindowMessageLeftButtonDoubleClick = 0x0203;
        private const int WindowMessageRightButtonUp = 0x0205;

        private const uint MenuString = 0x00000000;
        private const uint TrackPopupLeftAlign = 0x0000;
        private const uint TrackPopupBottomAlign = 0x0020;
        private const uint TrackPopupReturnCommand = 0x0100;
        private const uint TrackPopupRightButton = 0x0002;
        private const uint OpenMenuCommand = 1001;
        private const uint ExitMenuCommand = 1002;
        private static readonly IntPtr DefaultApplicationIcon = new IntPtr(32512);

        private static readonly WindowProcedure WindowProcedureDelegate = WindowProcedureCallback;
        private static WindowsFloatingWindowHost activeInstance;

        private IntPtr windowHandle;
        private IntPtr previousWindowProcedure;
        private NotifyIconData notifyIconData;
        private bool trayIconVisible;
        private bool restoreRequested;
        private bool exitRequested;

        public bool IsAttached => windowHandle != IntPtr.Zero;

        public bool TryAttach()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (IsAttached)
            {
                return true;
            }

            using (Process process = Process.GetCurrentProcess())
            {
                process.Refresh();
                windowHandle = process.MainWindowHandle;
            }
            if (windowHandle == IntPtr.Zero)
            {
                windowHandle = GetActiveWindow();
            }

            if (windowHandle == IntPtr.Zero)
            {
                Debug.LogWarning("[WindowsFloatingWindowHost] Unity window handle was not found.");
                return false;
            }

            activeInstance = this;
            IntPtr callbackPointer = Marshal.GetFunctionPointerForDelegate(WindowProcedureDelegate);
            previousWindowProcedure = SetWindowProcedure(windowHandle, callbackPointer);
            return true;
#else
            return false;
#endif
        }

        public void ApplyBorderless()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!TryAttach())
            {
                return;
            }

            int style = GetWindowLong(windowHandle, WindowStyleIndex);
            style &= ~(WindowStyleCaption | WindowStyleThickFrame);
            style |= WindowStylePopup | WindowStyleVisible;
            SetWindowLong(windowHandle, WindowStyleIndex, style);
            SetWindowPos(
                windowHandle,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SetWindowPositionNoMove
                | SetWindowPositionNoSize
                | SetWindowPositionNoZOrder
                | SetWindowPositionFrameChanged);
#endif
        }

        public void MinimizeToTaskbar()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (TryAttach())
            {
                ShowWindow(windowHandle, ShowWindowMinimize);
            }
#endif
        }

        public bool FitToNearestMonitorWorkArea()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!TryGetWindowAndWorkArea(out _, out NativeRect workArea))
            {
                return false;
            }

            return SetWindowBounds(workArea);
#else
            return false;
#endif
        }

        public bool ConstrainToNearestMonitorWorkArea()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!TryGetWindowAndWorkArea(out NativeRect windowRect, out NativeRect workArea))
            {
                return false;
            }

            int workWidth = Math.Max(1, workArea.Right - workArea.Left);
            int workHeight = Math.Max(1, workArea.Bottom - workArea.Top);
            int width = Math.Min(Math.Max(1, windowRect.Right - windowRect.Left), workWidth);
            int height = Math.Min(Math.Max(1, windowRect.Bottom - windowRect.Top), workHeight);
            int maxLeft = workArea.Right - width;
            int maxTop = workArea.Bottom - height;
            int left = Math.Max(workArea.Left, Math.Min(windowRect.Left, maxLeft));
            int top = Math.Max(workArea.Top, Math.Min(windowRect.Top, maxTop));

            return SetWindowBounds(new NativeRect
            {
                Left = left,
                Top = top,
                Right = left + width,
                Bottom = top + height,
            });
#else
            return false;
#endif
        }

        public void HideToSystemTray()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!TryAttach())
            {
                Debug.LogWarning("[WindowsFloatingWindowHost] The window could not be attached for tray mode.");
                return;
            }

            if (!EnsureTrayIcon())
            {
                return;
            }

            ShowWindow(windowHandle, ShowWindowHide);
            Debug.Log("[WindowsFloatingWindowHost] Window hidden to the system tray.");
#endif
        }

        public void RestoreFromSystemTray()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!TryAttach())
            {
                return;
            }

            RemoveTrayIcon();
            ShowWindow(windowHandle, ShowWindowRestore);
            SetForegroundWindow(windowHandle);
            ApplyBorderless();
#endif
        }

        public void PollRequests(out bool shouldRestore, out bool shouldExit)
        {
            shouldRestore = restoreRequested;
            shouldExit = exitRequested;
            restoreRequested = false;
            exitRequested = false;
        }

        public void Dispose()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            RemoveTrayIcon();

            if (windowHandle != IntPtr.Zero && previousWindowProcedure != IntPtr.Zero)
            {
                SetWindowProcedure(windowHandle, previousWindowProcedure);
            }

            if (ReferenceEquals(activeInstance, this))
            {
                activeInstance = null;
            }

            previousWindowProcedure = IntPtr.Zero;
            windowHandle = IntPtr.Zero;
#endif
        }

        private bool EnsureTrayIcon()
        {
            if (trayIconVisible)
            {
                return true;
            }

            notifyIconData = new NotifyIconData
            {
                Size = Marshal.SizeOf<NotifyIconData>(),
                WindowHandle = windowHandle,
                Id = 1,
                Flags = NotifyIconMessage | NotifyIconIcon | NotifyIconTip,
                CallbackMessage = TrayCallbackMessage,
                IconHandle = LoadIcon(IntPtr.Zero, DefaultApplicationIcon),
                Tip = "GreenLight - double-click to restore",
            };

            trayIconVisible = ShellNotifyIcon(NotifyIconAdd, ref notifyIconData);
            if (!trayIconVisible)
            {
                Debug.LogWarning("[WindowsFloatingWindowHost] System tray icon could not be created.");
            }

            return trayIconVisible;
        }

        private bool TryGetWindowAndWorkArea(
            out NativeRect windowRect,
            out NativeRect workArea)
        {
            windowRect = default;
            workArea = default;

            if (!TryAttach() || !GetWindowRect(windowHandle, out windowRect))
            {
                Debug.LogWarning("[WindowsFloatingWindowHost] Native window bounds could not be read.");
                return false;
            }

            IntPtr monitorHandle = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
            if (monitorHandle == IntPtr.Zero)
            {
                Debug.LogWarning("[WindowsFloatingWindowHost] A monitor could not be selected for the window.");
                return false;
            }

            MonitorInfo monitorInfo = new MonitorInfo
            {
                Size = Marshal.SizeOf<MonitorInfo>(),
            };
            if (!GetMonitorInfo(monitorHandle, ref monitorInfo))
            {
                Debug.LogWarning("[WindowsFloatingWindowHost] Monitor work area could not be read.");
                return false;
            }

            workArea = monitorInfo.Work;
            return workArea.Right > workArea.Left && workArea.Bottom > workArea.Top;
        }

        private bool SetWindowBounds(NativeRect bounds)
        {
            int width = Math.Max(1, bounds.Right - bounds.Left);
            int height = Math.Max(1, bounds.Bottom - bounds.Top);
            return SetWindowPos(
                windowHandle,
                IntPtr.Zero,
                bounds.Left,
                bounds.Top,
                width,
                height,
                SetWindowPositionNoZOrder);
        }

        private void RemoveTrayIcon()
        {
            if (!trayIconVisible)
            {
                return;
            }

            ShellNotifyIcon(NotifyIconDelete, ref notifyIconData);
            trayIconVisible = false;
        }

        private void ShowTrayMenu()
        {
            IntPtr menu = CreatePopupMenu();
            if (menu == IntPtr.Zero)
            {
                return;
            }

            AppendMenu(menu, MenuString, OpenMenuCommand, "Open GreenLight");
            AppendMenu(menu, MenuString, ExitMenuCommand, "Exit");
            GetCursorPos(out NativePoint cursorPosition);
            SetForegroundWindow(windowHandle);

            uint command = TrackPopupMenu(
                menu,
                TrackPopupLeftAlign
                | TrackPopupBottomAlign
                | TrackPopupReturnCommand
                | TrackPopupRightButton,
                cursorPosition.X,
                cursorPosition.Y,
                0,
                windowHandle,
                IntPtr.Zero);
            DestroyMenu(menu);

            if (command == OpenMenuCommand)
            {
                restoreRequested = true;
            }
            else if (command == ExitMenuCommand)
            {
                exitRequested = true;
            }
        }

        [MonoPInvokeCallback(typeof(WindowProcedure))]
        private static IntPtr WindowProcedureCallback(
            IntPtr handle,
            uint message,
            IntPtr wordParameter,
            IntPtr longParameter)
        {
            WindowsFloatingWindowHost instance = activeInstance;
            if (instance != null
                && handle == instance.windowHandle
                && message == TrayCallbackMessage)
            {
                int mouseMessage = unchecked((int)longParameter.ToInt64());
                if (mouseMessage == WindowMessageLeftButtonDoubleClick)
                {
                    instance.restoreRequested = true;
                    return IntPtr.Zero;
                }

                if (mouseMessage == WindowMessageRightButtonUp)
                {
                    instance.ShowTrayMenu();
                    return IntPtr.Zero;
                }
            }

            if (instance != null && instance.previousWindowProcedure != IntPtr.Zero)
            {
                return CallWindowProc(
                    instance.previousWindowProcedure,
                    handle,
                    message,
                    wordParameter,
                    longParameter);
            }

            return DefWindowProc(handle, message, wordParameter, longParameter);
        }

        private static IntPtr SetWindowProcedure(IntPtr handle, IntPtr procedure)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(handle, WindowProcedureIndex, procedure)
                : new IntPtr(SetWindowLong32(handle, WindowProcedureIndex, procedure.ToInt32()));
        }

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

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NotifyIconData
        {
            public int Size;
            public IntPtr WindowHandle;
            public uint Id;
            public uint Flags;
            public uint CallbackMessage;
            public IntPtr IconHandle;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Tip;

            public uint State;
            public uint StateMask;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string Info;

            public uint TimeoutOrVersion;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string InfoTitle;

            public uint InfoFlags;
            public Guid GuidItem;
            public IntPtr BalloonIconHandle;
        }

        private delegate IntPtr WindowProcedure(
            IntPtr handle,
            uint message,
            IntPtr wordParameter,
            IntPtr longParameter);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr handle, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr handle, int index, int value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr handle, int index, int value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr handle, int index, IntPtr value);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr handle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(
            IntPtr handle,
            out NativeRect rect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(
            IntPtr handle,
            uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(
            IntPtr monitorHandle,
            ref MonitorInfo monitorInfo);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr handle, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(
            IntPtr previousProcedure,
            IntPtr handle,
            uint message,
            IntPtr wordParameter,
            IntPtr longParameter);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(
            IntPtr handle,
            uint message,
            IntPtr wordParameter,
            IntPtr longParameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AppendMenu(
            IntPtr menu,
            uint flags,
            uint itemId,
            string itemText);

        [DllImport("user32.dll")]
        private static extern uint TrackPopupMenu(
            IntPtr menu,
            uint flags,
            int x,
            int y,
            int reserved,
            IntPtr window,
            IntPtr rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyMenu(IntPtr menu);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out NativePoint point);
    }
}
