using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
namespace Verdant.App;

public sealed partial class MainWindow
{
    private UIElement Profiles()
    {
        var p = Editing;
        var left = Stack(Text("Your profiles", 20));
        foreach (var profile in Config.Profiles)
        { var item = profile; var b = Btn(item.Name + (item.Id == Config.DefaultProfileId ? "  ·  Default" : ""), () => { editingProfile = item.Id; Navigate(page); }); b.HorizontalContentAlignment = HorizontalAlignment.Left; if (item.Id == p.Id) b.Foreground = B("Accent"); left.Children.Add(b); }
        left.Children.Add(Row(Btn("New profile", () => { var n = new Profile { Name = "Profile " + (Config.Profiles.Count + 1) }; Commit(c => c.Profiles.Add(n), "Profile created."); editingProfile = n.Id; Navigate(page); }, true), Btn("Import…", ImportProfile)));
        var form = Stack(Text("Profile details", 22)); var name = Field(form, "Name", p.Name); var app = Field(form, "Linked application (full executable path)", p.ApplicationPath);
        form.Children.Add(Btn("Choose application…", () => { var f = PickOpen("Windows applications|*.exe"); if (f != null) app.Text = f; }));
        form.Children.Add(Text("When this application becomes active, its profile is selected. Other applications use your default profile. Manual selection lasts until the foreground window changes.", 12, "Muted"));
        form.Children.Add(Row(Btn("Save profile", () => { Commit(c => { var x = c.Profiles.First(x => x.Id == p.Id); x.Name = name.Text.Trim(); x.ApplicationPath = app.Text.Trim(); if (x.ApplicationPath.Length > 0 && (!Path.IsPathFullyQualified(x.ApplicationPath) || !File.Exists(x.ApplicationPath))) throw new InvalidDataException("Choose an existing application by its full path."); if (x.ApplicationPath.Length > 0 && c.Profiles.Any(q => q.Id != x.Id && string.Equals(q.ApplicationPath, x.ApplicationPath, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("This application already has a profile."); }, "Profile saved."); Navigate(page); }, true), Btn("Activate", () => { runtime.SelectProfile(p.Id); Notify("Profile activated: " + p.Name); })));
        form.Children.Add(Row(Btn("Make default", () => { Commit(c => c.DefaultProfileId = p.Id, "Default profile updated."); Navigate(page); }), Btn("Duplicate", () => { var copy = ConfigStore.Clone(p); copy.Id = Guid.NewGuid().ToString("N"); copy.Name += " copy"; copy.ApplicationPath = ""; Commit(c => c.Profiles.Add(copy), "Profile duplicated."); editingProfile = copy.Id; Navigate(page); })));
        form.Children.Add(Row(Btn("Export…", () => { var path = PickSave(p.Name + ".json"); if (path == null) return; var export = ConfigStore.Clone(Config); export.Profiles = [ConfigStore.Clone(p)]; export.DefaultProfileId = p.Id; foreach (var b in export.Profiles[0].Bindings.Values.Concat(export.Profiles[0].Layer.Values).Where(b => b.Kind == ActionKind.Profile)) { b.Target = p.Id; } ConfigStore.Save(path, export); Notify("Profile exported with macros. Cross-profile actions target the exported profile."); }), Btn("Delete profile", () => { if (Config.Profiles.Count == 1) throw new InvalidOperationException("Keep at least one profile."); if (!Confirm("Delete '" + p.Name + "'? Its button assignments will be removed.")) return; Commit(c => { c.Profiles.RemoveAll(x => x.Id == p.Id); if (c.DefaultProfileId == p.Id) c.DefaultProfileId = c.Profiles[0].Id; foreach (var q in c.Profiles) foreach (var b in q.Bindings.Values.Concat(q.Layer.Values).Where(b => b.Kind == ActionKind.Profile && b.Target == p.Id)) b.Target = c.DefaultProfileId; }, "Profile deleted."); editingProfile = Config.DefaultProfileId; Navigate(page); })));
        form.Children.Add(Text($"{p.Bindings.Count} standard assignments · {p.Layer.Count} layer assignments", 12, "Muted"));
        return Split(new ScrollViewer { Content = left, Margin = new(0, 0, 24, 0) }, new ScrollViewer { Content = Card(form) }, 480);
    }
    private void ImportProfile()
    {
        var path = PickOpen(); if (path == null) return; if (new FileInfo(path).Length > 20_000_000) throw new InvalidDataException("Profile file is too large.");
        var imported = ConfigStore.Read(path);
        var profileIds = imported.Profiles.ToDictionary(p => p.Id, _ => Guid.NewGuid().ToString("N")); var macroIds = imported.Macros.ToDictionary(m => m.Id, _ => Guid.NewGuid().ToString("N"));
        foreach (var m in imported.Macros) m.Id = macroIds[m.Id];
        foreach (var p in imported.Profiles)
        {
            p.Id = profileIds[p.Id]; p.Name += " (imported)"; p.ApplicationPath = "";
            foreach (var b in p.Bindings.Values.Concat(p.Layer.Values)) { if (b.Kind == ActionKind.Macro) b.Target = macroIds[b.Target]; if (b.ReleaseMacroId.Length > 0) b.ReleaseMacroId = macroIds[b.ReleaseMacroId]; if (b.Kind == ActionKind.Profile) b.Target = profileIds[b.Target]; }
        }
        Commit(c => { c.Profiles.AddRange(imported.Profiles); c.Macros.AddRange(imported.Macros); }, "Profiles imported. Review launch actions before activating."); editingProfile = imported.Profiles[0].Id; Navigate(page);
    }
}
