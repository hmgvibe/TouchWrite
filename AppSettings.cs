using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using TouchWrite.Recognition;
using static TouchWrite.Native.NativeMethods;

namespace TouchWrite
{
    /// <summary>A global hotkey: RegisterHotKey modifiers (MOD_*) plus a virtual-key code.</summary>
    internal sealed record Hotkey(uint Modifiers, uint Vk)
    {
        public static readonly Hotkey Default = new(MOD_CONTROL | MOD_SHIFT, 0x20); // Ctrl + Shift + Space, like macOS

        public string Label
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                if ((Modifiers & MOD_CONTROL) != 0) parts.Add("Ctrl");
                if ((Modifiers & MOD_ALT) != 0) parts.Add("Alt");
                if ((Modifiers & MOD_SHIFT) != 0) parts.Add("Shift");
                if ((Modifiers & MOD_WIN) != 0) parts.Add("Win");
                parts.Add(KeyName(Vk));
                return string.Join(" + ", parts);
            }
        }

        public static string KeyName(uint vk) => vk switch
        {
            0x20 => "Space",
            0x0D => "Enter",
            0x09 => "Tab",
            0x08 => "Backspace",
            0x2E => "Delete",
            0x2D => "Insert",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x25 => "←",
            0x26 => "↑",
            0x27 => "→",
            0x28 => "↓",
            0xC0 => "`",
            0xBD => "-",
            0xBB => "=",
            0xDB => "[",
            0xDD => "]",
            0xDC => "\\",
            0xBA => ";",
            0xDE => "'",
            0xBC => ",",
            0xBE => ".",
            0xBF => "/",
            >= 0x30 and <= 0x39 => ((char)vk).ToString(),
            >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            >= 0x60 and <= 0x69 => "Num " + (vk - 0x60),
            0x6A => "Num *",
            0x6B => "Num +",
            0x6D => "Num -",
            0x6E => "Num .",
            0x6F => "Num /",
            >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),
            0x13 => "Pause",
            0x91 => "ScrollLock",
            0x2C => "PrintScreen",
            _ => System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)vk).ToString(),
        };
    }

    internal sealed class AppSettings
    {
        /// <summary>Hotkeys offered before custom hotkeys existed; old settings files refer to them by id.</summary>
        private static readonly (string Id, Hotkey Key)[] LegacyHotkeys =
        {
            ("CtrlShiftSpace", Hotkey.Default),
            ("CtrlAltH", new(MOD_CONTROL | MOD_ALT, 0x48)),
            ("CtrlShiftH", new(MOD_CONTROL | MOD_SHIFT, 0x48)),
            ("AltBacktick", new(MOD_ALT, 0xC0)),
        };

        public string? RecognizerName { get; set; }
        /// <summary>Type the best candidate automatically after the pen stops. Off: tap a candidate or press Space.</summary>
        public bool AutoCommit { get; set; } = true;
        public int CommitDelayMs { get; set; } = 900;
        /// <summary>Old preset id; only read when no custom hotkey is stored.</summary>
        public string? HotkeyId { get; set; }

        public uint HotkeyModifiers { get; set; }
        public uint HotkeyVk { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public OutputScript OutputScript { get; set; } = OutputScript.AsRecognized;

        public double PadWidth { get; set; } = 560;

        /// <summary>Interface language: "Auto" (follow Windows), "zh-Hant" or "zh-Hans".</summary>
        public string UiLanguage { get; set; } = Loc.Auto;

        [JsonIgnore]
        public Hotkey Hotkey
        {
            get
            {
                if (HotkeyVk != 0) return new Hotkey(HotkeyModifiers, HotkeyVk);
                foreach (var (id, key) in LegacyHotkeys)
                    if (id == HotkeyId) return key;
                return Hotkey.Default;
            }
            set
            {
                HotkeyModifiers = value.Modifiers;
                HotkeyVk = value.Vk;
                HotkeyId = null;
            }
        }

        private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TouchWrite");
        internal static string FilePath => Path.Combine(Dir, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        // ---- Start with Windows (per-user Run key) ----
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "TouchWrite";

        public static bool LaunchAtStartup
        {
            get
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey);
                return k?.GetValue(RunName) is string;
            }
            set
            {
                using var k = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value) k.SetValue(RunName, $"\"{Environment.ProcessPath}\"");
                else k.DeleteValue(RunName, false);
            }
        }
    }
}
