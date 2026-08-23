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
    public static IReadOnlyList<string> FindCandidateExecutables(string installPath) => !Directory.Exists(installPath) ? [] : Directory.EnumerateFiles(installPath, "*.exe", SearchOption.AllDirectories)
        .Where(x => !new[] { "unins", "crash", "report", "redist", "setup", "helper" }.Any(term => Path.GetFileName(x).Contains(term, StringComparison.OrdinalIgnoreCase)))
        .Select(Path.GetFileName).Where(x => x is not null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).Take(30).ToArray();
    private static OpenXrGameAdapter BuiltInMetro() => new("metro-awakening", "Metro Awakening", "2669410", ["Impact-Win64-Shipping.exe", "Impact.exe"], 1, DateTimeOffset.UnixEpoch);
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
}
