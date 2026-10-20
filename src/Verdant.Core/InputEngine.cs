namespace Verdant.Core;

public readonly record struct InputToken(bool Keyboard, int Code);
public interface IInputSink { void Down(InputToken token); void Down(InputToken token, bool compatibility) => Down(token); void Up(InputToken token); void Move(int x, int y); void Wheel(int delta, bool horizontal); void TrackPhysical(InputToken token, bool down) { } }

// All ownership transitions and injection happen under one gate. Cancellation releases
// synchronously; a cancelled worker cannot inject again after StopAll returns.
public sealed class InputEngine(IInputSink sink) : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, Run> runs = [];
    private readonly Dictionary<InputToken, HashSet<string>> owners = [];
    private sealed class Run { public CancellationTokenSource Cancel = new(); public HashSet<InputToken> Held = []; public Task? Work; public string Label = ""; public string? Companion; }
    public event Action<string>? Error;
    public int ActiveCount { get { lock (gate) return runs.Count; } }
    public string ActiveSummary { get { lock (gate) return string.Join("; ", runs.Select(p => p.Key + ": " + p.Value.Label)); } }
    public bool IsActive(string id) { lock (gate) return runs.ContainsKey(id); }
    public InputToken[] HeldTokens { get { lock (gate) return owners.Where(p => p.Value.Count > 0).Select(p => p.Key).ToArray(); } }
    // Own an original hardware press without replacing it with SendInput.
    // Cleanup still uses Up, including emergency stop and crash recovery.
    public bool AdoptPhysicalMouse(string id, ButtonId button, string label, Binding? binding = null)
    {
        lock (gate)
        {
            var token = new InputToken(false, (int)button);
            if (button > ButtonId.Forward || runs.ContainsKey(id) || owners.ContainsKey(token)) return false;
            sink.TrackPhysical(token, true);
            var run = new Run { Label = label }; run.Held.Add(token);
            runs.Add(id, run); owners.Add(token, [id]);
            if (binding != null) StartExtra(id, run, binding);
            return true;
        }
    }
    // True permits the original hardware release. Other output owners retain it.
    public bool ReleasePhysicalMouse(string id, ButtonId button)
    {
        lock (gate)
        {
            var token = new InputToken(false, (int)button);
            if (!runs.TryGetValue(id, out var run) || !run.Held.Contains(token)) return true;
            if (run.Companion != null) Stop(run.Companion);
            var set = owners[token];
            if (set.Count == 1) sink.TrackPhysical(token, false);
            runs.Remove(id); run.Held.Remove(token); set.Remove(id); run.Cancel.Dispose();
            if (set.Count > 0) return false;
            owners.Remove(token); return true;
        }
    }
    public void Trigger(string id, Binding binding, bool down, Macro? macro = null)
    {
        lock (gate)
        {
            if (!down) { if (binding.Mode is PlayMode.Hold or PlayMode.RepeatWhileHeld) Stop(id); return; }
            if (runs.ContainsKey(id)) { if (binding.Mode is PlayMode.Toggle or PlayMode.RepeatToggle) Stop(id); return; }
            if (binding.Kind is not (ActionKind.Keyboard or ActionKind.Mouse or ActionKind.Macro)) return;
            var b = ConfigStore.Clone(binding);
            var r = new Run { Label = macro?.Name ?? b.Label }; runs.Add(id, r);
            try
            {
                if (b.Kind != ActionKind.Macro && b.Mode is PlayMode.Hold or PlayMode.Toggle)
                { foreach (var t in Tokens(b)) Press(id, r, t); StartExtra(id, r, b); return; }
                r.Work = Task.Run(async () =>
                {
                    try
                    {
                        var count = b.Mode == PlayMode.Count ? b.Count : b.Mode is PlayMode.RepeatToggle or PlayMode.RepeatWhileHeld || b.Kind == ActionKind.Macro && b.Mode is PlayMode.Hold or PlayMode.Toggle ? int.MaxValue : 1;
                        for (int i = 0; i < count; i++)
                        {
                            if (macro != null)
                            {
                                foreach (var e in macro.Events)
                                {
                                    await Task.Delay((int)Math.Round(e.DelayMs / macro.Speed), r.Cancel.Token);
                                    lock (gate) { r.Cancel.Token.ThrowIfCancellationRequested(); Execute(id, r, e); }
                                }
                            }
                            else
                            {
                                lock (gate) { r.Cancel.Token.ThrowIfCancellationRequested(); foreach (var t in Tokens(b)) Press(id, r, t); }
                                await Task.Delay(12, r.Cancel.Token);
                                lock (gate) { r.Cancel.Token.ThrowIfCancellationRequested(); ReleaseOwned(id, r); }
                            }
                            if (i + 1 < count) await Task.Delay(b.IntervalMs, r.Cancel.Token);
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { Error?.Invoke(ex.Message); }
                    finally { lock (gate) { if (runs.TryGetValue(id, out var current) && current == r) Stop(id); r.Cancel.Dispose(); } }
                });
            }
            catch (Exception ex) { Stop(id); Error?.Invoke(ex.Message); }
        }
    }
    // A companion has its own key ownership, but is stopped synchronously with its parent.
    // Delay the first tap so the original hardware press reaches the target first.
    private void StartExtra(string parentId, Run parent, Binding binding)
    {
        if (!binding.ExtraKeyEnabled || binding.ExtraOnDevice || binding.ExtraKeys.Count == 0 || binding.Mode != PlayMode.Toggle) return;
        var keys = binding.ExtraKeys.Distinct().Select(k => new InputToken(true, k)).ToArray();
        var interval = Math.Clamp(binding.ExtraIntervalMs, 100, 60000);
        var duration = Math.Clamp(binding.ExtraPressMs, 10, interval - 10);
        var compatibility = binding.ExtraCompatibilityInput;
        var id = parentId + "/extra";
        var r = new Run { Label = string.Join("+", binding.ExtraKeys.Select(KeyNames.Name)) + $" every {interval} ms" + (compatibility ? " (compatibility)" : "") };
        parent.Companion = id; runs.Add(id, r);
        r.Work = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(interval, r.Cancel.Token);
                while (true)
                {
                    lock (gate) { r.Cancel.Token.ThrowIfCancellationRequested(); foreach (var t in keys) Press(id, r, t, compatibility); }
                    await Task.Delay(duration, r.Cancel.Token);
                    lock (gate) { r.Cancel.Token.ThrowIfCancellationRequested(); ReleaseOwned(id, r); }
                    await Task.Delay(interval - duration, r.Cancel.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                lock (gate) { if (runs.TryGetValue(parentId, out var current) && current == parent) Stop(parentId); }
                Error?.Invoke(ex.Message);
            }
            finally { lock (gate) { if (runs.TryGetValue(id, out var current) && current == r) Stop(id); r.Cancel.Dispose(); } }
        });
    }
    private static IEnumerable<InputToken> Tokens(Binding b) => b.Kind == ActionKind.Mouse ? [new(false, (int)b.Mouse)] : b.Keys.Select(k => new InputToken(true, k));
    private void Execute(string id, Run r, MacroEvent e)
    {
        switch (e.Kind)
        {
            case EventKind.KeyDown: Press(id, r, new(true, e.Code)); break;
            case EventKind.KeyUp: Release(id, r, new(true, e.Code)); break;
            case EventKind.MouseDown: Press(id, r, new(false, e.Code)); break;
            case EventKind.MouseUp: Release(id, r, new(false, e.Code)); break;
            case EventKind.Move: sink.Move(e.X, e.Y); break;
            case EventKind.Wheel: sink.Wheel(e.Code, e.X != 0); break;
        }
    }
    private void Press(string id, Run r, InputToken t, bool compatibility = false)
    {
        if (r.Held.Contains(t)) return;
        if (!owners.TryGetValue(t, out var set)) { set = []; owners.Add(t, set); }
        if (set.Count == 0) sink.Down(t, compatibility);
        set.Add(id); r.Held.Add(t);
    }
    private void Release(string id, Run r, InputToken t)
    {
        if (!r.Held.Remove(t) || !owners.TryGetValue(t, out var set)) return;
        set.Remove(id);
        if (set.Count == 0) { try { sink.Up(t); owners.Remove(t); } catch (Exception ex) { Error?.Invoke(ex.Message); } }
    }
    private void ReleaseOwned(string id, Run r) { foreach (var t in r.Held.Reverse().ToArray()) Release(id, r, t); }
    public void Stop(string id)
    {
        lock (gate) { if (!runs.Remove(id, out var r)) return; if (r.Companion != null) Stop(r.Companion); r.Cancel.Cancel(); ReleaseOwned(id, r); if (r.Work == null) r.Cancel.Dispose(); }
    }
    public void StopAll()
    {
        lock (gate)
        {
            foreach (var id in runs.Keys.ToArray()) Stop(id);
            foreach (var t in owners.Keys.ToArray()) { try { sink.Up(t); owners.Remove(t); } catch (Exception ex) { Error?.Invoke(ex.Message); } }
        }
    }
    public void Dispose() => StopAll();
}
