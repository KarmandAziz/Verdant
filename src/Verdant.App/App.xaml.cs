using System.Windows;
namespace Verdant.App;

public partial class App : Application
{
    internal static string? ReviewDirectory;
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length == 2 && e.Args[0] == "--hardware-rewrite-test") { base.OnStartup(e); var test = new HardwareRewriteTest(e.Args[1]); MainWindow = test; test.Show(); return; }
        if (e.Args.Length == 2 && e.Args[0] == "--hardware-coordinator-test") { Task.Run(()=>HardwareCoordinatorTests.Run(e.Args[1])).GetAwaiter().GetResult(); Shutdown(); return; }
        if (e.Args.Length == 2 && e.Args[0] == "--bounded-hardware-test") { base.OnStartup(e); var test = new BoundedHardwareTests(e.Args[1]); MainWindow = test; test.Show(); return; }
        if (e.Args.Length == 2 && e.Args[0] == "--bounded-mode-stop-test") { base.OnStartup(e); var test = new BoundedHardwareTests(e.Args[1],true); MainWindow = test; test.Show(); return; }
        if (e.Args.Length == 2 && e.Args[0] == "--bounded-hold-stop-test") { base.OnStartup(e); var test = new BoundedHardwareTests(e.Args[1],true,true); MainWindow = test; test.Show(); return; }
        if (e.Args.Length == 2 && e.Args[0] == "--bounded-toggle-test") { base.OnStartup(e); var test = new BoundedToggleTests(e.Args[1]); MainWindow = test; test.Show(); return; }
        if (e.Args.Length == 2 && e.Args[0] == "--hardware-runtime-test") { base.OnStartup(e); var test = new HardwareRuntimeTests(e.Args[1]); MainWindow = test; test.Show(); return; }
        if (e.Args.Length == 2 && e.Args[0] == "--hardware-test") { File.WriteAllText(e.Args[1], "DISABLED: onboard toggle cancellation failed validation. No hardware playback was armed."); Shutdown(1); return; }
        if (e.Args.Length == 2 && e.Args[0] == "--keyboard-test") { base.OnStartup(e); var test = new KeyboardTests(e.Args[1]); MainWindow = test; test.Show(); return; }
        if (e.Args.Length == 2 && e.Args[0] == "--physical-mouse-test") { base.OnStartup(e); var test = new PhysicalMouseTest(e.Args[1]); MainWindow = test; test.Show(); return; }
        if (e.Args.Length == 2 && e.Args[0] == "--device-report") { File.WriteAllLines(e.Args[1], MouseDevice.Detect().Select(d => d.Name + " | " + d.Path)); Shutdown(); return; }
        if (e.Args.Length == 2 && e.Args[0] == "--mouse-test") { base.OnStartup(e); var test = new MouseTests(e.Args[1]); MainWindow = test; test.Show(); return; }
        if (e.Args.Length == 2 && e.Args[0] == "--render-review") { ReviewDirectory = Path.Combine(Path.GetFullPath(e.Args[1]), "review-data"); base.OnStartup(e); var render = new MainWindow { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000 }; MainWindow = render; render.Loaded += async (_, _) => await render.RenderReview(Path.GetFullPath(e.Args[1])); render.Show(); return; }
        if (e.Args.Length == 2 && e.Args[0] == "--guardian") { WindowsSink.Guardian(int.Parse(e.Args[1])); Shutdown(); return; }
        if (e.Args.Length == 1 && e.Args[0] == "--guardian-probe") { using var sink = new WindowsSink(); sink.Down(new(true, 135)); Thread.Sleep(30000); Shutdown(); return; }
        if (e.Args.Length == 2 && e.Args[0] == "--integration-test") { base.OnStartup(e); var test = new NativeTests(e.Args[1]); MainWindow = test; test.Show(); return; }
        if (e.Args.Length == 2 && e.Args[0] == "--review") ReviewDirectory = Path.GetFullPath(e.Args[1]);
        instance = new Mutex(true, "Local\\Verdant-Mouse-Manager" + (ReviewDirectory != null ? "-Review" : ""), out var created);
        if (!created) { MessageBox.Show("Verdant is already running. Open it from the system tray.", "Verdant"); Shutdown(); return; }
        base.OnStartup(e);
        MainWindow w;
        try { w = new MainWindow(); } catch (Exception ex) { MessageBox.Show("Verdant could not start: " + ex.Message, "Verdant", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); return; }
        MainWindow = w;
        DispatcherUnhandledException += (_, args) => { w.Emergency(); try { File.AppendAllText(Path.Combine(MainWindow is MainWindow m ? m.DataDirectory : Path.GetTempPath(), "errors.log"), DateTimeOffset.Now + " " + args.Exception + Environment.NewLine); } catch { } };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => w.Emergency();
        w.Show(); if (e.Args.Contains("--minimized") || w.Config.StartMinimized) w.Hide();
    }
}
