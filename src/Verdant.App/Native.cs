using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.IO.MemoryMappedFiles;
using Microsoft.Win32.SafeHandles;

namespace Verdant.App;

internal static class Native
{
    public const nuint Marker = 0x56455244;
    public delegate nint HookProc(int code, nint w, nint l);
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct MouseHook { public Point Point; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] public struct KeyHook { public uint Vk, Scan, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] public struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] public struct KeyInput { public ushort Vk, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Explicit)] public struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Key; }
    [StructLayout(LayoutKind.Sequential)] public struct Input { public uint Type; public InputUnion Value; }
    [StructLayout(LayoutKind.Sequential)] public struct RawDevice { public nint Handle; public uint Type; }
    [StructLayout(LayoutKind.Sequential)] public struct Registration { public ushort Page, Usage; public uint Flags; public nint Target; }
    [DllImport("user32.dll", SetLastError = true)] public static extern nint SetWindowsHookEx(int type, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] public static extern nint CallNextHookEx(nint hook, int code, nint w, nint l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, nuint extra);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern nint GetMessageExtraInfo();
    [DllImport("user32.dll", EntryPoint="MapVirtualKeyW")] private static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterRawInputDevices(Registration[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)] public static extern uint GetRawInputDeviceList([Out] RawDevice[]? list, ref uint count, uint size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetRawInputDeviceInfoW")] public static extern uint DeviceName(nint device, uint command, StringBuilder? text, ref uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("hid.dll")] public static extern bool HidD_GetProductString(SafeFileHandle device, byte[] buffer, uint length);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] public static extern uint CM_Locate_DevNodeW(out uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll")] public static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] public static extern uint CM_Get_Device_IDW(uint node, StringBuilder id, uint length, uint flags);
    public static void Send(Input input) { if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1) throw new InvalidOperationException("Windows rejected input. Check whether the target runs as administrator."); }
    internal static KeyInput KeyEvent(int vk, bool down)
    {
        var scan = MapVirtualKey((uint)vk, 4); // MAPVK_VK_TO_VSC_EX includes E0/E1 prefixes.
        // Pause/PrintScreen and keys without a hardware mapping retain virtual-key handling.
        var useScan = scan != 0 && (scan >> 8) != 0xE1 && vk is not (0x13 or 0x2C);
        // Some layouts map navigation VKs to the unprefixed keypad scan code.
        var extended = (scan >> 8) == 0xE0 || vk is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0x5B or 0x5C or 0x6F or 0xA3 or 0xA5 or >= 0xA6 and <= 0xB7;
        return new KeyInput { Vk=useScan ? (ushort)0 : (ushort)vk, Scan=(ushort)(scan & 0xFF), Flags=(down ? 0u : 2u) | (useScan ? 8u : 0u) | (extended ? 1u : 0u), Extra=Marker };
    }
    public static void Key(int vk, bool down, bool compatibility = false)
    {
        var key = KeyEvent(vk, down);
        if (compatibility)
        {
            // The legacy Windows path supplies VK and scan together. It remains
            // ordinary injected input; no driver or injected-flag removal is involved.
            keybd_event((byte)vk, (byte)key.Scan, key.Flags & 3, Marker);
        }
        else Send(new Input { Type = 1, Value = new InputUnion { Key = key } });
    }
    public static void Mouse(int button, bool down)
    {
        uint flags = button switch { 0 => down ? 2u : 4u, 1 => down ? 8u : 16u, 2 => down ? 32u : 64u, _ => down ? 128u : 256u };
        Send(new Input { Value = new InputUnion { Mouse = new MouseInput { Flags = flags, Data = button >= 3 ? (uint)(button - 2) : 0, Extra = Marker } } });
    }
}
internal sealed class WindowsSink : IInputSink, IDisposable
{
    private readonly MemoryMappedFile map;
    private readonly MemoryMappedViewAccessor state;
    private readonly Process? guardianProcess;
    public WindowsSink(bool guardian = true)
    {
        map = MemoryMappedFile.CreateNew("Local\\Verdant-input-" + Environment.ProcessId, 512);
        state = map.CreateViewAccessor();
        if (guardian)
        {
            guardianProcess = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { Arguments = "--guardian " + Environment.ProcessId, UseShellExecute = false, CreateNoWindow = true });
            var wait = Stopwatch.StartNew();
            while (state.ReadByte(511) != 1 && guardianProcess is { HasExited: false } && wait.ElapsedMilliseconds < 5000) Thread.Sleep(10);
            if (state.ReadByte(511) != 1) throw new InvalidOperationException("Input recovery could not start. No bindings were enabled.");
        }
    }
    public void Down(InputToken t) => Down(t, false);
    public void Down(InputToken t, bool compatibility) { if (guardianProcess?.HasExited == true) throw new InvalidOperationException("The recovery process stopped. Restart Verdant before using bindings."); state.Write(t.Keyboard ? t.Code : 256 + t.Code, (byte)(t.Keyboard && compatibility ? 2 : 1)); if (t.Keyboard) Native.Key(t.Code, true, compatibility); else Native.Mouse(t.Code, true); }
    public void Up(InputToken t) { if (t.Keyboard) Native.Key(t.Code, false, state.ReadByte(t.Code) == 2); else Native.Mouse(t.Code, false); state.Write(t.Keyboard ? t.Code : 256 + t.Code, (byte)0); }
    public void TrackPhysical(InputToken t, bool down) { if (down && guardianProcess?.HasExited == true) throw new InvalidOperationException("Input recovery stopped. Restart Verdant."); state.Write(t.Keyboard ? t.Code : 256 + t.Code, down ? (byte)1 : (byte)0); }
    public void Move(int x, int y)
    {
        var left = Native.GetSystemMetrics(76); var top = Native.GetSystemMetrics(77);
        var w = Math.Max(2, Native.GetSystemMetrics(78)); var h = Math.Max(2, Native.GetSystemMetrics(79));
        Native.Send(new Native.Input { Value = new Native.InputUnion { Mouse = new Native.MouseInput { X = (int)Math.Clamp((long)(x - left) * 65535 / (w - 1), 0, 65535), Y = (int)Math.Clamp((long)(y - top) * 65535 / (h - 1), 0, 65535), Flags = 0xC001, Extra = Native.Marker } } });
    }
    public void Wheel(int delta, bool horizontal) => Native.Send(new Native.Input { Value = new Native.InputUnion { Mouse = new Native.MouseInput { Data = unchecked((uint)delta), Flags = horizontal ? 0x1000u : 0x800u, Extra = Native.Marker } } });
    public void Dispose() { guardianProcess?.Dispose(); state.Dispose(); map.Dispose(); }
    public static void Guardian(int pid)
    {
        try
        {
            using var parent = Process.GetProcessById(pid);
            using var map = MemoryMappedFile.OpenExisting("Local\\Verdant-input-" + pid);
            using var view = map.CreateViewAccessor();
            view.Write(511, (byte)1);
            parent.WaitForExit();
            try { BasiliskPlayback.Recover(BasiliskPlayback.LeasePath(pid)); }
            catch(Exception ex) { try { File.WriteAllText(BasiliskPlayback.LeasePath(pid)+".error",ex.Message); } catch { } }
            for (int i = 1; i < 261; i++) if (view.ReadByte(i) != 0) { try { if (i < 256) Native.Key(i, false, view.ReadByte(i) == 2); else Native.Mouse(i - 256, false); } catch { } }
        }
        catch (ArgumentException) { }
        catch (FileNotFoundException) { }
    }
}
internal sealed class Hooks : IDisposable
{
    internal const nuint TestMarker = 0x56455453;
    internal bool AcceptTestInput;
    internal string TestTrace = "No mouse callback";
    private readonly Native.HookProc mouseProc, keyProc;
    private readonly nint mouse, key;
    public Func<ButtonId, bool, bool>? Button;
    public Action<MacroEvent>? Record;
    public bool RecordMoves;
    public int IgnoredInjected;
    public Hooks()
    {
        mouseProc = Mouse; keyProc = Key;
        mouse = Native.SetWindowsHookEx(14, mouseProc, Native.GetModuleHandle(null), 0);
        key = Native.SetWindowsHookEx(13, keyProc, Native.GetModuleHandle(null), 0);
        if (mouse == 0 || key == 0) { Dispose(); throw new InvalidOperationException("Windows input hooks could not be installed."); }
    }
    private nint Mouse(int code, nint w, nint l)
    {
        if (code >= 0)
        {
            var m = Marshal.PtrToStructure<Native.MouseHook>(l);
            if (AcceptTestInput && (int)w is 0x20B or 0x20C) TestTrace = $"msg={w:X} flags={m.Flags:X} extra={m.Extra:X} data={m.Data:X}";
            if ((m.Flags & 1) != 0 || m.Extra == Native.Marker) Interlocked.Increment(ref IgnoredInjected);
            if ((m.Flags & 1) == 0 && m.Extra != Native.Marker || AcceptTestInput && m.Extra == TestMarker)
            {
                var msg = (int)w;
                ButtonId? b = msg switch { 0x201 or 0x202 => ButtonId.Left, 0x204 or 0x205 => ButtonId.Right, 0x207 or 0x208 => ButtonId.Middle, 0x20B or 0x20C => (m.Data >> 16) == 1 ? ButtonId.Back : ButtonId.Forward, 0x20A => (short)(m.Data >> 16) > 0 ? ButtonId.WheelUp : ButtonId.WheelDown, 0x20E => (short)(m.Data >> 16) > 0 ? ButtonId.WheelRight : ButtonId.WheelLeft, _ => null };
                bool down = msg is 0x201 or 0x204 or 0x207 or 0x20B or 0x20A or 0x20E;
                if (Record != null)
                {
                    if (msg == 0x200 && RecordMoves) Record(new() { Kind = EventKind.Move, X = m.Point.X, Y = m.Point.Y });
                    else if (msg is 0x20A or 0x20E) Record(new() { Kind = EventKind.Wheel, Code = (short)(m.Data >> 16), X = msg == 0x20E ? 1 : 0 });
                    else if (b != null) Record(new() { Kind = down ? EventKind.MouseDown : EventKind.MouseUp, Code = (int)b });
                }
                if (b != null && Button?.Invoke(b.Value, down) == true) return 1;
            }
        }
        return Native.CallNextHookEx(mouse, code, w, l);
    }
    private nint Key(int code, nint w, nint l)
    {
        if (code >= 0)
        {
            var k = Marshal.PtrToStructure<Native.KeyHook>(l);
            if ((k.Flags & 16) != 0 || k.Extra == Native.Marker) Interlocked.Increment(ref IgnoredInjected);
            if ((k.Flags & 16) == 0 && k.Extra != Native.Marker) Record?.Invoke(new() { Kind = (int)w is 0x100 or 0x104 ? EventKind.KeyDown : EventKind.KeyUp, Code = (int)k.Vk });
        }
        return Native.CallNextHookEx(key, code, w, l);
    }
    public void Dispose() { if (mouse != 0) Native.UnhookWindowsHookEx(mouse); if (key != 0) Native.UnhookWindowsHookEx(key); }
}
internal sealed record MouseDevice(string Name, string Path)
{
    public override string ToString() => Name;
    public static List<MouseDevice> Detect()
    {
        uint count = 0; uint size = (uint)Marshal.SizeOf<Native.RawDevice>();
        if (Native.GetRawInputDeviceList(null, ref count, size) == uint.MaxValue) throw new InvalidOperationException("Mouse enumeration failed.");
        var devices = new Native.RawDevice[count];
        var result = Native.GetRawInputDeviceList(devices, ref count, size);
        if (result == uint.MaxValue) return [];
        var list = new List<MouseDevice>();
        foreach (var d in devices.Take((int)result).Where(d => d.Type == 0))
        {
            uint chars = 0; Native.DeviceName(d.Handle, 0x20000007, null, ref chars);
            var name = new StringBuilder((int)chars + 1); Native.DeviceName(d.Handle, 0x20000007, name, ref chars);
            string path = name.ToString(), label = "HID mouse";
            if (!IsPhysicalMouse(path)) continue;
            using (var handle = Native.CreateFile(path, 0, 3, 0, 3, 0, 0))
            { byte[] buffer = new byte[512]; if (!handle.IsInvalid && Native.HidD_GetProductString(handle, buffer, 512)) label = Encoding.Unicode.GetString(buffer).TrimEnd('\0'); }
            list.Add(new(label, path));
        }
        return list;
    }
    // Raw Input's mouse type also includes keyboard mouse emulation and virtual
    // collections. Only list devices with a mouse-specific hardware ancestor.
    private static bool IsPhysicalMouse(string path)
    {
        if (!path.StartsWith(@"\\?\", StringComparison.Ordinal)) return false;
        var parts = path[4..].Split('#');
        if (parts.Length < 3 || Native.CM_Locate_DevNodeW(out var node, string.Join("\\", parts.Take(3)), 0) != 0) return false;
        for (var depth = 0; depth < 8; depth++)
        {
            var id = new StringBuilder(512);
            if (Native.CM_Get_Device_IDW(node, id, 512, 0) != 0) return false;
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + id);
            var compatible = key?.GetValue("CompatibleIDs") as string[] ?? [];
            if (compatible.Any(s => s.Equals(@"USB\Class_03&SubClass_01&Prot_02", StringComparison.OrdinalIgnoreCase) || s.Equals(@"*PNP0F13", StringComparison.OrdinalIgnoreCase))) return true;
            if (Native.CM_Get_Parent(out var parent, node, 0) != 0) break;
            node = parent;
        }
        return false;
    }
}
