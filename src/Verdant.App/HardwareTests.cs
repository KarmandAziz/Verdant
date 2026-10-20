using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Verdant.App;

internal sealed class HardwareTests : Window
{
    private readonly string report;
    private readonly TextBlock instruction=new() { Margin=new(24),FontSize=20,TextWrapping=TextWrapping.Wrap };
    private readonly List<string> results=[];
    private readonly DispatcherTimer timer=new() { Interval=TimeSpan.FromMilliseconds(100) };
    private WindowsSink? recovery;
    private BasiliskPlayback? hardware;
    private Hooks? hooks;
    private nint handle;
    private int downs,ups,stage,baseline;
    private bool mouseHeld,started,finished;
    private long next,deadline;
    public HardwareTests(string report)
    {
        this.report=report; Title="Verdant on-mouse toggle test"; Width=650; Height=250;
        WindowStartupLocation=WindowStartupLocation.CenterScreen; Content=instruction;
        instruction.Text="Focus this window to begin the hardware toggle test.";
        SourceInitialized+=(_,_)=>handle=new WindowInteropHelper(this).Handle;
        Loaded+=async (_,_)=> {
            Activate(); var end=Environment.TickCount64+30000;
            while(Native.GetForegroundWindow()!=handle && Environment.TickCount64<end) await Task.Delay(100);
            Start();
        };
        Closing+=(_,_)=>Finish();
        timer.Tick+=(_,_)=>Tick();
    }
    private void Log(string line) { results.Add(line); File.WriteAllLines(report,results); }
    private void Start()
    {
        if(started) return; started=true;
        try
        {
            if(Native.GetForegroundWindow()!=handle) throw new Exception("Test window did not receive focus.");
            recovery=new WindowsSink();
            hooks=new Hooks { Record=e=>{
                if(e.Kind==EventKind.MouseDown && e.Code==4) { mouseHeld=true; Log("EVENT Mouse5 DOWN"); }
                if(e.Kind==EventKind.MouseUp && e.Code==4) { mouseHeld=false; Log("EVENT Mouse5 UP"); }
                if(e.Code==70 && e.Kind==EventKind.KeyDown) { downs++; Log($"EVENT F DOWN {downs}, Mouse5={mouseHeld}"); }
                if(e.Code==70 && e.Kind==EventKind.KeyUp) { ups++; Log("EVENT F UP "+ups); }
            }};
            var b=new Binding { Kind=ActionKind.Mouse,Mouse=ButtonId.Forward,Mode=PlayMode.Toggle,
                ExtraKeyEnabled=true,ExtraKeys=[70],ExtraIntervalMs=1000,ExtraPressMs=65,ExtraOnDevice=true };
            hardware=new BasiliskPlayback(b); hardware.Arm();
            Log("READY: combined hardware toggle armed, macro readback verified.");
            instruction.Text="1 / 3 — Press the FRONT side button once. Keep this window focused.";
            deadline=Environment.TickCount64+150000; timer.Start();
        }
        catch(Exception ex) { Log("ERROR "+ex.Message); Finish(); }
    }
    private void Tick()
    {
        try
        {
            if(Environment.TickCount64>deadline) throw new Exception("Hardware test timed out.");
            if(stage==0 && downs>=2)
            {
                if(!mouseHeld || (Native.GetAsyncKeyState(6)&0x8000)==0) throw new Exception("Mouse5 did not remain held during F playback.");
                Log("PASS Hardware F repeats while Mouse5 remains held.");
                stage=1; instruction.Text="2 / 3 — Press the FRONT side button again to stop.";
            }
            else if(stage==1 && !mouseHeld && downs==ups)
            { baseline=downs; next=Environment.TickCount64+1200; stage=2; }
            else if(stage==2 && Environment.TickCount64>=next)
            {
                if(downs!=baseline || (Native.GetAsyncKeyState(70)&0x8000)!=0 || (Native.GetAsyncKeyState(6)&0x8000)!=0)
                    throw new Exception("Second click did not stop/release both outputs.");
                Log("PASS Second physical click releases Mouse5 and F and stops repeat.");
                stage=3; instruction.Text="3 / 3 — Press the FRONT side button once more. Verdant will stop it automatically.";
            }
            else if(stage==3 && downs>=baseline+2)
            {
                baseline=downs; hardware!.Stop(); next=Environment.TickCount64+1400; stage=4;
                instruction.Text="Checking automatic stop and restoring your button…";
            }
            else if(stage==4 && Environment.TickCount64>=next)
            {
                if(downs!=baseline || mouseHeld || (Native.GetAsyncKeyState(6)&0x8000)!=0 || (Native.GetAsyncKeyState(70)&0x8000)!=0)
                {
                    Log("FAIL Remapping does not stop/release the active hardware macro.");
                    hardware!.Arm(); stage=5;
                    instruction.Text="Press the FRONT side button once to stop the test. Automatic stop did not pass.";
                    deadline=Environment.TickCount64+120000;
                }
                else { Log("PASS Automatic hardware stop releases both outputs and prevents later F."); Finish(); }
            }
            else if(stage==5 && !mouseHeld && downs==ups) Finish();
        }
        catch(Exception ex) { Log("ERROR "+ex.Message); Finish(); }
    }
    private void Finish()
    {
        if(finished) return; finished=true; timer.Stop();
        try { hardware?.Dispose(); Log("RESTORED original mapping; owned macro removed."); }
        catch(Exception ex) { Log("RESTORE ERROR "+ex.Message); }
        hooks?.Dispose(); recovery?.Dispose();
        Log("COMPLETE"); Application.Current.Shutdown();
    }
}
