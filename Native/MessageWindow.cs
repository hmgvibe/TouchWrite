using System;
using System.Windows.Interop;
using static TouchWrite.Native.NativeMethods;

namespace TouchWrite.Native
{
    /// <summary>Invisible message-only window that receives raw touchpad input and the global hotkey.</summary>
    internal sealed class MessageWindow : IDisposable
    {
        private const int HotkeyId = 0x5457; // "TW"
        private readonly HwndSource _source;
        private bool _hotkeyRegistered;

        public IntPtr Handle => _source.Handle;
        public event Action<IntPtr>? RawInput;
        public event Action? HotkeyPressed;

        public MessageWindow()
        {
            var p = new HwndSourceParameters("TouchWriteMessages")
            {
                ParentWindow = new IntPtr(-3), // HWND_MESSAGE
                WindowStyle = 0,
                Width = 0,
                Height = 0,
            };
            _source = new HwndSource(p);
            _source.AddHook(WndProc);
        }

        public bool SetHotkey(uint modifiers, uint vk)
        {
            if (_hotkeyRegistered) UnregisterHotKey(Handle, HotkeyId);
            _hotkeyRegistered = RegisterHotKey(Handle, HotkeyId, modifiers | MOD_NOREPEAT, vk);
            return _hotkeyRegistered;
        }

        public void ClearHotkey()
        {
            if (_hotkeyRegistered) UnregisterHotKey(Handle, HotkeyId);
            _hotkeyRegistered = false;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_INPUT)
            {
                RawInput?.Invoke(lParam);
            }
            else if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                HotkeyPressed?.Invoke();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_hotkeyRegistered) UnregisterHotKey(Handle, HotkeyId);
            _source.Dispose();
        }
    }
}
