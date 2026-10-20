using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
namespace Verdant.App;

internal sealed class NativeTests : Window
{
    private readonly string output;
    private readonly List<string> results = [];
    private readonly TextBox box = new() { Text = "Isolated native input test target. F24 is used; no ordinary text is typed.", AcceptsReturn = true };
    private int downs, ups;
    public NativeTests(string path)
    {
        output = path; Title = "Verdant native input verification"; Width = 650; Height = 260;
        var start = new Button { Content = "Run native input tests", Margin = new Thickness(12), Padding = new Thickness(12) };
        var panel = new StackPanel(); panel.Children.Add(box); panel.Children.Add(start); Content = panel;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.F24) downs++; }; PreviewKeyUp += (_, e) => { if (e.Key == Key.F24) ups++; };
        start.Click += async (_, _) => { start.IsEnabled = false; await Run(); };
    }
    private void Check(bool value, string name) { results.Add((value ? "PASS " : "FAIL ") + name); if (!value) throw new Exception(name); }
    private void RequireFocus() { if (Native.GetForegroundWindow() != new WindowInteropHelper(this).Handle) throw new Exception("Test window lost foreground; injection aborted."); }
    private async Task Run()
    {
        using var sink = new WindowsSink(false); using var engine = new InputEngine(sink); using var hooks = new Hooks();
        try
        {
            Activate(); box.Focus(); await Task.Delay(300); RequireFocus();
            Check(System.Runtime.InteropServices.Marshal.SizeOf<Native.Input>() == 40, "x64 INPUT structure size");
            var devices = MouseDevice.Detect(); Check(devices.Count > 0, "Raw Input enumerates connected mouse interfaces (" + devices.Count + ")");
            int recorded = 0; hooks.Record = _ => recorded++;
            var binding = new Binding { Kind = ActionKind.Keyboard, Mode = PlayMode.Toggle, Keys = [135] };
            engine.Trigger("probe", binding, true); await Task.Delay(100);
            Check(downs > 0 && (Native.GetAsyncKeyState(135) & 0x8000) != 0, "SendInput delivers F24 down to WPF and Windows reports it held");
            engine.Trigger("probe", binding, false); await Task.Delay(80); Check((Native.GetAsyncKeyState(135) & 0x8000) != 0, "Toggle remains held after trigger release");
            RequireFocus(); engine.Trigger("probe", binding, true); await Task.Delay(100); Check(ups > 0 && (Native.GetAsyncKeyState(135) & 0x8000) == 0, "Second press delivers release");
            Check(hooks.IgnoredInjected >= 2 && recorded == 0, "Injected keyboard events excluded from recording / recursion");
            RequireFocus(); engine.Trigger("probe", binding, true); await Task.Delay(40); engine.StopAll(); await Task.Delay(40); Check((Native.GetAsyncKeyState(135) & 0x8000) == 0, "StopAll releases Windows key state");
            RequireFocus(); using var worker = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { Arguments = "--guardian-probe", UseShellExecute = false, CreateNoWindow = true })!;
            for (int i = 0; i < 50 && (Native.GetAsyncKeyState(135) & 0x8000) == 0; i++) await Task.Delay(100);
            Check((Native.GetAsyncKeyState(135) & 0x8000) != 0, "Crash probe holds F24");
            await Task.Delay(600); worker.Kill(); await worker.WaitForExitAsync();
            for (int i = 0; i < 30 && (Native.GetAsyncKeyState(135) & 0x8000) != 0; i++) await Task.Delay(100);
            Check((Native.GetAsyncKeyState(135) & 0x8000) == 0, "Guardian releases key after forced termination of test process");
        }
        catch (Exception e) { results.Add("ERROR " + e.Message); }
        finally { engine.StopAll(); try { Native.Key(135, false); } catch { } File.WriteAllLines(output, results); Application.Current.Shutdown(results.Any(s => s.StartsWith("FAIL") || s.StartsWith("ERROR")) ? 1 : 0); }
    }
}
