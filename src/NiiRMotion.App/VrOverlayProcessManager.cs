using System.Diagnostics;
using System.IO;

namespace NiiRMotion.App;

internal sealed class VrOverlayProcessManager : IDisposable
{
    private Process? _ownedProcess;
    private string OverlayPath => Path.Combine(AppContext.BaseDirectory, "VrOverlay", "NiiMotion.VrOverlay.exe");

    public void EnsureRunning()
    {
        var steamVrRunning = Process.GetProcessesByName("vrserver").Length > 0 || Process.GetProcessesByName("vrmonitor").Length > 0;
        if (!steamVrRunning) { Stop(); return; }
        if (_ownedProcess is { HasExited: false }) return;
        _ownedProcess?.Dispose(); _ownedProcess = null;
        if (Process.GetProcessesByName("NiiMotion.VrOverlay").Any(IsThisPackage)) return;
        if (!File.Exists(OverlayPath)) return;
        try
        {
            _ownedProcess = Process.Start(new ProcessStartInfo(OverlayPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(OverlayPath)!
            });
        }
        catch { _ownedProcess = null; }
    }

    public void Stop()
    {
        foreach (var process in Process.GetProcessesByName("NiiMotion.VrOverlay"))
        {
            try { if (IsThisPackage(process) && !process.HasExited) { process.Kill(); process.WaitForExit(3000); } }
            catch { }
            finally { process.Dispose(); }
        }
        _ownedProcess?.Dispose(); _ownedProcess = null;
    }

    private bool IsThisPackage(Process process)
    {
        try { return string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? ""), Path.GetFullPath(OverlayPath), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    public void Dispose() => Stop();
}
