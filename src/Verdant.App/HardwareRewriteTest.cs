using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Verdant.App;

// Six finite repetitions only. Examines whether firmware reads updated steps
// during playback. This experiment is never used by the normal coordinator.
internal sealed class HardwareRewriteTest : Window
{
    private readonly string report;
    private readonly List<string> lines=[];
    private readonly long started=Environment.TickCount64;
    private readonly TextBlock instruction=new() { Margin=new(24),FontSize=20,TextWrapping=TextWrapping.Wrap };
    private readonly DispatcherTimer timer=new() { Interval=TimeSpan.FromMilliseconds(20) };
    private Runtime? runtime;
    private Hooks? observer;
    private BasiliskPlayback? hardware;
    private nint handle;
    private int downs,ups,mouseDowns,baseline;
    private long deadline,rewritten;
    private bool finishing;
    internal HardwareRewriteTest(string report)
    {
        this.report=report; Title="Verdant isolated repeat check"; Width=700; Height=300;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var start=new System.Windows.Controls.Button { Content="Start check",Margin=new(24),Padding=new(16,8,16,8) };
        var panel=new StackPanel(); panel.Children.Add(instruction); panel.Children.Add(start); Content=panel;
        instruction.Text="This finite check sends at most six planned F taps. Keep this window focused.";
        SourceInitialized+=(_,_)=>handle=new WindowInteropHelper(this).Handle;
        Loaded+=(_,_)=>{ Log("WAITING; no device changes made."); Activate(); };
        start.Click+=(_,_)=>{
            start.IsEnabled=false;
            try {
                if(Native.GetForegroundWindow()!=handle) throw new Exception("Test window must be focused.");
                var config=new Configuration();
                var binding=new Binding { Kind=ActionKind.Mouse,Mouse=ButtonId.Forward,Mode=PlayMode.Toggle,
                    ExtraKeyEnabled=false,ExtraKeys=[70],ExtraIntervalMs=1000,ExtraPressMs=65 };
                config.Profiles[0].Bindings[ButtonId.Forward]=binding;
                ConfigStore.Validate(config); runtime=new Runtime(config,handle);
                observer=new Hooks { Record=e=>{
                    if(e.Code==70 && e.Kind==EventKind.KeyDown) { downs++; Log("F DOWN "+downs); }
                    if(e.Code==70 && e.Kind==EventKind.KeyUp) { ups++; Log("F UP "+ups); }
                    if(e.Code==4 && e.Kind==EventKind.MouseDown) { mouseDowns++; Log("Mouse5 DOWN "+mouseDowns); }
                    if(e.Code==4 && e.Kind==EventKind.MouseUp) Log("Mouse5 UP");
                }};
                hardware=new BasiliskPlayback(binding with { ExtraKeyEnabled=true },6,boundedToggle:true);
                hardware.Arm(); deadline=Environment.TickCount64+60000; timer.Start();
                instruction.Text="Press and release the FRONT side button ONCE, then move the mouse around inside this window. Do not click again. It closes automatically.";
                Log("ARMED finite six-tap sequence with explicit final Mouse5 release.");
            } catch(Exception ex) { Log("ERROR "+ex.Message); Finish(); }
        };
        timer.Tick+=(_,_)=>{
            try {
                if(Native.GetForegroundWindow()!=handle) throw new Exception("Focus lost.");
                if(Environment.TickCount64>deadline) throw new Exception("Timed out.");
                if(rewritten==0 && ups>=1) {
                    hardware!.Stop(); hardware.TestRemoveRepeatedMouseDown();
                    baseline=mouseDowns; rewritten=Environment.TickCount64;
                    deadline=rewritten+12000;
                    Log("REWRITTEN: all later mouse-down commands replaced with zero-length delays; bytes verified.");
                }
                if(rewritten!=0 && Environment.TickCount64-rewritten>=6500) {
                    Log(downs==6 && ups==6 && mouseDowns==baseline
                        ? "PASS six balanced F taps continued without later Mouse5 downs."
                        : $"FAIL live firmware body did not switch as required: F={downs}/{ups}; additional mouse downs={mouseDowns-baseline}.");
                    Finish();
                }
            } catch(Exception ex) { Log("ERROR "+ex.Message); Finish(); }
        };
        Closing+=(_,e)=>{ if(!finishing) { e.Cancel=true; Finish(); } };
    }
    private void Log(string value) { lines.Add($"{Environment.TickCount64-started,7} ms {value}"); File.WriteAllLines(report,lines); }
    private async void Finish()
    {
        if(finishing) return; finishing=true; timer.Stop();
        instruction.Text="Restoring your button…";
        try {
            if(hardware!=null) await Task.Run(()=>{ hardware.CancelPlayback(); hardware.Dispose(); });
        } catch(Exception ex) { Log("CLEANUP ERROR "+ex.Message); }
        observer?.Dispose(); runtime?.Dispose();
        Log("COMPLETE"); Application.Current.Shutdown();
    }
}
