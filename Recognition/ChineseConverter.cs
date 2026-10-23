using System.Collections.Generic;
using System.Linq;
using static TouchWrite.Native.NativeMethods;

namespace TouchWrite.Recognition
{
    internal enum OutputScript
    {
        AsRecognized,
        Traditional,
        Simplified,
    }

    /// <summary>Simplified ↔ Traditional Chinese conversion using the converter built into Windows.</summary>
    internal static class ChineseConverter
    {
        public static string Convert(string text, OutputScript script)
        {
            if (script == OutputScript.AsRecognized || string.IsNullOrEmpty(text)) return text;
            uint flags = script == OutputScript.Traditional ? LCMAP_TRADITIONAL_CHINESE : LCMAP_SIMPLIFIED_CHINESE;
            string locale = script == OutputScript.Traditional ? "zh-CN" : "zh-TW";
            int len = LCMapStringEx(locale, flags, text, text.Length, null, 0, default, default, default);
            if (len <= 0) return text;
            var buf = new char[len];
            len = LCMapStringEx(locale, flags, text, text.Length, buf, len, default, default, default);
            return len > 0 ? new string(buf, 0, len) : text;
        }

        public static List<string> Convert(IEnumerable<string> items, OutputScript script) =>
            items.Select(i => Convert(i, script)).Distinct().ToList();
    }
}
