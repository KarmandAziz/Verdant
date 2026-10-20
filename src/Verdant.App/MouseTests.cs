using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace Verdant.App;

// End-to-end Windows test. Tagged injected triggers are admitted only by this
// isolated Runtime, while normal production hooks continue to ignore injections.
internal sealed class MouseTests : Window
{
    private readonly string output;
    private readonly List<string> results = [];
    private int downs, ups;
    private readonly List<long> extraDowns = [];
    private int extraUps;
    private nint handle;
    public MouseTests(string output)
    {
        this.output = output; Title = "Verdant mouse toggle test"; Width = 620; Height = 240; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var start = new Button { Content = "Run mouse toggle tests", Margin = new(24), Padding = new(16) };
        Content = start;
        SourceInitialized += (_, _) => { handle = new WindowInteropHelper(this).Handle; HwndSource.FromHwnd(handle)!.AddHook(Receive); };
        var running = false;
        async void Start() { if (running) return; running = true; start.IsEnabled = false; await Task.Delay(300); await Run(); }
        start.Click += (_, _) => Start();
    }
    private nint Receive(nint h, int msg, nint w, nint l, ref bool handled)
    {
        if (msg is 0x100 or 0x101 && w == 70 && (nuint)Native.GetMessageExtraInfo() == Native.Marker)
        {
            if (msg == 0x100) extraDowns.Add(Environment.TickCount64); else extraUps++;
            handled = true;
        }
        if (msg is 0x20B or 0x20C && (nuint)Native.GetMessageExtraInfo() == Native.Marker)
        {
            if (msg == 0x20B) downs++; else ups++;
            handled = true; return 1;
        }
        return 0;
    }
    private void Check(bool value, string text) { results.Add((value ? "PASS " : "FAIL ") + text); File.WriteAllLines(output, results); if (!value) throw new Exception(text); }
    private async Task Press(bool down)
    {
        if (Native.GetForegroundWindow() != handle) throw new Exception("Test lost focus; no input sent.");
        await Task.Run(() => Native.Send(new Native.Input { Value = new Native.InputUnion { Mouse = new Native.MouseInput { Flags = down ? 128u : 256u, Data = 2, Extra = Hooks.TestMarker } } }));
        await Task.Delay(120);
    }
    private async Task Run()
    {
        Runtime? runtime = null;
        try
        {
            System.Windows.Input.Mouse.Capture(this);
            var config = new Configuration(); ConfigStore.Validate(config);
            var profile = config.Profiles.First(p => p.Id == config.DefaultProfileId);
            profile.Bindings[ButtonId.Forward] = new Binding { Kind = ActionKind.Mouse, Mouse = ButtonId.Forward, Mode = PlayMode.Toggle, ExtraKeyEnabled=true, ExtraKeys=[70], ExtraIntervalMs=1000, ExtraPressMs=150 };
            runtime = new Runtime(config, handle);
            Deactivated += (_, _) => runtime?.Pause();
            var fDown = Native.KeyEvent(70,true); var fUp = Native.KeyEvent(70,false);
            Check(fDown.Vk==0 && fDown.Scan==0x21 && fDown.Flags==8 && fUp.Scan==fDown.Scan && fUp.Flags==10, "F down/up use matching keyboard scan codes");
            Check((Native.KeyEvent(0x27,true).Flags & 9)==9, "Extended arrow key carries scan-code and extended flags");
            Check(runtime.Enabled, "Input hook installed");
            Check(MouseDevice.Detect().All(d => !d.Name.Contains("Keychron", StringComparison.OrdinalIgnoreCase)), "Keyboard mouse-emulation interface excluded");
            results.Add("MICE " + string.Join(", ", MouseDevice.Detect().Select(d => d.Name)));
            await Press(true);
            results.Add($"DETAIL active={runtime.Engine.IsActive("Forward")} enabled={runtime.Enabled} focused={Native.GetForegroundWindow()==handle} downs={downs} ups={ups} state={Native.GetAsyncKeyState(6):X} last={runtime.LastButtonEvent} error={runtime.LastError} hook={runtime.TestTrace}");
            Check(runtime.Engine.IsActive("Forward") && (Native.GetAsyncKeyState(6) & 0x8000) != 0, "First trigger starts toggle and Windows reports Mouse 5 down");
            Check(runtime.TestTrace.Contains($"extra={Hooks.TestMarker:X}"), "Same-button toggle preserves original down without replacing it");
            await Press(false);
            Check(runtime.Engine.IsActive("Forward") && (Native.GetAsyncKeyState(6) & 0x8000) != 0, "Trigger release suppressed; Windows still reports Mouse 5 down");
            await Task.Delay(2200);
            Check((Native.GetAsyncKeyState(6) & 0x8000) != 0, "Mouse remains held while extra key repeats");
            Check(extraDowns.Count >= 2 && extraUps == extraDowns.Count, "Focused Windows test receives balanced F taps while Mouse 5 remains held");
            Check(extraDowns.Zip(extraDowns.Skip(1), (a,b)=>b-a).All(ms=>ms is >=900 and <=1300), "F taps arrive about one second apart");
            await Press(true); await Press(false);
            results.Add($"DETAIL second active={runtime.Engine.IsActive("Forward")} downs={downs} ups={ups} state={Native.GetAsyncKeyState(6):X} last={runtime.LastButtonEvent} error={runtime.LastError}");
            Check(!runtime.Engine.IsActive("Forward") && (Native.GetAsyncKeyState(6) & 0x8000) == 0, "Second press releases Windows state");
            Check(runtime.TestTrace.Contains($"extra={Hooks.TestMarker:X}"), "Second click preserves original release without replacing it");
            var taps = extraDowns.Count;
            await Task.Delay(1100);
            Check(extraDowns.Count == taps && (Native.GetAsyncKeyState(70)&0x8000)==0 && runtime.Engine.ActiveCount==0, "Toggle off stops extra taps and releases F");
            runtime.Configure(config);
            await Press(true); await Press(false);
            results.Add($"DETAIL after-config active={runtime.Engine.IsActive("Forward")} downs={downs} ups={ups} state={Native.GetAsyncKeyState(6):X} last={runtime.LastButtonEvent} error={runtime.LastError}");
            Check(runtime.Engine.IsActive("Forward") && (Native.GetAsyncKeyState(6) & 0x8000) != 0, "Toggle still responds after configuration save");
            runtime.EditorFocused(); await Task.Delay(120);
            Check(!runtime.Engine.IsActive("Forward") && (Native.GetAsyncKeyState(6) & 0x8000) == 0, "Opening editor clears both toggle and Windows held state");
            await Press(true); await Press(false);
            runtime.Pause(); await Task.Delay(120);
            Check((Native.GetAsyncKeyState(6) & 0x8000) == 0, "Pause releases mouse output");
        }
        catch (Exception ex) { results.Add("ERROR " + ex.Message); }
        finally { runtime?.Dispose(); System.Windows.Input.Mouse.Capture(null); File.WriteAllLines(output, results); Application.Current.Shutdown(); }
    }
}
