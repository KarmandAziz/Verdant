using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Verdant.App;

// Direct HID implementation inside Verdant. Protocol/encoding adapted from
// Secretarybird (MIT); no executable or service from that project is used.
internal sealed class BasiliskPlayback : IOnMousePlayback
{
    internal sealed class Lease
    {
        public string Path { get; set; } = "";
        public byte[] Original { get; set; } = [];
        public byte[] Assignment { get; set; } = [];
        public byte[] Owner { get; set; } = [];
        public byte[] Data { get; set; } = [];
        public int[] Keys { get; set; } = [];
        public ushort MacroId { get; set; }
        public bool Allocated { get; set; }
        public bool HeaderWritten { get; set; }
        public bool AssignmentAttempted { get; set; }
        public int MaximumPlaybackMs { get; set; }
        public byte[] OriginalMode { get; set; } = [];
        public bool ModeAttempted { get; set; }
        public bool Continuous { get; set; }
        public bool PlaybackMayBeActive { get; set; }
    }
    private readonly Device device;
    private readonly Lease lease;
    private readonly string file;
    private long drainUntil;
    public bool Armed { get; private set; }
    public byte[] Data => lease.Data;
    internal static string LeasePath(int pid) => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Verdant",$"hardware-lease-{pid}.json");
    private void Save()
    {
        File.WriteAllText(file+".tmp",JsonSerializer.Serialize(lease));
        File.Move(file+".tmp",file,true);
    }
    internal static BasiliskPlayback CreateContinuous(Binding binding) => new(binding,continuous:true);
    public BasiliskPlayback(Binding binding, int boundedTestTaps=0,bool boundedMouseHold=false,bool boundedToggle=false,bool continuous=false)
    {
        var data=continuous ? OnboardMacro.EncodeKeyboard(binding) : boundedToggle ? OnboardMacro.EncodeBoundedToggleTest(binding,boundedTestTaps) :
            boundedMouseHold ? OnboardMacro.EncodeBoundedHoldTest(binding,boundedTestTaps) : OnboardMacro.EncodeBoundedKeyTest(binding,boundedTestTaps);
        if(data.Length>72) throw new InvalidDataException("Diagnostic macro exceeds one verified data packet.");
        file=LeasePath(Environment.ProcessId);
        if(File.Exists(file)) Recover(file);
        device=Device.Open();
        lease=new Lease { Path=device.Path,Original=device.Assignment(),Owner=Guid.NewGuid().ToByteArray(),
            Data=data,Keys=binding.ExtraKeys.ToArray(),MaximumPlaybackMs=boundedTestTaps*binding.ExtraIntervalMs,Continuous=continuous };
        try
        {
            lease.OriginalMode=device.Query(0,0x84,2).Take(2).ToArray();
            if(continuous && lease.OriginalMode[0]!=0)
                throw new InvalidOperationException("Hardware extra keys require the mouse's normal device mode.");
            var storage=device.Query(6,0x8E,14);
            uint free=((uint)storage[6]<<24)|((uint)storage[7]<<16)|((uint)storage[8]<<8)|storage[9];
            if(free<512) throw new InvalidOperationException("The mouse has insufficient free macro memory.");
            var taken=device.MacroIds(); int maximum=storage[0]*256+storage[1];
            lease.MacroId=(ushort)Enumerable.Range(1,maximum).FirstOrDefault(i=>!taken.Contains((ushort)i));
            if(lease.MacroId==0) throw new InvalidOperationException("No free macro slot on the mouse.");
            byte hi=(byte)(lease.MacroId>>8),lo=(byte)lease.MacroId;
            lease.Assignment=continuous ? [0,5,0,5,2,hi,lo,0,0,0] : [0,5,0,3,3,hi,lo,1,0,0];
            Save(); lease.Allocated=true; Save();
            device.Query(6,8,6,[hi,lo,0,0,0,(byte)lease.Data.Length]);
            byte[] header=new byte[250];
            lease.Owner.CopyTo(header,0);
            int keyCount=binding.ExtraKeys.Select(OnboardMacro.Usage).Distinct().Count();
            BitConverter.GetBytes(continuous ? 2+keyCount*2 : boundedTestTaps*(2+keyCount*2)+(boundedToggle?boundedTestTaps+1:boundedMouseHold?2:0)).CopyTo(header,16);
            Encoding.UTF8.GetBytes(continuous ? "Verdant extra key" : "Verdant bounded keyboard diagnostic").CopyTo(header,20);
            for(int offset=0;offset<header.Length;offset+=64)
            {
                var args=new byte[]{hi,lo,(byte)(offset>>8),(byte)offset,0,250}.Concat(header.Skip(offset).Take(64)).ToArray();
                device.Query(6,12,(byte)args.Length,args);
            }
            if(!device.Query(6,0x8C,22,[hi,lo,0,0]).Skip(6).Take(16).SequenceEqual(lease.Owner))
                throw new InvalidOperationException("Mouse macro header verification failed.");
            lease.HeaderWritten=true; Save();
            bool verified=false;
            for(int attempt=0;attempt<3;attempt++)
            {
                var args=new byte[]{hi,lo,0,0,0,0,(byte)lease.Data.Length}.Concat(lease.Data).ToArray();
                device.Query(6,9,(byte)args.Length,args);
                var size=device.Query(6,0x88,6,[hi,lo]);
                var read=device.Query(6,0x89,(byte)args.Length,[hi,lo,0,0,0,0,(byte)lease.Data.Length]);
                if(size.Skip(2).Take(4).SequenceEqual(new byte[]{0,0,0,(byte)lease.Data.Length}) &&
                   read[6]==lease.Data.Length && read.Skip(7).Take(lease.Data.Length).SequenceEqual(lease.Data))
                { verified=true; break; }
            }
            if(!verified) throw new InvalidOperationException("Mouse macro verification failed; original mapping retained.");
        }
        catch
        {
            device.Dispose();
            if(File.Exists(file)) Recover(file);
            throw;
        }
    }
    public void Arm()
    {
        if(Armed) return;
        if(!device.Assignment().SequenceEqual(lease.Original))
            throw new InvalidOperationException("Mouse assignment changed outside Verdant. Reopen Verdant before enabling on-mouse playback.");
        lease.AssignmentAttempted=true; lease.PlaybackMayBeActive=true; Save();
        device.Query(2,12,80,lease.Assignment);
        if(!device.Assignment().SequenceEqual(lease.Assignment)) throw new InvalidOperationException("Mouse toggle assignment did not verify.");
        Armed=true;
    }
    public void Stop()
    {
        // Restores the trigger only. Does NOT claim to cancel an in-flight sequence.
        RestoreMode(device,lease);
        if(!lease.AssignmentAttempted) return;
        Restore(device,lease);
        drainUntil=Environment.TickCount64+lease.MaximumPlaybackMs+250;
        Armed=false; lease.AssignmentAttempted=false; Save();
    }
    internal void TestModeCycleCancellation() => CancelPlayback();
    internal void TestRemoveRepeatedMouseDown()
    {
        if(lease.Continuous || lease.MaximumPlaybackMs!=6000 || lease.Data.Length>72)
            throw new InvalidOperationException("Live rewrite is restricted to the six-second finite diagnostic.");
        var data=lease.Data.ToArray();
        int replaced=0;
        for(int i=0;i<data.Length;)
        {
            var op=data[i];
            if(op==8 && data[i+1]==16) { data[i]=17; data[i+1]=0; replaced++; }
            i+=op switch { 1 or 2 or 8 or 17=>2,18=>3,_=>throw new InvalidDataException("Unexpected diagnostic command.") };
        }
        if(replaced!=6 || !data.TakeLast(2).SequenceEqual(new byte[]{8,0}))
            throw new InvalidDataException("Expected six finite repetitions and a final mouse release.");
        byte hi=(byte)(lease.MacroId>>8),lo=(byte)lease.MacroId;
        var args=new byte[]{hi,lo,0,0,0,0,(byte)data.Length}.Concat(data).ToArray();
        device.Query(6,9,(byte)args.Length,args);
        var read=device.Query(6,0x89,(byte)args.Length,[hi,lo,0,0,0,0,(byte)data.Length]);
        if(read[6]!=data.Length || !read.Skip(7).Take(data.Length).SequenceEqual(data))
            throw new InvalidOperationException("Finite diagnostic rewrite did not verify.");
    }
    public void CancelPlayback()
    {
        // Mapping restoration alone does not stop firmware playback. Mode cycling
        // was verified with finite F and combined mouse/F sequences on this device.
        if(lease.OriginalMode.Length!=2 || lease.OriginalMode[0]!=0)
            throw new InvalidOperationException("Mode-cycle diagnostic requires the original normal device mode.");
        Stop();
        lease.ModeAttempted=true; Save();
        try
        {
            device.Query(0,4,2,[3,0]);
            if(!device.Query(0,0x84,2).Take(2).SequenceEqual(new byte[]{3,0}))
                throw new InvalidOperationException("Driver mode did not verify.");
        }
        finally { RestoreMode(device,lease); Save(); }
        lease.PlaybackMayBeActive=false; drainUntil=0; Save();
    }
    private static void RestoreMode(Device device,Lease lease)
    {
        if(!lease.ModeAttempted) return;
        if(lease.OriginalMode.Length!=2) throw new InvalidDataException("Missing original device mode.");
        device.Query(0,4,2,lease.OriginalMode);
        if(!device.Query(0,0x84,2).Take(2).SequenceEqual(lease.OriginalMode))
            throw new InvalidOperationException("Original device mode did not restore.");
        lease.ModeAttempted=false;
    }
    private static void Restore(Device device,Lease lease)
    {
        var current=device.Assignment();
        if(current.SequenceEqual(lease.Assignment))
        {
            device.Query(2,12,80,lease.Original);
            if(!device.Assignment().SequenceEqual(lease.Original)) throw new InvalidOperationException("Could not restore the original mouse assignment.");
        }
        else if(!current.SequenceEqual(lease.Original))
            throw new InvalidOperationException("Mouse mapping changed externally; restoration requires review.");
    }
    public void Dispose()
    {
        try {
            if(lease.Continuous && lease.PlaybackMayBeActive) CancelPlayback(); else Stop();
            Thread.Sleep((int)Math.Clamp(drainUntil-Environment.TickCount64,0,6250));
            RemoveMacro(device,lease); if(File.Exists(file)) File.Delete(file);
        }
        finally { device.Dispose(); }
    }
    private static void RemoveMacro(Device device,Lease lease)
    {
        if(!lease.Allocated || !device.MacroIds().Contains(lease.MacroId)) return;
        byte hi=(byte)(lease.MacroId>>8),lo=(byte)lease.MacroId;
        if(lease.HeaderWritten && !device.Query(6,0x8C,22,[hi,lo,0,0]).Skip(6).Take(16).SequenceEqual(lease.Owner))
            throw new InvalidOperationException("Macro ownership changed; refusing to remove another macro.");
        device.Query(6,3,2,[hi,lo]);
        if(device.MacroIds().Contains(lease.MacroId)) throw new InvalidOperationException("Temporary mouse macro was not removed.");
    }
    internal static void Recover(string file)
    {
        if(!File.Exists(file)) return;
        var lease=JsonSerializer.Deserialize<Lease>(File.ReadAllText(file)) ?? throw new InvalidDataException("Invalid hardware recovery file.");
        if(lease.Original.Length!=10 || lease.Assignment.Length!=10 || lease.Owner.Length!=16 ||
           lease.MacroId==0 || lease.Keys.Any(k=>k is <1 or >255))
            throw new InvalidDataException("Invalid hardware recovery contents.");
        using var device=Device.Open(lease.Path);
        RestoreMode(device,lease);
        if(lease.AssignmentAttempted) Restore(device,lease);
        if(lease.Continuous && lease.PlaybackMayBeActive)
        {
            if(lease.OriginalMode.Length!=2 || lease.OriginalMode[0]!=0)
                throw new InvalidDataException("Missing original device mode for hardware recovery.");
            lease.ModeAttempted=true;
            File.WriteAllText(file,JsonSerializer.Serialize(lease));
            try {
                device.Query(0,4,2,[3,0]);
                if(!device.Query(0,0x84,2).Take(2).SequenceEqual(new byte[]{3,0}))
                    throw new InvalidOperationException("Recovery could not verify driver mode.");
            } finally { RestoreMode(device,lease); }
        }
        else if(!lease.Continuous)
        {
            if(lease.MaximumPlaybackMs is <1 or >6000)
                throw new InvalidDataException("Legacy unbounded hardware lease requires manual recovery.");
            Thread.Sleep(lease.MaximumPlaybackMs+250);
        }
        RemoveMacro(device,lease);
        // The finite keyboard sequence has had time to finish after trigger restoration.
        if(lease.PlaybackMayBeActive || lease.AssignmentAttempted)
        {
            foreach(var key in lease.Keys) Native.Key(key,false);
            Native.Mouse((int)ButtonId.Forward,false);
        }
        File.Delete(file);
    }

    private sealed class Device : IDisposable
    {
        public string Path { get; }
        private readonly SafeFileHandle handle;
        private Device(string path)
        {
            Path=path; handle=CreateFile(path,0,3,0,3,0,0);
            if(handle.IsInvalid) throw new InvalidOperationException("Cannot open the Basilisk V3 control interface.");
        }
        public static Device Open(string? expected=null)
        {
            HidD_GetHidGuid(out var guid);
            var set=SetupDiGetClassDevs(ref guid,0,0,0x12);
            var paths=new List<string>();
            try
            {
                for(uint i=0;;i++)
                {
                    var info=new InterfaceData { Size=Marshal.SizeOf<InterfaceData>() };
                    if(!SetupDiEnumDeviceInterfaces(set,0,ref guid,i,ref info)) break;
                    SetupDiGetDeviceInterfaceDetail(set,ref info,0,0,out var needed,0);
                    if(needed<8 || needed>65536) continue;
                    var buffer=Marshal.AllocHGlobal((int)needed);
                    try
                    {
                        Marshal.WriteInt32(buffer,IntPtr.Size==8?8:6);
                        if(!SetupDiGetDeviceInterfaceDetail(set,ref info,buffer,needed,out _,0)) continue;
                        var path=Marshal.PtrToStringUni(buffer+4)!;
                        if(path.Contains("vid_1532&pid_0099",StringComparison.OrdinalIgnoreCase) &&
                           path.Contains("mi_03",StringComparison.OrdinalIgnoreCase) &&
                           (expected==null || path.Equals(expected,StringComparison.OrdinalIgnoreCase))) paths.Add(path);
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            if(paths.Count!=1) throw new InvalidOperationException("On-mouse playback requires one connected USB Basilisk V3 (1532:0099).");
            return new Device(paths[0]);
        }
        public byte[] Assignment()
        {
            var data=Query(2,0x8C,80,[0,5,0]);
            return new byte[]{0,5,0}.Concat(data.Skip(3).Take(7)).ToArray();
        }
        public HashSet<ushort> MacroIds()
        {
            var countBytes=Query(6,0x80,2); int count=countBytes[0]*256+countBytes[1];
            if(count>1000) throw new InvalidOperationException("Invalid mouse macro count.");
            var result=new HashSet<ushort>();
            for(int offset=0;offset<count;offset+=32)
            {
                int n=Math.Min(32,count-offset);
                var ids=Query(6,0x8B,(byte)(n*2+4),[(byte)(offset>>8),(byte)offset]);
                for(int i=0;i<n;i++) result.Add((ushort)(ids[4+i*2]*256+ids[5+i*2]));
            }
            return result;
        }
        public byte[] Query(byte cls,byte id,byte size,byte[]? args=null)
        {
            args ??=[];
            if(args.Length>80 || size>80) throw new InvalidOperationException("Invalid HID report length.");
            byte[] request=new byte[91]; request[2]=0x1F; request[6]=size; request[7]=cls; request[8]=id;
            args.CopyTo(request,9); for(int i=3;i<89;i++) request[89]^=request[i];
            if(!HidD_SetFeature(handle,request,91)) throw new InvalidOperationException("Mouse feature request failed.");
            for(int i=0;i<200;i++)
            {
                Thread.Sleep(10); byte[] reply=new byte[91];
                if(!HidD_GetFeature(handle,reply,91)) throw new InvalidOperationException("Mouse feature response failed.");
                if(reply[7]!=cls || reply[8]!=id || reply[2]!=0x1F || reply[1] is 0 or 1) continue;
                byte crc=0; for(int j=3;j<89;j++) crc^=reply[j];
                if(reply[89]!=crc || reply[1]!=2) throw new InvalidOperationException($"Mouse rejected command {cls:X2}:{id:X2} (status {reply[1]}).");
                return reply.Skip(9).Take(80).ToArray();
            }
            throw new InvalidOperationException("Mouse feature request timed out.");
        }
        public void Dispose()=>handle.Dispose();
        [StructLayout(LayoutKind.Sequential)] private struct InterfaceData { public int Size; public Guid Class; public int Flags; public nint Reserved; }
        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("setupapi.dll",CharSet=CharSet.Unicode)] private static extern nint SetupDiGetClassDevs(ref Guid guid,nint enumerator,nint parent,uint flags);
        [DllImport("setupapi.dll")] private static extern bool SetupDiEnumDeviceInterfaces(nint set,nint device,ref Guid guid,uint index,ref InterfaceData data);
        [DllImport("setupapi.dll",CharSet=CharSet.Unicode)] private static extern bool SetupDiGetDeviceInterfaceDetail(nint set,ref InterfaceData data,nint detail,uint size,out uint needed,nint device);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(nint set);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern SafeFileHandle CreateFile(string path,uint access,uint share,nint security,uint creation,uint flags,nint template);
        [DllImport("hid.dll")] private static extern bool HidD_SetFeature(SafeFileHandle handle,byte[] buffer,int length);
        [DllImport("hid.dll")] private static extern bool HidD_GetFeature(SafeFileHandle handle,byte[] buffer,int length);
    }
}
