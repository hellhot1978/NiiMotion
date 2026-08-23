using System.Text.Json;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class OpenXrGameAdapterStore
{
    private sealed record Document(int SchemaVersion, DateTimeOffset UpdatedAtUtc, IReadOnlyList<OpenXrGameAdapter> Adapters);
    private readonly string _path;
    public OpenXrGameAdapterStore(string? configDirectory = null) => _path = Path.Combine(configDirectory ?? NiiMotionPaths.Config, "openxr-game-adapters.json");

    public IReadOnlyList<OpenXrGameAdapter> Load()
    {
        var adapters = new List<OpenXrGameAdapter> { BuiltInMetro() };
        try
        {
            if (File.Exists(_path)) adapters.AddRange((JsonSerializer.Deserialize<Document>(File.ReadAllText(_path), Options)?.Adapters ?? []).Where(x => OpenXrGameAdapterValidator.Validate(x).Count == 0));
        }
        catch { }
        return adapters.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Select(x => x.Last()).ToArray();
    }

    public void Save(OpenXrGameAdapter adapter, string? installPath = null)
    {
        var errors = OpenXrGameAdapterValidator.Validate(adapter).ToList();
        if (!string.IsNullOrWhiteSpace(installPath) && Directory.Exists(installPath))
            foreach (var executable in adapter.Executables.Where(x => !File.Exists(Path.Combine(installPath, x)))) errors.Add($"Oyun klasöründe bulunamadı: {executable}");
        if (errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        var user = Load().Where(x => x.Id != "metro-awakening" && !x.Id.Equals(adapter.Id, StringComparison.OrdinalIgnoreCase)).Append(adapter).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!); var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new Document(1, DateTimeOffset.UtcNow, user), Options)); File.Move(temp, _path, true);
    }

    public bool Remove(string id)
    {
        var existing = Load().Where(x => x.Id != "metro-awakening").ToArray(); var next = existing.Where(x => !x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (next.Length == existing.Length) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!); File.WriteAllText(_path, JsonSerializer.Serialize(new Document(1, DateTimeOffset.UtcNow, next), Options)); return true;
    }

    public OpenXrGameAdapter? Find(string id) => Load().FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    public static IReadOnlyList<string> FindCandidateExecutables(string installPath)
    {
        if (!Directory.Exists(installPath)) return [];
        var files = new List<string>(); var pending = new Stack<(string Path, int Depth)>(); pending.Push((installPath, 0));
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            try
            {
                files.AddRange(Directory.EnumerateFiles(current.Path, "*.exe"));
                if (current.Depth < 5) foreach (var directory in Directory.EnumerateDirectories(current.Path).Where(x => !new[] { "redist", "installer", "support", "crash" }.Any(term => Path.GetFileName(x).Contains(term, StringComparison.OrdinalIgnoreCase)))) pending.Push((directory, current.Depth + 1));
            }
            catch (UnauthorizedAccessException) { } catch (IOException) { }
        }
        return files.Where(x => !new[] { "unins", "crash", "report", "redist", "setup", "helper", "launcher" }.Any(term => Path.GetFileName(x).Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Select(Path.GetFileName).Where(x => x is not null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(ExecutableScore).ThenBy(x => x, StringComparer.OrdinalIgnoreCase).Take(30).ToArray();
    }
    public static string DetectEngine(string installPath)
    {
        try
        {
            if (Directory.EnumerateFiles(installPath, "UnityPlayer.dll", SearchOption.AllDirectories).Any()) return "Unity";
            if (Directory.EnumerateDirectories(installPath, "Binaries", SearchOption.AllDirectories).Any() || Directory.EnumerateFiles(installPath, "*-Win64-Shipping.exe", SearchOption.AllDirectories).Any()) return "Unreal Engine";
            if (Directory.EnumerateFiles(installPath, "openxr_loader.dll", SearchOption.AllDirectories).Any()) return "OpenXR Native";
        }
        catch (UnauthorizedAccessException) { } catch (IOException) { }
        return "Bilinmeyen motor";
    }
    private static int ExecutableScore(string path)
    {
        var name = Path.GetFileName(path); var score = 0;
        if (name.Contains("Win64-Shipping", StringComparison.OrdinalIgnoreCase)) score += 100;
        if (path.Contains("Binaries", StringComparison.OrdinalIgnoreCase)) score += 40;
        if (path.Contains("Win64", StringComparison.OrdinalIgnoreCase) || path.Contains("x64", StringComparison.OrdinalIgnoreCase)) score += 20;
        return score;
    }
    private static OpenXrGameAdapter BuiltInMetro() => new("metro-awakening", "Metro Awakening", "2669410", ["Impact-Win64-Shipping.exe", "Impact.exe"], 1, DateTimeOffset.UnixEpoch);
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
}
