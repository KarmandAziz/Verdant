using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;

namespace Verdant.App;

// Device-attributed notification for the first key emitted by our armed macro.
// Ordinary keyboard input never starts a mouse hold.
internal sealed class HardwareKeyTrigger : IDisposable
{
    private readonly HwndSource window;
    private readonly Action<int,bool> received;
    private readonly Dictionary<nint,bool> devices=[];
    internal HardwareKeyTrigger(Action<int,bool> received)
    {
        this.received=received;
        window=new HwndSource(new HwndSourceParameters("Verdant hardware key trigger") {
            ParentWindow=new nint(-3),WindowStyle=0,Width=0,Height=0 });
        window.AddHook(Message);
        if(!Native.RegisterRawInputDevices([new() { Page=1,Usage=6,Flags=0x100,Target=window.Handle }],1,
            (uint)Marshal.SizeOf<Native.Registration>()))
        {
            window.Dispose();
            throw new InvalidOperationException("Could not observe the mouse's hardware keyboard output.");
        }
    }
    private nint Message(nint hwnd,int msg,nint w,nint l,ref bool handled)
    {
        if(msg!=0xFF) return 0;
        uint size=0;
        if(GetRawInputData(l,0x10000003,0,ref size,24)==uint.MaxValue || size<40 || size>4096) return 0;
        var data=Marshal.AllocHGlobal((int)size);
        try {
            if(GetRawInputData(l,0x10000003,data,ref size,24)==uint.MaxValue) return 0;
            var header=Marshal.PtrToStructure<Header>(data);
            if(header.Type!=1 || header.Device==0) return 0;
            var key=Marshal.PtrToStructure<Keyboard>(data+24);
            if(!devices.TryGetValue(header.Device,out var supported))
            {
                uint chars=0; Native.DeviceName(header.Device,0x20000007,null,ref chars);
                if(chars==0 || chars>4096) return 0;
                var name=new StringBuilder((int)chars+1);
                Native.DeviceName(header.Device,0x20000007,name,ref chars);
                supported=name.ToString().Contains("VID_1532&PID_0099",StringComparison.OrdinalIgnoreCase);
                devices[header.Device]=supported;
            }
            if(supported) received(key.Vk,(key.Flags&1)==0);
        }
        finally { Marshal.FreeHGlobal(data); }
        return 0;
    }
    public void Dispose()
    {
        Native.RegisterRawInputDevices([new() { Page=1,Usage=6,Flags=1 }],1,(uint)Marshal.SizeOf<Native.Registration>());
        window.RemoveHook(Message); window.Dispose();
    }
    [StructLayout(LayoutKind.Sequential)] private struct Header { public uint Type,Size; public nint Device,WParam; }
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard { public ushort Scan,Flags,Reserved,Vk; public uint Message,Extra; }
    [DllImport("user32.dll")] private static extern uint GetRawInputData(nint input,uint command,nint data,ref uint size,uint headerSize);
}
