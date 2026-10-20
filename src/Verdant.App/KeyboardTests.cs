using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.IO.MemoryMappedFiles;

namespace Verdant.App;

// Keyboard-only integration test: never moves/clicks the mouse and refuses
// key-down delivery whenever this isolated window is not foreground.
internal sealed class KeyboardTests : Window
{
    private readonly string output;
    private readonly List<string> results = [];
    private readonly List<long> downs = [], ups = [];
    private nint handle;
    private InputEngine? engine;
    private bool started, interrupted;
    private readonly TextBlock status = new() { Text="Keep this window focused. Keyboard checks start automatically.", Margin=new(24), TextWrapping=TextWrapping.Wrap };

    public KeyboardTests(string output)
    {
        this.output=output; Title="Verdant keyboard verification"; Width=580; Height=190;
        WindowStartupLocation=WindowStartupLocation.CenterScreen; Content=status;
        SourceInitialized+=(_,_)=> { handle=new WindowInteropHelper(this).Handle; HwndSource.FromHwnd(handle)!.AddHook(Receive); };
        Loaded+=async (_,_)=> {
            if(started) return; started=true;
            Activate();
            var end=Environment.TickCount64+30000;
            while(Native.GetForegroundWindow()!=handle && Environment.TickCount64<end) await Task.Delay(100);
            await Run();
        };
        Deactivated+=(_,_)=> { if(engine==null) return; interrupted=true; engine.StopAll(); };
        Closed+=(_,_)=>engine?.StopAll();
    }
    private nint Receive(nint hwnd, int msg, nint w, nint l, ref bool handled)
    {
        if(msg is 0x100 or 0x101 && w==70 && (nuint)Native.GetMessageExtraInfo()==Native.Marker)
        {
            (msg==0x100 ? downs : ups).Add(Environment.TickCount64);
            handled=true;
        }
        return 0;
    }
    private void Check(bool condition, string message)
    {
        results.Add((condition ? "PASS " : "FAIL ")+message);
        File.WriteAllLines(output,results);
        if(!condition) throw new InvalidOperationException(message);
    }
    private async Task Until(Func<bool> condition)
    {
        var end=Environment.TickCount64+2500;
        while(!condition() && Environment.TickCount64<end && !interrupted) await Task.Delay(5);
        if(interrupted || !condition()) throw new InvalidOperationException("Test interrupted or expected input timed out.");
    }
    private async Task Run()
    {
        try
        {
            Check(Native.GetForegroundWindow()==handle,"Isolated test window has focus");
            using var sink=new WindowsSink();
            using var guarded=new ForegroundSink(sink,handle);
            using var testEngine=new InputEngine(guarded); engine=testEngine;
            string? error=null; engine.Error+=text=>error=text;
            var b=new Binding { Kind=ActionKind.Keyboard, Keys=[135], Mode=PlayMode.Toggle,
                ExtraKeyEnabled=true, ExtraKeys=[70], ExtraIntervalMs=1000, ExtraPressMs=65, ExtraCompatibilityInput=true };
            status.Text="Checking 65 ms F presses every second and toggle cleanup…";
            engine.Trigger("test",b,true); engine.Trigger("test",b,false);
            await Until(()=>downs.Count>=1);
            using(var map=MemoryMappedFile.OpenExisting("Local\\Verdant-input-"+Environment.ProcessId))
            using(var state=map.CreateViewAccessor())
                Check(state.ReadByte(70)==2,"Recovery state remembers the compatibility release method");
            await Until(()=>ups.Count>=2);
            Check((Native.GetAsyncKeyState(135)&0x8000)!=0,"Primary output stays held across repeated F presses");
            Check(downs.Count==ups.Count && downs.Count>=2,"Windows receives balanced compatibility F presses");
            Check(downs.Zip(ups,(down,up)=>up-down).All(ms=>ms is >=50 and <=200),"Observed F press durations are within 50–200 ms (65 ms requested)");
            Check(downs.Zip(downs.Skip(1),(a,b)=>b-a).All(ms=>ms is >=900 and <=1250),"F presses repeat approximately every second");
            await Until(()=>downs.Count>=3);
            engine.Trigger("test",b,true);
            await Until(()=>ups.Count==downs.Count);
            Check((Native.GetAsyncKeyState(70)&0x8000)==0 && (Native.GetAsyncKeyState(135)&0x8000)==0,"Toggle off during F press releases both outputs");
            var count=downs.Count; await Task.Delay(1100);
            Check(!interrupted && downs.Count==count && engine.ActiveCount==0 && error==null,"No further F presses after toggle off");
            results.Add("DETAIL press durations ms: "+string.Join(", ",downs.Zip(ups,(down,up)=>up-down)));
            results.Add("COMPLETE: Windows verification only; game acceptance requires a manual check.");
        }
        catch(Exception ex) { results.Add("ERROR "+ex.Message); }
        finally { engine?.StopAll(); engine=null; File.WriteAllLines(output,results); Application.Current.Shutdown(); }
    }
    private sealed class ForegroundSink(WindowsSink sink, nint window) : IInputSink, IDisposable
    {
        public void Down(InputToken t)=>Down(t,false);
        public void Down(InputToken t,bool compatibility)
        {
            if(Native.GetForegroundWindow()!=window) throw new InvalidOperationException("Focus changed; no key-down sent.");
            sink.Down(t,compatibility);
        }
        public void Up(InputToken t)=>sink.Up(t);
        public void Move(int x,int y)=>throw new NotSupportedException();
        public void Wheel(int delta,bool horizontal)=>throw new NotSupportedException();
        public void Dispose() { }
    }
}
