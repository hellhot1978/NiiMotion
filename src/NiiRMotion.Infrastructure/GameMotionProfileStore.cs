using System.Text.Json;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class GameMotionProfileStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public GameMotionProfileStore(string? configDirectory = null) => _path = Path.Combine(configDirectory ?? NiiMotionPaths.Config, "game-motion-profiles.json");

    public GameMotionProfile LoadActive()
    {
        var config = Path.GetDirectoryName(_path)!; var selected = new GameSelectionStore(config).Load(); var stored = LoadAll().FirstOrDefault(x => x.GameId == selected);
        if (stored is not null) return stored.Safe();
        var customSpeed = ResolveCustomSpeed(config, selected);
        return BuiltIn(selected) with { SpeedMultiplier = customSpeed };
    }

    public GameMotionProfile Load(string gameId)
    {
        var stored = LoadAll().FirstOrDefault(x => x.GameId == gameId); if (stored is not null) return stored.Safe();
        var config = Path.GetDirectoryName(_path)!; var customSpeed = ResolveCustomSpeed(config, gameId);
        return BuiltIn(gameId) with { SpeedMultiplier = customSpeed };
    }

    public IReadOnlyList<GameMotionProfile> LoadAll()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize<GameMotionProfile[]>(File.ReadAllText(_path)) ?? [] : []; } catch { return []; }
    }

    public void Save(GameMotionProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!); var profiles = LoadAll().Where(x => x.GameId != profile.GameId).Append(profile.Safe()).ToArray();
        if (File.Exists(_path)) File.Copy(_path, _path + $".backup-{DateTime.Now:yyyyMMdd-HHmmss}", true);
        var temp = _path + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(profiles, Options)); File.Move(temp, _path, true);
    }

    public GameMotionProfile Reset(string gameId)
    {
        var profiles = LoadAll().Where(x => x.GameId != gameId).ToArray(); Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        if (File.Exists(_path)) File.Copy(_path, _path + $".backup-{DateTime.Now:yyyyMMdd-HHmmss}", true);
        var temp = _path + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(profiles, Options)); File.Move(temp, _path, true); return Load(gameId);
    }

    private static GameMotionProfile BuiltIn(string gameId) => gameId switch
    {
        "half-life-alyx" => GameMotionProfile.Default(gameId, "alyx-openvr-v2"),
        "arizona-sunshine-2" => GameMotionProfile.Default(gameId, "arizona2-steamvr-v1"),
        "metro-awakening" => GameMotionProfile.Default(gameId, "metro-openxr-layer-v1"),
        _ => GameMotionProfile.Default(gameId)
    };

    private static double ResolveCustomSpeed(string configDirectory, string gameId)
    {
        var root = Path.GetDirectoryName(configDirectory);
        var steamVr = new GameAdapterStore(root).Load().FirstOrDefault(x => x.Id.Equals(gameId, StringComparison.OrdinalIgnoreCase))?.SpeedMultiplier;
        if (steamVr is not null) return steamVr.Value;
        return new OpenXrGameAdapterStore(configDirectory).Find(gameId)?.SpeedMultiplier ?? 1;
    }
}
