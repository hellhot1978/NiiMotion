using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NiiRMotion.Infrastructure;

public enum SystemMode { Original, NiiMotion }

public sealed class SystemModeService
{
    private static string DriverPath
    {
        get
        {
            var installed = Path.Combine(AppContext.BaseDirectory, "OpenVRDriver");
            return installed;
        }
    }
    private static string VrPathReg => Path.Combine(SteamInstallLocator.FindSteamVr() ?? @"C:\Program Files (x86)\Steam\steamapps\common\SteamVR", "bin", "win64", "vrpathreg.exe");
    private const string SteamVrSettings = @"C:\Program Files (x86)\Steam\config\steamvr.vrsettings";
    private static string AlyxAutoExec => Path.Combine(SteamInstallLocator.FindGame("546560") ?? "", "game", "hlvr", "cfg", "autoexec.cfg");
    private static string AlyxTouchBindings => Path.Combine(SteamInstallLocator.FindGame("546560") ?? "", "game", "hlvr", "cfg", "bindings_touch.json");
    private static string AlyxTouchBindingsBackup => Path.Combine(NiiMotionPaths.Config, "bindings_touch.original.json");
    private static string Arizona2TouchBindings => Path.Combine(SteamInstallLocator.FindGame("1540210") ?? "", "ArizonaSunshine2_Data", "StreamingAssets", "SteamVR", "bindings_oculus_touch.json");
    private static string Arizona2TouchBindingsBackup => Path.Combine(NiiMotionPaths.Config, "arizona2_bindings_oculus_touch.original.json");
    private static string SteamExe => SteamInstallLocator.FindSteamExe() ?? @"C:\Program Files (x86)\Steam\steam.exe";

    public SystemMode CurrentMode => IsNiiMotionDriverRegistered() ? SystemMode.NiiMotion : SystemMode.Original;

    public async Task ApplyAsync(SystemMode mode, bool launchSteamVr = false, CancellationToken cancellationToken = default)
    {
        StopVrProcesses();
        await Task.Delay(900, cancellationToken);

        RunVrPathReg(mode == SystemMode.NiiMotion ? "adddriver" : "removedriver", DriverPath);
        RemoveBodyWalkSettings();
        EnsureGameOverrides(mode);
        if (OperatingSystem.IsWindows()) { var game = new GameSelectionStore().Load(); new OpenXrLayerRegistrationService().Apply(mode == SystemMode.NiiMotion && new OpenXrGameAdapterStore().Find(game) is not null); }
        SaveMode(mode);

        if (launchSteamVr && File.Exists(SteamExe))
            Process.Start(new ProcessStartInfo(SteamExe, "-applaunch 250820") { UseShellExecute = true });
    }

    public void EnsureGameOverrides(SystemMode mode)
    {
        var selected = new GameSelectionStore().Load();
        var alyx = mode == SystemMode.NiiMotion && selected == "half-life-alyx";
        var arizona = mode == SystemMode.NiiMotion && selected == "arizona-sunshine-2";
        SetAlyxOverrides(alyx);
        SetAlyxControllerBindingOverride(alyx);
        SetArizona2ControllerBindingOverride(arizona);
        if (OperatingSystem.IsWindows()) new OpenXrLayerRegistrationService().Apply(mode == SystemMode.NiiMotion && new OpenXrGameAdapterStore().Find(selected) is not null);
    }

    public async Task StopSteamVrAsync(CancellationToken cancellationToken = default)
    {
        StopVrProcesses();
        await Task.Delay(900, cancellationToken);
    }

    private static bool IsNiiMotionDriverRegistered()
    {
        if (!File.Exists(VrPathReg)) return false;
        try
        {
            var info = new ProcessStartInfo(VrPathReg, "show") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            using var process = Process.Start(info);
            var output = process?.StandardOutput.ReadToEnd() ?? "";
            process?.WaitForExit(3000);
            return output.Contains(DriverPath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static void RunVrPathReg(string verb, string path)
    {
        if (!File.Exists(VrPathReg)) throw new FileNotFoundException("SteamVR sürücü yöneticisi bulunamadı.", VrPathReg);
        using var process = Process.Start(new ProcessStartInfo(VrPathReg, $"{verb} \"{path}\"")
        {
            UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true
        }) ?? throw new InvalidOperationException("SteamVR sürücü yöneticisi başlatılamadı.");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(5000);
        if (process.ExitCode != 0) throw new InvalidOperationException($"SteamVR sürücü geçişi başarısız: {error.Trim()}");
    }

    private static void RemoveBodyWalkSettings()
    {
        if (!File.Exists(SteamVrSettings)) return;
        var root = JsonNode.Parse(File.ReadAllText(SteamVrSettings))?.AsObject() ?? new JsonObject();
        if (root.Remove("driver_bodywalkvr_virtual"))
            AtomicWrite(SteamVrSettings, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void SetAlyxOverrides(bool enabled)
    {
        var lines = File.Exists(AlyxAutoExec) ? File.ReadAllLines(AlyxAutoExec).ToList() : [];
        lines.RemoveAll(x => x.TrimStart().StartsWith("hlvr_continuous_normal_speed ", StringComparison.OrdinalIgnoreCase)
                          || x.TrimStart().StartsWith("hlvr_continuous_combat_speed ", StringComparison.OrdinalIgnoreCase));
        if (enabled)
        {
            lines.Add("hlvr_continuous_normal_speed 300");
            lines.Add("hlvr_continuous_combat_speed 300");
        }
        if (lines.Count == 0) { if (File.Exists(AlyxAutoExec)) File.Delete(AlyxAutoExec); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(AlyxAutoExec)!);
        AtomicWrite(AlyxAutoExec, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    private static void SetAlyxControllerBindingOverride(bool enabled)
    {
        if (!File.Exists(AlyxTouchBindings)) return;
        if (enabled)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AlyxTouchBindingsBackup)!);
            if (!File.Exists(AlyxTouchBindingsBackup)) File.Copy(AlyxTouchBindings, AlyxTouchBindingsBackup);
            AtomicWrite(AlyxTouchBindings, AlyxBindingOverride.RemovePhysicalForwardVector(File.ReadAllText(AlyxTouchBindings)));
        }
        else if (File.Exists(AlyxTouchBindingsBackup))
        {
            AtomicWrite(AlyxTouchBindings, File.ReadAllText(AlyxTouchBindingsBackup));
        }
    }

    private static void SetArizona2ControllerBindingOverride(bool enabled)
    {
        if (!File.Exists(Arizona2TouchBindings)) return;
        if (enabled)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Arizona2TouchBindingsBackup)!);
            if (!File.Exists(Arizona2TouchBindingsBackup)) File.Copy(Arizona2TouchBindings, Arizona2TouchBindingsBackup);
            AtomicWrite(Arizona2TouchBindings, AlyxBindingOverride.RemoveArizonaSunshine2PhysicalMovement(File.ReadAllText(Arizona2TouchBindings)));
        }
        else if (File.Exists(Arizona2TouchBindingsBackup))
        {
            AtomicWrite(Arizona2TouchBindings, File.ReadAllText(Arizona2TouchBindingsBackup));
        }
    }

    private static void StopVrProcesses()
    {
        foreach (var name in new[] { "hlvr", "vrmonitor", "vrserver", "vrdashboard", "vrcompositor", "vrwebhelper" })
            foreach (var process in Process.GetProcessesByName(name))
                try { process.Kill(true); process.WaitForExit(2500); } catch { }
    }

    private static void SaveMode(SystemMode mode)
    {
        var path = Path.Combine(NiiMotionPaths.Config, "system-mode.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicWrite(path, JsonSerializer.Serialize(new { mode = mode.ToString(), changedAt = DateTimeOffset.Now }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void AtomicWrite(string path, string content)
    {
        var temp = path + ".niirmotion.tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, true);
    }
}
