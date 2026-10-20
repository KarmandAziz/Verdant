using Verdant.Core;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

var tests = new List<(string, Func<Task>)>();
void Test(string name, Func<Task> test) => tests.Add((name, test));
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Expected validation failure"); }
Binding Key(PlayMode mode, int key = 87) => new() { Kind = ActionKind.Keyboard, Mode = mode, Keys = [key], IntervalMs = 20 };
Test("Toggle holds after release, second press releases", () => { var sink = new Sink(); using var e = new InputEngine(sink); var b = Key(PlayMode.Toggle); e.Trigger("5", b, true); e.Trigger("5", b, false); Check(sink.Held.Count == 1 && e.ActiveCount == 1); e.Trigger("5", b, true); Check(sink.Held.Count == 0 && e.ActiveCount == 0); return Task.CompletedTask; });
Test("Hold follows physical release", () => { var sink = new Sink(); using var e = new InputEngine(sink); var b = Key(PlayMode.Hold); e.Trigger("4", b, true); Check(sink.Held.ContainsKey(new(true, 87))); e.Trigger("4", b, false); Check(sink.Held.IsEmpty); return Task.CompletedTask; });
Test("Shared owners do not prematurely release", () => { var s = new Sink(); using var e = new InputEngine(s); var b = Key(PlayMode.Toggle); e.Trigger("4", b, true); e.Trigger("5", b, true); e.Trigger("4", b, true); Check(s.Held.Count == 1); e.StopAll(); Check(s.Held.IsEmpty && s.Events.Count(x => x.StartsWith("down")) == 1); return Task.CompletedTask; });
Test("Mouse toggle ownership", () => { var s = new Sink(); using var e = new InputEngine(s); var b = new Binding { Kind = ActionKind.Mouse, Mouse = ButtonId.Left, Mode = PlayMode.Toggle }; e.Trigger("4", b, true); Check(s.Held.ContainsKey(new(false, 0))); e.Trigger("4", b, false); Check(s.Held.Count == 1); e.Trigger("4", b, true); Check(s.Held.IsEmpty); return Task.CompletedTask; });
Test("Repeat executes and cancels without late injection", async () => { var s = new Sink(); using var e = new InputEngine(s); var b = Key(PlayMode.RepeatWhileHeld); e.Trigger("4", b, true); await Task.Delay(180); e.Trigger("4", b, false); var count = s.Events.Count; Check(count >= 4); await Task.Delay(80); Check(s.Events.Count == count && s.Held.IsEmpty); });
Test("Count executes exact number", async () => { var s = new Sink(); using var e = new InputEngine(s); var b = Key(PlayMode.Count); b.Count = 3; e.Trigger("4", b, true); await Task.Delay(250); Check(s.Events.Count(x => x.StartsWith("down")) == 3 && s.Held.IsEmpty); });
Test("Cancel long macro releases held input immediately", async () => { var s = new Sink(); using var e = new InputEngine(s); var m = new Macro { Events = [new() { Kind = EventKind.KeyDown, Code = 87 }, new() { Kind = EventKind.KeyUp, Code = 87, DelayMs = 60000 }] }; e.Trigger("4", new() { Kind = ActionKind.Macro }, true, m); await Task.Delay(40); Check(s.Held.Count == 1); var watch = Stopwatch.StartNew(); e.StopAll(); Check(s.Held.IsEmpty && watch.ElapsedMilliseconds < 100); await Task.Delay(40); Check(s.Held.IsEmpty); });
Test("Rapid stop/restart does not let old finally stop new run", async () => { var s = new Sink(); using var e = new InputEngine(s); var repeat = Key(PlayMode.RepeatToggle); for (int i = 0; i < 50; i++) { e.Trigger("4", repeat, true); e.StopAll(); } e.Trigger("4", Key(PlayMode.Toggle), true); await Task.Delay(70); Check(s.Held.Count == 1 && e.ActiveCount == 1); });
Test("Disabled/default never inject", () => { var s = new Sink(); using var e = new InputEngine(s); e.Trigger("4", new() { Kind = ActionKind.Disabled }, true); e.Trigger("5", new(), true); Check(s.Events.IsEmpty); return Task.CompletedTask; });
Test("Sink failure is surfaced and state cleaned", () => { var s = new Sink { FailDown = true }; using var e = new InputEngine(s); string? error = null; e.Error += v => error = v; e.Trigger("4", Key(PlayMode.Toggle), true); e.StopAll(); Check(error != null && e.ActiveCount == 0); return Task.CompletedTask; });
Test("Profile persistence and atomic backup", () => { var dir = Path.Combine(Path.GetTempPath(), "VerdantTests-" + Guid.NewGuid()); Directory.CreateDirectory(dir); var file = Path.Combine(dir, "config.json"); var c = new Configuration(); c.Profiles[0].Bindings[ButtonId.Forward] = Key(PlayMode.Toggle); ConfigStore.Save(file, c); c.Profiles[0].Name = "Changed"; ConfigStore.Save(file, c); Check(ConfigStore.Read(file).Profiles[0].Name == "Changed"); Check(ConfigStore.Read(file + ".bak").Profiles[0].Name == "Everyday"); Directory.Delete(dir, true); return Task.CompletedTask; });
Test("Foreground application matching and default", () => { var c = new Configuration(); c.DefaultProfileId = c.Profiles[0].Id; var p = new Profile { Name = "Editor", ApplicationPath = @"C:\Tools\Editor.exe" }; c.Profiles.Add(p); Check(ConfigStore.Resolve(c, @"c:\tools\EDITOR.exe").Id == p.Id); Check(ConfigStore.Resolve(c, null).Id == c.DefaultProfileId); return Task.CompletedTask; });
Test("Version 1 migration and future-version rejection", () => { var file = Path.GetTempFileName(); var c = new Configuration { Version = 1 }; File.WriteAllText(file, JsonSerializer.Serialize(c, ConfigStore.Json)); Check(ConfigStore.Read(file).Version == 2); c.Version = 99; Reject(() => ConfigStore.Validate(c)); File.Delete(file); return Task.CompletedTask; });
Test("Macro rejects unpaired releases and dangling holds", () => { Reject(() => ConfigStore.ValidateMacro(new() { Events = [new() { Kind = EventKind.KeyDown, Code = 87 }] })); Reject(() => ConfigStore.ValidateMacro(new() { Events = [new() { Kind = EventKind.KeyUp, Code = 87 }] })); ConfigStore.ValidateMacro(new() { Events = [new() { Kind = EventKind.KeyDown, Code = 87 }, new() { Kind = EventKind.KeyUp, Code = 87 }] }); return Task.CompletedTask; });
Test("Invalid config references and wheel hold rejected", () => { var c = new Configuration(); c.Profiles[0].Bindings[ButtonId.WheelUp] = Key(PlayMode.Hold); Reject(() => ConfigStore.Validate(c)); c.Profiles[0].Bindings.Clear(); c.Profiles[0].Bindings[ButtonId.Forward] = new() { Kind = ActionKind.Macro, Target = "missing" }; Reject(() => ConfigStore.Validate(c)); return Task.CompletedTask; });
Test("Shortcut parser and invalid keys", () => { Check(KeyNames.Parse("Ctrl+Shift+W").SequenceEqual(new[] { 17, 16, 87 })); Reject(() => KeyNames.Parse("not-a-key")); return Task.CompletedTask; });
Test("Quick Toggle maps each default button to its own held output",()=> { foreach(var id in Enum.GetValues<ButtonId>().Take(5)) { var b=new Dictionary<ButtonId,Binding>(); QuickToggle.Set(b,id,true); Check(b[id].Kind==ActionKind.Mouse && b[id].Mouse==id && b[id].Mode==PlayMode.Toggle); var s=new Sink(); using var e=new InputEngine(s); e.Trigger("button",b[id],true); e.Trigger("button",b[id],false); Check(s.Held.ContainsKey(new(false,(int)id))); e.Trigger("button",b[id],true); Check(s.Held.IsEmpty); QuickToggle.Set(b,id,false); Check(b.Count==0); } return Task.CompletedTask; });
Test("Quick Toggle restores customized behavior",()=> { var b=new Dictionary<ButtonId,Binding> { [ButtonId.Forward]=Key(PlayMode.RepeatWhileHeld) }; QuickToggle.Set(b,ButtonId.Forward,true); Check(b[ButtonId.Forward].Keys.SequenceEqual(new[]{87})); QuickToggle.Set(b,ButtonId.Forward,false); Check(b[ButtonId.Forward].Mode==PlayMode.RepeatWhileHeld); return Task.CompletedTask; });
Test("Quick Toggle respects layer inheritance",()=> { var layer=new Dictionary<ButtonId,Binding>(); QuickToggle.Set(layer,ButtonId.Back,true,Key(PlayMode.Hold)); Check(layer[ButtonId.Back].Kind==ActionKind.Keyboard); QuickToggle.Set(layer,ButtonId.Back,false); Check(layer.Count==0); return Task.CompletedTask; });
Test("Quick Toggle rejects scrolling and non-holdable actions",()=> { Reject(()=>QuickToggle.Set([],ButtonId.WheelUp,true)); Reject(()=>QuickToggle.Set(new(){[ButtonId.Left]=new(){Kind=ActionKind.Launch}},ButtonId.Left,true)); return Task.CompletedTask; });
int failures = 0;
Test("Physical mouse toggle preserves original down and up without injection", () => { var s = new Sink(); using var e = new InputEngine(s); Check(e.AdoptPhysicalMouse("5", ButtonId.Forward, "physical")); Check(e.IsActive("5") && e.HeldTokens.Contains(new(false,4)) && s.Events.IsEmpty); Check(e.ReleasePhysicalMouse("5",ButtonId.Forward)); Check(!e.IsActive("5") && e.HeldTokens.Length==0 && s.Events.IsEmpty); return Task.CompletedTask; });
Test("Physical mouse toggle emergency cleanup emits release", () => { var s = new Sink(); using var e = new InputEngine(s); e.AdoptPhysicalMouse("5", ButtonId.Forward, "physical"); e.StopAll(); Check(s.Events.Count==1 && s.Events.Single().StartsWith("up")); return Task.CompletedTask; });
Test("Physical release does not release another output owner", () => { var s = new Sink(); using var e = new InputEngine(s); e.AdoptPhysicalMouse("5",ButtonId.Forward,"physical"); e.Trigger("4",new Binding {Kind=ActionKind.Mouse,Mouse=ButtonId.Forward,Mode=PlayMode.Toggle},true); Check(!e.ReleasePhysicalMouse("5",ButtonId.Forward)); Check(e.IsActive("4") && e.HeldTokens.Length==1 && s.Events.IsEmpty); e.StopAll(); Check(s.Events.Count==1); return Task.CompletedTask; });
Test("Physical adoption does not steal existing output ownership", () => { var s = new Sink(); using var e = new InputEngine(s); e.Trigger("4",new Binding {Kind=ActionKind.Mouse,Mouse=ButtonId.Forward,Mode=PlayMode.Toggle},true); Check(!e.AdoptPhysicalMouse("5",ButtonId.Forward,"physical") && !e.IsActive("5")); return Task.CompletedTask; });
Binding Extra() => new() { Kind=ActionKind.Mouse, Mouse=ButtonId.Forward, Mode=PlayMode.Toggle, ExtraKeyEnabled=true, ExtraKeys=[70], ExtraIntervalMs=100 };
async Task WaitUntil(Func<bool> condition) { var clock=Stopwatch.StartNew(); while(!condition() && clock.ElapsedMilliseconds<2000) await Task.Delay(5); Check(condition(),"Timed out waiting for output"); }
Test("Physical toggle repeats F and preserves original mouse events", async () => {
    var s=new Sink(); using var e=new InputEngine(s); Check(e.AdoptPhysicalMouse("5",ButtonId.Forward,"physical",Extra()));
    await WaitUntil(()=>s.Events.Count(x=>x.StartsWith("down"))>=3);
    Check(e.HeldTokens.Contains(new(false,4))); Check(s.Events.All(x=>x.Contains("Keyboard = True")));
    Check(e.ReleasePhysicalMouse("5",ButtonId.Forward)); var count=s.Events.Count;
    await Task.Delay(150); Check(s.Events.Count==count && e.ActiveCount==0 && s.Held.IsEmpty);
});
Test("Synthetic toggle retains main hold while extra taps, then stops both", async () => {
    var s=new Sink(); using var e=new InputEngine(s); var b=Extra(); b.Mouse=ButtonId.Right;
    e.Trigger("5",b,true); e.Trigger("5",b,false);
    await WaitUntil(()=>s.Events.Count(x=>x.StartsWith("down") && x.Contains("Code = 70"))>=2);
    Check(s.Held.ContainsKey(new(false,1))); e.Trigger("5",b,true); var count=s.Events.Count;
    await Task.Delay(150); Check(s.Events.Count==count && s.Held.IsEmpty && e.ActiveCount==0);
});
Test("Emergency cleanup releases an extra key during its tap", async () => {
    var s=new Sink(); using var e=new InputEngine(s); e.AdoptPhysicalMouse("5",ButtonId.Forward,"physical",Extra());
    await WaitUntil(()=>s.Held.ContainsKey(new(true,70))); e.StopAll(); var count=s.Events.Count;
    await Task.Delay(150); Check(s.Held.IsEmpty && e.HeldTokens.Length==0 && s.Events.Count==count);
});
Test("Disabled extra keys and ordinary Hold do not repeat", async () => {
    var s=new Sink(); using var e=new InputEngine(s); var b=Extra(); b.ExtraKeyEnabled=false;
    e.AdoptPhysicalMouse("5",ButtonId.Forward,"physical",b); await Task.Delay(160); Check(s.Events.IsEmpty); e.ReleasePhysicalMouse("5",ButtonId.Forward);
    b.ExtraKeyEnabled=true; b.Mode=PlayMode.Hold; e.Trigger("5",b,true); await Task.Delay(160); Check(s.Events.Count==1); e.StopAll();
});
Test("Extra key survives serialization and toggle off/on", () => {
    var c=new Configuration(); var b=Extra(); c.Profiles[0].Bindings[ButtonId.Forward]=b; ConfigStore.Validate(c);
    c=ConfigStore.Clone(c); var bindings=c.Profiles[0].Bindings; QuickToggle.Set(bindings,ButtonId.Forward,false); QuickToggle.Set(bindings,ButtonId.Forward,true);
    Check(bindings[ButtonId.Forward].ExtraKeyEnabled && bindings[ButtonId.Forward].ExtraKeys.SequenceEqual(new[]{70}) && bindings[ButtonId.Forward].ExtraIntervalMs==100);
    b=bindings[ButtonId.Forward]; b.ExtraIntervalMs=99; Reject(()=>ConfigStore.Validate(c)); b.ExtraIntervalMs=1000; b.ExtraKeys=[]; Reject(()=>ConfigStore.Validate(c)); return Task.CompletedTask;
});
Test("Extra shortcut cannot conflict with main held keyboard output", () => {
    var c=new Configuration(); var b=Key(PlayMode.Toggle,70); b.ExtraKeyEnabled=true; b.ExtraKeys=[70]; c.Profiles[0].Bindings[ButtonId.Forward]=b;
    Reject(()=>ConfigStore.Validate(c)); b.ExtraKeys=[71]; ConfigStore.Validate(c); return Task.CompletedTask;
});
Test("Extra key failure stops parent and releases its mouse hold", async () => {
    var s=new Sink {FailDown=true}; using var e=new InputEngine(s); string? error=null; e.Error+=v=>error=v;
    e.AdoptPhysicalMouse("5",ButtonId.Forward,"physical",Extra()); await WaitUntil(()=>error!=null);
    Check(e.ActiveCount==0 && e.HeldTokens.Length==0 && s.Events.Any(x=>x.StartsWith("up") && x.Contains("Keyboard = False")));
});
Test("Extra key supports a longer press and validates interval spacing", async () => {
    var s=new Sink(); using var e=new InputEngine(s); var b=Extra(); b.ExtraPressMs=150; b.ExtraIntervalMs=300;
    var c=new Configuration(); c.Profiles[0].Bindings[ButtonId.Forward]=b; ConfigStore.Validate(c);
    e.AdoptPhysicalMouse("5",ButtonId.Forward,"physical",b); await WaitUntil(()=>s.Held.ContainsKey(new(true,70)));
    await Task.Delay(75); Check(s.Held.ContainsKey(new(true,70))); await WaitUntil(()=>!s.Held.ContainsKey(new(true,70)));
    e.StopAll(); b.ExtraPressMs=300; Reject(()=>ConfigStore.Validate(c));
});
Test("Compatibility input routes only extra keys and stops during a press", async () => {
    var s=new Sink(); using var e=new InputEngine(s); var b=Extra(); b.ExtraCompatibilityInput=true; b.ExtraPressMs=65;
    var c=new Configuration(); c.Profiles[0].Bindings[ButtonId.Forward]=b;
    b=ConfigStore.Clone(c).Profiles[0].Bindings[ButtonId.Forward]; Check(b.ExtraCompatibilityInput);
    e.Trigger("5",b,true); e.Trigger("5",b,false); await WaitUntil(()=>s.Held.ContainsKey(new(true,70)));
    Check(s.Routes.Any(x=>x.Token==new InputToken(false,4) && !x.Compatibility));
    Check(s.Routes.Any(x=>x.Token==new InputToken(true,70) && x.Compatibility));
    e.Trigger("5",b,true); var count=s.Events.Count; await Task.Delay(160);
    Check(s.Held.IsEmpty && e.ActiveCount==0 && s.Events.Count==count);
});
Test("Compatibility extra key shares ownership without releasing another binding", async () => {
    var s=new Sink(); using var e=new InputEngine(s); var b=Extra(); b.ExtraCompatibilityInput=true;
    e.Trigger("keyboard",Key(PlayMode.Toggle,70),true); e.AdoptPhysicalMouse("5",ButtonId.Forward,"physical",b);
    await Task.Delay(250); Check(s.Routes.Count==1 && !s.Routes.Single().Compatibility);
    e.ReleasePhysicalMouse("5",ButtonId.Forward); Check(s.Held.ContainsKey(new(true,70)));
    e.StopAll(); Check(s.Held.IsEmpty);
});
Test("On-mouse F compiler preserves mouse hold and produces balanced timed F", () => {
    var b=Extra(); b.ExtraIntervalMs=1000; b.ExtraPressMs=65; b.ExtraOnDevice=true;
    Check(OnboardMacro.Encode(b).SequenceEqual(new byte[]{8,16,1,9,17,65,2,9,18,3,167}));
    var c=new Configuration(); c.Profiles[0].Bindings[ButtonId.Forward]=b; ConfigStore.Validate(c);
    Check(ConfigStore.Clone(c).Profiles[0].Bindings[ButtonId.Forward].ExtraOnDevice);
    return Task.CompletedTask;
});
Test("On-mouse chords release in reverse and reject unsupported outputs", () => {
    var b=Extra(); b.ExtraKeys=[17,65]; b.ExtraIntervalMs=1000; b.ExtraPressMs=65;
    Check(OnboardMacro.Encode(b).SequenceEqual(new byte[]{8,16,1,224,1,4,17,65,2,4,2,224,18,3,167}));
    b.ExtraKeys=[175]; Reject(()=>OnboardMacro.Encode(b));
    b.ExtraKeys=[70]; b.Mouse=ButtonId.Back; Reject(()=>OnboardMacro.Encode(b));
    return Task.CompletedTask;
});
Test("Bounded hardware diagnostic has six balanced F taps, no mouse commands, and a six-second limit", () => {
    var b=Extra(); b.ExtraIntervalMs=1000; b.ExtraPressMs=65;
    var bytes=OnboardMacro.EncodeBoundedKeyTest(b,6);
    int down=0,up=0,total=0; bool held=false;
    for(int i=0;i<bytes.Length;)
    {
        var op=bytes[i++];
        if(op==1) { Check(bytes[i++]==9 && !held); held=true; down++; }
        else if(op==2) { Check(bytes[i++]==9 && held); held=false; up++; }
        else if(op==17) total+=bytes[i++];
        else if(op==18) { total+=bytes[i]*256+bytes[i+1]; i+=2; }
        else throw new Exception("Unexpected command in keyboard-only diagnostic: "+op);
    }
    Check(down==6 && up==6 && !held && total==6000);
    Reject(()=>OnboardMacro.EncodeBoundedKeyTest(b,0));
    Reject(()=>OnboardMacro.EncodeBoundedKeyTest(b,7));
    b.ExtraIntervalMs=1001; Reject(()=>OnboardMacro.EncodeBoundedKeyTest(b,6));
    return Task.CompletedTask;
});
Test("Bounded combined diagnostic contains an explicit final mouse release", () => {
    var b=Extra(); b.ExtraIntervalMs=1000; b.ExtraPressMs=65;
    var bytes=OnboardMacro.EncodeBoundedHoldTest(b,6);
    Check(bytes.Take(2).SequenceEqual(new byte[]{8,16}) && bytes.TakeLast(2).SequenceEqual(new byte[]{8,0}));
    Check(bytes.Skip(2).Take(bytes.Length-4).SequenceEqual(OnboardMacro.EncodeBoundedKeyTest(b,6)));
    Reject(()=>OnboardMacro.EncodeBoundedHoldTest(b,0));
    return Task.CompletedTask;
});
Test("Finite toggle diagnostic renews mouse-down without intermediate releases", () => {
    var b=Extra(); b.ExtraIntervalMs=1000; b.ExtraPressMs=65;
    var bytes=OnboardMacro.EncodeBoundedToggleTest(b,6);
    int mouseDowns=0,mouseUps=0,fDowns=0,fUps=0,total=0;
    for(int i=0;i<bytes.Length;)
    {
        var op=bytes[i++];
        switch(op)
        {
            case 8: var mask=bytes[i++]; if(mask==16) mouseDowns++; else { Check(mask==0 && i==bytes.Length); mouseUps++; } break;
            case 1: Check(bytes[i++]==9); fDowns++; break;
            case 2: Check(bytes[i++]==9); fUps++; break;
            case 17: total+=bytes[i++]; break;
            case 18: total+=bytes[i]*256+bytes[i+1]; i+=2; break;
            default: throw new Exception("Unexpected diagnostic opcode.");
        }
    }
    Check(mouseDowns==6 && mouseUps==1 && fDowns==6 && fUps==6 && total==6000 && bytes.Length<=72);
    Reject(()=>OnboardMacro.EncodeBoundedToggleTest(b,7));
    return Task.CompletedTask;
});
Test("Separated hardware F keeps its press duration, repetition interval and continuous mouse hold", () => {
    var b=Extra(); b.ExtraIntervalMs=1000; b.ExtraPressMs=65;
    foreach(var timing in new[]{(1000,65),(100,90),(60000,1000)})
    {
        b.ExtraIntervalMs=timing.Item1; b.ExtraPressMs=timing.Item2;
        var bytes=OnboardMacro.Encode(b,separateMouseAndKey:true);
        int time=0,down=-1,up=-1,mouseDowns=0; bool mouseHeld=false;
        for(int i=0;i<bytes.Length;)
        {
            switch(bytes[i++])
            {
                case 8: Check(bytes[i++]==16 && !mouseHeld); mouseHeld=true; mouseDowns++; break;
                case 1: Check(bytes[i++]==9 && mouseHeld && down<0); down=time; break;
                case 2: Check(bytes[i++]==9 && down>=0 && up<0); up=time; break;
                case 17: time+=bytes[i++]; break;
                case 18: time+=bytes[i]*256+bytes[i+1]; i+=2; break;
                default: throw new Exception("Unexpected hardware command.");
            }
        }
        Check(mouseHeld && mouseDowns==1 && down>0 && up-down==b.ExtraPressMs && time==b.ExtraIntervalMs && time>up);
        if(b.ExtraIntervalMs==1000) Check(down==100);
    }
    return Task.CompletedTask;
});
Test("Continuous hardware keyboard payload contains no mouse commands and retains balanced one-second F", () => {
    var b=Extra(); b.ExtraIntervalMs=1000; b.ExtraPressMs=65;
    var bytes=OnboardMacro.EncodeKeyboard(b);
    int time=0,downs=0,ups=0; bool held=false;
    for(int i=0;i<bytes.Length;)
    {
        switch(bytes[i++])
        {
            case 1: Check(bytes[i++]==9 && !held); held=true; downs++; break;
            case 2: Check(bytes[i++]==9 && held && time==65); held=false; ups++; break;
            case 17: time+=bytes[i++]; break;
            case 18: time+=bytes[i]*256+bytes[i+1]; i+=2; break;
            default: throw new Exception("Mouse or unsupported command in keyboard-only loop.");
        }
    }
    Check(downs==1 && ups==1 && !held && time==1000);
    return Task.CompletedTask;
});
Test("Hardware-started mouse hold emits one down and accepts real physical OFF without software F", async () => {
    var s=new Sink(); using var e=new InputEngine(s); var b=Extra(); b.ExtraOnDevice=true;
    e.Trigger("Forward",b,true); await Task.Delay(200);
    Check(s.Events.Count==1 && s.Events.Single().StartsWith("down") && s.Held.ContainsKey(new(false,4)));
    Check(e.ReleasePhysicalMouse("Forward",ButtonId.Forward));
    await Task.Delay(100);
    Check(e.ActiveCount==0 && e.HeldTokens.Length==0 && s.Events.Count==1);
});
Test("Hardware extra keys do not also start software F or change physical hold ownership", async () => {
    var s=new Sink(); using var e=new InputEngine(s); var b=Extra(); b.ExtraOnDevice=true;
    Check(e.AdoptPhysicalMouse("Forward",ButtonId.Forward,"hardware F",b));
    await Task.Delay(200);
    Check(e.IsActive("Forward") && e.HeldTokens.Contains(new(false,4)) && s.Events.IsEmpty);
    Check(e.ReleasePhysicalMouse("Forward",ButtonId.Forward));
    await Task.Delay(150);
    Check(e.ActiveCount==0 && s.Events.IsEmpty && s.Held.IsEmpty);
});
foreach (var (name, test) in tests) { try { await test(); Console.WriteLine("PASS " + name); } catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
Console.WriteLine($"{tests.Count - failures}/{tests.Count} passed"); return failures == 0 ? 0 : 1;
sealed class Sink : IInputSink
{
    public ConcurrentDictionary<InputToken, bool> Held = new(); public ConcurrentQueue<string> Events = new(); public bool FailDown;
    public ConcurrentQueue<(InputToken Token, bool Compatibility)> Routes = new();
    public void Down(InputToken t, bool compatibility) { Routes.Enqueue((t,compatibility)); Down(t); }
    public void Down(InputToken t) { if (FailDown) throw new InvalidOperationException("Injected failure"); Held[t] = true; Events.Enqueue("down " + t); }
    public void Up(InputToken t) { Held.TryRemove(t, out _); Events.Enqueue("up " + t); }
    public void Move(int x, int y) => Events.Enqueue("move"); public void Wheel(int delta, bool horizontal) => Events.Enqueue("wheel");
}
