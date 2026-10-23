using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static TouchWrite.Native.NativeMethods;

namespace TouchWrite.UI
{
    /// <summary>What the header of the tray menu says about the app.</summary>
    internal enum TrayStatus { Ready, Writing, NoTouchpad, NoRecognizer, HotkeyTaken }

    /// <summary>Windows 11 style menu shown when the tray icon is right-clicked. Closes when it loses focus.</summary>
    public partial class TrayMenu : Window
    {
        private bool _closing;
        private IntPtr _hwnd;

        internal event Action? ToggleRequested;
        internal event Action? SettingsRequested;
        internal event Action? GesturesRequested;
        internal event Action? ExitRequested;

        internal TrayMenu(bool active, string hotkey, TrayStatus status)
        {
#pragma warning disable WPF0001
            ThemeMode = ThemeMode.System;
#pragma warning restore WPF0001
            InitializeComponent();

            HotkeyText.Text = hotkey;
            if (active)
            {
                ToggleButton.Style = (Style)FindResource("SecondaryButton");
                ToggleText.Text = "停止手寫";
                ToggleGlyph.Text = ""; // ✕
                HotkeyChip.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");
                HotkeyText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            }

            (string text, string brush) = status switch
            {
                TrayStatus.Writing => ("手寫中", "AccentFillColorDefaultBrush"),
                TrayStatus.NoTouchpad => ("找不到精確式觸控板", "SystemFillColorCriticalBrush"),
                TrayStatus.NoRecognizer => ("沒有中文手寫辨識", "SystemFillColorCautionBrush"),
                TrayStatus.HotkeyTaken => ("快捷鍵被其他程式佔用", "SystemFillColorCautionBrush"),
                _ => ("已就緒", "SystemFillColorSuccessBrush"),
            };
            StatusText.Text = text;
            StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
            ToggleButton.IsEnabled = status != TrayStatus.NoTouchpad || active;
            if (!ToggleButton.IsEnabled) ToggleButton.Opacity = 0.5;

            Loc.Apply(this);

            // Deactivation also happens while the window is closing; closing it again from inside that
            // message would throw inside the window procedure and crash, so defer and guard it.
            Deactivated += (_, _) => Dispatcher.BeginInvoke(new Action(CloseMenu));
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) CloseMenu();
                else if (e.Key == Key.Down) MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                else if (e.Key == Key.Up) MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
            int round = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        }

        /// <summary>Shows the menu just above the mouse pointer (i.e. above the tray icon).</summary>
        public void ShowNearCursor()
        {
            GetCursorPos(out var cursor);
            Show();
            Place(cursor);
            // If the window moved to a monitor with another DPI, WPF resizes it; place again with the final size.
            Dispatcher.BeginInvoke(new Action(() => Place(cursor)), System.Windows.Threading.DispatcherPriority.Loaded);
            Activate();
            ToggleButton.Focus();

            var dur = TimeSpan.FromMilliseconds(170);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
            RootShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, dur) { EasingFunction = ease });
        }

        private void Place(POINT cursor)
        {
            if (_hwnd == IntPtr.Zero) return;
            var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST), ref mi)) return;
            var wa = mi.rcWork;
            UpdateLayout();
            GetWindowRect(_hwnd, out var r);
            int w = r.Right - r.Left, h = r.Bottom - r.Top, gap = 12;

            int x = Math.Clamp(cursor.X - w / 2, wa.Left + gap, wa.Right - w - gap);
            int y;
            if (cursor.Y >= wa.Bottom) y = wa.Bottom - h - gap;          // taskbar at the bottom
            else if (cursor.Y <= wa.Top) y = wa.Top + gap;               // taskbar at the top
            else y = Math.Clamp(cursor.Y - h - gap, wa.Top + gap, wa.Bottom - h - gap);
            if (cursor.X >= wa.Right) x = wa.Right - w - gap;            // taskbar on the right
            else if (cursor.X <= wa.Left) x = wa.Left + gap;             // taskbar on the left

            SetWindowPos(_hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _closing = true; // however the window is being closed (by us or by the app)
            base.OnClosing(e);
        }

        private void CloseMenu()
        {
            if (_closing) return;
            _closing = true;
            Close();
        }

        // Close first, then act, so the app underneath gets focus back before anything happens.
        private void Run(Action? action)
        {
            CloseMenu();
            Dispatcher.BeginInvoke(new Action(() => action?.Invoke()));
        }

        private void Toggle_Click(object sender, RoutedEventArgs e) => Run(ToggleRequested);
        private void Settings_Click(object sender, RoutedEventArgs e) => Run(SettingsRequested);
        private void Gestures_Click(object sender, RoutedEventArgs e) => Run(GesturesRequested);
        private void Exit_Click(object sender, RoutedEventArgs e) => Run(ExitRequested);
    }
}
