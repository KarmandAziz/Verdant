using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Verdant.App;

// Tests the production coordinator in an isolated profile/window. The launcher
// enforces an independent process deadline and verifies guardian recovery.
internal sealed class HardwareRuntimeTests : Window
{
    private readonly string report;
    private readonly List<string> lines=[];
    private readonly TextBlock instruction=new() { Margin=new(24),FontSize=20,TextWrapping=TextWrapping.Wrap };
    private readonly DispatcherTimer timer=new() { Interval=TimeSpan.FromMilliseconds(20) };
    private readonly long started=Environment.TickCount64;
    private Runtime? runtime;
    private nint handle;
    private int stage,scenario=1,downs,ups,baseline;
    private long deadline,quietUntil;
    private bool finishing,heldThroughout;
    public HardwareRuntimeTests(string report)
    {
        this.report=report; Title="Verdant autoloot verification"; Width=700; Height=300;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var start=new System.Windows.Controls.Button { Content="Start verification",Margin=new(24,0,24,16),Padding=new(16,8,16,8),FontSize=18 };
        var panel=new StackPanel(); panel.Children.Add(instruction); panel.Children.Add(start); Content=panel;
        instruction.Text="Keep this window focused. Follow the prompts to check ON/OFF, automatic stop, and crash recovery.";
        SourceInitialized+=(_,_)=>handle=new WindowInteropHelper(this).Handle;
        Loaded+=(_,_)=>{ Log("WAITING for Start verification; no device changes made."); Activate(); };
        start.Click+=(_,_)=>{
            start.IsEnabled=false; start.Visibility=Visibility.Collapsed;
            try
            {
                if(Native.GetForegroundWindow()!=handle) throw new Exception("Test window must be focused.");
                var config=new Configuration();
                config.Profiles[0].Bindings[ButtonId.Forward]=new Binding { Kind=ActionKind.Mouse,Mouse=ButtonId.Forward,
                    Mode=PlayMode.Toggle,ExtraKeyEnabled=true,ExtraOnDevice=true,ExtraKeys=[70],ExtraIntervalMs=1000,ExtraPressMs=65 };
                ConfigStore.Validate(config); runtime=new Runtime(config,handle,true);
                Prepare(); timer.Start();
            }
            catch(Exception ex) { Log("ERROR "+ex.Message); Finish(); }
        };
        timer.Tick+=(_,_)=>Tick();
        Closing+=(_,e)=>{ if(!finishing) { e.Cancel=true; Finish(); } };
    }
    private void Log(string text)
    {
        lines.Add($"{Environment.TickCount64-started,7} ms {text}"); File.WriteAllLines(report,lines);
    }
    private void Prepare()
    {
        stage=0; deadline=Environment.TickCount64+60000; baseline=downs; heldThroughout=true;
        instruction.Text="Preparing the mouse… Please wait.";
    }
    private void Tick()
    {
        try
        {
            var counts=runtime!.HardwareFCounts;
            if(counts.Down!=downs || counts.Up!=ups) {
                downs=counts.Down; ups=counts.Up;
                Log($"Basilisk Raw Input F counts {downs}/{ups}");
            }
            if(runtime!.LastError!=null) throw new Exception(runtime.LastError);
            if(Native.GetForegroundWindow()!=handle) throw new Exception("Focus lost; stopping verification.");
            if(Environment.TickCount64>deadline) throw new Exception("Verification stage timed out.");
            if(stage==0 && runtime.HardwareReady)
            {
                stage=1;
                instruction.Text=$"Check {scenario} / 3 — Press and release the FRONT side button ONCE to turn on.";
                Log($"READY scenario {scenario}; hardware armed by production coordinator.");
            }
            else if(stage==1 && runtime.Engine.IsActive("Forward"))
            {
                stage=2; deadline=Environment.TickCount64+15000;
                instruction.Text="Move the mouse around inside this window while the hold and F continue. Wait for the next instruction…";
            }
            else if(stage==2)
            {
                if((Native.GetAsyncKeyState(6)&0x8000)==0 || !runtime.Engine.IsActive("Forward")) heldThroughout=false;
                if(ups>=baseline+4)
                {
                    if(!heldThroughout) throw new Exception("Mouse hold did not remain active during F repetition.");
                    Log($"PASS scenario {scenario}: held mouse and at least four balanced hardware F taps.");
                    if(scenario==1) { stage=3; instruction.Text="Press and release the FRONT side button again NOW to turn OFF."; }
                    else if(scenario==2)
                    {
                        runtime.Pause(); stage=4;
                        instruction.Text="Checking automatic stop… Do not press anything.";
                        Log("Requested production Pause.");
                    }
                    else
                    {
                        File.WriteAllText(report+".crash-ready",Environment.ProcessId.ToString());
                        stage=9; instruction.Text="Checking crash recovery… The test will close automatically.";
                        Log("CRASH READY; launcher will terminate this test and verify guardian cleanup.");
                    }
                }
            }
            else if(stage==3 && !runtime.Engine.IsActive("Forward")) { stage=4; Log("Physical OFF received."); }
            else if(stage==4 && runtime.Engine.ActiveCount==0 && (scenario==1?runtime.HardwareReady:runtime.HardwareStopped))
            {
                if((Native.GetAsyncKeyState(6)&0x8000)!=0 || (Native.GetAsyncKeyState(70)&0x8000)!=0 || downs!=ups)
                    throw new Exception("Stop completed with an input still down.");
                baseline=downs; quietUntil=Environment.TickCount64+1500; stage=5;
                instruction.Text="Both inputs are up. Checking for any later F taps…";
            }
            else if(stage==5 && Environment.TickCount64>=quietUntil)
            {
                if(downs!=baseline || downs!=ups || runtime.Engine.ActiveCount!=0 ||
                    (Native.GetAsyncKeyState(6)&0x8000)!=0 || (Native.GetAsyncKeyState(70)&0x8000)!=0)
                    throw new Exception("An input continued after stop.");
                Log($"PASS scenario {scenario}: both released; no later F for 1500 ms.");
                scenario++; runtime.Resume(); Prepare();
            }
        }
        catch(Exception ex) { Log("ERROR "+ex.Message); Finish(); }
    }
    private async void Finish()
    {
        if(finishing) return; finishing=true; timer.Stop();
        instruction.Text="Stopping hardware playback and restoring your button…";
        if(runtime!=null) await Task.Run(()=>runtime.Dispose());
        Log("COMPLETE"); Application.Current.Shutdown();
    }
}
