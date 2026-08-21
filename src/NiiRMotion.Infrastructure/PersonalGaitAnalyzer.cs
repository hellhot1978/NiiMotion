using System.Numerics;
using System.Text.Json;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed record PersonalGaitAnalysis(PersonalGaitPace Pace, int AcceptedSessions, long AcceptedSamples);

public sealed class PersonalGaitAnalyzer
{
    public PersonalGaitAnalysis Analyze(string dataRoot)
    {
        var learningRoot = Path.Combine(dataRoot, "joycon-learning");
        var completed = ReadCompletedParts(Path.Combine(learningRoot, "progress-v2.json"));
        var values = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase)
        {
            ["slow"] = [], ["natural"] = [], ["fast"] = []
        };
        var sessions = 0;

        if (Directory.Exists(learningRoot))
        {
            foreach (var folder in Directory.EnumerateDirectories(learningRoot, "part-*-*"))
            {
                var sessionPath = Path.Combine(folder, "session.json");
                var samplesPath = Path.Combine(folder, "joycons.jsonl");
                if (!File.Exists(sessionPath) || !File.Exists(samplesPath)) continue;
                using var session = JsonDocument.Parse(File.ReadAllText(sessionPath));
                if (!TryInt(session.RootElement, "part", out var part) || !completed.Contains(part)) continue;
                sessions++;
                ReadLearningSamples(samplesPath, values);
            }
        }

        if (Directory.Exists(dataRoot))
        {
            foreach (var folder in Directory.EnumerateDirectories(dataRoot))
            {
                var sessionPath = Path.Combine(folder, "session.json");
                var samplesPath = Path.Combine(folder, "joycons.jsonl");
                if (!File.Exists(sessionPath) || !File.Exists(samplesPath)) continue;
                using var session = JsonDocument.Parse(File.ReadAllText(sessionPath));
                if (!TryString(session.RootElement, "label", out var label) || !values.ContainsKey(label)) continue;
                if (ReadGuidedSamples(samplesPath, values[label])) sessions++;
            }
        }

        foreach (var (label, samples) in values)
            if (samples.Count < 100) throw new InvalidOperationException($"{TurkishLabel(label)} için yeterli doğrulanmış kayıt yok ({samples.Count} örnek). Önce ilgili kalibrasyonu tamamla.");

        var pace = new PersonalGaitPace(Percentile(values["slow"], .95), Percentile(values["natural"], .95), Percentile(values["fast"], .95));
        if (!(pace.SlowP95Dps > 0 && pace.NaturalP95Dps > pace.SlowP95Dps && pace.FastP95Dps > pace.NaturalP95Dps))
            throw new InvalidOperationException("Yavaş, doğal ve hızlı kayıtlar birbirinden güvenilir biçimde ayrılamadı. Hız kalibrasyonlarını yeniden kaydet.");
        return new(pace, sessions, values.Values.Sum(x => (long)x.Count));
    }

    public async Task ApplyAsync(PersonalGaitAnalysis analysis, string outputPath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var temporary = outputPath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(analysis.Pace, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        File.Move(temporary, outputPath, true);
    }

    private static HashSet<int> ReadCompletedParts(string path)
    {
        if (!File.Exists(path)) return [];
        return (JsonSerializer.Deserialize<int[]>(File.ReadAllText(path)) ?? []).ToHashSet();
    }

    private static void ReadLearningSamples(string path, Dictionary<string, List<double>> values)
    {
        foreach (var line in File.ReadLines(path))
        {
            using var row = JsonDocument.Parse(line);
            if (!TryString(row.RootElement, "activity", out var activity)) continue;
            var target = activity switch { "slow_walk" => "slow", "natural_walk" => "natural", "fast_walk" => "fast", _ => null };
            if (target is not null && TryMagnitude(row.RootElement, out var magnitude)) values[target].Add(magnitude);
        }
    }

    private static bool ReadGuidedSamples(string path, List<double> target)
    {
        var rows = new List<(long Elapsed, double Magnitude)>();
        foreach (var line in File.ReadLines(path))
        {
            using var row = JsonDocument.Parse(line);
            if (TryLong(row.RootElement, "elapsedMs", out var elapsed) && TryMagnitude(row.RootElement, out var magnitude)) rows.Add((elapsed, magnitude));
        }
        if (rows.Count == 0) return false;
        var start = rows.Min(x => x.Elapsed) + 5000;
        var end = rows.Max(x => x.Elapsed) - 5000;
        target.AddRange(rows.Where(x => x.Elapsed >= start && x.Elapsed <= end).Select(x => x.Magnitude));
        return true;
    }

    private static bool TryMagnitude(JsonElement row, out double magnitude)
    {
        magnitude = 0;
        if (!TryProperty(row, "sample", out var sample) || !TryProperty(sample, "AngularVelocityDps", out var vector)) return false;
        if (!TryDouble(vector, "X", out var x) || !TryDouble(vector, "Y", out var y) || !TryDouble(vector, "Z", out var z)) return false;
        magnitude = new Vector3((float)x, (float)y, (float)z).Length();
        return double.IsFinite(magnitude);
    }

    private static double Percentile(List<double> values, double fraction)
    {
        values.Sort();
        return Math.Round(values[Math.Min(values.Count - 1, (int)Math.Round((values.Count - 1) * fraction))], 2);
    }

    private static bool TryProperty(JsonElement value, string name, out JsonElement property)
    {
        if (value.TryGetProperty(name, out property)) return true;
        foreach (var candidate in value.EnumerateObject()) if (candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { property = candidate.Value; return true; }
        property = default; return false;
    }
    private static bool TryString(JsonElement value, string name, out string result) { result = ""; return TryProperty(value, name, out var p) && p.ValueKind == JsonValueKind.String && (result = p.GetString() ?? "").Length > 0; }
    private static bool TryInt(JsonElement value, string name, out int result) { result = 0; return TryProperty(value, name, out var p) && p.TryGetInt32(out result); }
    private static bool TryLong(JsonElement value, string name, out long result) { result = 0; return TryProperty(value, name, out var p) && p.TryGetInt64(out result); }
    private static bool TryDouble(JsonElement value, string name, out double result) { result = 0; return TryProperty(value, name, out var p) && p.TryGetDouble(out result); }
    private static string TurkishLabel(string label) => label switch { "slow" => "Yavaş yürüyüş", "natural" => "Doğal yürüyüş", _ => "Hızlı yürüyüş" };
}
