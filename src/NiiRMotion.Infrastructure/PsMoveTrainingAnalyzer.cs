using System.Text.Json;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public readonly record struct PsMoveTrainingObservation(string Label, LegSide Side, double ElapsedMilliseconds, double AngularSpeedRadps);

public sealed class PsMoveTrainingAnalyzer
{
    public async Task<PsMoveTrainingProfile> AnalyzeAsync(string recordingRoot, CancellationToken cancellationToken = default)
    {
        var observations = new List<PsMoveTrainingObservation>(200_000);
        double totalDurationSeconds = 0;
        foreach (var file in Directory.EnumerateFiles(recordingRoot, "samples.jsonl", SearchOption.AllDirectories))
        {
            double lastElapsedMilliseconds = 0;
            using var stream = File.OpenRead(file);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                using var document = JsonDocument.Parse(line); var root = document.RootElement;
                var sample = root.GetProperty("sample"); var angular = sample.GetProperty("AngularVelocityRadps");
                var x = angular.GetProperty("X").GetDouble(); var y = angular.GetProperty("Y").GetDouble(); var z = angular.GetProperty("Z").GetDouble();
                lastElapsedMilliseconds = root.GetProperty("elapsedMs").GetDouble();
                observations.Add(new(root.GetProperty("label").GetString()!, (LegSide)sample.GetProperty("Side").GetInt32(), lastElapsedMilliseconds, Math.Sqrt(x * x + y * y + z * z)));
            }
            totalDurationSeconds += lastElapsedMilliseconds / 1000d;
        }
        return Analyze(observations) with { DurationSeconds = totalDurationSeconds };
    }

    public PsMoveTrainingProfile Analyze(IReadOnlyCollection<PsMoveTrainingObservation> observations)
    {
        if (observations.Count < 10_000) throw new InvalidDataException("At least 10,000 labeled PS Move samples are required.");
        var groups = observations.GroupBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => Anchor(x.Select(y => y.AngularSpeedRadps)), StringComparer.OrdinalIgnoreCase);
        Require(groups, "stand", "slow_walk", "natural_walk", "fast_walk");
        var stand = observations.Where(x => x.Label == "stand").Select(x => x.AngularSpeedRadps).Order().ToArray();
        var slow = observations.Where(x => x.Label == "slow_walk").Select(x => x.AngularSpeedRadps).Order().ToArray();
        var natural = groups["natural_walk"]; var fast = groups["fast_walk"];
        var leftNatural = observations.Where(x => x.Label == "natural_walk" && x.Side == LegSide.Left).Average(x => x.AngularSpeedRadps);
        var rightNatural = observations.Where(x => x.Label == "natural_walk" && x.Side == LegSide.Right).Average(x => x.AngularSpeedRadps);
        // A label switches at the same instant as the instruction. The first samples after
        // "stand" can therefore contain the final swing. Median/P75 describe true rest
        // without learning that transition tail as continued walking.
        var release = Math.Max(groups["stand"].MedianRadps * 3, Percentile(stand, .75));
        var activation = Math.Clamp(Math.Max(release * 1.6, groups["slow_walk"].MedianRadps * .55), release + .04, natural.MedianRadps * .85);
        return new(1, DateTimeOffset.UtcNow, SensorPlacement.CalfLowerLeg, observations.Count,
            observations.Max(x => x.ElapsedMilliseconds) / 1000d, release, activation,
            groups["slow_walk"].MedianRadps, natural.MedianRadps, fast.MedianRadps,
            rightNatural <= .0001 ? 1 : leftNatural / rightNatural, groups);
    }

    public static async Task SaveAsync(PsMoveTrainingProfile profile, string path, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        File.Move(temporary, path, true);
    }

    private static PsMoveMotionAnchor Anchor(IEnumerable<double> source)
    {
        var values = source.Order().ToArray();
        return new(values.Average(), Percentile(values, .5), Percentile(values, .95), values.Length);
    }
    private static double Percentile(double[] ordered, double fraction) => ordered[(int)Math.Clamp(Math.Round((ordered.Length - 1) * fraction), 0, ordered.Length - 1)];
    private static void Require(IReadOnlyDictionary<string, PsMoveMotionAnchor> groups, params string[] labels)
    {
        foreach (var label in labels) if (!groups.ContainsKey(label)) throw new InvalidDataException($"Required PS Move label is missing: {label}");
    }
}
