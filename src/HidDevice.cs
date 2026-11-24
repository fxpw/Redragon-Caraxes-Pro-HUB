using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Caraxes
{
    internal sealed class HidDevice
    {
        internal const string UsbId = "258A:0049";
        internal const string CommandId = "MI_01 / COL05";
        internal const string DataId = "MI_01 / COL06";
        internal const int DataLength = 1032;
        internal const int CanvasLength = 382;
        const string Prefix = @"\\?\hid#vid_258a&pid_0049&mi_01&col";
        const string ReceiverFeaturePath = @"\\?\hid#vid_25a7&pid_fa70&mi_01&col06#";

        readonly string commandPath;
        readonly string dataPath;
        readonly string canvasPath;

        HidDevice(string commandPath, string dataPath, string canvasPath)
        {
            this.commandPath = commandPath;
            this.dataPath = dataPath;
            this.canvasPath = canvasPath;
        }

        internal string Identity { get { return dataPath; } }
        internal bool HasCanvas { get { return canvasPath != null; } }

        internal static bool IsConfigurationPath(string path)
        {
            return path.StartsWith(Prefix + "05#", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(Prefix + "06#", StringComparison.OrdinalIgnoreCase);
        }

        internal static HidDevice Find()
        {
            var commands = new Dictionary<uint, string>();
            var replies = new Dictionary<uint, string>();
            var canvases = new Dictionary<uint, string>();
            foreach (var item in FindInterfaces(Prefix))
            {
                string path = item.Key;
                bool command = path.StartsWith(Prefix + "05#", StringComparison.OrdinalIgnoreCase);
                bool canvas = path.StartsWith(Prefix + "08#", StringComparison.OrdinalIgnoreCase);
                if (!command && !canvas && !path.StartsWith(Prefix + "06#", StringComparison.OrdinalIgnoreCase)) continue;
                uint parent;
                if (Native.CM_Get_Parent(out parent, item.Value, 0) != 0) continue;
                using (var handle = Native.Open(path))
                {
                    if (!handle.IsInvalid && Matches(handle, command ? 6 : canvas ? CanvasLength : DataLength))
                        (command ? commands : canvas ? canvases : replies).Add(parent, path);
                }
            }

            if (commands.Count > 1 || replies.Count > 1)
                throw new InvalidOperationException("Multiple matching USB keyboards are connected.");
            foreach (var item in commands)
            {
                string reply;
                if (!replies.TryGetValue(item.Key, out reply)) continue;
                string canvas;
                canvases.TryGetValue(item.Key, out canvas);
                return new HidDevice(item.Value, reply, canvas);
            }
            return null;
        }

        internal static string ReadReceiverFeature(string inputPath)
        {
            // Resolve the bound MI_00 collection to its USB device, then look for its MI_01 sibling.
            int suffix = inputPath == null ? -1 : inputPath.LastIndexOf("#{", StringComparison.Ordinal);
            if (suffix <= 4) throw new IOException("The bound receiver is unavailable.");
            string instance = inputPath.Substring(4, suffix - 4).Replace('#', '\\');
            uint input, receiver;
            if (Native.CM_Locate_DevNode(out input, instance, 0) != 0 || !TryGetUsbParent(input, out receiver))
                throw new IOException("The bound receiver is unavailable.");

            foreach (var item in FindInterfaces(ReceiverFeaturePath))
            {
                uint parent;
                if (!TryGetUsbParent(item.Value, out parent) || parent != receiver) continue;
                using (var handle = Native.Open(item.Key))
                {
                    if (handle.IsInvalid) throw LastError("Open receiver HID collection");
                    if (!Matches(handle, 8, receiver: true)) break;
                    var report = new byte[8];
                    report[0] = 6;
                    if (!Native.HidD_GetFeature(handle, report, report.Length)) throw LastError("Read receiver Feature report");
                    if (report[0] != 6) throw new InvalidDataException("Unexpected receiver report ID.");
                    return BitConverter.ToString(report).Replace('-', ' ');
                }
            }
            throw new IOException("The verified receiver report is unavailable.");
        }

        static bool TryGetUsbParent(uint instance, out uint parent)
        {
            uint compositeInterface;
            parent = 0;
            return Native.CM_Get_Parent(out compositeInterface, instance, 0) == 0 &&
                Native.CM_Get_Parent(out parent, compositeInterface, 0) == 0;
        }

        static IEnumerable<KeyValuePair<string, uint>> FindInterfaces(string prefix)
        {
            Guid guid;
            Native.HidD_GetHidGuid(out guid);
            IntPtr set = Native.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12);
            if (set == new IntPtr(-1)) throw LastError("Enumerate HID devices");
            try
            {
                for (uint index = 0; ; index++)
                {
                    var item = new Native.InterfaceData { Size = (uint)Marshal.SizeOf(typeof(Native.InterfaceData)) };
                    if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref item))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == 259) break;
                        throw new Win32Exception(error, "Enumerate HID interfaces");
                    }
                    uint size;
                    Native.SetupDiGetDeviceInterfaceDetail(set, ref item, IntPtr.Zero, 0, out size, IntPtr.Zero);
                    if (size == 0) continue;
                    IntPtr buffer = Marshal.AllocHGlobal((int)size);
                    string path;
                    var info = new Native.DeviceInfo { Size = (uint)Marshal.SizeOf(typeof(Native.DeviceInfo)) };
                    try
                    {
                        Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                        if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref item, buffer, size, out size, ref info)) continue;
                        path = Marshal.PtrToStringUni(IntPtr.Add(buffer, 4));
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                    if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        yield return new KeyValuePair<string, uint>(path, info.Instance);
                }
            }
            finally { Native.SetupDiDestroyDeviceInfoList(set); }
        }

        static bool Matches(SafeFileHandle handle, int featureLength, bool receiver = false)
        {
            var attributes = new Native.Attributes { Size = (uint)Marshal.SizeOf(typeof(Native.Attributes)) };
            if (!Native.HidD_GetAttributes(handle, ref attributes) ||
                (receiver ? attributes.Vendor != 0x25a7 || attributes.Product != 0xfa70 || attributes.Version != 0x0300
                    : attributes.Vendor != 0x258a || attributes.Product != 0x0049))
                return false;
            IntPtr data;
            if (!Native.HidD_GetPreparsedData(handle, out data)) return false;
            try
            {
                Native.Caps caps;
                return Native.HidP_GetCaps(data, out caps) == 0x110000 &&
                    caps.Page == (receiver ? 0xff02 : 0xff00) && caps.Usage == (receiver ? 2 : 1) && caps.Input == 0 && caps.Output == 0 &&
                    caps.Feature == featureLength;
            }
            finally { Native.HidD_FreePreparsedData(data); }
        }

        internal Session Open() { return new Session(commandPath, dataPath, canvasPath); }

        internal sealed class Session : IDisposable
        {
            readonly SafeFileHandle command;
            readonly SafeFileHandle data;
            readonly string canvasPath;
            SafeFileHandle canvas;

            internal Session(string commandPath, string dataPath, string canvasPath)
            {
                this.canvasPath = canvasPath;
                command = OpenChecked(commandPath, 6);
                try { data = OpenChecked(dataPath, DataLength); }
                catch { command.Dispose(); throw; }
            }

            static SafeFileHandle OpenChecked(string path, int featureLength)
            {
                SafeFileHandle handle = Native.Open(path);
                if (handle.IsInvalid)
                {
                    var error = LastError("Open configuration HID collection");
                    handle.Dispose();
                    throw error;
                }
                if (Matches(handle, featureLength)) return handle;
                handle.Dispose();
                throw new IOException("The configuration HID collection changed.");
            }

            internal byte[] GetSettings()
            {
                // Stock read selector (the short collection's descriptor length is 6).
                var request = new byte[] { 5, 0x83, 0xb6, 0, 0, 0, 0 };
                if (!Native.HidD_SetFeature(command, request, request.Length)) throw LastError("Select RGB settings for reading");
                System.Threading.Thread.Sleep(20);
                var reply = new byte[DataLength];
                reply[0] = 6;
                if (!Native.HidD_GetFeature(data, reply, reply.Length)) throw LastError("Read RGB settings");
                return reply;
            }

            internal void SetSettings(byte[] report)
            {
                if (report.Length != DataLength || report[0] != 6 || report[1] != 3 || report[2] != 0xb6)
                    throw new InvalidDataException("Invalid RGB settings report.");
                if (!Native.HidD_SetFeature(data, report, report.Length)) throw LastError("Write RGB settings");
            }

            internal void ShowCanvas(byte[] report)
            {
                if (report.Length != CanvasLength || report[0] != 8 || report[1] != 0x0a || report[2] != 0x7a || report[3] != 1)
                    throw new InvalidDataException("Invalid RGB canvas report.");
                if (canvas == null) canvas = OpenCanvas();
                if (!Native.HidD_SetFeature(canvas, report, report.Length)) throw LastError("Show RGB canvas");
            }

            SafeFileHandle OpenCanvas()
            {
                if (canvasPath == null) throw new IOException("The RGB canvas collection is unavailable.");
                return OpenChecked(canvasPath, CanvasLength);
            }

            public void Dispose()
            {
                if (canvas != null) canvas.Dispose();
                data.Dispose();
                command.Dispose();
            }
        }

        static Win32Exception LastError(string operation)
        {
            int error = Marshal.GetLastWin32Error();
            return new Win32Exception(error, operation + ": " + new Win32Exception(error).Message);
        }

        // SetupAPI enumeration and HID caps adapted from FURYCUBE-G9-PRO (MIT).
        static class Native
        {
            [StructLayout(LayoutKind.Sequential)] internal struct InterfaceData { public uint Size; public Guid Guid; public uint Flags; public IntPtr Reserved; }
            [StructLayout(LayoutKind.Sequential)] internal struct DeviceInfo { public uint Size; public Guid ClassGuid; public uint Instance; public IntPtr Reserved; }
            [StructLayout(LayoutKind.Sequential)] internal struct Attributes { public uint Size; public ushort Vendor, Product, Version; }
            [StructLayout(LayoutKind.Sequential)] internal struct Caps
            {
                public ushort Usage, Page, Input, Output, Feature;
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
                public ushort LinkCount, InputButtons, InputValues, InputIndices, OutputButtons, OutputValues, OutputIndices, FeatureButtons, FeatureValues, FeatureIndices;
            }

            internal static SafeFileHandle Open(string path) { return CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero); }
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
            [DllImport("hid.dll")] internal static extern void HidD_GetHidGuid(out Guid guid);
            [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);
            [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
            [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_FreePreparsedData(IntPtr data);
            [DllImport("hid.dll")] internal static extern int HidP_GetCaps(IntPtr data, out Caps caps);
            [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetFeature(SafeFileHandle handle, [In, Out] byte[] report, int length);
            [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] report, int length);
            [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr window, uint flags);
            [DllImport("setupapi.dll", SetLastError = true)] internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref InterfaceData item);
            [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData item, IntPtr detail, uint length, out uint needed, IntPtr info);
            [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData item, IntPtr detail, uint length, out uint needed, ref DeviceInfo info);
            [DllImport("setupapi.dll")] internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
            [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] internal static extern uint CM_Locate_DevNode(out uint instance, string deviceId, uint flags);
            [DllImport("cfgmgr32.dll")] internal static extern uint CM_Get_Parent(out uint parent, uint child, uint flags);
        }
    }
}
