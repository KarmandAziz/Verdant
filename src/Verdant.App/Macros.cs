using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Verdant.App;

public sealed partial class MainWindow
{
    private Macro draft = new();
    private readonly Stack<Macro> undo = [], redo = [];
    private ListBox? eventList;
    private TextBox? macroName, macroSpeed;
    private CheckBox? recordMovement, recordDelay;
    private TextBlock? recordStatus;
    private bool draftDirty;
    private System.Windows.Point dragStart;
    private UIElement Macros()
    {
        var root = new Grid(); root.ColumnDefinitions.Add(new() { Width = new(190) }); root.ColumnDefinitions.Add(new());
        var library = Stack(Text("Library", 20), Btn("New macro", () => { if (!LeaveDraft()) return; draft = new(); undo.Clear(); redo.Clear(); draftDirty = false; Navigate(page); }, true));
        foreach (var m in Config.Macros) { var saved = m; library.Children.Add(Btn(saved.Name, () => { if (!LeaveDraft()) return; draft = ConfigStore.Clone(saved); undo.Clear(); redo.Clear(); draftDirty = false; Navigate(page); })); }
        library.Children.Add(Btn("Import macro…", () => { if (!LeaveDraft()) return; var f = PickOpen(); if (f == null) return; if (new FileInfo(f).Length > 20_000_000) throw new InvalidDataException("Macro file is too large."); var m = JsonSerializer.Deserialize<Macro>(File.ReadAllText(f), ConfigStore.Json) ?? throw new InvalidDataException("Invalid macro."); ConfigStore.ValidateMacro(m); m.Id = Guid.NewGuid().ToString("N"); draft = m; draftDirty = true; Navigate(page); }));
        library.IsEnabled = !isRecording; root.Children.Add(new ScrollViewer { Content = library, Margin = new(0, 0, 18, 0) });
        var editor = new Grid(); editor.RowDefinitions.Add(new() { Height = GridLength.Auto }); editor.RowDefinitions.Add(new()); editor.RowDefinitions.Add(new() { Height = GridLength.Auto }); Grid.SetColumn(editor, 1); root.Children.Add(editor);
        var header = new StackPanel(); var names = new Grid(); names.ColumnDefinitions.Add(new()); names.ColumnDefinitions.Add(new() { Width = new(120) });
        var namePanel = new StackPanel { Margin = new(0, 0, 16, 0) }; macroName = Field(namePanel, "Macro name", draft.Name); macroName.TextChanged += (_, _) => { draft.Name = macroName.Text; draftDirty = true; }; names.Children.Add(namePanel);
        var speedPanel = new StackPanel(); macroSpeed = Field(speedPanel, "Speed · 0.1–10×", draft.Speed.ToString(System.Globalization.CultureInfo.InvariantCulture)); macroSpeed.TextChanged += (_, _) => draftDirty = true; Grid.SetColumn(speedPanel, 1); names.Children.Add(speedPanel); header.Children.Add(names);
        recordMovement = new CheckBox { Content = "Mouse movement", IsChecked = false, Margin = new(0, 5, 20, 8) }; recordDelay = new CheckBox { Content = "Record delays", IsChecked = true };
        header.Children.Add(Row(recordMovement, recordDelay));
        header.Children.Add(Row(Btn(isRecording ? "Stop recording · " + recordShortcut : "Record · " + recordShortcut, ToggleRecord, true), Btn("Save macro", SaveMacro), Btn("Export…", () => { ReadDraftFields(); ConfigStore.ValidateMacro(draft); var f = PickSave(draft.Name + ".json"); if (f != null) { File.WriteAllText(f, JsonSerializer.Serialize(draft, ConfigStore.Json)); Notify("Macro exported."); } }), Btn("Delete", () => { if (!Config.Macros.Any(m => m.Id == draft.Id)) { Notify("This draft is not saved."); return; } if (Config.Profiles.Any(p => p.Bindings.Values.Concat(p.Layer.Values).Any(b => (b.Kind == ActionKind.Macro && b.Target == draft.Id || b.ReleaseMacroId == draft.Id)))) throw new InvalidOperationException("Remove this macro's button assignments before deleting it."); if (Confirm("Delete this saved macro?")) { Commit(c => c.Macros.RemoveAll(m => m.Id == draft.Id), "Macro deleted."); draft = new(); draftDirty = false; Navigate(page); } })));
        recordStatus = Text(isRecording ? "Recording physical input. " + recordShortcut + " stops; injected events are ignored." : recordShortcut + " starts recording. Macros must balance every press with a release.", 12, "Muted"); header.Children.Add(recordStatus); editor.Children.Add(header);
        eventList = new ListBox { SelectionMode = SelectionMode.Extended, AllowDrop = true, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12 };
        eventList.ItemsSource = draft.Events; eventList.SelectionChanged += (_, _) => UpdateEventEditor();
        eventList.PreviewMouseLeftButtonDown += (_, e) => dragStart = e.GetPosition(eventList);
        eventList.MouseMove += (_, e) => { if (e.LeftButton != MouseButtonState.Pressed || eventList.SelectedIndex < 0) return; var p = e.GetPosition(eventList); if (Math.Abs(p.X - dragStart.X) + Math.Abs(p.Y - dragStart.Y) > 10) DragDrop.DoDragDrop(eventList, eventList.SelectedIndex.ToString(), DragDropEffects.Move); };
        eventList.Drop += (_, e) => { if (!int.TryParse(e.Data.GetData(typeof(string)) as string, out var from)) return; var target = ItemsControl.ContainerFromElement(eventList, e.OriginalSource as DependencyObject) as ListBoxItem; var to = target == null ? draft.Events.Count - 1 : eventList.ItemContainerGenerator.IndexFromContainer(target); if (from < 0 || from >= draft.Events.Count || to < 0) return; Snapshot(); var item = draft.Events[from]; draft.Events.RemoveAt(from); draft.Events.Insert(to, item); RefreshEvents(to); };
        var eventsPanel = new Grid(); eventsPanel.ColumnDefinitions.Add(new()); eventsPanel.ColumnDefinitions.Add(new() { Width = new(240) }); eventsPanel.Children.Add(eventList);
        eventEditor = new StackPanel { Margin = new(16, 0, 0, 0) }; var eventScroll = new ScrollViewer { Content = eventEditor }; Grid.SetColumn(eventScroll, 1); eventsPanel.Children.Add(eventScroll); Grid.SetRow(eventsPanel, 1); editor.Children.Add(eventsPanel); eventsPanel.IsEnabled = !isRecording; UpdateEventEditor();
        var footer = new StackPanel { Margin = new(0, 14, 0, 0) };
        footer.Children.Add(Row(Btn("Insert", () => { Snapshot(); var index = Math.Max(0, eventList.SelectedIndex); draft.Events.Insert(index, new() { Kind = EventKind.KeyDown, Code = 87 }); draft.Events.Insert(index + 1, new() { Kind = EventKind.KeyUp, Code = 87, DelayMs = 50 }); RefreshEvents(index); }), Btn("Duplicate", () => { if (eventList.SelectedItems.Count == 0) return; Snapshot(); var selected = eventList.SelectedItems.Cast<MacroEvent>().Select(ConfigStore.Clone).ToList(); draft.Events.InsertRange(eventList.SelectedIndex + 1, selected); RefreshEvents(); }), Btn("Remove", () => { if (eventList.SelectedItems.Count == 0) return; Snapshot(); foreach (var e in eventList.SelectedItems.Cast<MacroEvent>().ToArray()) draft.Events.Remove(e); RefreshEvents(); }), Btn("↑", () => MoveEvent(-1)), Btn("↓", () => MoveEvent(1))));
        footer.Children.Add(Row(Btn("Undo", () => { if (undo.Count == 0) return; redo.Push(ConfigStore.Clone(draft)); draft = undo.Pop(); draftDirty = true; Navigate(page); }), Btn("Redo", () => { if (redo.Count == 0) return; undo.Push(ConfigStore.Clone(draft)); draft = redo.Pop(); draftDirty = true; Navigate(page); }), Btn("Copy", () => { if (eventList.SelectedItems.Count > 0) Clipboard.SetText(JsonSerializer.Serialize(eventList.SelectedItems.Cast<MacroEvent>().ToList(), ConfigStore.Json)); }), Btn("Paste", () => { var events = JsonSerializer.Deserialize<List<MacroEvent>>(Clipboard.GetText(), ConfigStore.Json) ?? []; if (events.Count > 100000) throw new InvalidDataException("Too many events."); Snapshot(); draft.Events.InsertRange(Math.Max(0, eventList.SelectedIndex), events); RefreshEvents(); })));
        footer.Children.Add(Text("Playback is triggered from a mouse assignment. Choose Once, Count, Hold, Toggle, or repeat behavior in Customize. This keeps playback out of the editor.", 11, "Muted")); Grid.SetRow(footer, 2); editor.Children.Add(footer); footer.IsEnabled = !isRecording; return root;
    }
    private StackPanel? eventEditor;
    private void UpdateEventEditor()
    {
        if (eventEditor == null) return; eventEditor.Children.Clear();
        if (eventList?.SelectedItem is not MacroEvent e) { eventEditor.Children.Add(Text("Select an event to edit its action, timing, or group.", 14, "Muted")); return; }
        var kind = Choice(eventEditor, "Event type", Enum.GetValues<EventKind>(), e.Kind);
        var code = Field(eventEditor, "Code (virtual key / mouse 0–4)", e.Code.ToString());
        var delay = Field(eventEditor, "Delay before event (ms)", e.DelayMs.ToString());
        var x = Field(eventEditor, "X (move) / horizontal wheel 1", e.X.ToString()); var y = Field(eventEditor, "Y (move)", e.Y.ToString());
        var group = Field(eventEditor, "Group label", e.Group);
        eventEditor.Children.Add(Btn("Update selected events", () => { var value = new MacroEvent { Kind = (EventKind)kind.SelectedItem, Code = int.Parse(code.Text), DelayMs = int.Parse(delay.Text), X = int.Parse(x.Text), Y = int.Parse(y.Text), Group = group.Text }; if (value.DelayMs < 0 || value.DelayMs > 3600000) throw new InvalidDataException("Delay must be 0–3600000 ms."); Snapshot(); var indexes = eventList!.SelectedItems.Cast<MacroEvent>().Select(ev => draft.Events.IndexOf(ev)).ToArray(); foreach (var i in indexes) draft.Events[i] = ConfigStore.Clone(value); RefreshEvents(indexes.FirstOrDefault()); }));
        eventEditor.Children.Add(Btn("Group selected", () => { Snapshot(); foreach (var ev in eventList!.SelectedItems.Cast<MacroEvent>()) ev.Group = group.Text; RefreshEvents(); }));
    }
    private void ReadDraftFields() { if (macroName != null) draft.Name = macroName.Text.Trim(); if (macroSpeed != null) draft.Speed = double.Parse(macroSpeed.Text, System.Globalization.CultureInfo.InvariantCulture); }
    private void Snapshot() { ReadDraftFields(); undo.Push(ConfigStore.Clone(draft)); redo.Clear(); draftDirty = true; }
    private bool LeaveDraft() => !draftDirty || Confirm("Discard unsaved changes to this macro?");
    private void RefreshEvents(int selected = -1) { if (eventList != null) { eventList.ItemsSource = null; eventList.ItemsSource = draft.Events; if (selected >= 0 && selected < draft.Events.Count) { eventList.SelectedIndex = selected; eventList.ScrollIntoView(draft.Events[selected]); } } }
    private void MoveEvent(int delta) { var from = eventList?.SelectedIndex ?? -1; var to = from + delta; if (from < 0 || to < 0 || to >= draft.Events.Count) return; Snapshot(); (draft.Events[from], draft.Events[to]) = (draft.Events[to], draft.Events[from]); RefreshEvents(to); }
    private void SaveMacro() { if (isRecording) throw new InvalidOperationException("Stop recording before saving."); ReadDraftFields(); ConfigStore.ValidateMacro(draft); Commit(c => { c.Macros.RemoveAll(m => m.Id == draft.Id); c.Macros.Add(ConfigStore.Clone(draft)); }, "Macro saved."); draftDirty = false; Navigate(page); }
    private void ToggleRecord()
    {
        if (isRecording) { StopRecording(); return; }
        Snapshot(); runtime.StartRecording(recordMovement?.IsChecked == true, recordDelay?.IsChecked != false); isRecording = true; Notify("Recording. Use " + recordShortcut + " to stop."); Navigate("Macros");
    }
    private void StopRecording()
    {
        var events = runtime.StopRecording(); isRecording = false;
        // Clicking Stop is an editor action; remove its trailing left click if it occurred here.
        if (IsActive && events.LastOrDefault()?.Kind == EventKind.MouseUp && events.LastOrDefault()?.Code == 0 && events.Count >= 2 && events[^2].Kind == EventKind.MouseDown && events[^2].Code == 0) events.RemoveRange(events.Count - 2, 2);
        draft.Events.AddRange(events); draftDirty = true; Notify($"Recorded {events.Count} events. Review and save your macro."); Navigate("Macros");
    }
}



