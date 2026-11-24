using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Caraxes
{
    internal sealed class LayerRemap : IDisposable
    {
        internal const int EscapeAction = 256;
        const int BackquoteKey = 0xc0;
        const int CapsLock = 20;
        static readonly ushort[] Modifiers = { 0xa0, 0xa1, 0xa2, 0xa3, 0xa4, 0xa5, 0x5b, 0x5c };
        readonly Dictionary<int, Binding> swallowed = new Dictionary<int, Binding>();
        readonly HashSet<int> held = new HashSet<int>();
        readonly HashSet<int> forwarded = new HashSet<int>();
        readonly HashSet<int> hardwareFn = new HashSet<int>();
        readonly object stateLock = new object();
        readonly HookProc callback;
        Profile profile;
        Profile windowsProfile;
        Profile testProfile;
        IntPtr hook;
        Thread hookThread;
        uint hookThreadId;
        bool capsHeld;
        bool capsLockTarget;
        bool editorCapsReserved;
        bool editorCapsCaptured;

        internal LayerRemap() { callback = Filter; }
        internal bool Active { get { lock (stateLock) return windowsProfile != null; } }
        internal static bool CapsLockOn { get { return (GetKeyState(CapsLock) & 1) != 0; } }
        internal Func<bool> IsTestWindowFocused { get; set; }
        internal event Action<bool> HeldChanged;
        internal event Action<int, bool> CapturedKey;
        internal event Action<int, bool> SentMapping;

        internal sealed class Binding
        {
            internal int Key;
            internal bool Preserve;
        }

        internal sealed class Profile
        {
            internal readonly int[] Activators;
            internal readonly Dictionary<int, Binding>[] Bindings;

            internal Profile(int count)
            {
                Activators = new int[count + 1];
                Bindings = new Dictionary<int, Binding>[count + 1];
            }

            internal bool IsTrigger(int key) { return key > 0 && key < 250 && Array.IndexOf(Activators, key) >= 0; }
        }

        internal static Profile Parse(Dictionary<string, object> source)
        {
            object value;
            var layers = source.TryGetValue("layers", out value) ? value as System.Collections.IList : null;
            if (source.Count != 2 || layers == null || layers.Count > 61 || !source.TryGetValue("base", out value))
                throw new InvalidDataException("Invalid layer profile.");

            var profile = new Profile(layers.Count);
            profile.Bindings[0] = ParseBindings(value);
            var used = new HashSet<int>();
            for (int index = 0; index < layers.Count; index++)
            {
                var layer = layers[index] as Dictionary<string, object>;
                if (layer == null || layer.Count != 2 || !layer.TryGetValue("key", out value) || !(value is int))
                    throw new InvalidDataException("Invalid layer key.");
                int key = (int)value;
                if (key != 0 && (!(LayoutKey(key) || key == 20 || key == 91 || key >= 160 && key <= 165 || key == 250 || key == 251) || !used.Add(key)))
                    throw new InvalidDataException("Unknown or duplicate layer key.");
                profile.Activators[index + 1] = key;
                if (!layer.TryGetValue("bindings", out value)) throw new InvalidDataException("Missing layer assignments.");
                profile.Bindings[index + 1] = ParseBindings(value);
            }
            return profile;
        }

        static Dictionary<int, Binding> ParseBindings(object value)
        {
            var source = value as Dictionary<string, object>;
            if (source == null || source.Count > 52) throw new InvalidDataException("Invalid layer assignments.");
            var result = new Dictionary<int, Binding>();
            foreach (var entry in source)
            {
                int vk;
                var raw = entry.Value as Dictionary<string, object>;
                if (!Int32.TryParse(entry.Key, out vk) || vk.ToString(System.Globalization.CultureInfo.InvariantCulture) != entry.Key ||
                    !LayoutKey(vk) || raw == null || raw.Count != 2 || !raw.ContainsKey("key") || !raw.ContainsKey("preserve") ||
                    !(raw["key"] is int) || !ActionKey((int)raw["key"]) || !(raw["preserve"] is bool))
                    throw new InvalidDataException("Invalid key assignment.");
                result.Add(vk, new Binding { Key = (int)raw["key"], Preserve = (bool)raw["preserve"] });
            }
            return result;
        }

        static bool LayoutKey(int key)
        {
            return key >= 48 && key <= 57 || key >= 65 && key <= 90 || key >= 186 && key <= 191 ||
                key >= 219 && key <= 222 || key == 8 || key == 9 || key == 13 || key == 27 || key == 32;
        }

        static bool ActionKey(int key)
        {
            return LayoutKey(key) || key == 19 || key == 20 || key >= 33 && key <= 40 || key >= 44 && key <= 46 ||
                key >= 91 && key <= 93 || key >= 96 && key <= 135 || key == 144 || key == 145 ||
                key >= 160 && key <= 183 || key == 192 || key == EscapeAction || key == 257;
        }

        internal void Start(Profile next)
        {
            Observe();
            lock (stateLock)
            {
                windowsProfile = next;
                SelectProfile();
                NormalizeCaps();
            }
        }

        internal void SetTestProfile(Profile next)
        {
            if (next != null) Observe();
            lock (stateLock)
            {
                testProfile = next;
                SelectProfile();
                NormalizeCaps();
            }
        }

        internal void CheckTestFocus(bool restoreCaps)
        {
            lock (stateLock)
            {
                SelectProfile();
                if (restoreCaps) NormalizeCaps();
            }
        }

        void NormalizeCaps()
        {
            bool reserved = (profile != null && profile.IsTrigger(CapsLock)) || EditorReservesCaps();
            if (reserved && IsTestWindowFocused != null && IsTestWindowFocused() && CapsLockOn != capsLockTarget && !CapsKeyDown())
                Tap(CapsLock, true);
        }

        internal void SetEditorCaps(bool reserved)
        {
            if (reserved) Observe();
            lock (stateLock) editorCapsReserved = reserved;
        }

        bool EditorReservesCaps()
        {
            return editorCapsReserved && IsTestWindowFocused != null && IsTestWindowFocused();
        }

        internal bool SetCapsLock(bool enabled)
        {
            lock (stateLock)
            {
                if (CapsLockOn != enabled && (CapsKeyDown() || !Tap(CapsLock, true))) return false;
                capsLockTarget = enabled;
                return true;
            }
        }

        static bool CapsKeyDown() { return (GetAsyncKeyState(CapsLock) & 0x8000) != 0; }

        void Observe()
        {
            if (hookThread != null && hookThread.IsAlive) return;
            var installed = new TaskCompletionSource<int>();
            hookThread = new Thread(() => RunHook(installed)) { Name = "Caraxes keyboard hook", IsBackground = true };
            hookThread.Start();
            int error = installed.Task.GetAwaiter().GetResult();
            if (error != 0) throw new Win32Exception(error, "Start keyboard hook");
        }

        void RunHook(TaskCompletionSource<int> installed)
        {
            // Keep WebView rendering and HID work out of the hook's message queue.
            NativeMessage message;
            PeekMessage(out message, IntPtr.Zero, 0, 0, 0);
            hookThreadId = GetCurrentThreadId();
            hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
            {
                installed.SetResult(Marshal.GetLastWin32Error());
                return;
            }
            installed.SetResult(0);
            try
            {
                while (GetMessage(out message, IntPtr.Zero, 0, 0) > 0)
                    DispatchMessage(ref message);
            }
            finally
            {
                UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
        }

        internal void SetProfile(Profile next)
        {
            lock (stateLock)
            {
                windowsProfile = next;
                SelectProfile();
            }
        }

        void SelectProfile()
        {
            var selected = testProfile != null && IsTestWindowFocused != null && IsTestWindowFocused() ? testProfile : windowsProfile;
            if (profile == selected) return;
            profile = selected;
            held.Clear();
        }

        internal void FnChanged(int key, bool down)
        {
            lock (stateLock)
            {
                if (down) hardwareFn.Add(key);
                else hardwareFn.Remove(key);
            }
        }

        internal void ResetHeldLayer()
        {
            lock (stateLock)
            {
                SetHeld(false);
                foreach (int vk in swallowed.Keys) ReportCaptured(vk, false);
                swallowed.Clear();
                held.Clear();
                forwarded.Clear();
                hardwareFn.Clear();
                editorCapsCaptured = false;
            }
        }

        void SetHeld(bool down)
        {
            if (capsHeld == down) return;
            capsHeld = down;
            if (HeldChanged != null) HeldChanged(down);
        }

        IntPtr Filter(int code, IntPtr message, IntPtr data)
        {
            if (code < 0 || (Marshal.ReadInt32(data, 8) & 16) != 0)
                return CallNextHookEx(hook, code, message, data);

            int vk = Marshal.ReadInt32(data);
            long kind = message.ToInt64();
            bool down = kind == 0x100 || kind == 0x104;
            lock (stateLock)
            {
                SelectProfile();
                if (vk == CapsLock)
                {
                    SetHeld(down);
                    if (editorCapsCaptured || (down && (profile == null || !profile.IsTrigger(CapsLock)) && EditorReservesCaps()))
                    {
                        editorCapsCaptured = down;
                        return new IntPtr(1);
                    }
                }
                if (Process(vk, down, SendMapping)) return new IntPtr(1);
            }
            return CallNextHookEx(hook, code, message, data);
        }

        bool SendMapping(int key, bool preserve)
        {
            bool success = Tap(key, preserve);
            if (SentMapping != null) SentMapping(key, success);
            return success;
        }

        internal bool Process(int vk, bool down, Func<int, bool, bool> send)
        {
            if (!down)
            {
                forwarded.Remove(vk);
                if (!swallowed.Remove(vk)) return false;
                held.Remove(vk);
                ReportCaptured(vk, false);
                return true;
            }

            Binding existing;
            if (swallowed.TryGetValue(vk, out existing))
            {
                if (existing != null && existing == Resolve(vk)) send(existing.Key, existing.Preserve);
                return true;
            }
            if (profile == null) return false;
            if (forwarded.Contains(vk)) return false;

            if (profile.IsTrigger(vk))
            {
                if (hardwareFn.Count == 0) held.Add(vk);
                swallowed.Add(vk, null);
                ReportCaptured(vk, true);
                return true;
            }
            Binding binding = Resolve(vk);
            if (binding != null && send(binding.Key, binding.Preserve))
            {
                swallowed.Add(vk, binding);
                ReportCaptured(vk, true);
                return true;
            }
            forwarded.Add(vk);
            return false;
        }

        Binding Resolve(int vk)
        {
            if (profile == null || hardwareFn.Count != 0) return null;
            return Resolve(profile, held, vk);
        }

        internal bool PreviewRawKey(int vk, HashSet<int> physicalKeys)
        {
            lock (stateLock)
            {
                if (testProfile == null || testProfile.IsTrigger(vk) || hardwareFn.Count != 0 ||
                    physicalKeys.Contains(250) || physicalKeys.Contains(251) || IsTestWindowFocused == null || !IsTestWindowFocused()) return false;
                var binding = Resolve(testProfile, physicalKeys, vk);
                return binding != null && binding.Key != vk && SendMapping(binding.Key, binding.Preserve);
            }
        }

        static Binding Resolve(Profile profile, HashSet<int> held, int vk)
        {
            Binding binding;
            for (int index = profile.Activators.Length - 1; index > 0; index--)
            {
                if (!held.Contains(profile.Activators[index])) continue;
                if (profile.Bindings[index].TryGetValue(vk, out binding)) return binding;
                break;
            }
            return profile.Bindings[0].TryGetValue(vk, out binding) ? binding : null;
        }

        void ReportCaptured(int vk, bool down)
        {
            if (CapturedKey != null) CapturedKey(vk, down);
        }

        bool Down(int vk) { return !held.Contains(vk) && (GetAsyncKeyState(vk) & 0x8000) != 0; }

        bool Tap(int key, bool preserve)
        {
            var inputs = new List<Input>();
            bool keepShift = false;
            bool addShift = false;
            if (key == EscapeAction)
            {
                keepShift = Down(0xa0) || Down(0xa1);
                addShift = !keepShift && (Down(0xa2) || Down(0xa3));
                preserve = !keepShift && (preserve || addShift);
                key = BackquoteKey;
            }
            var held = ReleaseModifiers(inputs, preserve, keepShift);
            if (addShift) inputs.Add(Key(0xa0));
            inputs.Add(Key((ushort)key));
            inputs.Add(Key((ushort)key, true));
            if (addShift) inputs.Add(Key(0xa0, true));
            RestoreModifiers(inputs, held);
            return Send(inputs);
        }

        List<ushort> ReleaseModifiers(List<Input> input, bool preserve, bool keepShift)
        {
            var held = new List<ushort>();
            if (preserve) return held;
            foreach (ushort modifier in Modifiers)
            {
                if (keepShift && (modifier == 0xa0 || modifier == 0xa1) || !Down(modifier)) continue;
                held.Add(modifier);
                input.Add(Key(modifier, true));
            }
            return held;
        }

        static void RestoreModifiers(List<Input> input, List<ushort> held)
        {
            foreach (ushort modifier in held) input.Add(Key(modifier));
        }

        static bool Send(List<Input> inputs)
        {
            return SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(Input))) == inputs.Count;
        }

        static Input Key(ushort key, bool up = false)
        {
            uint flags = up ? 2u : 0u;
            ushort vk = key == 257 ? (ushort)13 : key;
            ushort scan = key == BackquoteKey ? (ushort)0x29 : (ushort)(MapVirtualKey(vk, 0) & 0xff);
            if (key >= 33 && key <= 40 || key >= 44 && key <= 46 || key == 111 || key == 144 ||
                key >= 166 && key <= 183 || key == 0xa3 || key == 0xa5 || key >= 91 && key <= 93 || key == 257)
                flags |= 1;
            // Match the stock Fn+Esc key and identify extended navigation keys by scan code.
            if (key == BackquoteKey || key >= 33 && key <= 40 && scan != 0) flags |= 8;
            return new Input { Type = 1, Data = new InputData { Keyboard = new KeyboardInput {
                Vk = (flags & 8) == 0 ? vk : (ushort)0, Scan = scan, Flags = flags
            } } };
        }

        public void Dispose()
        {
            if (hookThread != null && hookThread.IsAlive)
            {
                PostThreadMessage(hookThreadId, 0x12, UIntPtr.Zero, IntPtr.Zero);
                hookThread.Join(1000);
            }
            lock (stateLock)
            {
                testProfile = null;
                windowsProfile = null;
                SelectProfile();
                ResetHeldLayer();
            }
        }

        delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        struct Input { internal uint Type; internal InputData Data; }
        [StructLayout(LayoutKind.Explicit)]
        struct InputData
        {
            [FieldOffset(0)] internal KeyboardInput Keyboard;
            [FieldOffset(0)] internal MouseInput Sizing;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct KeyboardInput { internal ushort Vk, Scan; internal uint Flags, Time; internal IntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)]
        struct MouseInput { internal int X, Y; internal uint Button, Flags, Time; internal IntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)]
        struct NativeMessage
        {
            internal IntPtr Window;
            internal uint Message;
            internal UIntPtr WParam;
            internal IntPtr LParam;
            internal uint Time;
            internal int X, Y;
            internal uint Reserved;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int id, HookProc hook, IntPtr module, uint thread);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint first, uint last, uint remove);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetMessage(out NativeMessage message, IntPtr window, uint first, uint last);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr DispatchMessage(ref NativeMessage message);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool PostThreadMessage(uint thread, uint message, UIntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern short GetKeyState(int vk);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern uint SendInput(uint count, Input[] inputs, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    }
}
