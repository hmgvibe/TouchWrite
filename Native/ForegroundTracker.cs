using System;
using System.Diagnostics;
using System.Text;
using static TouchWrite.Native.NativeMethods;

namespace TouchWrite.Native
{
    /// <summary>
    /// Remembers the last window the user was working in (ignoring the taskbar and TouchWrite itself),
    /// so starting handwriting from the tray can hand focus back to it — otherwise text would go to the taskbar.
    /// </summary>
    internal sealed class ForegroundTracker : IDisposable
    {
        private static readonly string[] ShellClasses =
        {
            "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
            "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow", "Windows.UI.Core.CoreWindow",
        };

        private readonly WinEventProc _proc;
        private readonly IntPtr _hook;
        private readonly uint _ownPid = (uint)Environment.ProcessId;

        public IntPtr LastAppWindow { get; private set; }

        public ForegroundTracker()
        {
            _proc = OnForegroundChanged;
            _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
            Remember(GetForegroundWindow());
        }

        private void OnForegroundChanged(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time) => Remember(hwnd);

        private void Remember(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == _ownPid) return;
            var cls = new StringBuilder(128);
            GetClassName(hwnd, cls, cls.Capacity);
            if (Array.IndexOf(ShellClasses, cls.ToString()) >= 0) return;
            LastAppWindow = hwnd;
        }

        /// <summary>Gives focus back to the last app window, if it still exists. Returns true on success.</summary>
        public bool RestoreFocus()
        {
            var hwnd = LastAppWindow;
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return false;
            if (GetForegroundWindow() == hwnd) return true;
            return SetForegroundWindow(hwnd);
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
        }
    }
}
