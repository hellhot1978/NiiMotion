using Microsoft.Win32;
using System.Runtime.Versioning;

namespace NiiRMotion.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class OpenXrLayerRegistrationService
{
    private const string RegistryPath = @"Software\Khronos\OpenXR\1\ApiLayers\Implicit";
    public static string LayerRoot
    {
        get
        {
            var installed = Path.Combine(AppContext.BaseDirectory, "OpenXRLayer");
            return File.Exists(Path.Combine(installed, "niirmotion_openxr.json")) ? installed : Path.Combine(NiiMotionPaths.Root, "native", "openxr-layer", "dist");
        }
    }
    public static string ManifestPath => Path.Combine(LayerRoot, "niirmotion_openxr.json");
    public bool IsInstalled => File.Exists(ManifestPath) && File.Exists(Path.Combine(LayerRoot, "bin", "win64", "niirmotion_openxr.dll"));
    public bool IsEnabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(RegistryPath); return key?.GetValue(ManifestPath) is int value && value == 0; }
    }
    public void Apply(bool enabled)
    {
        if (!IsInstalled) { if (enabled) throw new FileNotFoundException("NiiMotion OpenXR katmanı bulunamadı.", ManifestPath); return; }
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, true);
        key.SetValue(ManifestPath, enabled ? 0 : 1, RegistryValueKind.DWord);
        _ = NiiMotionEventLog.WriteAsync("openxr", enabled ? "layer-enabled" : "layer-disabled", "OpenXR API katmanı durumu değiştirildi.", new { manifest = ManifestPath });
    }
}
