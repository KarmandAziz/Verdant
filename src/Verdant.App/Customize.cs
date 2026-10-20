using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Verdant.App;

public sealed partial class MainWindow
{
    private UIElement Customize()
    {
        var left = new Grid { Margin = new(0, 0, 20, 0) }; left.RowDefinitions.Add(new() { Height = GridLength.Auto }); left.RowDefinitions.Add(new()); left.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var device = devices.FirstOrDefault(d => d.Path == selectedDevice);
        var summary = Stack(Text(device?.Name ?? "Standard mouse", 23), Text("GLOBAL BINDINGS  /  GENERIC ILLUSTRATION", 10, "Muted"));
        if (devices.Count > 1) { var picker = Choice(summary, "Connected mouse", devices, device); picker.SelectionChanged += (_, _) => { if (picker.SelectedItem is MouseDevice d) { selectedDevice = d.Path; Navigate(page); } }; }
        var controls = Row(Btn(sideView ? "Top view" : "Side view", () => { sideView = !sideView; Navigate(page); }), Btn(editLayer ? "Layer bindings" : "Standard bindings", () => { editLayer = !editLayer; Navigate(page); })); summary.Children.Add(controls); left.Children.Add(summary);
        var scene = MouseScene(); ((FrameworkElement)scene).Height=260; left.RowDefinitions[1].Height=GridLength.Auto; Grid.SetRow(scene, 1); left.Children.Add(scene);
        var list = new WrapPanel { Margin = new(0, 14, 0, 0) };
        foreach (var button in Enum.GetValues<ButtonId>())
        {
            var id = button; var binding = (editLayer ? Editing.Layer : Editing.Bindings).GetValueOrDefault(id) ?? new();
            var b = Btn($"{Friendly(id)}\n{binding.Label}", () => { selectedButton = id; Navigate(page); }); b.FontSize = 11; b.MinWidth = 120;
            b.Foreground = B(id == selectedButton ? "Accent" : binding.Kind == ActionKind.Default ? "Muted" : "Ink"); b.BorderBrush = B(id == selectedButton ? "Accent" : "Line");
            list.Children.Add(b);
        }
        var lower = Stack(list, Text("Assignments apply to all standard mice, outside this editor. Extra vendor buttons require model-specific support.", 11, "Muted")); Grid.SetRow(lower, 2); left.Children.Add(lower);
        return Split(new ScrollViewer { Content=left }, new ScrollViewer { Content = Card(BindingEditor()) }, 320);
    }
    private CheckBox ToggleBox(ButtonId id)
    {
        var bindings=editLayer?Editing.Layer:Editing.Bindings;
        var effective=bindings.GetValueOrDefault(id)??(editLayer?Editing.Bindings.GetValueOrDefault(id):null)??new Binding();
        var toggle=new CheckBox { Content="Toggle", Style=(Style)FindResource("HoldToggle"), IsChecked=effective.Mode==PlayMode.Toggle && effective.Kind is ActionKind.Mouse or ActionKind.Keyboard,IsEnabled=QuickToggle.CanHold(effective),Margin=new(0),ToolTip=QuickToggle.CanHold(effective)?"Press once to hold this action. Press again to release. Changes save immediately.":"Choose a mouse or keyboard action to hold. Scrolling and action sequences do not have a held state." };
        System.Windows.Automation.AutomationProperties.SetName(toggle,"Toggle "+Friendly(id));
        toggle.Click+=(_,_)=>Guard(()=> { var profileId=Editing.Id; var on=toggle.IsChecked==true; Commit(c=> { var p=c.Profiles.First(p=>p.Id==profileId); QuickToggle.Set(editLayer?p.Layer:p.Bindings,id,on,editLayer?p.Bindings.GetValueOrDefault(id):null); },Friendly(id)+(on?": toggle on — press once to hold, again to release.":": toggle off — previous behavior restored.")); selectedButton=id; Navigate(page); });
        return toggle;
    }
    private static string Friendly(ButtonId id) => id switch { ButtonId.Left => "Left click", ButtonId.Right => "Right click", ButtonId.Middle => "Wheel click", ButtonId.Back => "Button 4 · Back", ButtonId.Forward => "Button 5 · Forward", _ => id.ToString().Replace("Wheel", "Scroll ") };
    private UIElement MouseScene()
    {
        var c = new Canvas { Width = 500, Height = 390, ClipToBounds = true };
        void Path(string data, Brush fill, Brush? stroke = null, double width = 1) { c.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse(data), Fill = fill, Stroke = stroke, StrokeThickness = width }); }
        var shell = new LinearGradientBrush(Color.FromRgb(66, 77, 71), Color.FromRgb(24, 30, 27), new Point(0, 0), new Point(1, 1));
        var rim = new SolidColorBrush(Color.FromRgb(85, 102, 91));
        if (!sideView)
        {
            Path("M 250 25 C 175 25 144 68 142 145 L 134 261 C 126 337 177 372 250 372 C 323 372 374 337 366 261 L 358 145 C 356 68 325 25 250 25 Z", shell, rim, 2);
            Path("M 250 29 L 250 167 M 147 155 C 181 174 215 177 250 174 C 290 177 322 174 354 155", Brushes.Transparent, rim, 1.5);
            Path("M 148 184 C 139 261 149 322 182 342 M 352 184 C 361 261 351 322 318 342", Brushes.Transparent, B("Line"), 3);
            var wheel = new Border { Width = 26, Height = 62, CornerRadius = new(10), Background = B("Canvas"), BorderBrush = rim, BorderThickness = new(2) }; Canvas.SetLeft(wheel, 237); Canvas.SetTop(wheel, 76); c.Children.Add(wheel);
            for (int i = 0; i < 7; i++) Path($"M 242 {84 + i * 7} L 258 {84 + i * 7}", Brushes.Transparent, rim);
            Path("M 230 286 L 250 314 L 270 286 M 241 286 L 250 300 L 259 286", Brushes.Transparent, B("Accent"), 2);
        }
        else
        {
            Path("M 64 263 C 82 196 152 109 236 112 C 327 114 402 191 440 266 L 426 295 L 80 295 Z", shell, rim, 2);
            Path("M 78 266 C 184 285 343 284 435 266 M 117 221 C 210 238 290 239 350 250", Brushes.Transparent, rim, 2);
            Path("M 134 215 L 183 205 L 196 224 L 145 234 Z", B("Canvas"), rim);
            Path("M 196 203 L 244 200 L 255 219 L 208 224 Z", B("Canvas"), rim);
        }
        var positions = sideView ? new Dictionary<ButtonId, (double, double)> { [ButtonId.Back] = (145, 213), [ButtonId.Forward] = (211, 203), [ButtonId.Left] = (349, 178), [ButtonId.Middle] = (314, 143) } : new Dictionary<ButtonId, (double, double)> { [ButtonId.Left] = (188, 91), [ButtonId.Right] = (292, 91), [ButtonId.Middle] = (250, 108), [ButtonId.Back] = (142, 229), [ButtonId.Forward] = (147, 184) };
        foreach (var (id, point) in positions)
        {
            var pos = !sideView && Config.Hotspots.TryGetValue(id, out var xy) && xy.Length == 2 ? (xy[0], xy[1]) : point;
            var binding = (editLayer ? Editing.Layer : Editing.Bindings).GetValueOrDefault(id) ?? new();
            var b = Btn(((int)id + 1).ToString(), () => { selectedButton = id; Navigate(page); }); b.Width = 34; b.Height = 34; b.Padding = new(0); b.Margin = new(0); b.ToolTip = Friendly(id) + " · " + binding.Label;
            b.Background = B(id == selectedButton ? "Accent" : "Raised"); b.Foreground = id == selectedButton ? B("Canvas") : B(binding.Kind == ActionKind.Default ? "Muted" : "Accent");
            if (binding.Kind == ActionKind.Disabled) b.Content = "×";
            System.Windows.Automation.AutomationProperties.SetName(b, Friendly(id) + ", " + binding.Label);
            Canvas.SetLeft(b, pos.Item1 - 17); Canvas.SetTop(b, pos.Item2 - 17); c.Children.Add(b);
        }
        return new Viewbox { Child = c, Stretch = Stretch.Uniform, MaxHeight = 430 };
    }
    private StackPanel BindingEditor()
    {
        var current = (editLayer ? Editing.Layer : Editing.Bindings).GetValueOrDefault(selectedButton) ?? (editLayer ? Editing.Bindings.GetValueOrDefault(selectedButton) : null) ?? new Binding();
        var panel = Stack(Text("BUTTON " + ((int)selectedButton + 1), 10, "Accent"), Text(Friendly(selectedButton), 23), Text($"{Editing.Name} · {(editLayer ? "Layer" : "Standard")}", 12, "Muted"));
        if (selectedButton <= ButtonId.Forward)
        {
            var hint = Text("Press once to hold. Press again to release.", 11, "Muted");
            hint.TextWrapping = TextWrapping.Wrap; hint.Margin = new(28, 6, 0, 0);
            panel.Children.Add(new Border { Background=B("Raised"), BorderBrush=B("Line"), BorderThickness=new(1), CornerRadius=new(7), Padding=new(12), Margin=new(0,8,0,18), Child=Stack(ToggleBox(selectedButton),hint) });
        }
        if (current.Kind is ActionKind.Mouse or ActionKind.Keyboard && current.Mode == PlayMode.Toggle)
        {
            var extra = new StackPanel();
            var enabled = new CheckBox { Content="Repeat an extra key", Style=(Style)FindResource("HoldToggle"), IsChecked=current.ExtraKeyEnabled, Margin=new(0,8,0,8) };
            extra.Children.Add(enabled);
            extra.Children.Add(Text("Runs while this toggle is on. Stops with the next click.",11,"Muted"));
            var extraKeys = Field(extra, "Key or shortcut", current.ExtraKeys.Count > 0 ? string.Join("+",current.ExtraKeys.Select(KeyNames.Name)) : "F");
            var extraInterval = Field(extra, "Tap every (ms · 100–60000)", current.ExtraIntervalMs.ToString());
            var extraPress = Field(extra, "Press length (ms · 10–1000)", current.ExtraPressMs.ToString());
            var compatibility = new CheckBox { Content="Compatibility input", Style=(Style)FindResource("HoldToggle"), IsChecked=current.ExtraCompatibilityInput, Margin=new(0,8,0,8), ToolTip="Try an alternate Windows key method if an app ignores the standard input. Applies only to this extra key." };
            extra.Children.Add(compatibility);
            var onMouse = new CheckBox { Content="Use mouse hardware", Style=(Style)FindResource("HoldToggle"), IsChecked=current.ExtraOnDevice,
                IsEnabled=selectedButton==ButtonId.Forward && current.Kind==ActionKind.Mouse && current.Mouse==ButtonId.Forward,
                Margin=new(0,8,0,8),ToolTip="USB Basilisk V3 only. The mouse sends the extra key; Verdant keeps control of the toggle and stops playback." };
            compatibility.IsEnabled=onMouse.IsChecked!=true;
            onMouse.Checked+=(_,_)=>compatibility.IsEnabled=false;
            onMouse.Unchecked+=(_,_)=>compatibility.IsEnabled=true;
            extra.Children.Add(onMouse);
            extra.Children.Add(Btn("Save extra key", () =>
            {
                var updated = ConfigStore.Clone(current);
                updated.ExtraKeyEnabled = enabled.IsChecked == true;
                updated.ExtraKeys = KeyNames.Parse(extraKeys.Text);
                updated.ExtraIntervalMs = int.Parse(extraInterval.Text);
                updated.ExtraPressMs = int.Parse(extraPress.Text);
                updated.ExtraCompatibilityInput = compatibility.IsChecked == true;
                updated.ExtraOnDevice = onMouse.IsEnabled && onMouse.IsChecked == true;
                // This is now an explicit assignment; switching Toggle off must retain its settings.
                updated.ToggleRestoresInheritance = false;
                var id = Editing.Id;
                Commit(c => { var p = c.Profiles.First(p => p.Id == id); (editLayer ? p.Layer : p.Bindings)[selectedButton] = updated; }, "Extra key saved. It starts on the next toggle."); Navigate(page);
            }, true));
            panel.Children.Add(new Expander { Header="Extra key" + (current.ExtraKeyEnabled ? " · " + string.Join("+",current.ExtraKeys.Select(KeyNames.Name)) : ""), IsExpanded=current.ExtraKeyEnabled, Content=extra, Foreground=B("Ink"), Margin=new(0,0,0,18) });
        }
        var kind = Choice(panel, "Assignment", Enum.GetValues<ActionKind>(), current.Kind);
        var fields = new StackPanel(); panel.Children.Add(fields);
        void Refresh()
        {
            fields.Children.Clear(); var k = (ActionKind)kind.SelectedItem;
            TextBox? keys = null, target = null, interval = null, count = null; ComboBox? choice = null, mode = null;
            if (k == ActionKind.Keyboard) { keys = Field(fields, "Key or shortcut", current.Keys.Count > 0 ? string.Join("+", current.Keys.Select(KeyNames.Name)) : "W"); fields.Children.Add(Text("Examples: W, Ctrl+Shift+S, VolumeUp, Win+D. Use Capture for punctuation and other keys.", 11, "Muted")); bool capturing = false; var capture = Btn("Capture shortcut", () => { capturing = true; keys.Focus(); keys.SelectAll(); Notify("Press the shortcut to capture."); }); fields.Children.Add(capture); keys.PreviewKeyDown += (_, e) => { if (!capturing) return; var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key; var mods = System.Windows.Input.Keyboard.Modifiers; if (key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt) return; keys.Text = (mods.HasFlag(System.Windows.Input.ModifierKeys.Control) ? "Ctrl+" : "") + (mods.HasFlag(System.Windows.Input.ModifierKeys.Shift) ? "Shift+" : "") + (mods.HasFlag(System.Windows.Input.ModifierKeys.Alt) ? "Alt+" : "") + KeyNames.Name(System.Windows.Input.KeyInterop.VirtualKeyFromKey(key)); capturing = false; e.Handled = true; }; }
            if (k == ActionKind.Mouse) choice = Choice(fields, "Mouse output", Enum.GetValues<ButtonId>().Take(5), current.Mouse);
            if (k == ActionKind.Macro) choice = Choice(fields, "Macro", Config.Macros, Config.Macros.FirstOrDefault(m => m.Id == current.Target));
            if (k == ActionKind.Profile) choice = Choice(fields, "Target profile", Config.Profiles, Config.Profiles.FirstOrDefault(p => p.Id == current.Target));
            if (k is ActionKind.Url or ActionKind.Launch)
            {
                target = Field(fields, k == ActionKind.Url ? "Web address" : "File or folder path", current.Target);
                if (k == ActionKind.Launch) fields.Children.Add(Btn("Browse file…", () => { var path = PickOpen("All files|*.*"); if (path != null) target.Text = path; }));
            }
            if (k is ActionKind.Keyboard or ActionKind.Mouse or ActionKind.Macro or ActionKind.Profile or ActionKind.Layer)
            {
                var modes = k is ActionKind.Profile or ActionKind.Layer ? new[] { PlayMode.Once, PlayMode.Hold } : Enum.GetValues<PlayMode>();
                if (selectedButton >= ButtonId.WheelUp) modes = modes.Where(m => m is not (PlayMode.Hold or PlayMode.RepeatWhileHeld)).ToArray();
                mode = Choice(fields, "Behavior", modes, current.Mode);
                fields.Children.Add(Text(k == ActionKind.Macro ? "Toggle repeats the macro until the next press. Hold repeats until release." : "Toggle holds the output until the next press. Hold follows the physical button. Repeat modes tap the output.", 11, "Muted"));
                if (k is ActionKind.Keyboard or ActionKind.Mouse or ActionKind.Macro) { interval = Field(fields, "Repeat gap (ms · 10–60000)", current.IntervalMs.ToString()); count = Field(fields, "Count (for Count behavior)", current.Count.ToString()); }
            }
            if (k == ActionKind.Layer) fields.Children.Add(Text("Activates this profile's alternate bindings. Unassigned layer buttons fall back to Standard.", 12, "Muted"));
            if (k == ActionKind.Disabled) fields.Children.Add(Text("The standard button event will be intercepted without producing output.", 12, "Muted"));
            ComboBox? releaseChoice = null;
            if (selectedButton < ButtonId.WheelUp && k == ActionKind.Macro) { var options = new List<Macro> { new() { Id = "", Name = "No release sequence" } }; options.AddRange(Config.Macros); releaseChoice = Choice(fields, "On physical release", options, options.FirstOrDefault(m => m.Id == current.ReleaseMacroId)); }
            fields.Children.Add(Btn("Apply assignment", () =>
            {
                var b = new Binding { Kind = k, Mode = mode?.SelectedItem is PlayMode m ? m : PlayMode.Once, Keys = keys != null ? KeyNames.Parse(keys.Text) : [], Target = choice?.SelectedItem is Macro ma ? ma.Id : choice?.SelectedItem is Profile pr ? pr.Id : target?.Text ?? "", Mouse = choice?.SelectedItem is ButtonId bi ? bi : ButtonId.Left, IntervalMs = interval != null ? int.Parse(interval.Text) : 100, Count = count != null ? int.Parse(count.Text) : 1 };
                b.ReleaseMacroId = releaseChoice?.SelectedItem is Macro release ? release.Id : "";
                b.ExtraKeyEnabled = current.ExtraKeyEnabled; b.ExtraKeys = [..current.ExtraKeys]; b.ExtraIntervalMs = current.ExtraIntervalMs;
                b.ExtraPressMs = current.ExtraPressMs;
                b.ExtraCompatibilityInput = current.ExtraCompatibilityInput;
                b.ExtraOnDevice = current.ExtraOnDevice && selectedButton==ButtonId.Forward && b.Kind==ActionKind.Mouse && b.Mouse==ButtonId.Forward && b.Mode==PlayMode.Toggle;
                var id = Editing.Id;
                Commit(c => { var p = c.Profiles.First(p => p.Id == id); (editLayer ? p.Layer : p.Bindings)[selectedButton] = b; }, "Assignment saved. Active outputs were released."); Navigate(page);
            }, true));
            fields.Children.Add(Btn("Restore default", () => { var id = Editing.Id; Commit(c => { var p = c.Profiles.First(p => p.Id == id); (editLayer ? p.Layer : p.Bindings).Remove(selectedButton); }, "Default restored."); Navigate(page); }));
            if (!sideView && selectedButton < ButtonId.WheelUp)
            {
                var position = new StackPanel(); var xy = Config.Hotspots.GetValueOrDefault(selectedButton) ?? new double[] { 250, 200 }; var x = Field(position, "Horizontal position (0–500)", xy[0].ToString()); var y = Field(position, "Vertical position (0–390)", xy[1].ToString());
                position.Children.Add(Btn("Save hotspot position", () => { var px = double.Parse(x.Text); var py = double.Parse(y.Text); if (!double.IsFinite(px) || !double.IsFinite(py) || px < 17 || px > 483 || py < 17 || py > 373) throw new InvalidDataException("Keep the hotspot inside the illustration."); Commit(c => c.Hotspots[selectedButton] = [px, py], "Hotspot moved."); Navigate(page); }));
                fields.Children.Add(new Expander { Header = "Illustration position", Content = position, Foreground = B("Muted"), Margin = new(0, 12, 0, 0) });
            }
        }
        kind.SelectionChanged += (_, _) => Refresh(); Refresh(); return panel;
    }
}

