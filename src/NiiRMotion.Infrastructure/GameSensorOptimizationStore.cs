using System.Text.Json;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class GameSensorOptimizationStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public GameSensorOptimizationStore(string? configDirectory = null) =>
        _path = Path.Combine(configDirectory ?? NiiMotionPaths.Config, "game-sensor-optimization.json");

    public GameSensorOptimization Load(string gameId, string motionProfileId)
    {
        var stored = LoadAll().FirstOrDefault(x => SameKey(x, gameId, motionProfileId));
        return (stored ?? Default(gameId, motionProfileId)).Safe();
    }

    public IReadOnlyList<GameSensorOptimization> LoadAll()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize<GameSensorOptimization[]>(File.ReadAllText(_path)) ?? [] : []; }
        catch { return []; }
    }

    public GameSensorOptimization Save(GameSensorOptimization value)
    {
        var safe = value.Safe();
        Write(LoadAll().Where(x => !SameKey(x, safe.GameId, safe.MotionProfileId)).Append(safe));
        return safe;
    }

    public GameSensorOptimization ApplyTelemetry(string gameId, string motionProfileId, StrideTelemetryMeasurement measurement)
    {
        var current = Load(gameId, motionProfileId);
        if (!AutomaticStrideOptimizer.TryOptimize(current.DistanceScale, measurement, out var scale, out var reason))
            return current with { Source = $"Reddedildi: {reason}", Confidence = 0, UpdatedAt = DateTimeOffset.UtcNow };
        var confidence = Math.Clamp(measurement.PhysicalSteps / 20d, .3, 1);
        return Save(current with { PreviousDistanceScale = current.DistanceScale, DistanceScale = scale, Source = "Oyun telemetrisi", Confidence = confidence, UpdatedAt = DateTimeOffset.UtcNow });
    }

    public GameSensorOptimization ApplyFeedback(string gameId, string motionProfileId, GamePaceFeedback feedback)
    {
        var current = Load(gameId, motionProfileId);
        var scale = GuidedGameOptimizer.Apply(current.DistanceScale, feedback);
        var source = feedback == GamePaceFeedback.Correct ? "Oyun içi hız doğrulandı" : "Oyun içi kısa doğrulama";
        return Save(current with { PreviousDistanceScale = current.DistanceScale, DistanceScale = scale, Source = source, Confidence = feedback == GamePaceFeedback.Correct ? 1 : .7, UpdatedAt = DateTimeOffset.UtcNow });
    }

    public GameSensorOptimization RestorePrevious(string gameId, string motionProfileId)
    {
        var current = Load(gameId, motionProfileId);
        return Save(current with { DistanceScale = current.PreviousDistanceScale, PreviousDistanceScale = current.DistanceScale, Source = "Önceki ayar", Confidence = 1, UpdatedAt = DateTimeOffset.UtcNow });
    }

    public GameSensorOptimization Reset(string gameId, string motionProfileId)
    {
        Write(LoadAll().Where(x => !SameKey(x, gameId, motionProfileId)));
        return Default(gameId, motionProfileId);
    }

    public static GameSensorOptimization Default(string gameId, string motionProfileId)
    {
        var scale = gameId.Equals("half-life-alyx", StringComparison.OrdinalIgnoreCase)
            && motionProfileId.Equals("psmove-only", StringComparison.OrdinalIgnoreCase) ? .41 : 1;
        return new(1, gameId, motionProfileId, scale, scale, "Doğrulanmış varsayılan", 1, DateTimeOffset.MinValue);
    }

    private void Write(IEnumerable<GameSensorOptimization> values)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(values.ToArray(), Options));
        File.Move(temp, _path, true);
    }

    private static bool SameKey(GameSensorOptimization value, string gameId, string profileId) =>
        value.GameId.Equals(gameId, StringComparison.OrdinalIgnoreCase) && value.MotionProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase);
}
