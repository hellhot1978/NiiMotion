namespace NiiRMotion.Infrastructure;

public sealed record StandaloneComponentStatus(string Id, string Name, bool Ready, bool Required, string Detail);
public sealed record StandaloneReadinessReport(IReadOnlyList<StandaloneComponentStatus> Components)
{
    public bool IsReady => Components.Where(x => x.Required).All(x => x.Ready);
    public int ReadyCount => Components.Count(x => x.Ready);
    public string Summary => IsReady
        ? "NiiMotion yapay zekâ veya ağ bağlantısı olmadan kullanıma hazır."
        : $"{Components.Count(x => x.Required && !x.Ready)} zorunlu yerel bileşen eksik.";
}

public sealed class StandaloneReadinessService
{
    private readonly string _baseDirectory;
    private readonly string _root;
    private readonly string _models;

    public StandaloneReadinessService(string? baseDirectory = null, string? root = null, string? models = null)
    {
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
        _root = root ?? NiiMotionPaths.Root;
        _models = models ?? NiiMotionPaths.Models;
    }

    public StandaloneReadinessReport Inspect()
    {
        var items = new List<StandaloneComponentStatus>
        {
            Files("runtime", "Bağımsız .NET çalışma zamanı", true, "coreclr.dll", "hostfxr.dll"),
            JsonDirectory("models", "Yerel hareket modelleri", true, _models),
            Files("openvr", "SteamVR analog hareket sürücüsü", true, Path.Combine("OpenVRDriver", "driver.vrdrivermanifest"), Path.Combine("OpenVRDriver", "bin", "win64", "driver_niirmotion.dll")),
            Files("openxr", "OpenXR hareket katmanı", true, Path.Combine("OpenXRLayer", "niirmotion_openxr.json"), Path.Combine("OpenXRLayer", "bin", "win64", "niirmotion_openxr.dll")),
            Files("overlay", "SteamVR içi NiiMotion paneli", true, Path.Combine("VrOverlay", "NiiMotion.VrOverlay.exe"), Path.Combine("VrOverlay", "openvr_api.dll"), Path.Combine("VrOverlay", "niirmotion.vrmanifest")),
            Files("psmove", "Çevrimdışı PS Move eşleştirme aracı", false, Path.Combine("Tools", "PSMoveAPI", "4.0.12", "psmove.exe"), Path.Combine("Tools", "PSMoveAPI", "4.0.12", "psmoveapi.dll")),
            WritableState()
        };
        return new(items);
    }

    public StandaloneReadinessReport RepairLocalState()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "config"));
        Directory.CreateDirectory(Path.Combine(_root, "data"));
        Directory.CreateDirectory(Path.Combine(_root, "logs"));
        return Inspect();
    }

    private StandaloneComponentStatus Files(string id, string name, bool required, params string[] relativePaths)
    {
        var missing = relativePaths.Where(path => !File.Exists(Path.Combine(_baseDirectory, path))).ToArray();
        return new(id, name, missing.Length == 0, required, missing.Length == 0 ? "Hazır" : "Eksik: " + string.Join(", ", missing.Select(Path.GetFileName)));
    }

    private static StandaloneComponentStatus JsonDirectory(string id, string name, bool required, string path)
    {
        var ready = Directory.Exists(path) && Directory.EnumerateFiles(path, "*.json", SearchOption.TopDirectoryOnly).Any();
        return new(id, name, ready, required, ready ? "Hazır" : "Yerel JSON paketi bulunamadı");
    }

    private StandaloneComponentStatus WritableState()
    {
        try
        {
            Directory.CreateDirectory(_root);
            var probe = Path.Combine(_root, $".write-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "NiiMotion");
            File.Delete(probe);
            return new("storage", "Kişisel veri alanı", true, true, "Hazır");
        }
        catch (Exception ex) { return new("storage", "Kişisel veri alanı", false, true, ex.GetBaseException().Message); }
    }
}
