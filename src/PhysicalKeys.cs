using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Caraxes
{
    internal sealed class PhysicalKeys
    {
        const uint Input = 0x10000003;
        const uint DeviceName = 0x20000007;
        const int MaxReport = 128;
        const string Prefix = @"\\?\hid#vid_258a&pid_0049&mi_";
        static readonly Regex ReceiverInterface = new Regex(
            @"\A\\\\\?\\hid#vid_25a7&pid_fa70&mi_00#[a-z0-9&._-]+#\{884b96c3-56ef-11d1-bc8c-00a0c91405dd\}\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        readonly Dictionary<IntPtr, bool> known = new Dictionary<IntPtr, bool>();
        readonly string receiverPath;

        [StructLayout(LayoutKind.Sequential)]
        struct Registration { internal ushort Page, Usage; internal uint Flags; internal IntPtr Window; }

        [StructLayout(LayoutKind.Sequential)]
        struct RawDevice { internal IntPtr Handle; internal uint Type; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterRawInputDevices(Registration[] devices, uint count, uint size);
        [DllImport("user32.dll")]
        static extern uint GetRawInputDeviceList([Out] RawDevice[] devices, ref uint count, uint size);
        [DllImport("user32.dll")]
        static extern uint GetRawInputData(IntPtr input, uint command, [Out] byte[] data, ref uint size, uint headerSize);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder name, ref uint size);

        internal PhysicalKeys(string storage = null)
        {
            if (storage == null) return;
            string path = Path.Combine(storage, "receiver-keyboard.txt");
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 2048) return;
                string saved = File.ReadAllText(path).Trim();
                if (ReceiverInterface.IsMatch(saved)) receiverPath = saved;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal string ReceiverInputPath { get { return receiverPath; } }

        internal bool ReceiverPresent()
        {
            if (receiverPath == null) return false;
            uint count = 0;
            uint size = (uint)Marshal.SizeOf(typeof(RawDevice));
            if (GetRawInputDeviceList(null, ref count, size) == uint.MaxValue || count == 0 || count > 8192) return false;
            var devices = new RawDevice[count];
            uint read = GetRawInputDeviceList(devices, ref count, size);
            if (read == uint.MaxValue) return false;
            for (int index = 0; index < read; index++)
            {
                var device = devices[index];
                if (device.Type != 1 || !IsReceiver(ReadName(device.Handle))) continue;
                known[device.Handle] = true;
                return true;
            }
            return false;
        }

        internal static bool Register(IntPtr window, bool vendorReports = false)
        {
            const uint inputSinkAndDeviceChange = 0x2100;
            var keyboard = new Registration { Page = 1, Usage = 6, Flags = inputSinkAndDeviceChange, Window = window };
            var vendor = new Registration { Page = 0xff00, Usage = 1, Flags = inputSinkAndDeviceChange, Window = window };
            var devices = vendorReports ? new[] { keyboard, vendor } : new[] { keyboard };
            return RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf(typeof(Registration)));
        }

        internal bool Changed(IntPtr device)
        {
            bool match;
            known.TryGetValue(device, out match);
            known.Remove(device);
            return match;
        }

        internal static void DecodeFn(byte[] report, Action<int, bool> key)
        {
            if (report.Length != 4 || report[0] != 3) return;
            // Isolated Fn1/Fn2 taps confirmed these pairs on the wired K644 Pro.
            if (report[1] == 0x20 && report[2] == 0 && report[3] <= 1) key(250, report[3] != 0);
            if (report[1] == 0x20 && report[2] == 1 && report[3] == 1) key(251, true);
            if (report[1] == 0 && report[2] == 0 && report[3] == 0) key(251, false);
        }

        bool IsCaraxes(IntPtr device)
        {
            if (device == IntPtr.Zero) return false;
            bool match;
            if (known.TryGetValue(device, out match)) return match;
            string path = ReadName(device);
            if (path == null) return false;
            match = IsReceiver(path) || path.StartsWith(Prefix + "00#", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(Prefix + "01&col04#", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(Prefix + "01&col03#", StringComparison.OrdinalIgnoreCase);
            known[device] = match;
            return match;
        }

        bool IsReceiver(string path)
        {
            // The generic receiver VID/PID alone cannot establish which device sent input.
            return receiverPath != null && String.Equals(path, receiverPath, StringComparison.OrdinalIgnoreCase);
        }

        static string ReadName(IntPtr device)
        {
            uint length = 0;
            GetRawInputDeviceInfo(device, DeviceName, null, ref length);
            if (length == 0 || length > 1024) return null;
            var name = new StringBuilder((int)length);
            return GetRawInputDeviceInfo(device, DeviceName, name, ref length) == uint.MaxValue ? null : name.ToString();
        }

        internal void Read(IntPtr input, Action<int, bool> key, Action<byte[]> vendor = null)
        {
            // RAWINPUTHEADER contains two 32-bit values and two pointer-width values.
            int header = 8 + 2 * IntPtr.Size;
            uint size = 0;
            GetRawInputData(input, Input, null, ref size, (uint)header);
            if (size < header + 8 || size > MaxReport) return;
            var report = new byte[size];
            if (GetRawInputData(input, Input, report, ref size, (uint)header) != size) return;
            var device = IntPtr.Size == 8 ? new IntPtr(BitConverter.ToInt64(report, 8)) : new IntPtr(BitConverter.ToInt32(report, 8));
            if (!IsCaraxes(device)) return;

            uint type = BitConverter.ToUInt32(report, 0);
            if (type == 1 && size >= header + 16)
            {
                int scan = BitConverter.ToUInt16(report, header);
                int flags = BitConverter.ToUInt16(report, header + 2);
                int vk = BitConverter.ToUInt16(report, header + 6);
                if (vk == 0x10) vk = scan == 0x36 ? 0xa1 : 0xa0;
                if (vk == 0x11) vk = (flags & 2) != 0 ? 0xa3 : 0xa2;
                if (vk == 0x12) vk = (flags & 2) != 0 ? 0xa5 : 0xa4;
                if (vk > 0 && vk < 255) key(vk, (flags & 1) == 0);
            }
            else if (type == 2 && vendor != null)
            {
                var bytes = new byte[(int)size - header - 8];
                Array.Copy(report, header + 8, bytes, 0, bytes.Length);
                vendor(bytes);
            }
        }
    }
}
