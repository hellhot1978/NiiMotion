using System.Text.RegularExpressions;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class SteamGameCatalog
{
    private readonly string[] _libraries;
    public SteamGameCatalog(IEnumerable<string>? libraries = null) => _libraries = (libraries ?? DefaultLibraries()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public IReadOnlyList<InstalledGame> Detect()
    {
        var manifests = ReadManifests();
        var custom = new GameAdapterStore().Load().Select(x => new GameDefinition(x.Id, x.Name, x.SteamAppId, "SteamVR · kullanıcı eşlemesi", true, $"Kullanıcı adaptörü · hız {x.SpeedMultiplier:0.00}×"));
        return BuiltInGames.All.Concat(custom).GroupBy(x => x.SteamAppId ?? x.Id, StringComparer.OrdinalIgnoreCase).Select(x => x.Last()).Select(game =>
        {
            if (game.SteamAppId is null || !manifests.TryGetValue(game.SteamAppId, out var entry)) return new InstalledGame(game, false, null);
            return new InstalledGame(game, true, Path.Combine(entry.Common, entry.InstallDir));
        }).ToArray();
    }

    public IReadOnlyList<SteamAppCandidate> DetectAdapterCandidates()
    {
        var supported = Detect().Where(x => x.Definition.MotionSupported).Select(x => x.Definition.SteamAppId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ReadManifests().Where(x => !supported.Contains(x.Key))
            .Select(x => new SteamAppCandidate(x.Key, x.Value.Name, Path.Combine(x.Value.Common, x.Value.InstallDir)))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private Dictionary<string, (string InstallDir, string Common, string Name)> ReadManifests()
    {
        var manifests = new Dictionary<string, (string InstallDir, string Common, string Name)>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in _libraries.Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(library, "appmanifest_*.acf"))
            {
                try
                {
                    var text = File.ReadAllText(path); var appId = Value(text, "appid"); var installDir = Value(text, "installdir"); var name = Value(text, "name");
                    if (!string.IsNullOrWhiteSpace(appId)) manifests[appId] = (installDir, Path.Combine(library, "common"), string.IsNullOrWhiteSpace(name) ? $"Steam {appId}" : name);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return manifests;
    }

    private static string Value(string text, string key)
    {
        var match = Regex.Match(text, $"\"{Regex.Escape(key)}\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : "";
    }
    private static IEnumerable<string> DefaultLibraries()
    {
        yield return @"C:\Program Files (x86)\Steam\steamapps";
        foreach (var drive in DriveInfo.GetDrives().Where(x => x.IsReady)) yield return Path.Combine(drive.RootDirectory.FullName, "SteamLibrary", "steamapps");
    }
}

public sealed record SteamAppCandidate(string AppId, string Name, string InstallPath)
{
    public override string ToString() => $"{Name}  ·  {AppId}";
}

public sealed class GameSelectionStore
{
    private static string PathName => Path.Combine(NiiMotionPaths.Config, "active-game.txt");
    public string Load() { try { return File.Exists(PathName) ? File.ReadAllText(PathName).Trim() : "half-life-alyx"; } catch { return "half-life-alyx"; } }
    public void Save(string gameId) { var temp = PathName + ".tmp"; File.WriteAllText(temp, gameId); File.Move(temp, PathName, true); }
}
