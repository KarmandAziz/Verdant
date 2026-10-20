using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Verdant.App;

public sealed partial class MainWindow : Window
{
    public readonly string DataDirectory = App.ReviewDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Verdant");
    private string ConfigPath => Path.Combine(DataDirectory, "configuration.json");
    public Configuration Config { get; private set; } = new();
    private readonly Runtime runtime;
    private readonly Forms.NotifyIcon tray;
    private readonly ContentControl body = new();
    private readonly TextBlock status = new(), activity = new(), heading = new(), subheading = new();
    private readonly ComboBox profilePicker = new() { Width = 190, Margin = new(0, 0, 12, 0) };
    private readonly Dictionary<string, Button> navigation = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private List<MouseDevice> devices = [];
    private string page = "Customize", editingProfile = "", selectedDevice = "";
    private ButtonId selectedButton = ButtonId.Forward;
    private bool editLayer, sideView, exiting, refreshing, isRecording;
    private nint handle, lastForeground;
    private Button pauseButton = new();
    private string recovery = "";
    private string recordShortcut = "F8";
    private bool emergencyAvailable, recordAvailable;
    private readonly List<string> log = [];
    private readonly Queue<string> inputDiagnostics = new();
    private string lastInputDiagnostic = "";
    public MainWindow()
    {
        Style = (Style)FindResource(typeof(Window));
        Title = "Verdant · Mouse & macro manager"; Width = 1340; Height = 880; MinWidth = 1040; MinHeight = 700; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/Verdant.ico"));
        Directory.CreateDirectory(DataDirectory);
        if (File.Exists(ConfigPath))
        {
            try { Config = ConfigStore.Read(ConfigPath); }
            catch (Exception e)
            {
                File.Copy(ConfigPath, ConfigPath + ".rejected-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true);
                try { Config = ConfigStore.Read(ConfigPath + ".bak"); recovery = "Recovered the last valid backup. "; } catch { recovery = "Loaded safe defaults. "; }
                recovery += "The unreadable configuration was preserved. " + e.Message;
            }
        }
        ConfigStore.Validate(Config); editingProfile = Config.DefaultProfileId;
        runtime = new(Config);
        // Editor clicks pass through. Release our held outputs first so a physical
        // release here cannot leave a toggle marked active with Windows reporting up.
        Activated += (_, _) => { if (runtime.Engine.ActiveCount > 0) { runtime.EditorFocused(); Notify("Held actions released while editing."); } };
        if (App.ReviewDirectory != null) { runtime.Pause(); Title += " [Review]"; }
        tray = new Forms.NotifyIcon { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application, Text = "Verdant mouse manager", Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWindow);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Verdant", null, (_, _) => Dispatcher.Invoke(ShowWindow));
        menu.Items.Add("Emergency stop", null, (_, _) => Dispatcher.Invoke(Emergency));
        menu.Items.Add("Resume bindings", null, (_, _) => Dispatcher.Invoke(() => { runtime.Resume(); Notify("Bindings resumed."); }));
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Exit)); tray.ContextMenuStrip = menu;
        BuildShell(); SourceInitialized += OnSource; Closing += (_, e) => { if (!exiting && Config.CloseToTray) { e.Cancel = true; Hide(); } else if (!exiting) { e.Cancel = true; Exit(); } };
        timer.Tick += (_, _) => Tick(); timer.Start();
        Navigate("Customize");
        if (recovery.Length > 0) { runtime.Pause(); Notify(recovery + " Bindings paused for review."); }
    }
    private Brush B(string key) => (Brush)FindResource(key);
    private TextBlock Text(string text, double size = 14, string color = "Ink") => new() { Text = text, FontSize = size, Foreground = B(color), Margin = new(0, 0, 0, 10) };
    private Button Btn(string text, Action action, bool primary = false)
    {
        var b = new Button { Content = text }; if (primary) b.Style = (Style)FindResource("Primary");
        System.Windows.Automation.AutomationProperties.SetName(b, text);
        b.Click += (_, _) => Guard(action); return b;
    }
    private void Guard(Action action) { try { action(); } catch (Exception e) { Notify(e.Message, true); } }
    private static StackPanel Stack(params UIElement[] children) { var s = new StackPanel(); foreach (var c in children) s.Children.Add(c); return s; }
    private static WrapPanel Row(params UIElement[] children) { var r = new WrapPanel(); foreach (var c in children) r.Children.Add(c); return r; }
    private Border Card(UIElement child, Thickness? margin = null) => new() { Background = B("Surface"), BorderBrush = B("Line"), BorderThickness = new(1), CornerRadius = new(12), Padding = new(22), Margin = margin ?? new(0, 0, 0, 16), Child = child };
    private TextBox Field(StackPanel parent, string label, string value = "")
    {
        var t = new TextBox { Text = value }; System.Windows.Automation.AutomationProperties.SetName(t, label);
        parent.Children.Add(new Label { Content = label, Target = t, Foreground = B("Muted"), Padding = new(0) }); parent.Children.Add(t); return t;
    }
    private ComboBox Choice<T>(StackPanel parent, string label, IEnumerable<T> values, T? selected = default)
    {
        var c = new ComboBox { ItemsSource = values.ToArray() }; System.Windows.Automation.AutomationProperties.SetName(c, label);
        parent.Children.Add(new Label { Content = label, Target = c, Foreground = B("Muted"), Padding = new(0) }); parent.Children.Add(c);
        if (selected != null) c.SelectedItem = selected; if (c.SelectedIndex < 0) c.SelectedIndex = 0; return c;
    }
    private Grid Split(UIElement left, UIElement right, double width = 330)
    {
        var g = new Grid(); g.ColumnDefinitions.Add(new()); g.ColumnDefinitions.Add(new() { Width = new GridLength(width) });
        g.Children.Add(left); Grid.SetColumn(right, 1); g.Children.Add(right); return g;
    }
    private void BuildShell()
    {
        var root = new Grid { Background = B("Canvas") }; root.ColumnDefinitions.Add(new() { Width = new(190) }); root.ColumnDefinitions.Add(new());
        var nav = new DockPanel { Margin = new(18, 24, 18, 20) };
        var brand = Stack(Text("◉  VERDANT", 21), Text("MOUSE & MACROS", 10, "Muted")); brand.Margin = new(0, 4, 0, 32); DockPanel.SetDock(brand, Dock.Top); nav.Children.Add(brand);
        var foot = Stack(Text("LOCAL BY DESIGN", 10, "Accent"), Text("No account. No cloud.\nYour setup stays here.", 12, "Muted")); DockPanel.SetDock(foot, Dock.Bottom); nav.Children.Add(foot);
        var links = new StackPanel();
        foreach (var name in new[] { "Dashboard", "Devices", "Customize", "Macros", "Profiles", "Settings" })
        { var n = name; var b = Btn(n, () => Navigate(n)); b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Margin = new(0, 0, 0, 8); b.Padding = new(16, 13, 16, 13); navigation[n] = b; links.Children.Add(b); }
        nav.Children.Add(links); root.Children.Add(new Border { Background = B("Surface"), BorderBrush = B("Line"), BorderThickness = new(0, 0, 1, 0), Child = nav });
        var main = new Grid { Margin = new(28, 24, 28, 16) }; main.RowDefinitions.Add(new() { Height = GridLength.Auto }); main.RowDefinitions.Add(new()); main.RowDefinitions.Add(new() { Height = GridLength.Auto }); Grid.SetColumn(main, 1); root.Children.Add(main);
        var top = new DockPanel { Margin = new(0, 0, 0, 22) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        profilePicker.SelectionChanged += (_, _) => { if (!refreshing && profilePicker.SelectedItem is Profile p) { runtime.SelectProfile(p.Id); editingProfile = p.Id; Navigate(page); } };
        actions.Children.Add(profilePicker);
        pauseButton = Btn("Pause bindings", () => { if (runtime.Enabled) Emergency(); else { runtime.Resume(); Notify("Bindings resumed."); } }); actions.Children.Add(pauseButton);
        DockPanel.SetDock(actions, Dock.Right); top.Children.Add(actions);
        heading.FontSize = 28; heading.FontWeight = FontWeights.SemiBold; subheading.Foreground = B("Muted"); subheading.Margin = new(0, 6, 0, 0); top.Children.Add(Stack(heading, subheading)); main.Children.Add(top);
        Grid.SetRow(body, 1); main.Children.Add(body);
        var bottom = new Grid { Margin = new(0, 14, 0, 0) }; bottom.ColumnDefinitions.Add(new()); bottom.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        status.Foreground = B("Muted"); status.FontSize = 12; status.Text = "Ready. Select a mouse button to make it yours."; status.MaxHeight = 54; bottom.Children.Add(status);
        activity.Foreground = B("Accent"); activity.FontSize = 12; activity.Margin = new(14, 0, 0, 0); Grid.SetColumn(activity, 1); bottom.Children.Add(activity); Grid.SetRow(bottom, 2); main.Children.Add(bottom); Content = root;
    }
    private void OnSource(object? sender, EventArgs e)
    {
        handle = new WindowInteropHelper(this).Handle; HwndSource.FromHwnd(handle)?.AddHook(WndProc);
        int dark = 1; Native.DwmSetWindowAttribute(handle, 20, ref dark, 4);
        var emergency = Native.RegisterHotKey(handle, 1, 0x4003, 0x13); emergencyAvailable = emergency; // Ctrl+Alt+Pause
        var record = Native.RegisterHotKey(handle, 2, 0x4000, 0x77);
        if (!record) { for (uint key = 0x78; key <= 0x7B && !record; key++) { record = Native.RegisterHotKey(handle, 2, 0x4000, key); if (record) { recordShortcut = "F" + (key - 0x6F); runtime.RecordingStopKey = (int)key; } } if (!record) recordShortcut = "Record button"; }
        recordAvailable = record;
        var cycle = Native.RegisterHotKey(handle, 3, 0x4003, 0x50);
        if (!emergency) { runtime.Pause(); Notify("Emergency shortcut Ctrl+Alt+Pause is unavailable. Bindings are paused; close the conflicting app and restart.", true); }
        else if (!record || !cycle) Notify($"Recording: {recordShortcut}. " + (!cycle ? "Ctrl+Alt+P is in use by another app; use the profile picker." : ""), true);
        else if (recordShortcut != "F8") Notify($"F8 is in use. Recording shortcut: {recordShortcut}.");
        Native.RegisterRawInputDevices([new() { Page = 1, Usage = 2, Flags = 0x2000, Target = handle }], 1, (uint)Marshal.SizeOf<Native.Registration>());
        RefreshDevices();
        Microsoft.Win32.SystemEvents.SessionSwitch += SessionSwitch;
        Microsoft.Win32.SystemEvents.PowerModeChanged += PowerChange;
    }
    private void SessionSwitch(object sender, SessionSwitchEventArgs e) { if (e.Reason == SessionSwitchReason.SessionLock) Dispatcher.BeginInvoke(Emergency); }
    private void PowerChange(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Suspend) Dispatcher.BeginInvoke(Emergency); }
    private nint WndProc(nint hwnd, int msg, nint w, nint l, ref bool handled)
    {
        if (msg == 0x312) { handled = true; if (w == 1) Emergency(); else if (w == 2 && page == "Macros") ToggleRecord(); else if (w == 3) { var index = Config.Profiles.FindIndex(p => p.Id == runtime.ActiveProfile); runtime.SelectProfile(Config.Profiles[(index + 1) % Config.Profiles.Count].Id); } }
        // Only an actual mouse removal releases held outputs. Unrelated device
        // broadcasts (audio, displays, etc.) must not cancel a mouse toggle.
        if (msg == 0xFE) { if (w == 2) runtime.DeviceChanged(); Dispatcher.BeginInvoke(RefreshDevices); }
        else if (msg == 0x219) Dispatcher.BeginInvoke(RefreshDevices);
        if (msg == 0x11) Emergency();
        return 0;
    }
    private void Tick()
    {
        var foreground = Native.GetForegroundWindow();
        if (foreground != lastForeground)
        {
            Native.GetWindowThreadProcessId(foreground, out var pid);
            if (pid != Environment.ProcessId && pid != 0)
            {
                lastForeground = foreground;
                try { using var process = Process.GetProcessById((int)pid); runtime.Foreground(process.MainModule?.FileName); } catch { /* Protected process: retain current profile. */ }
            }
        }
        activity.Text = isRecording ? $"● Recording · {runtime.RecordedCount} events · {recordShortcut} to stop" : $"{(runtime.Enabled ? "● Active" : "○ Paused")}  ·  {runtime.Engine.ActiveCount} running{(runtime.LayerActive ? " · Layer" : "")}";
        if ((!emergencyAvailable || App.ReviewDirectory != null) && runtime.Enabled && handle != 0) runtime.Pause();
        pauseButton.Content = runtime.Enabled ? "Pause bindings" : "Resume bindings";
        activity.ToolTip = runtime.Engine.ActiveSummary;
        if (!isRecording && runtime.Engine.ActiveCount > 0) activity.Text = runtime.Engine.ActiveSummary;
        var heldState = string.Join(", ", runtime.Engine.HeldTokens.Select(t =>
        {
            var vk = t.Keyboard ? t.Code : t.Code switch { 0 => 1, 1 => 2, 2 => 4, 3 => 5, 4 => 6, _ => 0 };
            return $"{(t.Keyboard ? "Key" : "Mouse")} {t.Code}: Windows {((Native.GetAsyncKeyState(vk) & 0x8000) != 0 ? "down" : "up/unavailable")}";
        }));
        activity.ToolTip = runtime.Engine.ActiveSummary + "\n" + heldState;
        var diagnostic = $"Enabled={runtime.Enabled}; {runtime.LastButtonEvent}; Active={runtime.Engine.ActiveSummary}; {heldState}; Error={runtime.LastError}";
        if (diagnostic != lastInputDiagnostic)
        {
            lastInputDiagnostic = diagnostic;
            inputDiagnostics.Enqueue($"{DateTime.Now:HH:mm:ss.fff} {diagnostic}");
            while (inputDiagnostics.Count > 100) inputDiagnostics.Dequeue();
            try { File.WriteAllLines(Path.Combine(DataDirectory, "input-diagnostics.log"), inputDiagnostics); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        activity.MaxWidth = 400; activity.TextTrimming = TextTrimming.CharacterEllipsis;
        if (runtime.LastError is { } error) { runtime.LastError = null; Notify(error, true); }
        refreshing = true;
        if (profilePicker.ItemsSource == null || profilePicker.Items.Count != Config.Profiles.Count || !ReferenceEquals(profilePicker.Items[0], Config.Profiles[0])) profilePicker.ItemsSource = Config.Profiles;
        profilePicker.SelectedItem = Config.Profiles.FirstOrDefault(p => p.Id == runtime.ActiveProfile); refreshing = false;
    }
    private void RefreshDevices()
    {
        Guard(() => devices = MouseDevice.Detect());
        if (!devices.Any(d => d.Path == selectedDevice)) selectedDevice = devices.FirstOrDefault()?.Path ?? "";
        if (page is "Devices" or "Customize") Navigate(page);
    }
    private void Notify(string message, bool error = false) { status.Text = message; status.Foreground = B(error ? "Accent" : "Muted"); log.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "  " + message); if (log.Count > 100) log.RemoveAt(100); }
    private void Commit(Action<Configuration> change, string message)
    {
        var next = ConfigStore.Clone(Config); change(next); ConfigStore.Save(ConfigPath, next); Config = next; runtime.Configure(next); Notify(message); Tick();
    }
    private Profile Editing => Config.Profiles.FirstOrDefault(p => p.Id == editingProfile) ?? Config.Profiles.First(p => p.Id == Config.DefaultProfileId);
    private void Navigate(string name)
    {
        if (isRecording && name != "Macros") { Notify("Stop recording with " + recordShortcut + " before leaving the macro editor."); return; }
        page = name; heading.Text = name;
        foreach (var (n, b) in navigation) { b.Background = B(n == name ? "Raised" : "Surface"); b.Foreground = B(n == name ? "Accent" : "Muted"); b.BorderThickness = new(n == name ? 1 : 0); }
        subheading.Text = name switch { "Customize" => "A familiar mouse. A setup that is yours.", "Macros" => "Record precisely. Edit freely. Play reliably.", "Profiles" => "The right controls for each application.", "Devices" => "Connected hardware and honest capabilities.", "Settings" => "Quiet in the background. Always in your control.", _ => "Your controls, at a glance." };
        body.Content = name switch { "Customize" => Customize(), "Devices" => Devices(), "Macros" => Macros(), "Profiles" => Profiles(), "Settings" => Settings(), _ => Dashboard() };
    }
    public void Emergency()
    {
        runtime.Pause(); if (isRecording) StopRecording();
        if (Dispatcher.CheckAccess()) Notify("Emergency stop: inputs released and bindings paused. Resume when ready.");
    }
    private void ShowWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void Exit()
    {
        if (exiting) return; exiting = true; timer.Stop(); Emergency();
        Microsoft.Win32.SystemEvents.SessionSwitch -= SessionSwitch; Microsoft.Win32.SystemEvents.PowerModeChanged -= PowerChange;
        for (int i = 1; i <= 3; i++) Native.UnregisterHotKey(handle, i);
        runtime.Dispose(); tray.Dispose(); System.Windows.Application.Current.Shutdown();
    }
    private UIElement Dashboard()
    {
        var p = Stack(Card(Stack(Text("Everything you need. Nothing following you.", 24), Text("Standard button remapping, held-key toggles, macros, and application profiles. All running on your PC.", 15, "Muted"), Row(Btn("Customize your mouse", () => Navigate("Customize"), true), Btn("Create a macro", () => Navigate("Macros"))))));
        p.Children.Add(Card(Stack(Text($"{devices.Count} connected mice    /    {Config.Profiles.Count} profiles    /    {Config.Macros.Count} macros", 22), Text("Keyboard emulation and virtual input interfaces are excluded.", 12, "Muted"))));
        p.Children.Add(Card(Stack(Text("Always within reach", 20), Text("Ctrl + Alt + Pause   Emergency stop and release all inputs\nF8   Start / stop recording while the Macros page is open\nCtrl + Alt + P   Next profile", 15), Text("Mappings are global across standard mice. Verdant's own editor is exempt. Elevated apps and some games may reject injected input.", 13, "Muted"))));
        p.Children.Add(Card(Stack(Text("Recent activity", 18), Text(log.Count == 0 ? "No changes yet." : string.Join("\n", log.Take(8)), 12, "Muted")))); return new ScrollViewer { Content = p };
    }
    private UIElement Devices()
    {
        var p = Stack(Row(Btn("Refresh devices", RefreshDevices)), Text("Connected mice", 20));
        if (devices.Count == 0) p.Children.Add(Card(Text("No confirmed mouse hardware found. Connect a supported mouse and refresh.")));
        foreach (var d in devices) { var device = d; p.Children.Add(Card(Stack(Text(d.Name, 21), Text(d.Path, 11, "Muted"), Text("Standard mouse interface · Generic illustration\nFive standard buttons and wheel events can be intercepted globally when emitted by the device.", 14, "Muted"), Btn("Customize standard buttons", () => { selectedDevice = device.Path; Navigate("Customize"); }, true)))); }
        p.Children.Add(Card(Stack(Text("What this connection supports", 20), Text("The list includes confirmed USB mouse hardware and PS/2 mice. Keyboard emulation, virtual devices and unclassified interfaces are omitted; some Bluetooth or nonstandard mice may not appear. Bindings still apply globally to standard mouse events. Side buttons must emit Back/Forward events.\n\nVendor-only buttons, DPI stages, lighting, polling rate, and onboard profiles are not configured by Verdant.", 14, "Muted")))); return new ScrollViewer { Content = p };
    }
    private UIElement Settings()
    {
        var p = new StackPanel();
        var general = Stack(Text("Background behavior", 21));
        void Check(string label, bool initial, Action<Configuration, bool> setter) { var c = new CheckBox { Content = label, IsChecked = initial }; c.Click += (_, _) => Guard(() => Commit(x => setter(x, c.IsChecked == true), "Settings saved.")); general.Children.Add(c); }
        Check("Start minimized", Config.StartMinimized, (c, b) => c.StartMinimized = b);
        Check("Close to the system tray", Config.CloseToTray, (c, b) => c.CloseToTray = b);
        Check("Automatically select profiles for foreground applications", Config.AutoSwitch, (c, b) => c.AutoSwitch = b);
        using var run = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
        var startup = new CheckBox { Content = "Start with Windows (administrator prompt)", IsChecked = run?.GetValue("Verdant") != null };
        startup.Click += (_, _) => Guard(() => { using var key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"); if (startup.IsChecked == true) key.SetValue("Verdant", StartupCommand()); else key.DeleteValue("Verdant", false); Notify("Windows startup preference updated."); }); general.Children.Add(startup);
        general.Children.Add(Text("Verdant always requests administrator access so it can work with elevated games. Windows may ask for approval when you launch it.", 12, "Muted")); p.Children.Add(Card(general));
        p.Children.Add(Card(Stack(Text("Safety & recovery", 21), Text("Ctrl + Alt + Pause stops all playback and pauses bindings. Profile changes, device changes, lock, suspend, and exit release owned inputs. A small guardian process releases tracked inputs if the main process terminates unexpectedly.\n\nA Windows crash, power loss, an unresponsive desktop, or an elevated target cannot be recovered reliably by an ordinary desktop app.", 14, "Muted"), Row(Btn("Release all inputs and pause", Emergency), Btn("Open configuration folder", () => Process.Start(new ProcessStartInfo(DataDirectory) { UseShellExecute = true }))))));
        p.Children.Add(Card(Stack(Text("Local storage", 21), Text(DataDirectory, 12, "Muted"), Text("Versioned JSON with validation, atomic replacement, and a previous-version backup. No network services, accounts, analytics, or automatic updates.", 14, "Muted"), Text("Verdant 0.1 · Windows x64 · .NET 10 / WPF", 12, "Muted")))); return new ScrollViewer { Content = p };
    }
    private string? PickOpen(string filter = "JSON files|*.json") { var d = new OpenFileDialog { Filter = filter }; return d.ShowDialog(this) == true ? d.FileName : null; }
    private static string StartupCommand()
    {
        // A direct requireAdministrator executable is blocked in the Run key.
        // An ordinary shell launcher requests consent; it does not bypass UAC.
        var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        var executable = Environment.ProcessPath!.Replace("'", "''");
        return $"\"{shell}\" -NoProfile -NonInteractive -WindowStyle Hidden -Command \"Start-Process -FilePath '{executable}' -ArgumentList '--minimized' -Verb RunAs -WindowStyle Hidden\"";
    }
    private string? PickSave(string name) { var d = new SaveFileDialog { Filter = "JSON files|*.json", FileName = name, DefaultExt = ".json" }; return d.ShowDialog(this) == true ? d.FileName : null; }
    private bool Confirm(string text) => MessageBox.Show(this, text, "Verdant", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}


