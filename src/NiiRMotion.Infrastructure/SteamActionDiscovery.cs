using System.Text.Json;
using System.Text.RegularExpressions;

namespace NiiRMotion.Infrastructure;

public sealed record SteamActionCandidate(string ActionSet, string Path)
{
    public override string ToString() => Path;
}
public enum VrInputRuntime { SteamVrActions, OpenXr, Unknown }
public sealed record SteamInputInspection(VrInputRuntime Runtime, IReadOnlyList<SteamActionCandidate> Actions, string Message);

public sealed class SteamActionDiscovery
{
    private static readonly Regex ActionPath = new(@"^/actions/[^/]+/in/[^\s]+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<SteamActionCandidate> Discover(string installPath)
    {
        if (!Directory.Exists(installPath)) return [];
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(installPath, "*.json", SearchOption.AllDirectories).Where(Relevant).Take(600).ToArray(); }
        catch (UnauthorizedAccessException) { return []; }
        catch (IOException) { return []; }
        foreach (var file in files)
        {
            try
            {
                if (new FileInfo(file).Length > 2_000_000) continue;
                using var document = JsonDocument.Parse(File.ReadAllText(file)); Collect(document.RootElement, found);
            }
            catch (JsonException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return found.Select(x => new SteamActionCandidate(x[..x.IndexOf("/in/", StringComparison.OrdinalIgnoreCase)], x))
            .OrderByDescending(x => IsLikelyMovement(x.Path)).ThenBy(x => x.Path).ToArray();
    }

    public SteamInputInspection Inspect(string installPath)
    {
        var actions = Discover(installPath);
        if (actions.Count > 0) return new SteamInputInspection(VrInputRuntime.SteamVrActions, actions, $"{actions.Count} SteamVR girdisi bulundu.");
        if (ContainsFile(installPath, "openxr_loader.dll") || ContainsFile(installPath, "OVRPlugin.dll"))
            return new SteamInputInspection(VrInputRuntime.OpenXr, actions, "Bu oyun OpenXR kullanıyor; SteamVR action dosyası bulunmaması normal. Genel eşleme yerine oyuna özel OpenXR adaptörü gerekir.");
        return new SteamInputInspection(VrInputRuntime.Unknown, actions, "Desteklenen SteamVR action dosyası bulunamadı. Girdi sistemi doğrulanmadan eşleme oluşturulamaz.");
    }

    private static bool Relevant(string path)
    {
        var name = Path.GetFileName(path);
        return name.Contains("action", StringComparison.OrdinalIgnoreCase) || name.Contains("binding", StringComparison.OrdinalIgnoreCase) || name.Contains("input", StringComparison.OrdinalIgnoreCase);
    }
    private static void Collect(JsonElement element, HashSet<string> found)
    {
        if (element.ValueKind == JsonValueKind.String) { var value = element.GetString(); if (value is not null && ActionPath.IsMatch(value)) found.Add(value); return; }
        if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) Collect(child, found);
        if (element.ValueKind == JsonValueKind.Object) foreach (var property in element.EnumerateObject()) { if (ActionPath.IsMatch(property.Name)) found.Add(property.Name); Collect(property.Value, found); }
    }
    private static bool IsLikelyMovement(string value) => new[] { "move", "walk", "locomotion", "axis0", "joystick" }.Any(x => value.Contains(x, StringComparison.OrdinalIgnoreCase));
    private static bool ContainsFile(string root, string fileName)
    {
        try { return Directory.Exists(root) && Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).Take(1).Any(); }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }
}
