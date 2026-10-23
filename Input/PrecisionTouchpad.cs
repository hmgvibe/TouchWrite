using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using static TouchWrite.Native.NativeMethods;

namespace TouchWrite.Input
{
    /// <summary>One finger touching the touchpad. X and Y are normalized to 0..1 (0,0 = top-left).</summary>
    internal readonly record struct TouchContact(int Id, double X, double Y);

    /// <summary>
    /// Reads absolute finger positions from a Windows Precision Touchpad through Raw Input (HID usage page 0x0D, usage 0x05).
    /// </summary>
    internal sealed class PrecisionTouchpad : IDisposable
    {
        private const ushort PageGenericDesktop = 0x01;
        private const ushort PageDigitizer = 0x0D;
        private const ushort UsageTouchpad = 0x05;
        private const ushort UsageX = 0x30;
        private const ushort UsageY = 0x31;
        private const ushort UsageTipSwitch = 0x42;
        private const ushort UsageConfidence = 0x47;
        private const ushort UsageContactId = 0x51;
        private const ushort UsageContactCount = 0x54;

        private readonly Dictionary<IntPtr, Device?> _devices = new();
        private IntPtr _buffer = IntPtr.Zero;
        private uint _bufferSize;
        private IntPtr _registeredHwnd = IntPtr.Zero;

        /// <summary>Raised on the window thread with the contacts currently touching (empty when all fingers lifted).</summary>
        public event Action<IReadOnlyList<TouchContact>>? FrameReceived;

        /// <summary>Width / height of the touch surface.</summary>
        public double AspectRatio { get; private set; } = 1.6;

        public static bool IsPresent() => EnumerateTouchpads().Count > 0;

        public bool Register(IntPtr hwnd)
        {
            var dev = new[]
            {
                new RAWINPUTDEVICE { usUsagePage = PageDigitizer, usUsage = UsageTouchpad, dwFlags = RIDEV_INPUTSINK, hwndTarget = hwnd },
            };
            bool ok = RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            if (ok)
            {
                _registeredHwnd = hwnd;
                foreach (var h in EnumerateTouchpads())
                {
                    var d = GetDevice(h);
                    if (d != null) { AspectRatio = d.Aspect; break; }
                }
            }
            return ok;
        }

        public void Unregister()
        {
            if (_registeredHwnd == IntPtr.Zero) return;
            var dev = new[]
            {
                new RAWINPUTDEVICE { usUsagePage = PageDigitizer, usUsage = UsageTouchpad, dwFlags = RIDEV_REMOVE, hwndTarget = IntPtr.Zero },
            };
            RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            _registeredHwnd = IntPtr.Zero;
        }

        /// <summary>Call from the window procedure for WM_INPUT.</summary>
        public void ProcessInput(IntPtr lParam)
        {
            uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
            uint size = 0;
            GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize);
            if (size == 0) return;
            if (size > _bufferSize)
            {
                if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
                _buffer = Marshal.AllocHGlobal((int)size);
                _bufferSize = size;
            }
            if (GetRawInputData(lParam, RID_INPUT, _buffer, ref size, headerSize) != size) return;

            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(_buffer);
            if (header.dwType != RIM_TYPEHID) return;

            var device = GetDevice(header.hDevice);
            if (device == null) return;
            AspectRatio = device.Aspect;

            int sizeHid = Marshal.ReadInt32(_buffer, (int)headerSize);
            int count = Marshal.ReadInt32(_buffer, (int)headerSize + 4);
            if (sizeHid <= 0 || count <= 0) return;

            if (device.Report.Length != sizeHid) device.Report = new byte[sizeHid];
            for (int i = 0; i < count; i++)
            {
                Marshal.Copy(_buffer + (int)headerSize + 8 + i * sizeHid, device.Report, 0, sizeHid);
                ParseReport(device);
            }
        }

        private void ParseReport(Device d)
        {
            byte[] report = d.Report;
            uint len = (uint)report.Length;

            int contactCount = -1;
            if (d.ContactCountLink.HasValue &&
                HidP_GetUsageValue(HIDP_REPORT_TYPE.Input, PageDigitizer, d.ContactCountLink.Value, UsageContactCount,
                    out uint cc, d.Preparsed, report, len) == HIDP_STATUS_SUCCESS)
            {
                contactCount = (int)cc;
            }

            if (contactCount > 0)
            {
                // First report of a frame (hybrid mode may split one frame across several reports).
                d.Pending.Clear();
                d.Expected = contactCount;
                d.Read = 0;
            }
            else if (contactCount == 0)
            {
                if (d.Expected == 0) return; // stray continuation report
            }
            else
            {
                d.Pending.Clear();
                d.Expected = d.Slots.Count;
                d.Read = 0;
            }

            foreach (var slot in d.Slots)
            {
                if (d.Read >= d.Expected) break;
                d.Read++;

                uint usageLen = (uint)d.UsageList.Length;
                bool tip = false, confident = !slot.HasConfidence;
                if (HidP_GetUsages(HIDP_REPORT_TYPE.Input, PageDigitizer, slot.Link, d.UsageList, ref usageLen, d.Preparsed, report, len) == HIDP_STATUS_SUCCESS)
                {
                    for (int i = 0; i < usageLen; i++)
                    {
                        if (d.UsageList[i] == UsageTipSwitch) tip = true;
                        else if (d.UsageList[i] == UsageConfidence) confident = true;
                    }
                }
                if (!tip || !confident) continue;

                if (HidP_GetUsageValue(HIDP_REPORT_TYPE.Input, PageGenericDesktop, slot.Link, UsageX, out uint rx, d.Preparsed, report, len) != HIDP_STATUS_SUCCESS) continue;
                if (HidP_GetUsageValue(HIDP_REPORT_TYPE.Input, PageGenericDesktop, slot.Link, UsageY, out uint ry, d.Preparsed, report, len) != HIDP_STATUS_SUCCESS) continue;
                int id = slot.Link;
                if (slot.HasId && HidP_GetUsageValue(HIDP_REPORT_TYPE.Input, PageDigitizer, slot.Link, UsageContactId, out uint rid, d.Preparsed, report, len) == HIDP_STATUS_SUCCESS)
                    id = (int)rid;

                double x = Normalize(rx, slot.X);
                double y = Normalize(ry, slot.Y);
                d.Pending.Add(new TouchContact(id, x, y));
            }

            if (d.Read >= d.Expected)
            {
                var frame = d.Pending.ToArray();
                d.Pending.Clear();
                d.Expected = 0;
                d.Read = 0;
                FrameReceived?.Invoke(frame);
            }
        }

        private static double Normalize(uint raw, Axis a)
        {
            double v = (a.Min < 0 && a.BitSize > 0 && a.BitSize < 32 && (raw & (1u << (a.BitSize - 1))) != 0)
                ? (int)(raw | (~0u << a.BitSize))   // sign-extend
                : raw;
            double t = (v - a.Min) / Math.Max(1.0, a.Max - (double)a.Min);
            return Math.Clamp(t, 0, 1);
        }

        // ------------------------------------------------------------------ device discovery

        private static List<IntPtr> EnumerateTouchpads()
        {
            var result = new List<IntPtr>();
            uint count = 0;
            uint itemSize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
            if (GetRawInputDeviceList(null, ref count, itemSize) != 0 || count == 0) return result;
            var list = new RAWINPUTDEVICELIST[count];
            uint got = GetRawInputDeviceList(list, ref count, itemSize);
            if (got == uint.MaxValue) return result;
            for (int i = 0; i < got; i++)
            {
                if (list[i].dwType != RIM_TYPEHID) continue;
                var info = new RID_DEVICE_INFO { cbSize = (uint)Marshal.SizeOf<RID_DEVICE_INFO>() };
                uint sz = info.cbSize;
                IntPtr p = Marshal.AllocHGlobal((int)sz);
                try
                {
                    Marshal.StructureToPtr(info, p, false);
                    if (GetRawInputDeviceInfo(list[i].hDevice, RIDI_DEVICEINFO, p, ref sz) == uint.MaxValue) continue;
                    info = Marshal.PtrToStructure<RID_DEVICE_INFO>(p);
                    if (info.hid.usUsagePage == PageDigitizer && info.hid.usUsage == UsageTouchpad)
                        result.Add(list[i].hDevice);
                }
                finally { Marshal.FreeHGlobal(p); }
            }
            return result;
        }

        private Device? GetDevice(IntPtr hDevice)
        {
            if (_devices.TryGetValue(hDevice, out var cached)) return cached;
            Device? d = null;
            try { d = Device.Open(hDevice); } catch { d = null; }
            _devices[hDevice] = d;
            return d;
        }

        /// <summary>Human-readable description of every touchpad found, for diagnostics.</summary>
        public static string Describe()
        {
            var sb = new StringBuilder();
            var pads = EnumerateTouchpads();
            sb.AppendLine($"Precision touchpads found: {pads.Count}");
            foreach (var h in pads)
            {
                var d = Device.Open(h);
                if (d == null) { sb.AppendLine($"  {h}: could not read HID descriptor"); continue; }
                sb.AppendLine($"  {h}: contact slots per report={d.Slots.Count}, contact-count usage={(d.ContactCountLink.HasValue ? "yes" : "no")}, aspect={d.Aspect:F3}");
                foreach (var s in d.Slots)
                    sb.AppendLine($"    link {s.Link}: X[{s.X.Min}..{s.X.Max}] Y[{s.Y.Min}..{s.Y.Max}] id={s.HasId} confidence={s.HasConfidence}");
                d.Dispose();
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            Unregister();
            foreach (var d in _devices.Values) d?.Dispose();
            _devices.Clear();
            if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }

        // ------------------------------------------------------------------ HID descriptor model

        private readonly record struct Axis(int Min, int Max, int BitSize);

        private sealed class Slot
        {
            public ushort Link;
            public Axis X;
            public Axis Y;
            public bool HasX, HasY, HasId, HasConfidence;
        }

        private sealed class Device : IDisposable
        {
            public IntPtr Preparsed;
            public ushort? ContactCountLink;
            public List<Slot> Slots = new();
            public ushort[] UsageList = Array.Empty<ushort>();
            public double Aspect = 1.6;
            public byte[] Report = Array.Empty<byte>();
            public List<TouchContact> Pending = new();
            public int Expected;
            public int Read;

            public static Device? Open(IntPtr hDevice)
            {
                uint size = 0;
                GetRawInputDeviceInfo(hDevice, RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
                if (size == 0) return null;
                IntPtr pre = Marshal.AllocHGlobal((int)size);
                if (GetRawInputDeviceInfo(hDevice, RIDI_PREPARSEDDATA, pre, ref size) == uint.MaxValue)
                {
                    Marshal.FreeHGlobal(pre);
                    return null;
                }

                var d = new Device { Preparsed = pre };
                if (HidP_GetCaps(pre, out var caps) != HIDP_STATUS_SUCCESS) { d.Dispose(); return null; }

                var slots = new SortedDictionary<ushort, Slot>();
                Slot SlotFor(ushort link)
                {
                    if (!slots.TryGetValue(link, out var s)) slots[link] = s = new Slot { Link = link };
                    return s;
                }

                double physW = 0, physH = 0;

                ushort nValue = caps.NumberInputValueCaps;
                if (nValue > 0)
                {
                    var buf = new byte[nValue * HIDP_CAPS_SIZE];
                    if (HidP_GetValueCaps(HIDP_REPORT_TYPE.Input, buf, ref nValue, pre) == HIDP_STATUS_SUCCESS)
                    {
                        for (int i = 0; i < nValue; i++)
                        {
                            int o = i * HIDP_CAPS_SIZE;
                            ushort page = BitConverter.ToUInt16(buf, o + 0);
                            ushort link = BitConverter.ToUInt16(buf, o + 6);
                            bool isRange = buf[o + 12] != 0;
                            ushort bitSize = BitConverter.ToUInt16(buf, o + 18);
                            int logMin = BitConverter.ToInt32(buf, o + 40);
                            int logMax = BitConverter.ToInt32(buf, o + 44);
                            int phyMin = BitConverter.ToInt32(buf, o + 48);
                            int phyMax = BitConverter.ToInt32(buf, o + 52);
                            ushort uMin = BitConverter.ToUInt16(buf, o + 56);
                            ushort uMax = isRange ? BitConverter.ToUInt16(buf, o + 58) : uMin;

                            if (logMax <= logMin && bitSize > 0 && bitSize < 32)
                            {
                                logMin = 0;
                                logMax = (int)((1L << bitSize) - 1);
                            }

                            bool Has(ushort u) => u >= uMin && u <= uMax;

                            if (page == PageGenericDesktop && Has(UsageX))
                            {
                                var s = SlotFor(link);
                                s.X = new Axis(logMin, logMax, bitSize);
                                s.HasX = true;
                                if (phyMax > phyMin) physW = phyMax - phyMin;
                            }
                            if (page == PageGenericDesktop && Has(UsageY))
                            {
                                var s = SlotFor(link);
                                s.Y = new Axis(logMin, logMax, bitSize);
                                s.HasY = true;
                                if (phyMax > phyMin) physH = phyMax - phyMin;
                            }
                            if (page == PageDigitizer && Has(UsageContactId)) SlotFor(link).HasId = true;
                            if (page == PageDigitizer && Has(UsageContactCount)) d.ContactCountLink = link;
                        }
                    }
                }

                ushort nButton = caps.NumberInputButtonCaps;
                if (nButton > 0)
                {
                    var buf = new byte[nButton * HIDP_CAPS_SIZE];
                    if (HidP_GetButtonCaps(HIDP_REPORT_TYPE.Input, buf, ref nButton, pre) == HIDP_STATUS_SUCCESS)
                    {
                        for (int i = 0; i < nButton; i++)
                        {
                            int o = i * HIDP_CAPS_SIZE;
                            ushort page = BitConverter.ToUInt16(buf, o + 0);
                            ushort link = BitConverter.ToUInt16(buf, o + 6);
                            bool isRange = buf[o + 12] != 0;
                            ushort uMin = BitConverter.ToUInt16(buf, o + 56);
                            ushort uMax = isRange ? BitConverter.ToUInt16(buf, o + 58) : uMin;
                            if (page == PageDigitizer && UsageConfidence >= uMin && UsageConfidence <= uMax && slots.ContainsKey(link))
                                slots[link].HasConfidence = true;
                        }
                    }
                }

                foreach (var s in slots.Values)
                    if (s.HasX && s.HasY) d.Slots.Add(s);
                if (d.Slots.Count == 0) { d.Dispose(); return null; }

                uint maxUsages = HidP_MaxUsageListLength(HIDP_REPORT_TYPE.Input, PageDigitizer, pre);
                d.UsageList = new ushort[Math.Max(16u, maxUsages)];

                var first = d.Slots[0];
                if (physW > 0 && physH > 0) d.Aspect = physW / physH;
                else d.Aspect = (first.X.Max - (double)first.X.Min) / Math.Max(1.0, first.Y.Max - (double)first.Y.Min);
                if (d.Aspect < 0.5 || d.Aspect > 4) d.Aspect = 1.6;
                return d;
            }

            public void Dispose()
            {
                if (Preparsed != IntPtr.Zero) Marshal.FreeHGlobal(Preparsed);
                Preparsed = IntPtr.Zero;
            }
        }
    }
}
