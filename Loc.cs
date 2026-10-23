using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using TouchWrite.Recognition;

namespace TouchWrite
{
    /// <summary>
    /// Interface language. All UI text is written in Traditional Chinese; for Simplified Chinese it is converted at
    /// runtime: first the words that differ between regions (滑鼠 → 鼠标, 設定 → 设置 …), then character by character
    /// with the converter built into Windows.
    /// </summary>
    internal static class Loc
    {
        public const string Auto = "Auto";
        public const string Traditional = "zh-Hant";
        public const string Simplified = "zh-Hans";

        /// <summary>Elements tagged with this are left alone (e.g. handwriting candidates, language names).</summary>
        public const string NoLocTag = "NoLoc";

        public static bool IsSimplified { get; private set; }

        /// <summary>Words that are not just a different character form in Simplified Chinese. Longest first.</summary>
        private static readonly (string From, string To)[] Terms =
        {
            ("以系統管理員身分執行", "以管理员身份运行"),
            ("時間與語言", "时间和语言"),
            ("語言與地區", "语言和区域"),
            ("系統管理員", "管理员"),
            ("語言套件", "语言包"),
            ("設定檔", "配置文件"),
            ("資料夾", "文件夹"),
            ("系統匣", "系统托盘"),
            ("滑鼠", "鼠标"),
            ("螢幕", "屏幕"),
            ("程式", "程序"),
            ("辨識", "识别"),
            ("偵測", "检测"),
            ("開啟", "打开"),
            ("登入", "登录"),
            ("執行", "运行"),
            ("結束", "退出"),
            ("一般", "常规"),
            ("介面", "界面"),
            ("預設", "默认"),
            ("內建", "内置"),
            ("支援", "支持"),
            ("視窗", "窗口"),
            ("身分", "身份"),
            ("目前", "当前"),
            ("筆劃", "笔画"),
            ("設定", "设置"),
            ("資料", "数据"),
            // Characters that are also valid Simplified forms, so the Windows converter leaves them alone.
            ("後", "后"),
            ("於", "于"),
            ("「", "“"),
            ("」", "”"),
        };

        private static readonly Dictionary<string, string> Cache = new();
        private static readonly ConditionalWeakTable<object, Entry> Originals = new();

        private sealed class Entry
        {
            public string Original = "";
            public string Shown = "";
        }

        public static void SetLanguage(string? setting)
        {
            IsSimplified = setting switch
            {
                Simplified => true,
                Traditional => false,
                _ => SystemPrefersSimplified(),
            };
        }

        public static bool SystemPrefersSimplified()
        {
            var c = CultureInfo.CurrentUICulture;
            for (; c != null && c.Name.Length > 0; c = c.Parent)
            {
                if (c.Name is "zh-CN" or "zh-SG" or "zh-Hans") return true;
                if (c.Name is "zh-TW" or "zh-HK" or "zh-MO" or "zh-Hant") return false;
            }
            return false;
        }

        /// <summary>Text for the current interface language. <paramref name="traditional"/> is the Traditional Chinese source text.</summary>
        public static string T(string traditional)
        {
            if (!IsSimplified || string.IsNullOrEmpty(traditional)) return traditional;
            lock (Cache)
            {
                if (Cache.TryGetValue(traditional, out var hit)) return hit;
                string s = traditional;
                foreach (var (from, to) in Terms) s = s.Replace(from, to);
                s = ChineseConverter.Convert(s, OutputScript.Simplified);
                Cache[traditional] = s;
                return s;
            }
        }

        /// <summary>
        /// Translates every piece of text under <paramref name="root"/>. Safe to call again after the language changes
        /// or after code sets new (Traditional) text: the original text of each element is remembered.
        /// </summary>
        public static void Apply(DependencyObject root)
        {
            if (root is FrameworkElement fe && fe.Tag as string == NoLocTag) return;

            switch (root)
            {
                case Window w:
                    w.Title = Translate(w, w.Title);
                    break;
                case TextBlock tb:
                    foreach (var inline in tb.Inlines.ToList()) ApplyInline(inline);
                    break;
                case ContentControl cc when cc.Content is string text:
                    cc.Content = Translate(cc, text);
                    break;
            }

            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
                Apply(child);
        }

        private static void ApplyInline(Inline inline)
        {
            if (inline is Run run) run.Text = Translate(run, run.Text);
            else if (inline is Span span)
                foreach (var i in span.Inlines.ToList()) ApplyInline(i);
        }

        private static string Translate(object owner, string current)
        {
            var e = Originals.GetOrCreateValue(owner);
            // If the text is no longer what we last showed, code has set new (Traditional) text.
            if (current != e.Shown) e.Original = current;
            e.Shown = T(e.Original);
            return e.Shown;
        }
    }
}
