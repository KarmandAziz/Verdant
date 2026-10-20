using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
namespace Verdant.App;

internal sealed class PhysicalMouseTest : Window
{
    private Runtime? runtime;
    private readonly TextBlock text = new() { FontSize=19, Margin=new(24), TextWrapping=TextWrapping.Wrap };
    private readonly DispatcherTimer timer = new() { Interval=TimeSpan.FromMilliseconds(100) };
    private readonly Queue<string> lines = new();
    private string previous = "";
    public PhysicalMouseTest(string output)
    {
        Title="Verdant physical mouse test"; Width=660; Height=320; Content=text;
        SourceInitialized += (_,_) =>
        {
            var config=new Configuration(); ConfigStore.Validate(config);
            config.Profiles[0].Bindings[ButtonId.Forward]=new() { Kind=ActionKind.Mouse,Mouse=ButtonId.Forward,Mode=PlayMode.Toggle };
            runtime=new Runtime(config,new WindowInteropHelper(this).Handle);
            timer.Start();
        };
        timer.Tick+=(_,_)=>
        {
            if(runtime==null)return;
            bool active=runtime.Engine.IsActive("Forward"), down=(Native.GetAsyncKeyState(6)&0x8000)!=0;
            text.Text="Press and release your physical Mouse Button 5 once. Wait a second, then press it again.\n\n"+
                $"Toggle: {(active?"ON":"OFF")}    Windows button state: {(down?"DOWN":"UP")}\n\n{runtime.LastButtonEvent}\n{runtime.LastError}";
            var line=$"Toggle={active}; WindowsDown={down}; {runtime.LastButtonEvent}; Error={runtime.LastError}";
            if(line!=previous) { previous=line;lines.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff")+" "+line);while(lines.Count>100)lines.Dequeue();File.WriteAllLines(output,lines); }
        };
        Deactivated+=(_,_)=>runtime?.Pause(); Activated+=(_,_)=>runtime?.Resume();
        Closed+=(_,_)=>{timer.Stop();runtime?.Dispose();};
    }
}
