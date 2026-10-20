using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Verdant.App;

// Finite diagnostics only. The optional mouse hold has an explicit device release.
internal sealed class BoundedHardwareTests : Window
{
    private readonly string report;
    private readonly List<string> lines=[];
    private readonly TextBlock instruction=new() { Margin=new(24),FontSize=20,TextWrapping=TextWrapping.Wrap };
    private readonly DispatcherTimer timer=new() { Interval=TimeSpan.FromMilliseconds(50) };
    private readonly long started=Environment.TickCount64;
    private BasiliskPlayback? hardware;
    private Hooks? hooks;
    private WindowsSink? guardian;
    private nint handle;
    private int downs,ups,downsAtRestore;
    private long deadline,restoredAt;
    private bool finishing;
    private readonly bool modeCycle;
    private readonly bool mouseHold;
    private bool suppressedRelease,holdStayedDown=true,stopRequested,holdObserved,releaseChecked;
    private long firstF;
    public BoundedHardwareTests(string report,bool modeCycle=false,bool mouseHold=false)
    {
        this.report=report; this.modeCycle=modeCycle; this.mouseHold=mouseHold;
        Title=mouseHold?"Verdant bounded hold-and-F test":modeCycle?"Verdant bounded mode-stop test":"Verdant bounded F stop test"; Width=640; Height=260;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var startButton=new System.Windows.Controls.Button { Content="Start test",Margin=new(24,0,24,16),Padding=new(16,8,16,8),FontSize=18 };
        var panel=new StackPanel(); panel.Children.Add(instruction); panel.Children.Add(startButton); Content=panel;
        instruction.Text="Click Start test when ready, then follow the instructions. Keep this window focused until it closes.";
        SourceInitialized+=(_,_)=>handle=new WindowInteropHelper(this).Handle;
        Loaded+=(_,_)=> { Log("WAITING for Start test; no device changes made."); Activate(); };
        startButton.Click+=(_,_)=>{
            if(finishing) return;
            startButton.IsEnabled=false; startButton.Visibility=Visibility.Collapsed;
            try
            {
                if(Native.GetForegroundWindow()!=handle) throw new Exception("Window never received focus; no device changes made.");
                guardian=new WindowsSink();
                hooks=new Hooks { Record=Observe,Button=(button,down)=>{
                    if(!mouseHold || button!=ButtonId.Forward) return false;
                    if(!down && !suppressedRelease && !stopRequested) {
                        suppressedRelease=true; Log("SUPPRESSED first physical Mouse5 UP, matching normal toggle."); return true;
                    }
                    guardian.TrackPhysical(new(false,4),down); return false;
                }};
                hardware=new BasiliskPlayback(new Binding { Kind=ActionKind.Mouse,Mouse=ButtonId.Forward,
                    Mode=PlayMode.Toggle,ExtraKeyEnabled=true,ExtraKeys=[70],ExtraPressMs=65,ExtraIntervalMs=1000 },6,mouseHold);
                hardware.Arm(); deadline=Environment.TickCount64+60000;
                instruction.Text=mouseHold ? "Press and release the FRONT side button ONCE, then wait. Keep this window focused. This finite test checks the hold, F taps, and automatic release." :
                    "Press the FRONT side button ONCE, then wait. Keep this window focused. The test sends at most six F taps and no mouse hold.";
                Log(mouseHold ? "ARMED finite Mouse5 down, six F taps, explicit Mouse5 up, Times(1)." :
                    "ARMED six F taps, Times(1), no mouse events in payload."); timer.Start();
            }
            catch(Exception ex) { Log("ERROR "+ex.Message); Finish(); }
        };
        timer.Tick+=(_,_)=>Tick();
        Closing+=(_,e)=>{ if(!finishing) { e.Cancel=true; Finish(); } };
    }
    private void Log(string value)
    {
        lines.Add($"{Environment.TickCount64-started,7} ms {value}");
        File.WriteAllLines(report,lines);
    }
    private void Observe(MacroEvent e)
    {
        if(mouseHold && e.Code==4 && e.Kind is EventKind.MouseDown or EventKind.MouseUp)
            Log("EVENT "+e.Kind+" Mouse5");
        if(e.Code!=70 || e.Kind is not (EventKind.KeyDown or EventKind.KeyUp)) return;
        if(e.Kind==EventKind.KeyDown) { downs++; if(firstF==0) firstF=Environment.TickCount64; } else ups++;
        Log($"F {(e.Kind==EventKind.KeyDown?"DOWN":"UP")} counts={downs}/{ups}");
        // Leave the input hook immediately; HID requests run from the dispatcher timer.
    }
    private void Tick()
    {
        try
        {
            if(mouseHold && !stopRequested && firstF!=0 && Environment.TickCount64-firstF>200 &&
                (Native.GetAsyncKeyState(6)&0x8000)==0) holdStayedDown=false;
            if(restoredAt==0 && (ups>=(mouseHold?2:1) || Native.GetForegroundWindow()!=handle || Environment.TickCount64>deadline))
            {
                stopRequested=true;
                holdObserved=mouseHold && ups>=2 && Environment.TickCount64-firstF>=900;
                if(mouseHold) Log(!holdObserved ? "INCONCLUSIVE focus/timeout ended the hold observation early." :
                    holdStayedDown && suppressedRelease && (Native.GetAsyncKeyState(6)&0x8000)!=0 ?
                        "PASS Mouse5 stayed down after initial physical release during F taps." :
                        "FAIL Mouse5 did not stay down throughout the observed hold.");
                hardware!.Stop();
                var cyclePerformed=modeCycle && firstF!=0;
                if(cyclePerformed) hardware.TestModeCycleCancellation();
                restoredAt=Environment.TickCount64; downsAtRestore=downs;
                Log(cyclePerformed ? "TRIGGER RESTORED; normal-to-driver-to-original mode verified. Observing F." :
                    "TRIGGER RESTORED; observing whether already-running finite playback continues.");
                instruction.Text="Checking cancellation. Do not press the side button again; this closes automatically.";
            }
            if(mouseHold && restoredAt!=0 && !releaseChecked && Environment.TickCount64-restoredAt>=250)
            {
                releaseChecked=true;
                if((Native.GetAsyncKeyState(6)&0x8000)!=0)
                {
                    Log("FAIL Mode cycle did not release Mouse5; issuing Windows release immediately.");
                    guardian!.Up(new(false,4));
                }
                else Log("PASS Mouse5 released after mode-cycle stop.");
                if((Native.GetAsyncKeyState(70)&0x8000)!=0)
                {
                    Log("FAIL F remained down after mode cycle; issuing Windows release immediately.");
                    Native.Key(70,false);
                }
            }
            if(restoredAt!=0 && Environment.TickCount64-restoredAt>=7000)
            {
                Log(downsAtRestore==0 ? "INCONCLUSIVE no F received before restoration." :
                    downs>downsAtRestore ? "FAIL Cancellation attempt did not stop ongoing F playback." :
                    downs==ups ? "PASS No further F after cancellation attempt; observed keys balanced." :
                    "FAIL F did not release.");
                if(mouseHold && !holdObserved) Log("Combined hold acceptance remains INCONCLUSIVE; full observation required.");
                Finish();
            }
        }
        catch(Exception ex) { Log("ERROR "+ex.Message); Finish(); }
    }
    private async void Finish()
    {
        if(finishing) return; finishing=true; timer.Stop();
        instruction.Text="Finishing the bounded sequence and restoring your normal button…";
        try {
            if(hardware!=null) { await Task.Run(()=>hardware.Dispose()); Log("RESTORED mapping and removed owned finite macro."); }
            else Log("NO HARDWARE PLAYBACK created.");
        }
        catch(Exception ex) { Log("CLEANUP ERROR "+ex.Message); }
        hooks?.Dispose();
        if(mouseHold && guardian!=null) { try { guardian.Up(new(false,4)); } catch(Exception ex) { Log("WINDOWS RELEASE ERROR "+ex.Message); } }
        guardian?.Dispose(); Log("COMPLETE"); Application.Current.Shutdown();
    }
}
