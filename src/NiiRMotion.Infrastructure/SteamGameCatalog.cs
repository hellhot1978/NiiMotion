using System.Text.RegularExpressions;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class SteamGameCatalog
{
    private readonly string[] _libraries;
    public SteamGameCatalog(IEnumerable<string>? libraries = null) => _libraries = (libraries ?? DefaultLibraries()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public IReadOnlyList<InstalledGame> Detect()
    {
        var manifests = new Dictionary<string, (string InstallDir, string Common)>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in _libraries.Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(library, "appmanifest_*.acf"))
            {
                try
                {
                    var text = File.ReadAllText(path); var appId = Value(text, "appid"); var installDir = Value(text, "installdir");
                    if (!string.IsNullOrWhiteSpace(appId)) manifests[appId] = (installDir, Path.Combine(library, "common"));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return BuiltInGames.All.Select(game =>
        {
            if (game.SteamAppId is null || !manifests.TryGetValue(game.SteamAppId, out var entry)) return new InstalledGame(game, false, null);
            return new InstalledGame(game, true, Path.Combine(entry.Common, entry.InstallDir));
        }).ToArray();
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

public sealed class GameSelectionStore
{
    private static string PathName => Path.Combine(NiiMotionPaths.Config, "active-game.txt");
    public string Load() { try { return File.Exists(PathName) ? File.ReadAllText(PathName).Trim() : "half-life-alyx"; } catch { return "half-life-alyx"; } }
    public void Save(string gameId) { var temp = PathName + ".tmp"; File.WriteAllText(temp, gameId); File.Move(temp, PathName, true); }
}
