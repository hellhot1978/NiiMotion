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
    private bool _ownsSingleInstanceMutex;
    private ApplicationSafetyService? _safety;
    private PreviousRunStatus? _previousRun;
    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, @"Local\NiiMotion.App.Singleton", out var firstInstance);
        _ownsSingleInstanceMutex = firstInstance;
        if (!firstInstance) { _singleInstanceMutex.Dispose(); _singleInstanceMutex = null; Shutdown(); return; }
        base.OnStartup(e);
        NiiMotionPaths.Initialize();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        _safety = new ApplicationSafetyService(); _previousRun = _safety.Begin();
        _ = new DataMigrationService().Run();
        _ = new WorkspaceMaintenanceService().Run();
        var deviceCalibrationScreenshotArg = e.Args.FirstOrDefault(x => x.StartsWith("--device-calibration-screenshot=", StringComparison.OrdinalIgnoreCase));
        if (deviceCalibrationScreenshotArg is not null)
        {
            var raw = deviceCalibrationScreenshotArg[(deviceCalibrationScreenshotArg.IndexOf('=') + 1)..].Split('|', 2);
            if (!Enum.TryParse<NiiRMotion.Core.SensorFamily>(raw[0], true, out var sensor)) sensor = NiiRMotion.Core.SensorFamily.JoyCon;
            var calibrationWindow = new DeviceCalibrationWindow(sensor); MainWindow = calibrationWindow; calibrationWindow.Show(); SaveScreenshotAndExit(calibrationWindow, raw.Length > 1 ? raw[1] : Path.Combine(NiiMotionPaths.Logs, "device-calibration-preview.png")); return;
        }
        var hardwareSetupScreenshotArg = e.Args.FirstOrDefault(x => x.StartsWith("--hardware-setup-screenshot=", StringComparison.OrdinalIgnoreCase));
        if (hardwareSetupScreenshotArg is not null)
        {
            var hardwarePath = hardwareSetupScreenshotArg[(hardwareSetupScreenshotArg.IndexOf('=') + 1)..];
            var previewInventory = new NiiRMotion.Core.UserHardwareInventory(1, true, true, true, true, true, DateTimeOffset.UtcNow);
            var setupWindow = new HardwareSetupWindow(previewInventory); MainWindow = setupWindow; setupWindow.Show(); SaveScreenshotAndExit(setupWindow, hardwarePath); return;
        }
        var boardLabScreenshotArg = e.Args.FirstOrDefault(x => x.StartsWith("--board-lab-screenshot=", StringComparison.OrdinalIgnoreCase));
        Window window = boardLabScreenshotArg is null ? new MainWindow() : new BoardLabWindow(); MainWindow = window; window.Show();
        if (_previousRun?.WasUnclean == true && boardLabScreenshotArg is null)
            window.Dispatcher.BeginInvoke(() => MessageBox.Show(window, _previousRun.Message, "Güvenli kurtarma", MessageBoxButton.OK, MessageBoxImage.Information), DispatcherPriority.ApplicationIdle);
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
        _safety?.Complete(); _safety = null;
        if (_ownsSingleInstanceMutex) _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose(); _singleInstanceMutex = null; _ownsSingleInstanceMutex = false;
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        RecordCrash(e.Exception); ApplicationSafetyService.ForceSafeZero();
        MessageBox.Show("NiiMotion beklenmedik bir sorunla karşılaştı. Hareket çıkışı güvenle durduruldu. Sistem Tanılama bölümünden destek paketi oluşturabilirsin.", "NiiMotion güvenli duruş", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; Shutdown(1);
    }
    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e) { ApplicationSafetyService.ForceSafeZero(); if (e.ExceptionObject is Exception ex) RecordCrash(ex); }
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) { RecordCrash(e.Exception); e.SetObserved(); }
    private static void RecordCrash(Exception ex)
    {
        try { _ = NiiMotionEventLog.WriteAsync("application", "crash", "Uygulama beklenmedik biçimde kapandı; güvenli sıfır uygulandı.", new { type = ex.GetType().Name, ex.Message, stack = ex.StackTrace }); } catch { }
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
