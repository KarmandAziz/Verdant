using System.Text.Json;
using System.Text.Json.Serialization;

namespace Verdant.Core;

public enum ButtonId { Left, Right, Middle, Back, Forward, WheelUp, WheelDown, WheelLeft, WheelRight }
public enum ActionKind { Default, Disabled, Keyboard, Mouse, Macro, Launch, Url, Profile, Layer }
public enum PlayMode { Once, Hold, Toggle, RepeatWhileHeld, RepeatToggle, Count }
public enum EventKind { KeyDown, KeyUp, MouseDown, MouseUp, Move, Wheel }
public sealed record MacroEvent
{
    public EventKind Kind { get; set; }
    public int Code { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int DelayMs { get; set; }
    public string Group { get; set; } = "";
    public override string ToString() => $"{DelayMs,5} ms   {Kind,-12} {Code} {(Kind == EventKind.Move ? $"({X}, {Y})" : "")}  {Group}";
}
public sealed record Macro
{
    public int Version { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New macro";
    public List<MacroEvent> Events { get; set; } = [];
    public double Speed { get; set; } = 1;
    public override string ToString() => Name;
}
public sealed record Binding
{
    public ActionKind Kind { get; set; }
    public PlayMode Mode { get; set; }
    public PlayMode ModeBeforeToggle { get; set; } = PlayMode.Once;
    public bool ToggleRestoresInheritance { get; set; }
    public List<int> Keys { get; set; } = [];
    public ButtonId Mouse { get; set; } = ButtonId.Left;
    public string Target { get; set; } = "";
    public string ReleaseMacroId { get; set; } = "";
    public int IntervalMs { get; set; } = 100;
    public int Count { get; set; } = 1;
    public bool ExtraKeyEnabled { get; set; }
    public List<int> ExtraKeys { get; set; } = [];
    public int ExtraIntervalMs { get; set; } = 1000;
    public int ExtraPressMs { get; set; } = 40;
    public bool ExtraCompatibilityInput { get; set; }
    public bool ExtraOnDevice { get; set; }
    public string Label => Kind switch { ActionKind.Default => "Default", ActionKind.Disabled => "Disabled", ActionKind.Keyboard => string.Join(" + ", Keys.Select(KeyNames.Name)) + " · " + Mode, ActionKind.Mouse => Mouse + " · " + Mode, _ => Kind + " · " + Mode };
}
public static class QuickToggle
{
    public static bool CanHold(Binding binding) => binding.Kind is ActionKind.Default or ActionKind.Mouse or ActionKind.Keyboard;
    public static void Set(Dictionary<ButtonId,Binding> bindings,ButtonId button,bool enabled,Binding? inherited=null)
    {
        if(button>ButtonId.Forward) throw new InvalidDataException("Scrolling has no held button state.");
        var present=bindings.TryGetValue(button,out var current);
        var binding=ConfigStore.Clone(current??inherited??new Binding());
        if(!CanHold(binding)) throw new InvalidDataException("Toggle-hold needs a mouse or keyboard action.");
        if(enabled)
        {
            if(binding.Mode==PlayMode.Toggle && binding.Kind!=ActionKind.Default) return;
            binding.ToggleRestoresInheritance=!present || binding.Kind==ActionKind.Default;
            binding.ModeBeforeToggle=binding.Mode;
            if(binding.Kind==ActionKind.Default) { binding.Kind=ActionKind.Mouse; binding.Mouse=button; }
            binding.Mode=PlayMode.Toggle; bindings[button]=binding;
        }
        else if(binding.Mode==PlayMode.Toggle)
        {
            if(binding.ToggleRestoresInheritance && present) bindings.Remove(button);
            else if(binding.ToggleRestoresInheritance) bindings[button]=new Binding();
            else { binding.Mode=binding.ModeBeforeToggle; bindings[button]=binding; }
        }
    }
}
public sealed record Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Everyday";
    public string ApplicationPath { get; set; } = "";
    public Dictionary<ButtonId, Binding> Bindings { get; set; } = [];
    public Dictionary<ButtonId, Binding> Layer { get; set; } = [];
    public override string ToString() => Name;
}
public sealed record Configuration
{
    public int Version { get; set; } = 2;
    public List<Profile> Profiles { get; set; } = [new()];
    public List<Macro> Macros { get; set; } = [];
    public string DefaultProfileId { get; set; } = "";
    public bool AutoSwitch { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool CloseToTray { get; set; } = true;
    public Dictionary<ButtonId, double[]> Hotspots { get; set; } = [];
}
public static class KeyNames
{
    public static readonly Dictionary<string, int> Special = new(StringComparer.OrdinalIgnoreCase) { ["Ctrl"] = 0x11, ["Shift"] = 0x10, ["Alt"] = 0x12, ["Win"] = 0x5B, ["Space"] = 0x20, ["Enter"] = 13, ["Tab"] = 9, ["Escape"] = 27, ["Backspace"] = 8, ["Delete"] = 46, ["Insert"] = 45, ["Home"] = 36, ["End"] = 35, ["PageUp"] = 33, ["PageDown"] = 34, ["Left"] = 37, ["Up"] = 38, ["Right"] = 39, ["Down"] = 40, ["VolumeUp"] = 175, ["VolumeDown"] = 174, ["Mute"] = 173, ["PlayPause"] = 179, ["NextTrack"] = 176, ["PreviousTrack"] = 177 };
    public static string Name(int key) => Special.FirstOrDefault(x => x.Value == key).Key ?? (key is >= 65 and <= 90 or >= 48 and <= 57 ? ((char)key).ToString() : key is >= 112 and <= 135 ? "F" + (key - 111) : "0x" + key.ToString("X2"));
    public static List<int> Parse(string text) => text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(s => Special.TryGetValue(s, out var n) ? n : s.Length == 1 && char.IsAsciiLetterOrDigit(s[0]) ? char.ToUpperInvariant(s[0]) : s.StartsWith('F') && int.TryParse(s[1..], out n) && n is >= 1 and <= 24 ? n + 111 : s.StartsWith("0x") && int.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber, null, out n) && n is > 0 and < 256 ? n : throw new InvalidDataException($"Unknown key: {s}. Use W, Ctrl+Shift+S, F8, or 0xBA.")).Distinct().ToList();
}
public static class ConfigStore
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;
    public static Configuration Read(string path)
    {
        var result = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Empty configuration.");
        if (result.Version == 1) result.Version = 2;
        Validate(result); return result;
    }
    public static void Save(string path, Configuration value)
    {
        Validate(value); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        using (var f = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { JsonSerializer.Serialize(f, value, Json); f.Flush(true); }
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
    }
    public static void Validate(Configuration c)
    {
        if (c.Version != 2) throw new InvalidDataException("This configuration version is not supported.");
        if (c.Profiles is null || c.Profiles.Count == 0 || c.Profiles.Count > 10000 || c.Macros is null) throw new InvalidDataException("At least one profile is required.");
        if (c.Profiles.Any(p => p is null || string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.Id) || p.Bindings is null || p.Layer is null)) throw new InvalidDataException("Invalid profile.");
        if (c.Profiles.Select(p => p.Id).Distinct().Count() != c.Profiles.Count || c.Macros.Select(m => m.Id).Distinct().Count() != c.Macros.Count) throw new InvalidDataException("Duplicate IDs.");
        var linked = c.Profiles.Where(p => !string.IsNullOrWhiteSpace(p.ApplicationPath)).Select(p => p.ApplicationPath).ToArray();
        if (linked.Distinct(StringComparer.OrdinalIgnoreCase).Count() != linked.Length) throw new InvalidDataException("Only one profile may be linked to an application.");
        if (c.Hotspots is null || c.Hotspots.Any(p => !Enum.IsDefined(p.Key) || p.Value is null || p.Value.Length != 2 || p.Value.Any(v => !double.IsFinite(v)) || p.Value[0] < 17 || p.Value[0] > 483 || p.Value[1] < 17 || p.Value[1] > 373)) throw new InvalidDataException("Invalid illustration positions.");
        if (string.IsNullOrEmpty(c.DefaultProfileId)) c.DefaultProfileId = c.Profiles[0].Id;
        if (!c.Profiles.Any(p => p.Id == c.DefaultProfileId)) throw new InvalidDataException("Default profile is missing.");
        foreach (var m in c.Macros) ValidateMacro(m);
        foreach (var p in c.Profiles) foreach (var entry in p.Bindings.Concat(p.Layer))
        {
            var b = entry.Value;
            if (!Enum.IsDefined(entry.Key) || b is null || !Enum.IsDefined(b.Kind) || !Enum.IsDefined(b.Mode) || b.Keys is null || b.Keys.Any(k => k is < 1 or > 255) || b.IntervalMs is < 10 or > 60000 || b.Count is < 1 or > 10000) throw new InvalidDataException("Invalid binding or timing.");
            if (b.Kind == ActionKind.Keyboard && b.Keys.Count == 0) throw new InvalidDataException("Choose at least one key.");
            if (b.ExtraKeys is null || b.ExtraKeys.Any(k => k is < 1 or > 255) || b.ExtraIntervalMs is < 100 or > 60000 || b.ExtraKeyEnabled && b.ExtraKeys.Count == 0) throw new InvalidDataException("Choose an extra key and an interval from 100 to 60000 ms.");
            if (b.ExtraPressMs < 10 || b.ExtraPressMs > 1000 || b.ExtraPressMs > b.ExtraIntervalMs - 10) throw new InvalidDataException("Press length must be 10–1000 ms and at least 10 ms shorter than the tap interval.");
            if (b.ExtraKeyEnabled && b.Kind == ActionKind.Keyboard && b.Keys.Intersect(b.ExtraKeys).Any()) throw new InvalidDataException("The extra shortcut cannot include a key held by the main assignment.");
            if (b.ExtraOnDevice && b.ExtraKeyEnabled && b.Mode==PlayMode.Toggle)
            {
                if (entry.Key!=ButtonId.Forward) throw new InvalidDataException("On-mouse playback currently supports the Basilisk V3 front side button.");
                _=OnboardMacro.Encode(b);
            }
            if (b.Kind == ActionKind.Macro && !c.Macros.Any(m => m.Id == b.Target)) throw new InvalidDataException("The assigned macro is missing.");
            if (b.ReleaseMacroId.Length > 0 && !c.Macros.Any(m => m.Id == b.ReleaseMacroId)) throw new InvalidDataException("The release macro is missing.");
            if (b.ReleaseMacroId.Length > 0 && (b.Kind != ActionKind.Macro || entry.Key >= ButtonId.WheelUp)) throw new InvalidDataException("Release sequences require a physical button with a macro assignment.");
            if (b.Kind is ActionKind.Profile or ActionKind.Layer && b.Mode is not (PlayMode.Once or PlayMode.Hold)) throw new InvalidDataException("Profile and layer actions support Once or Hold.");
            if (b.Kind == ActionKind.Profile && !c.Profiles.Any(q => q.Id == b.Target)) throw new InvalidDataException("The assigned profile is missing.");
            if (b.Kind == ActionKind.Url && (!Uri.TryCreate(b.Target, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))) throw new InvalidDataException("Use an http or https URL.");
            if (b.Kind == ActionKind.Launch && !Path.IsPathFullyQualified(b.Target)) throw new InvalidDataException("Choose an absolute file or folder path.");
            if (b.Kind == ActionKind.Mouse && (!Enum.IsDefined(b.Mouse) || b.Mouse > ButtonId.Forward)) throw new InvalidDataException("Choose one of the five standard mouse buttons.");
            if (entry.Key >= ButtonId.WheelUp && b.Mode is PlayMode.Hold or PlayMode.RepeatWhileHeld) throw new InvalidDataException("Wheel actions have no release event. Choose Once or Toggle.");
        }
    }
    public static void ValidateMacro(Macro m)
    {
        if (m is null || m.Version != 1 || string.IsNullOrWhiteSpace(m.Name) || string.IsNullOrWhiteSpace(m.Id) || m.Events is null || m.Events.Count is 0 or > 100000 || !double.IsFinite(m.Speed) || m.Speed is < 0.1 or > 10) throw new InvalidDataException("A macro needs a name, at least one event, a supported version, and a speed from 0.1 to 10.");
        var held = new HashSet<(bool, int)>();
        foreach (var e in m.Events)
        {
            if (e is null || !Enum.IsDefined(e.Kind) || e.DelayMs is < 0 or > 3600000) throw new InvalidDataException("Invalid event or delay.");
            var key = e.Kind is EventKind.KeyDown or EventKind.KeyUp;
            if (key && e.Code is < 1 or > 255 || e.Kind is EventKind.MouseDown or EventKind.MouseUp && e.Code is < 0 or > 4) throw new InvalidDataException("Invalid input code.");
            if (e.Kind is EventKind.KeyDown or EventKind.MouseDown) { if (!held.Add((key, e.Code))) throw new InvalidDataException("Repeated down event without a release."); }
            if (e.Kind is EventKind.KeyUp or EventKind.MouseUp) { if (!held.Remove((key, e.Code))) throw new InvalidDataException("Release event without a matching press."); }
        }
        if (held.Count > 0) throw new InvalidDataException("Every macro press must have a matching release. Use a Toggle binding to hold a key.");
    }
    public static Profile Resolve(Configuration c, string? path) => c.Profiles.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.ApplicationPath) && string.Equals(p.ApplicationPath, path, StringComparison.OrdinalIgnoreCase)) ?? c.Profiles.First(p => p.Id == c.DefaultProfileId);
}
