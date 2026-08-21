using System.Text.Json;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed record PsMoveReplayLabelResult(string Label, long Samples, long ActiveSamples, double ActiveRatio);

public static class PsMoveReplayValidator
{
    public static async Task<IReadOnlyList<PsMoveReplayLabelResult>> ValidateAsync(string root, PsMoveTrainingProfile profile, CancellationToken cancellationToken = default)
    {
        var totals = new Dictionary<string, (long Samples, long Active)>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(root, "samples.jsonl", SearchOption.AllDirectories))
        {
            var engine = new PsMoveGaitEngine(profile);
            using var reader = new StreamReader(File.OpenRead(file));
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                using var doc = JsonDocument.Parse(line); var rootElement = doc.RootElement; var sample = rootElement.GetProperty("sample");
                var angular = sample.GetProperty("AngularVelocityRadps"); var x = angular.GetProperty("X").GetDouble(); var y = angular.GetProperty("Y").GetDouble(); var z = angular.GetProperty("Z").GetDouble();
                var ticks = sample.GetProperty("Timestamp").GetProperty("MonotonicTicks").GetInt64();
                engine.Observe((LegSide)sample.GetProperty("Side").GetInt32(), new System.Numerics.Vector3((float)x, (float)y, (float)z), ticks);
                var active = engine.Update(ticks).TargetSpeed > 0; var label = rootElement.GetProperty("label").GetString()!;
                var current = totals.GetValueOrDefault(label); totals[label] = (current.Samples + 1, current.Active + (active ? 1 : 0));
            }
        }
        return totals.Select(x => new PsMoveReplayLabelResult(x.Key, x.Value.Samples, x.Value.Active, x.Value.Active / (double)x.Value.Samples)).OrderBy(x => x.Label).ToArray();
    }
}
