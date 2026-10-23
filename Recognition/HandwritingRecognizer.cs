using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using WinFoundation = global::Windows.Foundation;
using WinInk = global::Windows.UI.Input.Inking;

namespace TouchWrite.Recognition
{
    /// <summary>A point of a stroke, in "ink units" (the writing pad is about 1000 units tall).</summary>
    internal readonly record struct InkPt(double X, double Y);

    /// <summary>Wraps the handwriting recognizers that ship with Windows language packs.</summary>
    internal sealed class HandwritingRecognizer
    {
        /// <summary>A recognition that takes longer than this is abandoned, so the app never waits forever.</summary>
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2.5);

        private WinInk.InkRecognizerContainer _container = new();
        private readonly SemaphoreSlim _lock = new(1, 1);
        private IReadOnlyList<WinInk.InkRecognizer> _recognizers = Array.Empty<WinInk.InkRecognizer>();

        public string? CurrentName { get; private set; }

        public HandwritingRecognizer()
        {
            try { _recognizers = _container.GetRecognizers(); }
            catch { _recognizers = Array.Empty<WinInk.InkRecognizer>(); }
        }

        public IReadOnlyList<string> Names => _recognizers.Select(r => r.Name).ToList();

        public bool HasChinese => _recognizers.Any(r => IsChinese(r.Name));

        public static bool IsChinese(string name) =>
            name.Contains("中文") || name.Contains("Chinese", StringComparison.OrdinalIgnoreCase);

        /// <summary>Select a recognizer by name; falls back to a Chinese one, then to the system default.</summary>
        public void Select(string? name)
        {
            var r = _recognizers.FirstOrDefault(x => x.Name == name)
                    ?? _recognizers.FirstOrDefault(x => IsChinese(x.Name) && (x.Name.Contains("繁") || x.Name.Contains("Traditional")))
                    ?? _recognizers.FirstOrDefault(x => IsChinese(x.Name))
                    ?? _recognizers.FirstOrDefault();
            if (r == null) { CurrentName = null; return; }
            _container.SetDefaultRecognizer(r);
            CurrentName = r.Name;
        }

        /// <summary>After a recognition hangs, start over with a fresh recognizer object.</summary>
        private void ResetContainer()
        {
            try
            {
                _container = new WinInk.InkRecognizerContainer();
                _recognizers = _container.GetRecognizers();
                Select(CurrentName);
            }
            catch (Exception ex) { Log.Error("reset recognizer", ex); }
        }

        /// <summary>Recognize the strokes. Returns candidates, best first (empty if nothing recognized).</summary>
        public async Task<List<string>> RecognizeAsync(IReadOnlyList<IReadOnlyList<InkPt>> strokes)
        {
            if (strokes.Count == 0 || CurrentName == null) return new List<string>();
            await _lock.WaitAsync().ConfigureAwait(true);
            try
            {
                var builder = new WinInk.InkStrokeBuilder();
                var attrs = new WinInk.InkDrawingAttributes { Size = new WinFoundation.Size(4, 4) };
                builder.SetDefaultDrawingAttributes(attrs);

                var ink = new WinInk.InkStrokeContainer();
                foreach (var s in strokes)
                {
                    if (s.Count == 0) continue;
                    var pts = new List<WinInk.InkPoint>(s.Count + 1);
                    foreach (var p in s) pts.Add(new WinInk.InkPoint(new WinFoundation.Point(p.X, p.Y), 0.5f));
                    if (pts.Count == 1) // a dot: give it a tiny length so the recognizer sees it
                        pts.Add(new WinInk.InkPoint(new WinFoundation.Point(s[0].X + 1, s[0].Y + 1), 0.5f));
                    ink.AddStroke(builder.CreateStrokeFromInkPoints(pts, Matrix3x2.Identity));
                }

                var started = DateTime.UtcNow;
                var op = _container.RecognizeAsync(ink, WinInk.InkRecognitionTarget.All).AsTask();
                if (await Task.WhenAny(op, Task.Delay(Timeout)).ConfigureAwait(true) != op)
                {
                    Log.Write($"Recognition timed out after {Timeout.TotalSeconds}s ({strokes.Count} strokes); resetting recognizer");
                    _ = op.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                    ResetContainer();
                    return new List<string>();
                }
                var results = await op.ConfigureAwait(true);
                double ms = (DateTime.UtcNow - started).TotalMilliseconds;
                if (ms > 800) Log.Write($"Slow recognition: {ms:0} ms ({strokes.Count} strokes)");
                return DemoteSymbols(Combine(results.Select(r => (IReadOnlyList<string>)r.GetTextCandidates().ToList()).ToList()));
            }
            catch (Exception ex)
            {
                Log.Error($"recognition ({strokes.Count} strokes)", ex);
                return new List<string>();
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// The recognizer often ranks look-alike symbols first ("+" for 十, "¥" for 口).
        /// Keep letters, digits and characters ahead of pure-symbol candidates.
        /// </summary>
        private static List<string> DemoteSymbols(List<string> candidates)
        {
            static bool IsSymbolOnly(string s) =>
                s.All(ch => char.IsPunctuation(ch) || char.IsSymbol(ch) || char.IsWhiteSpace(ch));
            if (candidates.Count == 0 || !IsSymbolOnly(candidates[0])) return candidates;
            string? firstWord = candidates.FirstOrDefault(c => !IsSymbolOnly(c));
            if (firstWord == null) return candidates;
            // Move the best real character to the front; everything else keeps its order.
            return new[] { firstWord }.Concat(candidates.Where(c => c != firstWord)).ToList();
        }

        /// <summary>Several recognized segments (e.g. two characters) → whole-string candidates.</summary>
        private static List<string> Combine(List<IReadOnlyList<string>> segments)
        {
            segments = segments.Where(s => s.Count > 0).ToList();
            var outList = new List<string>();
            if (segments.Count == 0) return outList;
            if (segments.Count == 1) return segments[0].Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();

            string top = string.Concat(segments.Select(s => s[0]));
            outList.Add(top);
            // Vary one segment at a time, last segment first (usually the newest character).
            for (int rank = 1; rank < 10; rank++)
            {
                for (int i = segments.Count - 1; i >= 0; i--)
                {
                    if (rank >= segments[i].Count) continue;
                    var parts = segments.Select(s => s[0]).ToArray();
                    parts[i] = segments[i][rank];
                    outList.Add(string.Concat(parts));
                }
            }
            return outList.Distinct().ToList();
        }
    }
}
