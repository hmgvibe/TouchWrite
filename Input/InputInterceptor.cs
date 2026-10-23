using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using static TouchWrite.Native.NativeMethods;

namespace TouchWrite.Input
{
    /// <summary>
    /// While handwriting mode is on: freezes the mouse pointer (so writing on the touchpad doesn't move it or click),
    /// and lets the app claim a few keys (digits, Esc, Space...) before the focused app sees them.
    /// Hooks run on their own thread so a busy UI thread never makes the system mouse lag.
    /// </summary>
    internal sealed class InputInterceptor : IDisposable
    {
        private readonly LowLevelProc _mouseProc;
        private readonly LowLevelProc _keyboardProc;
        private readonly HashSet<uint> _swallowedKeys = new();
        private Thread? _thread;
        private uint _threadId;
        private IntPtr _mouseHook, _keyboardHook;

        /// <summary>Called on the hook thread for every physical key-down. Return true to hide the key from other apps. Must be fast.</summary>
        public Func<uint, bool>? KeyDownFilter { get; set; }

        public bool IsRunning => _thread != null && _mouseHook != IntPtr.Zero && _keyboardHook != IntPtr.Zero;

        public InputInterceptor()
        {
            _mouseProc = MouseHook;
            _keyboardProc = KeyboardHook;
        }

        public void Start()
        {
            if (_thread != null) return;
            using var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() =>
            {
                _threadId = GetCurrentThreadId();
                IntPtr mod = GetModuleHandle(null);
                _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, mod, 0);
                _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, mod, 0);
                if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
                    TouchWrite.Log.Write($"SetWindowsHookEx failed: mouse={_mouseHook}, keyboard={_keyboardHook}, error={Marshal.GetLastWin32Error()}");
                ready.Set();
                while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
                if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
                if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
                _mouseHook = _keyboardHook = IntPtr.Zero;
            })
            {
                IsBackground = true,
                Name = "TouchWrite input hooks",
                Priority = ThreadPriority.AboveNormal,
            };
            _thread.Start();
            ready.Wait(2000);
        }

        public void Stop()
        {
            var t = _thread;
            if (t == null) return;
            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            if (!t.Join(1000)) TouchWrite.Log.Write("Input hook thread did not stop within 1s");
            _thread = null;
            lock (_swallowedKeys) _swallowedKeys.Clear();
        }

        private IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                uint flags = (uint)Marshal.ReadInt32(lParam, 12);
                if ((flags & LLMHF_INJECTED) == 0)
                    return (IntPtr)1; // block physical pointer movement, clicks and scrolling
            }
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                uint vk = (uint)Marshal.ReadInt32(lParam, 0);
                uint flags = (uint)Marshal.ReadInt32(lParam, 8);
                if ((flags & LLKHF_INJECTED) == 0)
                {
                    int msg = wParam.ToInt32();
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        bool swallow = false;
                        try { swallow = KeyDownFilter?.Invoke(vk) ?? false; } catch { }
                        if (swallow)
                        {
                            lock (_swallowedKeys) _swallowedKeys.Add(vk);
                            return (IntPtr)1;
                        }
                    }
                    else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                    {
                        lock (_swallowedKeys)
                        {
                            if (_swallowedKeys.Remove(vk)) return (IntPtr)1;
                        }
                    }
                }
            }
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        public static bool IsModifierDown()
        {
            const int VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
            return (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
                || (GetAsyncKeyState(VK_MENU) & 0x8000) != 0
                || (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0
                || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
        }

        public void Dispose() => Stop();
    }
}
