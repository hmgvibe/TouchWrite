using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using static TouchWrite.Native.NativeMethods;

namespace TouchWrite.UI
{
    /// <summary>The floating panel that mirrors the touchpad: candidate strip on top, ink below.</summary>
    public partial class WritingWindow : Window
    {
        /// <summary>Top fraction of the touchpad used as the candidate strip.</summary>
        public const double CandidateZone = 0.2;

        /// <summary>Right-hand fraction of the candidate strip used by the delete key.</summary>
        public const double BackspaceZone = 0.15;

        private static readonly Brush InkBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF7)));
        private static readonly Brush SelectedBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF)));
        private static readonly Brush HoverBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)));
        private static readonly Brush ContactBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x55, 0x0A, 0x84, 0xFF)));

        private double _padW = 560, _padH = 350;
        private Polyline? _currentStroke;
        private readonly List<Border> _candidateCells = new();
        private int _selected = -1, _hover = -1;
        private bool _committedStyle;
        private readonly List<Ellipse> _contactDots = new();
        private IntPtr _hwnd;
        private bool _visible;

        public WritingWindow()
        {
            InitializeComponent();
        }

        private static Brush Frozen(SolidColorBrush b) { b.Freeze(); return b; }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
            long ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
            ex |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT;
            SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(ex));
        }

        // ------------------------------------------------------------------ layout

        public void Configure(double aspect, double padWidth, string? recognizerName)
        {
            _padW = Math.Clamp(padWidth, 360, 1000);
            _padH = Math.Round(_padW / Math.Clamp(aspect, 1.0, 2.6));
            PadFrame.Width = _padW + 2;
            PadFrame.Height = _padH + 2;
            Pad.Width = _padW;
            Pad.Height = _padH;
            CandidateStrip.Height = Math.Round(_padH * CandidateZone);
            BackspaceColumn.Width = new GridLength(Math.Round(_padW * BackspaceZone));
            WritingHint.Margin = new Thickness(0, CandidateStrip.Height, 0, 0);
            RecognizerLabel.Text = recognizerName ?? "未找到手寫辨識引擎";
            Loc.Apply(this);
        }

        private Point ToPad(double x, double y) => new(x * _padW, y * _padH);

        // ------------------------------------------------------------------ show / hide

        public void ShowOnScreen()
        {
            if (!IsVisible) Show();
            PlaceNearForegroundWindow();
            // DPI may change after moving to another monitor; place again once WPF has re-laid out.
            Dispatcher.BeginInvoke(new Action(PlaceNearForegroundWindow), System.Windows.Threading.DispatcherPriority.Loaded);

            _visible = true;
            var dur = TimeSpan.FromMilliseconds(180);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
            RootShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dur) { EasingFunction = ease });
        }

        public void HideAnimated()
        {
            _visible = false;
            var dur = TimeSpan.FromMilliseconds(140);
            var fade = new DoubleAnimation(0, dur);
            fade.Completed += (_, _) => { if (!_visible) Hide(); };
            Root.BeginAnimation(OpacityProperty, fade);
            RootShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, dur));
        }

        private void PlaceNearForegroundWindow()
        {
            if (_hwnd == IntPtr.Zero) return;
            IntPtr fg = GetForegroundWindow();
            IntPtr mon;
            if (fg != IntPtr.Zero) mon = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
            else { GetCursorPos(out var pt); mon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST); }

            var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(mon, ref mi)) return;
            var wa = mi.rcWork;

            UpdateLayout();
            GetWindowRect(_hwnd, out var r);
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            int x = wa.Left + (wa.Right - wa.Left - w) / 2;
            int y = wa.Bottom - h - (int)((wa.Bottom - wa.Top) * 0.04);
            SetWindowPos(_hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        }

        // ------------------------------------------------------------------ ink

        public bool HasInk => InkLayer.Children.Count > 0;

        public void BeginStroke(double x, double y)
        {
            WritingHint.Visibility = Visibility.Collapsed;
            _currentStroke = new Polyline
            {
                Stroke = InkBrush,
                StrokeThickness = Math.Max(4.5, _padW / 100),
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            var p = ToPad(x, y);
            _currentStroke.Points.Add(p);
            _currentStroke.Points.Add(p);
            InkLayer.Children.Add(_currentStroke);
        }

        public void AddPoint(double x, double y) => _currentStroke?.Points.Add(ToPad(x, y));

        public void EndStroke() => _currentStroke = null;

        public void CancelStroke()
        {
            if (_currentStroke != null) InkLayer.Children.Remove(_currentStroke);
            _currentStroke = null;
            if (!HasInk) WritingHint.Visibility = Visibility.Visible;
        }

        public void RemoveLastStroke()
        {
            if (InkLayer.Children.Count > 0) InkLayer.Children.RemoveAt(InkLayer.Children.Count - 1);
            _currentStroke = null;
            if (!HasInk) WritingHint.Visibility = Visibility.Visible;
        }

        /// <summary>Clears the ink. With <paramref name="animate"/>, the strokes float up and fade like they were "sent".</summary>
        public void ClearInk(bool animate)
        {
            _currentStroke = null;
            if (animate && InkLayer.Children.Count > 0)
            {
                var layer = new Canvas { IsHitTestVisible = false };
                var shift = new TranslateTransform();
                layer.RenderTransform = shift;
                while (InkLayer.Children.Count > 0)
                {
                    var child = InkLayer.Children[0];
                    InkLayer.Children.RemoveAt(0);
                    layer.Children.Add(child);
                }
                FadeHost.Children.Add(layer);
                var dur = TimeSpan.FromMilliseconds(260);
                var fade = new DoubleAnimation(1, 0, dur);
                fade.Completed += (_, _) => FadeHost.Children.Remove(layer);
                layer.BeginAnimation(OpacityProperty, fade);
                shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -_padH * 0.08, dur) { EasingFunction = new CubicEase() });
            }
            else
            {
                InkLayer.Children.Clear();
            }
            WritingHint.Visibility = Visibility.Visible;
        }

        // ------------------------------------------------------------------ finger dots

        public void ShowContacts(IReadOnlyList<Point> normalized)
        {
            while (_contactDots.Count < normalized.Count)
            {
                var e = new Ellipse { Width = 22, Height = 22, Fill = ContactBrush };
                _contactDots.Add(e);
                ContactLayer.Children.Add(e);
            }
            for (int i = 0; i < _contactDots.Count; i++)
            {
                var dot = _contactDots[i];
                if (i < normalized.Count)
                {
                    var p = ToPad(normalized[i].X, normalized[i].Y);
                    Canvas.SetLeft(dot, p.X - dot.Width / 2);
                    Canvas.SetTop(dot, p.Y - dot.Height / 2);
                    dot.Visibility = Visibility.Visible;
                }
                else dot.Visibility = Visibility.Collapsed;
            }
        }

        // ------------------------------------------------------------------ candidates

        public int CandidateCount => _candidateCells.Count;

        /// <param name="committedStyle">true = these are alternatives for text already typed (selection replaces it).</param>
        public void SetCandidates(IReadOnlyList<string> candidates, int selected, bool committedStyle)
        {
            CandidateGrid.Children.Clear();
            _candidateCells.Clear();
            _committedStyle = committedStyle;
            _hover = -1;
            int n = candidates.Count;
            CandidateGrid.Columns = Math.Max(1, n);
            double fontSize = Math.Clamp(CandidateStrip.Height * 0.42, 16, 30);
            for (int i = 0; i < n; i++)
            {
                var number = new TextBlock
                {
                    Text = (i + 1).ToString(),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)),
                    Margin = new Thickness(6, 3, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                };
                var text = new TextBlock
                {
                    Text = candidates[i],
                    FontSize = candidates[i].Length > 2 ? fontSize * 0.75 : fontSize,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                var cell = new Border
                {
                    CornerRadius = new CornerRadius(9),
                    Margin = new Thickness(2),
                    Child = new Grid { Children = { text, number } },
                };
                _candidateCells.Add(cell);
                CandidateGrid.Children.Add(cell);
            }
            CandidatePlaceholder.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
            _selected = selected;
            RefreshCells();
        }

        public void ClearCandidates() => SetCandidates(Array.Empty<string>(), -1, false);

        public void SetSelected(int index)
        {
            _selected = index;
            RefreshCells();
        }

        public void SetHover(int index)
        {
            if (_hover == index) return;
            _hover = index;
            RefreshCells();
        }

        private void RefreshCells()
        {
            for (int i = 0; i < _candidateCells.Count; i++)
            {
                Brush bg = Brushes.Transparent;
                if (i == _hover) bg = HoverBrush;
                else if (i == _selected) bg = _committedStyle ? SelectedBrush : HoverBrush;
                _candidateCells[i].Background = bg;
            }
        }

        /// <summary>Renders the panel (as currently drawn) to a PNG over a sample backdrop. Used by `--preview`.</summary>
        public void RenderPreview(string path)
        {
            Root.BeginAnimation(OpacityProperty, null);
            Root.Opacity = 1;
            RootShift.Y = 0;
            Root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Root.Arrange(new Rect(Root.DesiredSize));
            Root.UpdateLayout();

            int w = (int)Math.Ceiling(Root.ActualWidth + Root.Margin.Left + Root.Margin.Right);
            int h = (int)Math.Ceiling(Root.ActualHeight + Root.Margin.Top + Root.Margin.Bottom);
            var backdrop = new DrawingVisual();
            using (var dc = backdrop.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x7A, 0x9C, 0xC6), Color.FromRgb(0xC9, 0xB6, 0xD9), 45), null, new Rect(0, 0, w, h));
                dc.PushTransform(new TranslateTransform(Root.Margin.Left, Root.Margin.Top));
                dc.DrawRectangle(new VisualBrush(Root), null, new Rect(0, 0, Root.ActualWidth, Root.ActualHeight));
            }
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(backdrop);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var fs = System.IO.File.Create(path);
            enc.Save(fs);
        }

        public void SetBackspacePressed(bool pressed) =>
            BackspaceKey.Background = pressed ? SelectedBrush : new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));

        public void SetLive(bool writing) =>
            LiveDot.Fill = writing ? SelectedBrush : new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
    }
}
