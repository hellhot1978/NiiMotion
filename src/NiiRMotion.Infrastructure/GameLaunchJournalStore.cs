using System.Text.Json;

namespace NiiRMotion.Infrastructure;

public enum GameLaunchStage
{
    Idle,
    ValidatingProfile,
    ValidatingCalibration,
    ValidatingSensors,
    WaitingForVirtualDesktop,
    ApplyingVrMode,
    StartingSteamVr,
    WaitingForMotionBridge,
    StartingLocomotion,
    StartingGame,
    Running,
    Failed
}

public sealed record GameLaunchJournal(
    int SchemaVersion,
    string GameId,
    string GameName,
    string MotionProfileId,
    bool NiiMotionEnabled,
    GameLaunchStage Stage,
    string Message,
    DateTimeOffset UpdatedAtUtc);

public sealed class GameLaunchJournalStore(string? path = null)
{
    private readonly string _path = path ?? Path.Combine(NiiMotionPaths.Config, "game-launch-session.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public GameLaunchJournal? Load()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize<GameLaunchJournal>(File.ReadAllText(_path)) : null; }
        catch { return null; }
    }

    public void Save(GameLaunchJournal value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options));
        File.Move(temporary, _path, true);
        _ = NiiMotionEventLog.WriteAsync("game-launch", value.Stage.ToString(), value.Message, new { value.GameId, value.MotionProfileId, value.NiiMotionEnabled });
    }

    public void Complete(string message = "Oturum kapatıldı.")
    {
        var current = Load();
        if (current is null) return;
        Save(current with { Stage = GameLaunchStage.Idle, Message = message, UpdatedAtUtc = DateTimeOffset.UtcNow });
    }
}
