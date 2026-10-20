using System.Diagnostics;
using System.Windows.Threading;

namespace Verdant.App;

internal sealed class Runtime : IDisposable
{
    private readonly object gate = new();
    private readonly WindowsSink sink = new();
    public readonly InputEngine Engine;
    private Hooks? hooks;
    private HardwareKeyTrigger? hardwareTrigger;
    private Dispatcher? hookDispatcher;
    private Configuration config;
    private Profile profile;
    private readonly Dictionary<ButtonId, (Binding Binding, Action? Release, int Generation)> pressed = [];
    private readonly HashSet<ButtonId> physicalToggles = [], releasePhysicalToggle = [];
    private int generation;
    private readonly Dictionary<ButtonId, long> lastLaunch = [];
    private bool layer, enabled = true, recording;
    private readonly List<MacroEvent> recorded = [];
    private readonly HashSet<(bool, int)> recordHeld = [];
    private readonly Stopwatch clock = new();
    private long last;
    private bool delays;
    private readonly OnMouseExtra? hardwareExtra;
    private long hardwareActivation;
    private int hardwareFDowns,hardwareFUps;
    internal (int Down,int Up) HardwareFCounts=>(Volatile.Read(ref hardwareFDowns),Volatile.Read(ref hardwareFUps));
    public int RecordingStopKey = 0x77;
    public string? LastError;
    public string LastButtonEvent = "No assigned button received";
    internal string TestTrace => hooks?.TestTrace ?? "No hook";
    public string ActiveProfile { get { lock (gate) return profile.Id; } }
    public bool LayerActive { get { lock (gate) return layer; } }
    public bool Enabled { get { lock (gate) return enabled; } }
    public int RecordedCount { get { lock (gate) return recorded.Count; } }
    private readonly nint testWindow;
    private readonly bool hardwareTest;
    internal bool HardwareReady=>hardwareExtra?.Ready??false;
    internal bool HardwareStopped=>hardwareExtra?.Stopped??true;
    public Runtime(Configuration value, nint testWindow = 0,bool hardwareTest=false)
    {
        this.testWindow = testWindow;
        this.hardwareTest=hardwareTest;
        config = ConfigStore.Clone(value); profile = config.Profiles.First(p => p.Id == config.DefaultProfileId);
        Engine = new(sink); Engine.Error += s => LastError = s;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() => { try { hookDispatcher = Dispatcher.CurrentDispatcher; hooks = new() { Button = Handle, AcceptTestInput = testWindow != 0 };
            if(testWindow==0 && App.ReviewDirectory==null || hardwareTest) hardwareTrigger=new HardwareKeyTrigger(HardwareKeyReceived);
        } catch (Exception e) { LastError = e.Message; enabled = false; } finally { ready.Set(); } Dispatcher.Run(); }) { IsBackground = true, Name = "Verdant input" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); ready.Wait();
        if(testWindow==0 && App.ReviewDirectory==null || hardwareTest)
            hardwareExtra=new OnMouseExtra(ReadHardwareState,s=>{ LastError=s; Pause(); });
    }
    private void HardwareKeyReceived(int key,bool down)
    {
        if(hardwareTest && key==70) {
            if(down) Interlocked.Increment(ref hardwareFDowns);
            else Interlocked.Increment(ref hardwareFUps);
        }
        if(!down) return;
        lock(gate)
        {
            if(hardwareExtra?.Ready!=true) return;
            var state=ReadHardwareState();
            if(!state.CanArm || state.Active || state.Binding is not { } binding || !binding.ExtraKeys.Contains(key)) return;
            // The device now sends only keys. Create one mouse hold, then reuse
            // the established physical OFF path. No repeated mouse commands.
            Engine.Trigger("Forward",binding,true);
            if(!Engine.IsActive("Forward")) { Pause(); return; }
            physicalToggles.Add(ButtonId.Forward);
            releasePhysicalToggle.Remove(ButtonId.Forward);
            pressed.Remove(ButtonId.Forward);
            hardwareActivation++;
            LastButtonEvent="Forward hardware key started mouse hold";
        }
    }
    private OnMouseExtra.State ReadHardwareState()
    {
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out var foregroundPid);
        lock(gate)
        {
            var b=layer && profile.Layer.TryGetValue(ButtonId.Forward,out var alternate) ? alternate : profile.Bindings.GetValueOrDefault(ButtonId.Forward);
            var eligible=b is { Kind:ActionKind.Mouse,Mouse:ButtonId.Forward,Mode:PlayMode.Toggle,ExtraKeyEnabled:true,ExtraOnDevice:true };
            var allowedWindow=hardwareTest ? Native.GetForegroundWindow()==testWindow : foregroundPid!=0 && foregroundPid!=Environment.ProcessId;
            return new(eligible?b:null,enabled && !recording && allowedWindow,generation,hardwareActivation,Engine.IsActive("Forward"));
        }
    }
    public void Configure(Configuration value) { lock (gate) { generation++; var id = profile.Id; Engine.StopAll(); config = ConfigStore.Clone(value); profile = config.Profiles.FirstOrDefault(p => p.Id == id) ?? config.Profiles.First(p => p.Id == config.DefaultProfileId); layer = false; } }
    public void SelectProfile(string id) { lock (gate) { var next = config.Profiles.FirstOrDefault(p => p.Id == id); if (next == null || next.Id == profile.Id) return; generation++; Engine.StopAll(); layer = false; profile = next; } }
    public void Foreground(string? path) { lock (gate) { if (config.AutoSwitch && !recording && !pressed.Values.Any(p => p.Binding.Kind == ActionKind.Profile && p.Binding.Mode == PlayMode.Hold)) SelectProfile(ConfigStore.Resolve(config, path).Id); } }
    public void Pause() { lock (gate) { generation++; enabled = false; Engine.StopAll(); layer = false; } }
    public void Resume() { lock (gate) enabled = hooks != null; }
    public void EditorFocused() { lock (gate) { generation++; Engine.StopAll(); layer = false; } }
    public void DeviceChanged() { lock (gate) { generation++; Engine.StopAll(); layer = false; pressed.Clear(); } }
    private bool Handle(ButtonId button, bool down)
    {
        lock (gate)
        {
            if (pressed.ContainsKey(button) || enabled && !recording && ((layer && profile.Layer.TryGetValue(button, out var diagnosticBinding) ? diagnosticBinding : profile.Bindings.GetValueOrDefault(button))?.Kind ?? ActionKind.Default) != ActionKind.Default)
                LastButtonEvent = $"{button} {(down ? "down" : "up")} at {DateTime.Now:HH:mm:ss.fff}";
            if (physicalToggles.Contains(button))
            {
                if (!Engine.IsActive(button.ToString())) { physicalToggles.Remove(button); releasePhysicalToggle.Remove(button); if (!down) { pressed.Remove(button); return false; } }
                else if (!down)
                {
                    pressed.Remove(button);
                    if (!releasePhysicalToggle.Remove(button)) return true;
                    physicalToggles.Remove(button);
                    return !Engine.ReleasePhysicalMouse(button.ToString(), button);
                }
                else
                {
                    // The next click supplies the real release; suppress its down.
                    if (pressed.ContainsKey(button)) return true;
                    pressed[button] = (new Binding { Mode = PlayMode.Toggle }, null, generation);
                    releasePhysicalToggle.Add(button); return true;
                }
            }
            if (!down && pressed.Remove(button, out var held)) { Engine.Trigger(button.ToString(), held.Binding, false); if (enabled && held.Generation == generation) { held.Release?.Invoke(); if (held.Binding.ReleaseMacroId.Length > 0) { var releaseMacro = config.Macros.FirstOrDefault(m => m.Id == held.Binding.ReleaseMacroId); if (releaseMacro != null) Engine.Trigger(button + "-release", new Binding { Kind = ActionKind.Macro }, true, releaseMacro); } } return true; }
            if (!enabled || recording) return false;
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out var pid);
            if (testWindow != 0 && Native.GetForegroundWindow() != testWindow) return false;
            if (pid == Environment.ProcessId && testWindow == 0) return false; // Keep the editor usable even after remapping primary click.
            if (!down) return false;
            if (pressed.ContainsKey(button)) return true;
            var binding = (layer && profile.Layer.TryGetValue(button, out var alternate) ? alternate : profile.Bindings.GetValueOrDefault(button)) ?? new Binding();
            if (binding.Kind == ActionKind.Default) return false;
            Action? release = null;
            try
            {
                if (binding.Kind == ActionKind.Mouse && binding.Mouse == button && binding.Mode == PlayMode.Toggle && Engine.AdoptPhysicalMouse(button.ToString(), button, binding.Label, binding))
                {
                    if(button==ButtonId.Forward) hardwareActivation++;
                    physicalToggles.Add(button); releasePhysicalToggle.Remove(button);
                    pressed[button] = (binding, null, generation);
                    return false;
                }
                switch (binding.Kind)
                {
                    case ActionKind.Layer:
                        generation++; Engine.StopAll(); layer = !layer;
                        if (binding.Mode == PlayMode.Hold) release = () => { Engine.StopAll(); layer = false; }; break;
                    case ActionKind.Profile:
                        var before = profile.Id; SelectProfile(binding.Target);
                        if (binding.Mode == PlayMode.Hold) release = () => SelectProfile(before); break;
                    case ActionKind.Launch:
                    case ActionKind.Url:
                        var now = Environment.TickCount64;
                        if (lastLaunch.TryGetValue(button, out var previous) && now - previous < 250) break;
                        lastLaunch[button] = now;
                        var target = binding.Target;
                        _ = Task.Run(() => { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception ex) { LastError = ex.Message; } }); break;
                    default: Engine.Trigger(button.ToString(), binding, true, config.Macros.FirstOrDefault(m => m.Id == binding.Target)); break;
                }
            }
            catch (Exception ex) { Engine.StopAll(); LastError = ex.Message; }
            if (button < ButtonId.WheelUp) pressed[button] = (binding, release, generation);
            return true;
        }
    }
    public void StartRecording(bool movement, bool recordDelay)
    {
        lock (gate) { Engine.StopAll(); recorded.Clear(); recordHeld.Clear(); last = 0; delays = recordDelay; recording = true; clock.Restart(); if (hooks != null) { hooks.RecordMoves = movement; hooks.Record = Capture; } }
    }
    private void Capture(MacroEvent e)
    {
        lock (gate)
        {
            if (!recording || recorded.Count >= 100000 || e.Kind is EventKind.KeyDown or EventKind.KeyUp && e.Code == RecordingStopKey) return;
            bool keyboard = e.Kind is EventKind.KeyDown or EventKind.KeyUp;
            if (e.Kind is EventKind.KeyDown or EventKind.MouseDown) { if (!recordHeld.Add((keyboard, e.Code))) return; }
            if (e.Kind is EventKind.KeyUp or EventKind.MouseUp) { if (!recordHeld.Remove((keyboard, e.Code))) return; }
            long now = clock.ElapsedMilliseconds;
            if (e.Kind == EventKind.Move && now - last < 10) return;
            e.DelayMs = delays ? (int)Math.Min(3600000, now - last) : 0; last = now; recorded.Add(e);
        }
    }
    public List<MacroEvent> StopRecording()
    {
        lock (gate)
        {
            recording = false; if (hooks != null) hooks.Record = null; clock.Stop();
            foreach (var (key, code) in recordHeld) recorded.Add(new() { Kind = key ? EventKind.KeyUp : EventKind.MouseUp, Code = code });
            recordHeld.Clear(); return ConfigStore.Clone(recorded);
        }
    }
    public void Dispose() { Pause(); hardwareExtra?.Dispose(); hookDispatcher?.Invoke(() => { hardwareTrigger?.Dispose(); hooks?.Dispose(); }); hookDispatcher?.BeginInvokeShutdown(DispatcherPriority.Send); Engine.Dispose(); sink.Dispose(); }
}
