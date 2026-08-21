using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NiiRMotion.Infrastructure;

namespace NiiRMotion.App;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, @"Local\NiiMotion.App.Singleton", out var firstInstance);
        if (!firstInstance) { Shutdown(); return; }
        base.OnStartup(e);
        NiiMotionPaths.Initialize();
        var boardLabScreenshotArg = e.Args.FirstOrDefault(x => x.StartsWith("--board-lab-screenshot=", StringComparison.OrdinalIgnoreCase));
        Window window = boardLabScreenshotArg is null ? new MainWindow() : new BoardLabWindow(); MainWindow = window; window.Show();
        if (boardLabScreenshotArg is not null)
        {
            var boardPath = boardLabScreenshotArg[(boardLabScreenshotArg.IndexOf('=') + 1)..];
            SaveScreenshotAndExit(window, boardPath); return;
        }
        var screenshotArg = e.Args.FirstOrDefault(x => x.StartsWith("--screenshot=", StringComparison.OrdinalIgnoreCase));
        if (screenshotArg is null) return;
        var path = screenshotArg[(screenshotArg.IndexOf('=') + 1)..];
        SaveScreenshotAndExit(window, path);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex(); _singleInstanceMutex?.Dispose(); _singleInstanceMutex = null;
        base.OnExit(e);
    }

    private void SaveScreenshotAndExit(Window window, string path)
    {
        window.Dispatcher.BeginInvoke(() =>
        {
            window.UpdateLayout();
            var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth)); var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = File.Create(path); encoder.Save(stream); window.Close(); Shutdown();
        }, DispatcherPriority.ApplicationIdle);
    }
}
