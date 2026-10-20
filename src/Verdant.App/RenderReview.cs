using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Verdant.App;

public sealed partial class MainWindow
{
    // App-owned rendering test: no input injection or interaction with another app.
    internal async Task RenderReview(string folder)
    {
        Directory.CreateDirectory(folder);
        try
        {
            await Task.Delay(700); Tick();
            foreach (var size in new[] { (1340d, 880d, "wide"), (1040d, 700d, "compact") })
            {
                Width = size.Item1; Height = size.Item2;
                foreach (var view in new[] { "Customize", "Macros", "Profiles", "Settings", "Devices", "Dashboard" })
                {
                    Navigate(view); await Dispatcher.InvokeAsync(() => UpdateLayout(), DispatcherPriority.ContextIdle); await Task.Delay(250);
                    var visual = (FrameworkElement)Content;
                    var bmp = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32); bmp.Render(visual);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp)); using var stream = File.Create(Path.Combine(folder, view + "-" + size.Item3 + ".png")); png.Save(stream);
                }
            }
            File.WriteAllText(Path.Combine(folder, "render-result.txt"), "PASS: all six native views rendered at wide and compact sizes. Offscreen layout evidence only; not an interactive desktop test.");
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(folder, "render-result.txt"), "FAIL: " + ex); }
        finally { Exit(); }
    }
}
