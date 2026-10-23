using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TouchWrite.Input;
using TouchWrite.Native;
using TouchWrite.Recognition;
using TouchWrite.UI;
using WinForms = System.Windows.Forms;

namespace TouchWrite
{
    public partial class App : Application
    {
        private Mutex? _singleInstance;
        private AppSettings _settings = new();
        private MessageWindow? _messages;
        private PrecisionTouchpad? _touchpad;
        private HandwritingRecognizer? _recognizer;
        private HandwritingController? _controller;
        private WinForms.NotifyIcon? _tray;
        private TrayMenu? _trayMenu;
        private ForegroundTracker? _foreground;
        private SettingsWindow? _settingsWindow;
        private Hotkey? _registeredHotkey;
        private bool _hotkeyOk;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            int diag = Array.IndexOf(e.Args, "--diag");
            if (diag >= 0)
            {
                string outFile = diag + 1 < e.Args.Length ? e.Args[diag + 1] : Path.Combine(Path.GetTempPath(), "TouchWrite-diag.txt");
                await Diagnostics.RunAsync(outFile);
                Shutdown();
                return;
            }

            int shot = Array.IndexOf(e.Args, "--settings-shot");
            if (shot >= 0 && shot + 1 < e.Args.Length)
            {
                if (shot + 3 < e.Args.Length) Loc.SetLanguage(e.Args[shot + 3]);
                await Diagnostics.CaptureSettingsAsync(e.Args[shot + 1], shot + 2 < e.Args.Length ? e.Args[shot + 2] : "system");
                Shutdown();
                return;
            }

            int trayShot = Array.IndexOf(e.Args, "--tray-shot");
            if (trayShot >= 0 && trayShot + 1 < e.Args.Length)
            {
                string theme = trayShot + 2 < e.Args.Length ? e.Args[trayShot + 2] : "system";
                if (trayShot + 3 < e.Args.Length) Loc.SetLanguage(e.Args[trayShot + 3]);
                try { await Diagnostics.CaptureTrayMenuAsync(e.Args[trayShot + 1], theme); }
                catch (Exception ex) { Log.Error("tray-shot", ex); }
                Shutdown();
                return;
            }

            int preview = Array.IndexOf(e.Args, "--preview");
            if (preview >= 0 && preview + 1 < e.Args.Length)
            {
                if (preview + 2 < e.Args.Length) Loc.SetLanguage(e.Args[preview + 2]);
                Diagnostics.RenderPreview(e.Args[preview + 1]);
                Shutdown();
                return;
            }

            _settings = AppSettings.Load();
            Loc.SetLanguage(_settings.UiLanguage);

            _singleInstance = new Mutex(true, @"Local\TouchWrite.SingleInstance", out bool first);
            if (!first)
            {
                MessageBox.Show(Loc.T("TouchWrite 已在執行，請在系統匣（右下角）找到它。"), "TouchWrite", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            Log.Write($"Started {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
            DispatcherUnhandledException += (_, a) => { Log.Error("UI thread", a.Exception); a.Handled = true; };
            TaskScheduler.UnobservedTaskException += (_, a) => { Log.Error("background task", a.Exception); a.SetObserved(); };
            AppDomain.CurrentDomain.UnhandledException += (_, a) => { if (a.ExceptionObject is Exception ex) Log.Error("fatal", ex); };

            _recognizer = new HandwritingRecognizer();
            _recognizer.Select(_settings.RecognizerName);

            _messages = new MessageWindow();
            _touchpad = new PrecisionTouchpad();
            _messages.RawInput += _touchpad.ProcessInput;
            bool hasTouchpad = PrecisionTouchpad.IsPresent() && _touchpad.Register(_messages.Handle);

            _controller = new HandwritingController(_touchpad, _recognizer, _settings);
            _controller.ActiveChanged += active => UpdateTrayState(active);
            _messages.HotkeyPressed += OnHotkey;
            _foreground = new ForegroundTracker();

            CreateTray();
            bool hotkeyOk = _hotkeyOk = _messages.SetHotkey(_settings.Hotkey.Modifiers, _settings.Hotkey.Vk);
            _registeredHotkey = _settings.Hotkey;

            if (!hasTouchpad)
                ShowBalloon("找不到精確式觸控板", "TouchWrite 需要 Windows 精確式觸控板（Precision Touchpad）才能讀取手指位置。", WinForms.ToolTipIcon.Warning);
            else if (_recognizer.CurrentName == null || !_recognizer.HasChinese)
                ShowBalloon("沒有中文手寫辨識", "請到「設定 > 時間與語言 > 語言與地區」，在中文語言的選項裡安裝「手寫」功能。", WinForms.ToolTipIcon.Warning);
            else if (!hotkeyOk)
                ShowBalloon("快捷鍵被佔用", $"{_settings.Hotkey.Label} 已被其他程式使用，請在設定中換一個。", WinForms.ToolTipIcon.Warning);
            else
                ShowBalloon("TouchWrite 已啟動", $"按 {_settings.Hotkey.Label} 開始在觸控板上手寫。", WinForms.ToolTipIcon.Info);
        }

        private void OnHotkey()
        {
            if (_controller == null) return;
            if (!_controller.IsActive && !PrecisionTouchpad.IsPresent())
            {
                ShowBalloon("找不到精確式觸控板", "TouchWrite 需要 Windows 精確式觸控板。", WinForms.ToolTipIcon.Warning);
                return;
            }
            _controller.Toggle();
        }

        // ------------------------------------------------------------------ tray

        private void CreateTray()
        {
            _tray = new WinForms.NotifyIcon
            {
                Icon = LoadIcon(),
                Visible = true,
            };
            _tray.MouseUp += (_, a) =>
            {
                if (a.Button == WinForms.MouseButtons.Right) ShowTrayMenu();
                else if (a.Button == WinForms.MouseButtons.Left) ToggleFromTray();
            };
            UpdateTrayState(false);
        }

        /// <summary>
        /// Clicking the tray moves focus to the taskbar. Before starting handwriting from there,
        /// hand focus back to the app the user was typing in, so the text goes to the right place.
        /// </summary>
        private void ToggleFromTray()
        {
            if (_controller == null) return;
            if (!_controller.IsActive) _foreground?.RestoreFocus();
            OnHotkey();
        }

        private void ShowTrayMenu()
        {
            _trayMenu?.Close();
            bool active = _controller?.IsActive ?? false;
            var menu = new TrayMenu(active, _settings.Hotkey.Label, CurrentStatus(active));
            menu.ToggleRequested += ToggleFromTray;
            menu.SettingsRequested += () => OpenSettings();
            menu.GesturesRequested += () => OpenSettings("Gestures");
            menu.ExitRequested += ExitApp;
            menu.Closed += (_, _) => { if (_trayMenu == menu) _trayMenu = null; };
            _trayMenu = menu;
            menu.ShowNearCursor();
        }

        private TrayStatus CurrentStatus(bool active)
        {
            if (active) return TrayStatus.Writing;
            if (!PrecisionTouchpad.IsPresent()) return TrayStatus.NoTouchpad;
            if (_recognizer == null || !_recognizer.HasChinese) return TrayStatus.NoRecognizer;
            if (!_hotkeyOk) return TrayStatus.HotkeyTaken;
            return TrayStatus.Ready;
        }

        private static System.Drawing.Icon LoadIcon()
        {
            try
            {
                var res = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
                if (res != null) return new System.Drawing.Icon(res.Stream, WinForms.SystemInformation.SmallIconSize);
            }
            catch { }
            return System.Drawing.SystemIcons.Application;
        }

        private void UpdateTrayState(bool active)
        {
            if (_tray == null) return;
            string key = _settings.Hotkey.Label;
            _tray.Text = active ? Loc.T("TouchWrite：手寫中") : $"TouchWrite（{key}）";
        }

        private void ShowBalloon(string title, string text, WinForms.ToolTipIcon icon)
        {
            if (_tray == null) return;
            _tray.BalloonTipTitle = Loc.T(title);
            _tray.BalloonTipText = Loc.T(text);
            _tray.BalloonTipIcon = icon;
            _tray.ShowBalloonTip(5000);
        }

        // ------------------------------------------------------------------ settings

        private void OpenSettings(string page = "General")
        {
            if (_recognizer == null || _touchpad == null) return;
            if (_settingsWindow != null)
            {
                _settingsWindow.SelectPage(page);
                if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
                _settingsWindow.Activate();
                return;
            }
            _settingsWindow = new SettingsWindow(_settings, _recognizer.Names, PrecisionTouchpad.IsPresent(),
                _touchpad.AspectRatio, _recognizer.CurrentName, ApplySettings, PauseHotkey, page);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }

        /// <summary>While the settings window records a new hotkey, the current one must not fire.</summary>
        private void PauseHotkey(bool pause)
        {
            if (_messages == null) return;
            if (pause)
            {
                _messages.ClearHotkey();
                _registeredHotkey = null;
            }
            else if (_registeredHotkey == null)
            {
                _hotkeyOk = _messages.SetHotkey(_settings.Hotkey.Modifiers, _settings.Hotkey.Vk);
                _registeredHotkey = _settings.Hotkey;
            }
        }

        /// <summary>Applies settings changed in the settings window. Returns false if the hotkey is taken.</summary>
        private bool ApplySettings(AppSettings s)
        {
            _settings = s;
            _settings.Save();
            _recognizer?.Select(_settings.RecognizerName);
            _controller?.ApplySettings(_settings);
            bool wasSimplified = Loc.IsSimplified;
            Loc.SetLanguage(_settings.UiLanguage);
            if (wasSimplified != Loc.IsSimplified) _trayMenu?.Close();
            if (_registeredHotkey != _settings.Hotkey)
            {
                _hotkeyOk = _messages?.SetHotkey(_settings.Hotkey.Modifiers, _settings.Hotkey.Vk) ?? false;
                _registeredHotkey = _settings.Hotkey;
                Log.Write($"Hotkey {_settings.Hotkey.Label}: {(_hotkeyOk ? "registered" : "taken")}");
            }
            UpdateTrayState(_controller?.IsActive ?? false);
            return _hotkeyOk;
        }

        private async void ExitApp()
        {
            Log.Write("Exit requested");
            if (_controller != null)
            {
                var off = _controller.DeactivateAsync();
                await Task.WhenAny(off, Task.Delay(2000)); // never let a stuck recognition block quitting
            }
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _controller?.Shutdown();
            _foreground?.Dispose();
            _touchpad?.Dispose();
            _messages?.Dispose();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }

    /// <summary>`TouchWrite.exe --diag out.txt`: checks the touchpad and the recognizer without any UI.</summary>
    internal static class Diagnostics
    {
        public static async Task RunAsync(string outFile)
        {
            var sb = new StringBuilder();
            try
            {
                sb.AppendLine(PrecisionTouchpad.Describe());

                var rec = new HandwritingRecognizer();
                sb.AppendLine("Recognizers: " + string.Join(" | ", rec.Names));
                rec.Select(null);
                sb.AppendLine("Selected: " + rec.CurrentName);

                // Synthetic characters drawn in ink units (pad ≈ 1600 × 1000).
                var samples = new (string name, InkPt[][] strokes)[]
                {
                    ("一", new[] { Line(500, 550, 1100, 550) }),
                    ("十", new[] { Line(450, 500, 1150, 500), Line(800, 250, 800, 850) }),
                    ("人", new[] { Line(800, 250, 500, 850), Line(780, 450, 1150, 850) }),
                    ("口", new[] { Line(550, 300, 550, 800), Concat(Line(550, 300, 1050, 300), Line(1050, 300, 1050, 800)), Line(550, 780, 1050, 780) }),
                    ("二", new[] { Line(600, 380, 1000, 380), Line(480, 720, 1120, 720) }),
                };
                foreach (var (name, strokes) in samples)
                {
                    var result = await rec.RecognizeAsync(strokes.Select(s => (System.Collections.Generic.IReadOnlyList<InkPt>)s).ToList());
                    sb.AppendLine($"Drew {name}: {string.Join(" ", result.Take(9))}");
                }

                if (Environment.GetCommandLineArgs().Contains("--stress"))
                {
                    // Random scribbles, like real touchpad input, fired the way the app fires them
                    // (a live recognition after each stroke, sometimes overlapping a commit).
                    var rnd = new Random(1);
                    var timings = new System.Collections.Generic.List<double>();
                    int empty = 0;
                    for (int round = 0; round < 150; round++)
                    {
                        int strokeCount = 1 + rnd.Next(rnd.Next(4) == 0 ? 40 : 12);
                        var strokes = new System.Collections.Generic.List<System.Collections.Generic.IReadOnlyList<InkPt>>();
                        for (int k = 0; k < strokeCount; k++)
                        {
                            int n = 1 + rnd.Next(rnd.Next(5) == 0 ? 600 : 80);
                            double x = rnd.NextDouble() * 1600, y = rnd.NextDouble() * 1000;
                            var pts = new System.Collections.Generic.List<InkPt>();
                            for (int q = 0; q < n; q++)
                            {
                                x = Math.Clamp(x + rnd.NextDouble() * 30 - 15, 0, 1600);
                                y = Math.Clamp(y + rnd.NextDouble() * 30 - 15, 0, 1000);
                                pts.Add(new InkPt(x, y));
                            }
                            strokes.Add(pts);
                        }
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var a = rec.RecognizeAsync(strokes);
                        var b = rnd.Next(3) == 0 ? rec.RecognizeAsync(strokes.Take(Math.Max(1, strokes.Count - 1)).ToList()) : Task.FromResult(new System.Collections.Generic.List<string>());
                        var r1 = await a; await b;
                        timings.Add(sw.Elapsed.TotalMilliseconds);
                        if (r1.Count == 0) empty++;
                    }
                    timings.Sort();
                    sb.AppendLine($"Stress: {timings.Count} rounds, empty={empty}, median={timings[timings.Count / 2]:0}ms, p95={timings[(int)(timings.Count * 0.95)]:0}ms, max={timings[^1]:0}ms");
                }

                sb.AppendLine("Traditional: 汉语 发展 → " + ChineseConverter.Convert("汉语 发展", OutputScript.Traditional));
                sb.AppendLine("Simplified: 漢語 發展 → " + ChineseConverter.Convert("漢語 發展", OutputScript.Simplified));
            }
            catch (Exception ex)
            {
                sb.AppendLine("ERROR: " + ex);
            }
            File.WriteAllText(outFile, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>Opens the settings window and saves a screenshot of every page (theme: system | light | dark).</summary>
        public static async Task CaptureSettingsAsync(string dir, string theme)
        {
            Directory.CreateDirectory(dir);
            var rec = new HandwritingRecognizer();
            rec.Select(null);
            var w = new SettingsWindow(new AppSettings(), rec.Names, PrecisionTouchpad.IsPresent(), 1.625, rec.CurrentName,
                // Pretend F9 is already taken by another app, to exercise the conflict path.
                s => { Loc.SetLanguage(s.UiLanguage); return s.Hotkey.Vk != 0x78; }, _ => { });
#pragma warning disable WPF0001
            if (theme == "dark") w.ThemeMode = ThemeMode.Dark;
            else if (theme == "light") w.ThemeMode = ThemeMode.Light;
#pragma warning restore WPF0001
            w.Topmost = true;
            w.Show();
            w.Activate();
            foreach (var page in new[] { "General", "Handwriting", "Gestures", "About" })
            {
                w.SelectPage(page);
                await Task.Delay(900);
                var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                Native.NativeMethods.DwmGetWindowAttribute(hwnd, 9, out var r, System.Runtime.InteropServices.Marshal.SizeOf<Native.NativeMethods.RECT>());
                using var bmp = new System.Drawing.Bitmap(r.Right - r.Left, r.Bottom - r.Top);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                    g.CopyFromScreen(r.Left, r.Top, 0, 0, bmp.Size);
                bmp.Save(Path.Combine(dir, $"{theme}-{(Loc.IsSimplified ? "hans" : "hant")}-{page}.png"));
                if (page is "Handwriting" or "Gestures")
                {
                    w.PageScroller.ScrollToEnd();
                    await Task.Delay(500);
                    using var bmp2 = new System.Drawing.Bitmap(r.Right - r.Left, r.Bottom - r.Top);
                    using (var g = System.Drawing.Graphics.FromImage(bmp2))
                        g.CopyFromScreen(r.Left, r.Top, 0, 0, bmp2.Size);
                    bmp2.Save(Path.Combine(dir, $"{theme}-{(Loc.IsSimplified ? "hans" : "hant")}-{page}-end.png"));
                }
            }

            // Record hotkeys the way a user would: click the recorder, press a key.
            async Task Snap(string name)
            {
                await Task.Delay(600);
                var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                Native.NativeMethods.DwmGetWindowAttribute(hwnd, 9, out var r, System.Runtime.InteropServices.Marshal.SizeOf<Native.NativeMethods.RECT>());
                using var bmp = new System.Drawing.Bitmap(r.Right - r.Left, r.Bottom - r.Top);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                    g.CopyFromScreen(r.Left, r.Top, 0, 0, bmp.Size);
                bmp.Save(Path.Combine(dir, $"{theme}-{(Loc.IsSimplified ? "hans" : "hant")}-hotkey-{name}.png"));
            }
            void Press(System.Windows.Input.Key key)
            {
                var source = PresentationSource.FromVisual(w);
                w.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source!, 0, key)
                    { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
            }
            w.SelectPage("General");
            w.HotkeyRecorder.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Snap("1-recording");
            Press(System.Windows.Input.Key.F9);
            await Snap("2-taken");
            w.HotkeyRecorder.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Press(System.Windows.Input.Key.F8);
            await Snap("3-recorded");

            // Switch the language live, as a user would from the 一般 page, and capture the result.
            w.SelectPage("General");
            w.LanguageBox.SelectedIndex = Loc.IsSimplified ? 1 : 2;
            await Task.Delay(900);
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                Native.NativeMethods.DwmGetWindowAttribute(hwnd, 9, out var r, System.Runtime.InteropServices.Marshal.SizeOf<Native.NativeMethods.RECT>());
                using var bmp = new System.Drawing.Bitmap(r.Right - r.Left, r.Bottom - r.Top);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                    g.CopyFromScreen(r.Left, r.Top, 0, 0, bmp.Size);
                bmp.Save(Path.Combine(dir, $"{theme}-switched-to-{(Loc.IsSimplified ? "hans" : "hant")}.png"));
            }
            w.Close();
        }

        /// <summary>Shows the tray menu in its "ready" and "writing" states and saves screenshots.</summary>
        public static async Task CaptureTrayMenuAsync(string dir, string theme)
        {
            Directory.CreateDirectory(dir);
            foreach (bool active in new[] { false, true })
            {
                var m = new TrayMenu(active, "Ctrl + Shift + Space", active ? TrayStatus.Writing : TrayStatus.Ready);
#pragma warning disable WPF0001
                if (theme == "dark") m.ThemeMode = ThemeMode.Dark;
                else if (theme == "light") m.ThemeMode = ThemeMode.Light;
#pragma warning restore WPF0001
                m.ShowNearCursor();
                await Task.Delay(800);
                var hwnd = new System.Windows.Interop.WindowInteropHelper(m).Handle;
                Native.NativeMethods.DwmGetWindowAttribute(hwnd, 9, out var r, System.Runtime.InteropServices.Marshal.SizeOf<Native.NativeMethods.RECT>());
                const int pad = 24; // include some of what is behind, to see the edges and backdrop
                using var bmp = new System.Drawing.Bitmap(r.Right - r.Left + 2 * pad, r.Bottom - r.Top + 2 * pad);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                    g.CopyFromScreen(r.Left - pad, r.Top - pad, 0, 0, bmp.Size);
                bmp.Save(Path.Combine(dir, $"tray-{theme}-{(Loc.IsSimplified ? "hans" : "hant")}-{(active ? "writing" : "ready")}.png"));
                m.Close();
                await Task.Delay(200);
            }
        }

        /// <summary>Draws a sample "中" with candidates into the writing panel and saves it as a PNG.</summary>
        public static void RenderPreview(string path)
        {
            var w = new WritingWindow();
            w.Configure(1.625, 560, "Microsoft 中文(繁體)手寫辨識器");
            void Stroke(params (double x, double y)[] pts)
            {
                w.BeginStroke(pts[0].x, pts[0].y);
                for (int i = 1; i < pts.Length; i++)
                {
                    var (x0, y0) = pts[i - 1];
                    var (x1, y1) = pts[i];
                    for (int k = 1; k <= 12; k++) w.AddPoint(x0 + (x1 - x0) * k / 12, y0 + (y1 - y0) * k / 12);
                }
                w.EndStroke();
            }
            Stroke((0.36, 0.40), (0.37, 0.68));
            Stroke((0.36, 0.40), (0.64, 0.39), (0.63, 0.67));
            Stroke((0.37, 0.66), (0.63, 0.66));
            Stroke((0.50, 0.27), (0.505, 0.62), (0.50, 0.90));
            w.SetCandidates(new[] { "中", "申", "甲", "由", "串", "忠", "沖" }, 0, false);
            w.SetHover(1);
            w.ShowContacts(new[] { new Point(0.50, 0.90) });
            w.RenderPreview(path);
            w.Close();
        }

        private static InkPt[] Line(double x1, double y1, double x2, double y2, int steps = 24) =>
            Enumerable.Range(0, steps + 1).Select(i => new InkPt(x1 + (x2 - x1) * i / steps, y1 + (y2 - y1) * i / steps)).ToArray();

        private static InkPt[] Concat(InkPt[] a, InkPt[] b) => a.Concat(b.Skip(1)).ToArray();
    }
}
