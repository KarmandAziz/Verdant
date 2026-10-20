using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Verdant.App;

// Exercises the unchanged Runtime physical toggle with a finite device sequence.
internal sealed class BoundedToggleTests : Window
{
    private readonly string report;
    private readonly List<string> lines=[];
    private readonly TextBlock instruction=new() { Margin=new(24),FontSize=20,TextWrapping=TextWrapping.Wrap };
    private readonly DispatcherTimer timer=new() { Interval=TimeSpan.FromMilliseconds(20) };
    private readonly long started=Environment.TickCount64;
    private Runtime? runtime;
    private BasiliskPlayback? hardware;
    private Hooks? observer;
    private nint handle;
    private int stage,cycle=1,downs,ups,baseline;
    private long triggeredAt,deadline,quietUntil;
    private bool finishing,heldThroughout;
    public BoundedToggleTests(string report)
    {
        this.report=report; Title="Verdant mouse toggle with hardware F test"; Width=680; Height=290;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var start=new System.Windows.Controls.Button { Content="Start test",Margin=new(24,0,24,16),Padding=new(16,8,16,8),FontSize=18 };
        var panel=new StackPanel(); panel.Children.Add(instruction); panel.Children.Add(start); Content=panel;
        instruction.Text="This checks two ON/OFF cycles. Each sequence is limited to six seconds. Keep this window focused and follow its prompts.";
        SourceInitialized+=(_,_)=>handle=new WindowInteropHelper(this).Handle;
        Loaded+=(_,_)=>{ Log("WAITING for Start test; no device changes made."); Activate(); };
        start.Click+=(_,_)=>{
            start.IsEnabled=false; start.Visibility=Visibility.Collapsed;
            try
            {
                if(Native.GetForegroundWindow()!=handle) throw new Exception("Test window must be focused.");
                var config=new Configuration();
                config.Profiles[0].Bindings[ButtonId.Forward]=new Binding {
                    Kind=ActionKind.Mouse,Mouse=ButtonId.Forward,Mode=PlayMode.Toggle };
                ConfigStore.Validate(config);
                runtime=new Runtime(config,handle);
                observer=new Hooks { Record=e=>{
                    if(e.Code==70 && e.Kind==EventKind.KeyDown) { downs++; Log($"F DOWN {downs}"); }
                    if(e.Code==70 && e.Kind==EventKind.KeyUp) { ups++; Log($"F UP {ups}"); }
                    if(e.Code==4 && e.Kind is EventKind.MouseDown or EventKind.MouseUp) Log("Mouse5 "+e.Kind);
                }};
                hardware=new BasiliskPlayback(new Binding { Kind=ActionKind.Mouse,Mouse=ButtonId.Forward,
                    Mode=PlayMode.Toggle,ExtraKeyEnabled=true,ExtraKeys=[70],ExtraPressMs=65,ExtraIntervalMs=1000 },6,false,true);
                Arm(); timer.Start();
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
    private void Arm()
    {
        hardware!.Arm(); stage=0; deadline=Environment.TickCount64+60000; baseline=downs; heldThroughout=true;
        instruction.Text=$"Cycle {cycle} / 2 — Press and release the FRONT side button ONCE to turn on.";
        Log($"ARMED cycle {cycle}: six finite iterations, explicit final mouse release.");
    }
    private void Tick()
    {
        try
        {
            if(Native.GetForegroundWindow()!=handle) throw new Exception("Focus lost; stopping the hardware sequence.");
            if(Environment.TickCount64>deadline) throw new Exception("Physical input wait timed out.");
            if(stage==0 && runtime!.Engine.IsActive("Forward"))
            {
                triggeredAt=Environment.TickCount64;
                hardware!.Stop(); // Detach the trigger; current firmware sequence keeps running.
                Log("Native macro started; standard physical Mouse5 mapping restored for OFF click.");
                stage=1;
                instruction.Text=$"Cycle {cycle} / 2 — F is repeating. Press and release the FRONT side button again NOW to turn off.";
            }
            else if(stage==1)
            {
                if(runtime!.Engine.IsActive("Forward"))
                {
                    if((Native.GetAsyncKeyState(6)&0x8000)==0) heldThroughout=false;
                    if(Environment.TickCount64-triggeredAt>5000) throw new Exception("Second click not received in the finite test window.");
                }
                else
                {
                    if(!heldThroughout || downs==baseline) throw new Exception("Hold or F output was not observed.");
                    hardware!.TestModeCycleCancellation();
                    Log("OFF click released Runtime hold; device mode cycle stopped keyboard sequence.");
                    baseline=downs; quietUntil=Environment.TickCount64+1500; stage=2;
                    instruction.Text="Checking that both outputs stopped… Please wait.";
                }
            }
            else if(stage==2 && Environment.TickCount64>=quietUntil)
            {
                if(downs!=baseline || downs!=ups || runtime!.Engine.ActiveCount!=0 ||
                    (Native.GetAsyncKeyState(6)&0x8000)!=0 || (Native.GetAsyncKeyState(70)&0x8000)!=0)
                    throw new Exception("OFF did not leave both inputs released with no later F.");
                Log($"PASS cycle {cycle}: held mouse with hardware F, OFF releases both, no later F for 1500 ms.");
                if(cycle++==1) Arm(); else { Log("PASS BOTH PHYSICAL TOGGLE CYCLES"); Finish(); }
            }
        }
        catch(Exception ex) { Log("ERROR "+ex.Message); Finish(); }
    }
    private async void Finish()
    {
        if(finishing) return; finishing=true; timer.Stop();
        instruction.Text="Stopping the diagnostic and restoring your normal configuration…";
        // Stop keyboard hardware before releasing Windows-owned mouse state.
        try { if(hardware!=null) { hardware.TestModeCycleCancellation(); await Task.Run(()=>hardware.Dispose()); Log("RESTORED hardware mapping/mode and removed owned macro."); } }
        catch(Exception ex) { Log("CLEANUP ERROR "+ex.Message); }
        observer?.Dispose(); runtime?.Dispose();
        try { Native.Key(70,false); Native.Mouse(4,false); } catch(Exception ex) { Log("WINDOWS RELEASE ERROR "+ex.Message); }
        Log("COMPLETE"); Application.Current.Shutdown();
    }
}
