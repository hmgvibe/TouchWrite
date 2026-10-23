using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using static TouchWrite.Native.NativeMethods;

namespace TouchWrite.Input
{
    /// <summary>Types text and keys into whatever window has keyboard focus.</summary>
    internal static class TextSender
    {
        public const ushort VK_BACK = 0x08;
        public const ushort VK_RETURN = 0x0D;
        public const ushort VK_SPACE = 0x20;

        public static void SendText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var inputs = new List<INPUT>(text.Length * 2);
            foreach (char c in text)
            {
                inputs.Add(Key(0, c, KEYEVENTF_UNICODE));
                inputs.Add(Key(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
            }
            Send(inputs);
        }

        public static void SendKey(ushort vk, int times = 1)
        {
            if (times <= 0) return;
            var inputs = new List<INPUT>(times * 2);
            for (int i = 0; i < times; i++)
            {
                inputs.Add(Key(vk, 0, 0));
                inputs.Add(Key(vk, 0, KEYEVENTF_KEYUP));
            }
            Send(inputs);
        }

        /// <summary>Number of Backspace presses needed to erase <paramref name="text"/>.</summary>
        public static int BackspacesFor(string text) => new StringInfo(text).LengthInTextElements;

        private static INPUT Key(ushort vk, ushort scan, uint flags) => new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } },
        };

        private static void Send(List<INPUT> inputs)
        {
            var arr = inputs.ToArray();
            SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
        }
    }
}
