using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TouchWrite.Input;
using TouchWrite.Recognition;
using TouchWrite.UI;

namespace TouchWrite
{
    /// <summary>
    /// Handwriting mode: turns touchpad frames into strokes and gestures, runs recognition, and types the result.
    ///
    /// One finger in the writing area   → ink. After a short pause the best candidate is typed (if auto-input is on).
    /// One finger tap in the top strip  → pick that candidate (replaces the text just typed).
    /// Tap the top-right corner         → delete (hold to repeat), like the ⌫ key on macOS.
    /// Two-finger swipe → / ↓ / ↑       → Space / Enter / undo last stroke.
    /// Two-finger tap                   → type the pending character now.
    /// </summary>
    internal sealed class HandwritingController
    {
        private enum Gesture { None, Writing, Candidate, Backspace, Multi, Ignore }
        private enum CandidateMode { None, Live, Committed }

        private const double InkScale = 1000;      // the pad is 1000 ink units tall
        private const double SwipeDistance = 0.16; // fraction of pad height
        private const int MaxCandidates = 9;

        private readonly Dispatcher _ui;
        private readonly PrecisionTouchpad _touchpad;
        private readonly HandwritingRecognizer _recognizer;
        private readonly InputInterceptor _interceptor = new();
        private readonly WritingWindow _window = new();
        private readonly DispatcherTimer _commitTimer;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private AppSettings _settings;

        // Ink waiting to be recognized and typed.
        private readonly List<List<InkPt>> _strokes = new();
        private List<InkPt>? _stroke;
        private int _strokeContactId;
        private long _strokeStartMs;
        private int _inkVersion;

        // Current touch gesture.
        private Gesture _gesture = Gesture.None;
        private long _gestureStartMs;
        private int _maxContacts;
        private Point _multiStart, _multiLast;
        private Point _lastSingle;
        private Point _candidateTouchStart;

        // Delete key in the top-right corner: tap to delete, hold to repeat.
        private readonly DispatcherTimer _backspaceTimer;
        private bool _backspaceRepeated;

        // Candidates shown in the strip.
        private List<string> _candidates = new();
        private CandidateMode _candidateMode = CandidateMode.None;
        private int _candidatesForVersion = -1;
        private string? _committedText;
        private Task _commitChain = Task.CompletedTask;

        // Snapshot for the keyboard hook thread.
        private volatile bool _hasInk;
        private volatile int _candidateCount;

        public bool IsActive { get; private set; }
        public event Action<bool>? ActiveChanged;

        public HandwritingController(PrecisionTouchpad touchpad, HandwritingRecognizer recognizer, AppSettings settings)
        {
            _ui = Dispatcher.CurrentDispatcher;
            _touchpad = touchpad;
            _recognizer = recognizer;
            _settings = settings;
            _touchpad.FrameReceived += OnFrame;
            _interceptor.KeyDownFilter = OnKeyDownHookThread;
            _commitTimer = new DispatcherTimer(DispatcherPriority.Normal, _ui);
            _commitTimer.Tick += async (_, _) => { _commitTimer.Stop(); await CommitPendingAsync(null); };
            _backspaceTimer = new DispatcherTimer(DispatcherPriority.Normal, _ui);
            _backspaceTimer.Tick += (_, _) =>
            {
                _backspaceRepeated = true;
                _backspaceTimer.Interval = TimeSpan.FromMilliseconds(90);
                DeleteBackward();
            };
        }

        public void ApplySettings(AppSettings settings)
        {
            _settings = settings;
            _commitTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(settings.CommitDelayMs, 250, 3000));
            if (!settings.AutoCommit) _commitTimer.Stop();
        }

        /// <summary>Starts the auto-input countdown, unless auto-input is switched off in settings.</summary>
        private void StartCommitTimer()
        {
            _commitTimer.Stop();
            if (_settings.AutoCommit && _strokes.Count > 0) _commitTimer.Start();
        }

        // ================================================================== on / off

        public void Toggle()
        {
            if (IsActive) _ = DeactivateAsync();
            else Activate();
        }

        public bool Activate()
        {
            if (IsActive) return true;
            IsActive = true;
            ApplySettings(_settings);
            ResetGesture();
            _window.Configure(_touchpad.AspectRatio, _settings.PadWidth, _recognizer.CurrentName);
            _window.ClearInk(false);
            _window.ClearCandidates();
            _window.ShowContacts(Array.Empty<Point>());
            _window.ShowOnScreen();
            _interceptor.Start();
            Log.Write($"Handwriting on (hooks {(_interceptor.IsRunning ? "installed" : "FAILED")})");
            ActiveChanged?.Invoke(true);
            return true;
        }

        /// <summary>
        /// Leaves handwriting mode. Always completes quickly: the mouse comes back and touch input stops at once;
        /// the last character on the pad is typed only if recognition answers within a short time.
        /// </summary>
        public async Task DeactivateAsync()
        {
            if (!IsActive) return;
            IsActive = false;          // stop turning touches into ink right away
            _interceptor.Stop();       // give the pointer and keyboard back
            Log.Write("Handwriting off");
            ActiveChanged?.Invoke(false);

            if (_stroke != null) EndStroke();
            var commit = CommitPendingAsync(null);
            if (await Task.WhenAny(commit, Task.Delay(1500)) != commit)
                Log.Write("Gave up typing the last character on exit (recognition did not answer)");

            if (IsActive) return;      // turned back on while we were waiting
            ResetGesture();            // also makes any late recognition result be ignored
            SetCandidates(new List<string>(), CandidateMode.None);
            _window.HideAnimated();
        }

        public void Shutdown()
        {
            _interceptor.Dispose();
            _window.Close();
        }

        // ================================================================== touch frames

        private void OnFrame(IReadOnlyList<TouchContact> contacts)
        {
            if (!IsActive) return;
            long now = _clock.ElapsedMilliseconds;
            int n = contacts.Count;
            _window.ShowContacts(contacts.Select(c => new Point(c.X, c.Y)).ToList());

            if (_gesture == Gesture.None)
            {
                if (n == 0) return;
                _gestureStartMs = now;
                _maxContacts = n;
                if (n == 1)
                {
                    var c = contacts[0];
                    _lastSingle = new Point(c.X, c.Y);
                    if (c.Y < WritingWindow.CandidateZone && c.X >= 1 - WritingWindow.BackspaceZone && _stroke == null)
                    {
                        _gesture = Gesture.Backspace;
                        _candidateTouchStart = _lastSingle;
                        _backspaceRepeated = false;
                        _backspaceTimer.Interval = TimeSpan.FromMilliseconds(500);
                        _backspaceTimer.Start();
                        _window.SetBackspacePressed(true);
                    }
                    else if (c.Y < WritingWindow.CandidateZone && _candidates.Count > 0 && _stroke == null)
                    {
                        _gesture = Gesture.Candidate;
                        _candidateTouchStart = _lastSingle;
                        _window.SetHover(CandidateIndexAt(c.X));
                    }
                    else
                    {
                        _gesture = Gesture.Writing;
                        StartStroke(c, now);
                    }
                }
                else
                {
                    _gesture = Gesture.Multi;
                    _multiStart = _multiLast = Centroid(contacts);
                }
                return;
            }

            switch (_gesture)
            {
                case Gesture.Writing:
                    if (n >= 2)
                    {
                        // A second finger landed. If the stroke has just begun it was really the start of a
                        // two-finger gesture; otherwise ignore the extra finger and keep writing.
                        if (now - _strokeStartMs < 280)
                        {
                            CancelStroke();
                            _gesture = Gesture.Multi;
                            _maxContacts = n;
                            _gestureStartMs = now;
                            _multiStart = _multiLast = Centroid(contacts);
                            return;
                        }
                    }
                    if (n == 0) { EndStroke(); _gesture = Gesture.None; return; }
                    if (contacts.All(c => c.Id != _strokeContactId))
                    {
                        // The writing finger lifted while another one stays down: finish the stroke.
                        EndStroke();
                        _gesture = Gesture.Ignore;
                        return;
                    }
                    AddStrokePoint(contacts.First(c => c.Id == _strokeContactId));
                    break;

                case Gesture.Candidate:
                    if (n == 0)
                    {
                        _window.SetHover(-1);
                        if (_lastSingle.Y < WritingWindow.CandidateZone * 1.6)
                            _ = SelectCandidateAsync(CandidateIndexAt(_lastSingle.X));
                        _gesture = Gesture.None;
                        return;
                    }
                    if (n == 1)
                    {
                        var c = contacts[0];
                        _lastSingle = new Point(c.X, c.Y);
                        if (c.Y > WritingWindow.CandidateZone * 1.6)
                        {
                            // Dragged well out of the strip: this was the top of a stroke, not a candidate tap.
                            _window.SetHover(-1);
                            _gesture = Gesture.Writing;
                            StartStroke(new TouchContact(c.Id, _candidateTouchStart.X, _candidateTouchStart.Y), now);
                            AddStrokePoint(c);
                            return;
                        }
                        _window.SetHover(CandidateIndexAt(c.X));
                    }
                    else
                    {
                        _window.SetHover(-1);
                        _gesture = Gesture.Ignore;
                    }
                    break;

                case Gesture.Backspace:
                {
                    bool OnKey(Point p) => p.Y < WritingWindow.CandidateZone * 1.6 && p.X >= 1 - WritingWindow.BackspaceZone - 0.04;
                    if (n == 0)
                    {
                        _backspaceTimer.Stop();
                        _window.SetBackspacePressed(false);
                        if (!_backspaceRepeated && OnKey(_lastSingle)) DeleteBackward();
                        _gesture = Gesture.None;
                        return;
                    }
                    if (n >= 2)
                    {
                        _backspaceTimer.Stop();
                        _window.SetBackspacePressed(false);
                        _gesture = Gesture.Ignore;
                        return;
                    }
                    var c = contacts[0];
                    _lastSingle = new Point(c.X, c.Y);
                    if (!_backspaceRepeated && c.Y > WritingWindow.CandidateZone * 1.6)
                    {
                        // Dragged down out of the key: it was the start of a stroke.
                        _backspaceTimer.Stop();
                        _window.SetBackspacePressed(false);
                        _gesture = Gesture.Writing;
                        StartStroke(new TouchContact(c.Id, _candidateTouchStart.X, _candidateTouchStart.Y), now);
                        AddStrokePoint(c);
                        return;
                    }
                    bool on = OnKey(_lastSingle);
                    _window.SetBackspacePressed(on);
                    if (!on) _backspaceTimer.Stop();
                    break;
                }

                case Gesture.Multi:
                    if (n > _maxContacts) _maxContacts = n;
                    if (n >= 2) _multiLast = Centroid(contacts);
                    if (n == 0)
                    {
                        _gesture = Gesture.None;
                        _ = HandleMultiFingerGestureAsync(now - _gestureStartMs);
                    }
                    break;

                case Gesture.Ignore:
                    if (n == 0) _gesture = Gesture.None;
                    break;
            }
        }

        private static Point Centroid(IReadOnlyList<TouchContact> contacts) =>
            new(contacts.Average(c => c.X), contacts.Average(c => c.Y));

        private int CandidateIndexAt(double x)
        {
            int count = _window.CandidateCount;
            double width = 1 - WritingWindow.BackspaceZone; // the delete key takes the right end of the strip
            if (count == 0 || x >= width) return -1;
            return Math.Clamp((int)(x / width * count), 0, count - 1);
        }

        private async Task HandleMultiFingerGestureAsync(long durationMs)
        {
            double aspect = _touchpad.AspectRatio;
            double dx = (_multiLast.X - _multiStart.X) * aspect; // in units of pad height
            double dy = _multiLast.Y - _multiStart.Y;
            double adx = Math.Abs(dx), ady = Math.Abs(dy);

            if (adx > SwipeDistance && adx > ady * 1.2)
            {
                // Delete is the key in the top-right corner (like macOS), so only a swipe to the right does something.
                if (dx > 0) { await CommitPendingAsync(null); TextSender.SendKey(TextSender.VK_SPACE); ForgetCommitted(); }
            }
            else if (ady > SwipeDistance && ady > adx * 1.2)
            {
                if (dy > 0) { await CommitPendingAsync(null); TextSender.SendKey(TextSender.VK_RETURN); ForgetCommitted(); }
                else UndoStroke();
            }
            else if (durationMs < 350 && adx < 0.06 && ady < 0.06)
            {
                await CommitPendingAsync(null);
            }
        }

        // ================================================================== strokes

        private InkPt ToInk(TouchContact c) => new(c.X * InkScale * _touchpad.AspectRatio, c.Y * InkScale);

        private void StartStroke(TouchContact c, long now)
        {
            _commitTimer.Stop();
            _inkVersion++; // a commit that is still recognizing must not swallow this stroke
            if (_candidateMode == CandidateMode.Committed) ForgetCommitted();
            _stroke = new List<InkPt> { ToInk(c) };
            _strokeContactId = c.Id;
            _strokeStartMs = now;
            _window.BeginStroke(c.X, c.Y);
            _window.SetLive(true);
        }

        private void AddStrokePoint(TouchContact c)
        {
            if (_stroke == null) return;
            var p = ToInk(c);
            var last = _stroke[^1];
            if (Math.Abs(p.X - last.X) + Math.Abs(p.Y - last.Y) < 1.5) return;
            _stroke.Add(p);
            _window.AddPoint(c.X, c.Y);
        }

        private void EndStroke()
        {
            if (_stroke == null) return;
            _strokes.Add(_stroke);
            _stroke = null;
            _window.EndStroke();
            InkChanged();
        }

        private void CancelStroke()
        {
            _stroke = null;
            _window.CancelStroke();
            _window.SetLive(false);
            StartCommitTimer();
        }

        private void UndoStroke()
        {
            if (_strokes.Count == 0) return;
            _strokes.RemoveAt(_strokes.Count - 1);
            _window.RemoveLastStroke();
            InkChanged();
        }

        private void ClearInk()
        {
            _commitTimer.Stop();
            _strokes.Clear();
            _stroke = null;
            _inkVersion++;
            _hasInk = false;
            _window.ClearInk(false);
            _window.SetLive(false);
            SetCandidates(new List<string>(), CandidateMode.None);
        }

        /// <summary>Ink was added or removed: re-recognize for the live candidate strip and restart the auto-commit timer.</summary>
        private void InkChanged()
        {
            int version = ++_inkVersion;
            _hasInk = _strokes.Count > 0;
            _window.SetLive(false);
            _commitTimer.Stop();
            if (_strokes.Count == 0)
            {
                SetCandidates(new List<string>(), CandidateMode.None);
                return;
            }
            StartCommitTimer();
            _ = RecognizeLiveAsync(version);
        }

        private async Task RecognizeLiveAsync(int version)
        {
            var snapshot = _strokes.Select(s => (IReadOnlyList<InkPt>)s.ToList()).ToList();
            var result = await _recognizer.RecognizeAsync(snapshot);
            if (version != _inkVersion || _strokes.Count == 0) return;
            SetCandidates(Prepare(result), CandidateMode.Live, version);
        }

        private List<string> Prepare(IEnumerable<string> raw) =>
            ChineseConverter.Convert(raw, _settings.OutputScript).Take(MaxCandidates).ToList();

        // ================================================================== typing

        /// <summary>
        /// Recognize the pending ink and type the chosen candidate (default: the best one).
        /// Commits are chained so text always reaches the app in order (e.g. character, then the space after it).
        /// </summary>
        private Task CommitPendingAsync(int? candidateIndex)
        {
            _commitChain = CommitAfterAsync(_commitChain, candidateIndex);
            return _commitChain;
        }

        private async Task CommitAfterAsync(Task previous, int? candidateIndex)
        {
            try { await Task.WhenAny(previous, Task.Delay(4000)); } catch { }
            try { await CommitCoreAsync(candidateIndex); }
            catch (Exception ex) { Log.Error("commit", ex); }
        }

        private async Task CommitCoreAsync(int? candidateIndex)
        {
            if (_strokes.Count == 0 || _stroke != null) return; // never cut a stroke that is still being drawn
            _commitTimer.Stop();
            int version = _inkVersion;
            List<string> cands;
            if (_candidateMode == CandidateMode.Live && _candidatesForVersion == version)
            {
                cands = _candidates;
            }
            else
            {
                var snapshot = _strokes.Select(s => (IReadOnlyList<InkPt>)s.ToList()).ToList();
                cands = Prepare(await _recognizer.RecognizeAsync(snapshot));
            }

            // New ink may have started while we were recognizing; only consume the strokes we recognized.
            if (version != _inkVersion) return;

            _strokes.Clear();
            _inkVersion++;
            _hasInk = false;
            _window.ClearInk(true);

            if (cands.Count == 0)
            {
                SetCandidates(new List<string>(), CandidateMode.None);
                return;
            }
            int idx = Math.Clamp(candidateIndex ?? 0, 0, cands.Count - 1);
            TextSender.SendText(cands[idx]);
            SetCandidates(cands, CandidateMode.Committed, selected: idx);
            _committedText = cands[idx];
        }

        private async Task SelectCandidateAsync(int index)
        {
            if (index < 0 || index >= _candidates.Count) return;
            if (_candidateMode == CandidateMode.Live)
            {
                await CommitPendingAsync(index);
            }
            else if (_candidateMode == CandidateMode.Committed && _committedText != null)
            {
                string replacement = _candidates[index];
                if (replacement != _committedText)
                {
                    TextSender.SendKey(TextSender.VK_BACK, TextSender.BackspacesFor(_committedText));
                    TextSender.SendText(replacement);
                    _committedText = replacement;
                }
                _window.SetSelected(index);
            }
        }

        private void DeleteBackward()
        {
            if (_strokes.Count > 0 || _stroke != null) { ClearInk(); return; }
            TextSender.SendKey(TextSender.VK_BACK);
            ForgetCommitted();
        }

        private void ForgetCommitted()
        {
            if (_candidateMode != CandidateMode.Committed) return;
            _committedText = null;
            SetCandidates(new List<string>(), CandidateMode.None);
        }

        private void SetCandidates(List<string> cands, CandidateMode mode, int version = -1, int selected = 0)
        {
            _candidates = cands;
            _candidateMode = cands.Count == 0 ? CandidateMode.None : mode;
            _candidatesForVersion = version;
            _candidateCount = cands.Count;
            if (_candidateMode != CandidateMode.Committed) _committedText = null;
            _window.SetCandidates(cands, cands.Count == 0 ? -1 : selected, mode == CandidateMode.Committed);
        }

        private void ResetGesture()
        {
            _gesture = Gesture.None;
            _backspaceTimer.Stop();
            _window.SetBackspacePressed(false);
            _commitTimer.Stop();
            _strokes.Clear();
            _stroke = null;
            _inkVersion++;
            _hasInk = false;
        }

        // ================================================================== keyboard (hook thread!)

        private bool OnKeyDownHookThread(uint vk)
        {
            const uint VK_BACK = 0x08, VK_RETURN = 0x0D, VK_ESCAPE = 0x1B, VK_SPACE = 0x20;
            bool modifier = InputInterceptor.IsModifierDown();
            bool ink = _hasInk;
            int cands = _candidateCount;

            int digit = -1;
            if (vk >= 0x31 && vk <= 0x39) digit = (int)(vk - 0x31);
            else if (vk >= 0x61 && vk <= 0x69) digit = (int)(vk - 0x61);

            if (!modifier)
            {
                if (digit >= 0 && digit < cands) { Post(() => _ = SelectCandidateAsync(digit)); return true; }
                if (vk == VK_ESCAPE) { Post(() => { if (_hasInk) ClearInk(); else _ = DeactivateAsync(); }); return true; }
                if (ink && vk == VK_BACK) { Post(UndoStroke); return true; }
                if (ink && (vk == VK_SPACE || vk == VK_RETURN)) { Post(() => _ = CommitPendingAsync(null)); return true; }
            }

            // Any other real keystroke means the user is typing elsewhere: the last typed text can no longer be swapped.
            if (!IsModifierKey(vk)) Post(ForgetCommitted);
            return false;
        }

        private static bool IsModifierKey(uint vk) =>
            vk is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C or 0x14;

        private void Post(Action a) => _ui.BeginInvoke(a);
    }
}
