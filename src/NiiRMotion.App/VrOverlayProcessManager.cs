using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace NiiRMotion.App;

internal sealed class VrOverlayProcessManager : IDisposable
{
    private const string ShowEventName = @"Local\NiiMotion.VrOverlay.Show";
    private const uint EventModifyState = 0x0002;
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

    public bool ShowInHeadset()
    {
        EnsureRunning();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var handle = OpenEvent(EventModifyState, false, ShowEventName);
            if (handle != IntPtr.Zero)
            {
                try { return SetEvent(handle); }
                finally { CloseHandle(handle); }
            }
            Thread.Sleep(100);
        }
        return false;
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

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenEvent(uint desiredAccess, bool inheritHandle, string name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetEvent(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
}
