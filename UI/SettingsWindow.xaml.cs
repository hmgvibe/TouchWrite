using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using TouchWrite.Recognition;

namespace TouchWrite.UI
{
    /// <summary>Windows 11 style settings: navigation on the left, cards on the right. Every change applies immediately.</summary>
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly Func<AppSettings, bool> _apply;
        private readonly double _touchpadAspect;
        private readonly Action<bool> _pauseHotkey;
        private bool _loading = true;
        private bool _recording;

        /// <param name="apply">Applies and saves the settings; returns false if the hotkey could not be registered.</param>
        /// <param name="pauseHotkey">true: stop listening for the global hotkey (while a new one is being recorded); false: resume.</param>
        internal SettingsWindow(AppSettings settings, IReadOnlyList<string> recognizers, bool hasTouchpad, double touchpadAspect,
            string? currentRecognizer, Func<AppSettings, bool> apply, Action<bool> pauseHotkey, string page = "General")
        {
#pragma warning disable WPF0001 // Fluent theme is marked experimental in .NET 9
            ThemeMode = ThemeMode.System;
#pragma warning restore WPF0001
            InitializeComponent();

            _settings = settings;
            _apply = apply;
            _pauseHotkey = pauseHotkey;
            _touchpadAspect = touchpadAspect;

            // ---- status ----
            SetStatus(TouchpadBadge, TouchpadGlyph, hasTouchpad,
                hasTouchpad ? "" : "", hasTouchpad ? "SystemFillColorSuccess" : "SystemFillColorCritical");
            TouchpadStatus.Text = hasTouchpad ? "已偵測到精確式觸控板" : "沒有偵測到精確式觸控板，無法手寫";

            bool hasChinese = recognizers.Any(HandwritingRecognizer.IsChinese);
            SetStatus(RecognizerBadge, RecognizerGlyph, hasChinese,
                hasChinese ? "" : "", hasChinese ? "SystemFillColorSuccess" : "SystemFillColorCaution");
            RecognizerStatus.Text = currentRecognizer == null ? "沒有安裝任何手寫辨識" :
                hasChinese ? currentRecognizer : "沒有中文手寫辨識，請到「手寫辨識」頁安裝";

            // ---- values ----
            ShowHotkeyKeys(settings.Hotkey);
            HotkeyReset.ToolTip = Loc.T("還原預設（Ctrl + Shift + Space）");
            QuickHotkey.Text = settings.Hotkey.Label;

            RecognizerBox.ItemsSource = recognizers;
            RecognizerBox.SelectedItem = recognizers.Contains(currentRecognizer ?? "") ? currentRecognizer : recognizers.FirstOrDefault();
            RecognizerBox.IsEnabled = recognizers.Count > 0;
            if (recognizers.Count == 0)
                RecognizerHelp.Text = "這台電腦還沒有手寫辨識。在「語言與地區」中選擇中文 → 語言選項 → 安裝「手寫」。";

            ScriptBox.SelectedIndex = (int)settings.OutputScript;
            LanguageAutoItem.Content = "跟隨系統（" + (Loc.SystemPrefersSimplified() ? "简体中文" : "繁體中文") + "）";
            LanguageBox.SelectedIndex = settings.UiLanguage switch { Loc.Traditional => 1, Loc.Simplified => 2, _ => 0 };
            DelaySlider.Value = settings.CommitDelayMs;
            AutoCommitBox.IsChecked = settings.AutoCommit;
            UpdateAutoCommitUi();
            WidthSlider.Value = settings.PadWidth;
            try { StartupBox.IsChecked = AppSettings.LaunchAtStartup; } catch { StartupBox.IsEnabled = false; }

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = $"版本 {version?.ToString(3) ?? "1.0.0"}";
            SettingsPathText.Text = AppSettings.FilePath;

            BuildGestureCards();
            BuildKeyList();
            UpdateValueLabels();
            UpdatePanelMock();

            Localize();
            _loading = false;
            SelectPage(page);
        }

        // ================================================================== navigation

        public void SelectPage(string page)
        {
            foreach (ListBoxItem item in Nav.Items)
                if ((string)item.Tag == page) { Nav.SelectedItem = item; return; }
            Nav.SelectedIndex = 0;
        }

        private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Nav.SelectedItem is not ListBoxItem item) return;
            var pages = new Dictionary<string, FrameworkElement>
            {
                ["General"] = PageGeneral,
                ["Handwriting"] = PageHandwriting,
                ["Gestures"] = PageGestures,
                ["About"] = PageAbout,
            };
            foreach (var (key, el) in pages)
                el.Visibility = key == (string)item.Tag ? Visibility.Visible : Visibility.Collapsed;

            PageScroller.ScrollToTop();
            var shown = pages[(string)item.Tag];
            var shift = new TranslateTransform(0, 10);
            shown.RenderTransform = shift;
            var dur = TimeSpan.FromMilliseconds(220);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            shown.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, dur) { EasingFunction = ease });
        }

        // ================================================================== applying changes

        private void AnySetting_Changed(object sender, SelectionChangedEventArgs e) => Commit();

        private void AnySlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateValueLabels();
            UpdatePanelMock();
            Commit();
        }

        private void Commit()
        {
            if (_loading) return;
            if (RecognizerBox.SelectedItem is string rec) _settings.RecognizerName = rec;
            _settings.OutputScript = (OutputScript)Math.Max(0, ScriptBox.SelectedIndex);
            _settings.CommitDelayMs = (int)DelaySlider.Value;
            _settings.AutoCommit = AutoCommitBox.IsChecked == true;
            _settings.UiLanguage = LanguageBox.SelectedIndex switch { 1 => Loc.Traditional, 2 => Loc.Simplified, _ => Loc.Auto };
            _settings.PadWidth = WidthSlider.Value;

            bool hotkeyOk = _apply(_settings);
            QuickHotkey.Text = _settings.Hotkey.Label;
            HotkeyWarning.Visibility = hotkeyOk ? Visibility.Collapsed : Visibility.Visible;
            HotkeyWarningText.Text = $"{_settings.Hotkey.Label} 已被其他程式使用，請換一個快捷鍵。";
            BuildKeyList();
            Localize();
        }

        // ================================================================== hotkey recorder

        private void HotkeyRecorder_Click(object sender, RoutedEventArgs e)
        {
            if (_recording) StopRecording();
            else StartRecording();
        }

        private void HotkeyRecorder_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => StopRecording();

        private void HotkeyReset_Click(object sender, RoutedEventArgs e)
        {
            StopRecording();
            TrySetHotkey(Hotkey.Default);
        }

        private void StartRecording()
        {
            _recording = true;
            _pauseHotkey(true); // otherwise pressing the current hotkey would toggle handwriting instead of being recorded
            ShowRecordingPrompt(0);
            HotkeyRecorder.SetResourceReference(Control.BorderBrushProperty, "AccentFillColorDefaultBrush");
            HotkeyRecorder.BorderThickness = new Thickness(2);
            HotkeyDesc.Text = "請按下組合鍵，按 Esc 取消";
            Loc.Apply(HotkeyDesc);
            HotkeyRecorder.Focus();
        }

        private void EndRecordingUi()
        {
            _recording = false;
            HotkeyRecorder.ClearValue(Control.BorderBrushProperty);
            HotkeyRecorder.ClearValue(Control.BorderThicknessProperty);
            HotkeyDesc.Text = "點右邊的按鈕，再按下新的組合鍵即可更換";
            Loc.Apply(HotkeyDesc);
            ShowHotkeyKeys(_settings.Hotkey);
        }

        private void StopRecording()
        {
            if (!_recording) return;
            EndRecordingUi();
            _pauseHotkey(false);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (!_recording) { base.OnPreviewKeyDown(e); return; }
            e.Handled = true;
            Key key = e.Key switch { Key.System => e.SystemKey, Key.ImeProcessed => e.ImeProcessedKey, _ => e.Key };
            uint mods = CurrentModifiers();

            if (key == Key.Escape && mods == 0) { StopRecording(); return; }
            if (IsModifierKey(key)) { ShowRecordingPrompt(mods); return; }

            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            if (vk == 0) return;
            bool functionKey = vk >= 0x70 && vk <= 0x87;
            if (mods == 0 && !functionKey)
            {
                ShowRecordingPrompt(0, "需要搭配 Ctrl、Alt、Shift 或 Win");
                return;
            }

            EndRecordingUi();
            TrySetHotkey(new Hotkey(mods, vk)); // registers the new hotkey (or puts the old one back)
            _pauseHotkey(false);                 // make sure a hotkey is registered even if nothing changed
        }

        protected override void OnPreviewKeyUp(KeyEventArgs e)
        {
            if (_recording)
            {
                e.Handled = true;
                ShowRecordingPrompt(CurrentModifiers());
                return;
            }
            base.OnPreviewKeyUp(e);
        }

        private static uint CurrentModifiers()
        {
            uint mods = 0;
            var m = Keyboard.Modifiers;
            if ((m & ModifierKeys.Control) != 0) mods |= Native.NativeMethods.MOD_CONTROL;
            if ((m & ModifierKeys.Alt) != 0) mods |= Native.NativeMethods.MOD_ALT;
            if ((m & ModifierKeys.Shift) != 0) mods |= Native.NativeMethods.MOD_SHIFT;
            if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= Native.NativeMethods.MOD_WIN;
            return mods;
        }

        private static bool IsModifierKey(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

        /// <summary>While recording: show the modifiers held so far, or a prompt / hint.</summary>
        private void ShowRecordingPrompt(uint mods, string? hint = null)
        {
            HotkeyKeys.Children.Clear();
            if (mods != 0)
            {
                foreach (var part in new Hotkey(mods, 0).Label.Split(" + ").SkipLast(1)) HotkeyKeys.Children.Add(Keycap(part));
                HotkeyKeys.Children.Add(new TextBlock { Text = "…", VerticalAlignment = VerticalAlignment.Center });
                return;
            }
            var prompt = new TextBlock { Text = Loc.T(hint ?? "請按下新的快捷鍵…"), VerticalAlignment = VerticalAlignment.Center };
            prompt.SetResourceReference(TextBlock.ForegroundProperty, hint == null ? "AccentTextFillColorPrimaryBrush" : "SystemFillColorCautionBrush");
            HotkeyKeys.Children.Add(prompt);
        }

        private void ShowHotkeyKeys(Hotkey key)
        {
            HotkeyKeys.Children.Clear();
            foreach (var part in key.Label.Split(" + ")) HotkeyKeys.Children.Add(Keycap(part));
            if (HotkeyKeys.Children.Count > 0 && HotkeyKeys.Children[^1] is Border last) last.Margin = new Thickness(0);
        }

        /// <summary>Switches to a new hotkey. If Windows or another app already uses it, keeps the old one.</summary>
        private void TrySetHotkey(Hotkey newKey)
        {
            var old = _settings.Hotkey;
            _settings.Hotkey = newKey;
            bool ok = _apply(_settings);
            if (!ok && newKey != old)
            {
                _settings.Hotkey = old;
                _apply(_settings);
                HotkeyWarningText.Text = $"{newKey.Label} 已被系統或其他程式使用，仍使用 {old.Label}。";
                HotkeyWarning.Visibility = Visibility.Visible;
            }
            else if (!ok)
            {
                HotkeyWarningText.Text = $"{newKey.Label} 已被系統或其他程式使用，請換一個快捷鍵。";
                HotkeyWarning.Visibility = Visibility.Visible;
            }
            else HotkeyWarning.Visibility = Visibility.Collapsed;

            ShowHotkeyKeys(_settings.Hotkey);
            QuickHotkey.Text = _settings.Hotkey.Label;
            BuildKeyList();
            Localize();
        }

        /// <summary>Shows all text in the current interface language (see <see cref="Loc"/>).</summary>
        private void Localize()
        {
            bool wasLoading = _loading;
            _loading = true;
            Loc.Apply(this);
            // A ComboBox keeps showing the old text of its selected item until the selection is refreshed.
            foreach (var box in new[] { ScriptBox, LanguageBox })
            {
                int i = box.SelectedIndex;
                box.SelectedIndex = -1;
                box.SelectedIndex = i;
            }
            _loading = wasLoading;
            // Text inside control templates is not in the logical tree; the switches read it from these resources.
            Resources["ToggleOnText"] = Loc.T("開");
            Resources["ToggleOffText"] = Loc.T("關");
        }

        private void AutoCommitBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateAutoCommitUi();
            Commit();
        }

        private void UpdateAutoCommitUi()
        {
            bool on = AutoCommitBox.IsChecked == true;
            AutoCommitDesc.Text = on
                ? "停筆一段時間後，自動輸入第一個候選字"
                : "已關閉：寫完後點選候選字，或按 Space、兩指輕點來輸入";
            DelayRow.IsEnabled = on;
            DelayRow.Opacity = on ? 1 : 0.5;
            if (!_loading) Loc.Apply(AutoCommitDesc);
        }

        private void StartupBox_Click(object sender, RoutedEventArgs e)
        {
            try { AppSettings.LaunchAtStartup = StartupBox.IsChecked == true; }
            catch { StartupBox.IsChecked = !StartupBox.IsChecked; }
        }

        private void UpdateValueLabels()
        {
            if (DelayText == null || WidthText == null) return;
            DelayText.Text = $"{DelaySlider.Value / 1000:0.0} 秒";
            WidthText.Text = $"{WidthSlider.Value:0} px";
        }

        /// <summary>Draws the writing panel to scale inside the miniature screen.</summary>
        private void UpdatePanelMock()
        {
            if (PanelMock == null) return;
            double screenW = Math.Max(800, SystemParameters.PrimaryScreenWidth);
            double screenH = Math.Max(600, SystemParameters.PrimaryScreenHeight);
            ScreenMock.Height = Math.Round(ScreenMock.Width * screenH / screenW);
            double scale = ScreenMock.Width / screenW;
            double padW = WidthSlider.Value;
            double padH = padW / Math.Clamp(_touchpadAspect, 1.0, 2.6);
            PanelMock.Width = (padW + 26) * scale;
            PanelMock.Height = (padH + 92) * scale;
            PanelMock.Margin = new Thickness(0, 0, 0, Math.Max(4, screenH * 0.04 * scale));
            PadMock.Margin = new Thickness(12 * scale + 1, 46 * scale, 12 * scale + 1, 44 * scale);
        }

        private void OpenLanguageSettings_Click(object sender, RoutedEventArgs e) => Launch("ms-settings:regionlanguage");

        private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(AppSettings.FilePath)!);
            Launch(System.IO.Path.GetDirectoryName(AppSettings.FilePath)!);
        }

        private static void Launch(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
        }

        // ================================================================== status badges

        private static void SetStatus(Border badge, TextBlock glyph, bool ok, string icon, string colorKey)
        {
            glyph.Text = icon;
            glyph.SetResourceReference(TextBlock.ForegroundProperty, colorKey + "Brush");
            badge.SetResourceReference(Border.BackgroundProperty, ok ? "SystemFillColorSuccessBackgroundBrush" : colorKey + "BackgroundBrush");
        }

        // ================================================================== gestures page

        private enum Art { Write, Pick, Delete, Right, Down, Up }

        private void BuildGestureCards()
        {
            var items = new (Art art, string title, string desc)[]
            {
                (Art.Write, "一指書寫", "寫完一個字，停筆自動輸入"),
                (Art.Pick, "輕點頂部選字", "點觸控板頂部，換成其他候選字"),
                (Art.Delete, "點右上角 ⌫", "刪除前一個字；按住可連續刪除"),
                (Art.Right, "兩指向右滑", "輸入空格"),
                (Art.Down, "兩指向下滑", "換行（Enter）"),
                (Art.Up, "兩指向上滑", "撤銷上一筆"),
            };
            GestureGrid.Children.Clear();
            foreach (var (art, title, desc) in items)
            {
                var card = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(4), Padding = new Thickness(16, 16, 16, 18) };
                var stack = new StackPanel();
                var artHost = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(0, 10, 0, 10),
                    Margin = new Thickness(0, 0, 0, 14),
                    Child = DrawArt(art),
                };
                artHost.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorSecondaryBrush");
                stack.Children.Add(artHost);
                stack.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("CardTitle"), FontWeight = FontWeights.SemiBold });
                stack.Children.Add(new TextBlock { Text = desc, Style = (Style)FindResource("CardDesc") });
                card.Child = stack;
                GestureGrid.Children.Add(card);
            }
        }

        /// <summary>A small trackpad with finger dots and an arrow.</summary>
        private static FrameworkElement DrawArt(Art art)
        {
            const double W = 176, H = 110;
            var c = new Canvas { Width = W, Height = H };
            var pad = new Rect(12, 6, 152, 98);

            var padShape = new Rectangle { Width = pad.Width, Height = pad.Height, RadiusX = 12, RadiusY = 12, StrokeThickness = 1.5 };
            padShape.SetResourceReference(Shape.FillProperty, "ControlFillColorDefaultBrush");
            padShape.SetResourceReference(Shape.StrokeProperty, "ControlStrongStrokeColorDefaultBrush");
            Place(c, padShape, pad.X, pad.Y);

            void Finger(double x, double y)
            {
                var halo = new Ellipse { Width = 26, Height = 26, Opacity = 0.22 };
                halo.SetResourceReference(Shape.FillProperty, "AccentFillColorDefaultBrush");
                Place(c, halo, x - 13, y - 13);
                var dot = new Ellipse { Width = 13, Height = 13 };
                dot.SetResourceReference(Shape.FillProperty, "AccentFillColorDefaultBrush");
                Place(c, dot, x - 6.5, y - 6.5);
            }

            void Arrow(double x1, double y1, double x2, double y2)
            {
                double ang = Math.Atan2(y2 - y1, x2 - x1);
                var fig = new PathFigure { StartPoint = new Point(x1, y1) };
                fig.Segments.Add(new LineSegment(new Point(x2, y2), true));
                var head = new PathFigure { StartPoint = new Point(x2 - 9 * Math.Cos(ang - 0.6), y2 - 9 * Math.Sin(ang - 0.6)) };
                head.Segments.Add(new LineSegment(new Point(x2, y2), true));
                head.Segments.Add(new LineSegment(new Point(x2 - 9 * Math.Cos(ang + 0.6), y2 - 9 * Math.Sin(ang + 0.6)), true));
                var path = new Path
                {
                    Data = new PathGeometry(new[] { fig, head }),
                    StrokeThickness = 2.5,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                };
                path.SetResourceReference(Shape.StrokeProperty, "AccentTextFillColorPrimaryBrush");
                c.Children.Add(path);
            }

            double cx = pad.X + pad.Width / 2, cy = pad.Y + pad.Height / 2 + 4;
            switch (art)
            {
                case Art.Write:
                {
                    var ink = new Path
                    {
                        Data = Geometry.Parse("M 52,80 C 58,44 74,34 80,52 S 84,86 98,66 S 112,36 122,50"),
                        StrokeThickness = 3.5,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                    };
                    ink.SetResourceReference(Shape.StrokeProperty, "TextFillColorPrimaryBrush");
                    c.Children.Add(ink);
                    Finger(122, 50);
                    break;
                }
                case Art.Pick:
                case Art.Delete:
                {
                    // Candidate strip along the top, with the ⌫ key at the right end.
                    double stripH = 26;
                    var divider = new Rectangle { Width = pad.Width, Height = 1 };
                    divider.SetResourceReference(Shape.FillProperty, "ControlStrongStrokeColorDefaultBrush");
                    Place(c, divider, pad.X, pad.Y + stripH);
                    double keyW = 34;
                    double cellW = (pad.Width - 8 - keyW) / 3;
                    for (int i = 0; i < 3; i++)
                    {
                        bool hot = art == Art.Pick && i == 1;
                        var cell = new Rectangle { Width = cellW - 4, Height = stripH - 8, RadiusX = 4, RadiusY = 4 };
                        cell.SetResourceReference(Shape.FillProperty, hot ? "AccentFillColorDefaultBrush" : "SubtleFillColorSecondaryBrush");
                        Place(c, cell, pad.X + 4 + i * cellW + 2, pad.Y + 4);
                        var glyph = new Rectangle { Width = 12, Height = 3, RadiusX = 1.5, RadiusY = 1.5, Opacity = 0.7 };
                        glyph.SetResourceReference(Shape.FillProperty, hot ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorTertiaryBrush");
                        Place(c, glyph, pad.X + 4 + i * cellW + cellW / 2 - 6, pad.Y + 4 + (stripH - 8) / 2 - 1.5);
                    }
                    double keyX = pad.X + pad.Width - 4 - keyW;
                    var key = new Rectangle { Width = keyW, Height = stripH - 8, RadiusX = 4, RadiusY = 4 };
                    key.SetResourceReference(Shape.FillProperty, art == Art.Delete ? "AccentFillColorDefaultBrush" : "SubtleFillColorSecondaryBrush");
                    Place(c, key, keyX, pad.Y + 4);
                    var keyGlyph = new TextBlock
                    {
                        Text = "",
                        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                        FontSize = 11,
                        Width = keyW,
                        TextAlignment = TextAlignment.Center,
                    };
                    keyGlyph.SetResourceReference(TextBlock.ForegroundProperty, art == Art.Delete ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
                    Place(c, keyGlyph, keyX, pad.Y + 8);

                    var ink = new Path { Data = Geometry.Parse("M 70,62 L 106,62 M 88,50 L 88,90"), StrokeThickness = 3, Opacity = 0.35, StrokeEndLineCap = PenLineCap.Round, StrokeStartLineCap = PenLineCap.Round };
                    ink.SetResourceReference(Shape.StrokeProperty, "TextFillColorPrimaryBrush");
                    c.Children.Add(ink);
                    if (art == Art.Pick) Finger(pad.X + 4 + 1.5 * cellW + 10, pad.Y + 24);
                    else Finger(keyX + keyW / 2 + 6, pad.Y + 24);
                    break;
                }
                case Art.Right:
                    Finger(cx - 38, cy - 9); Finger(cx - 38, cy + 9);
                    Arrow(cx - 18, cy, cx + 38, cy);
                    break;
                case Art.Down:
                    Finger(cx - 9, cy - 30); Finger(cx + 9, cy - 30);
                    Arrow(cx, cy - 14, cx, cy + 34);
                    break;
                case Art.Up:
                    Finger(cx - 9, cy + 28); Finger(cx + 9, cy + 28);
                    Arrow(cx, cy + 12, cx, cy - 36);
                    break;
            }
            return c;
        }

        private static void Place(Canvas c, UIElement e, double x, double y)
        {
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
            c.Children.Add(e);
        }

        private void BuildKeyList()
        {
            var rows = new (string[] keys, string desc)[]
            {
                (_settings.Hotkey.Label.Split(" + "), "開始 / 停止手寫"),
                (new[] { "1", "–", "9" }, "選擇對應的候選字"),
                (new[] { "Space", "Enter" }, "立即輸入正在寫的字"),
                (new[] { "Backspace" }, "撤銷上一筆（寫字途中）"),
                (new[] { "Esc" }, "清除筆劃；沒有筆劃時結束手寫"),
            };
            KeyList.Children.Clear();
            var group = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(0) };
            var stack = new StackPanel();
            for (int i = 0; i < rows.Length; i++)
            {
                var (keys, desc) = rows[i];
                var row = new DockPanel { Margin = new Thickness(20, 12, 20, 12), MinHeight = 28 };
                var caps = new StackPanel { Orientation = Orientation.Horizontal, Width = 260, VerticalAlignment = VerticalAlignment.Center };
                foreach (var k in keys)
                {
                    if (k == "–")
                    {
                        caps.Children.Add(new TextBlock { Text = "–", Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Style = (Style)FindResource("CardDesc") });
                        continue;
                    }
                    caps.Children.Add(Keycap(k));
                }
                DockPanel.SetDock(caps, Dock.Left);
                row.Children.Add(caps);
                row.Children.Add(new TextBlock { Text = desc, Style = (Style)FindResource("CardTitle"), VerticalAlignment = VerticalAlignment.Center });
                stack.Children.Add(row);
                if (i < rows.Length - 1)
                {
                    var line = new Border { Height = 1 };
                    line.SetResourceReference(Border.BackgroundProperty, "DividerStrokeColorDefaultBrush");
                    stack.Children.Add(line);
                }
            }
            group.Child = stack;
            KeyList.Children.Add(group);
        }

        private static Border Keycap(string text)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1, 1, 1, 2),
                Padding = new Thickness(9, 3, 9, 3),
                Margin = new Thickness(0, 0, 6, 0),
                MinWidth = 28,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = text,
                    FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            };
            b.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
            b.SetResourceReference(Border.BorderBrushProperty, "ControlStrongStrokeColorDefaultBrush");
            ((TextBlock)b.Child).SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            return b;
        }
    }
}
