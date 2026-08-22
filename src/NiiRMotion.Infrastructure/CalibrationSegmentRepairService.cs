using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class CalibrationSegmentRepairService
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public async Task<GuidedCalibrationResult> ReplaceAsync(GuidedCalibrationResult original, CalibrationQualitySegment segment, GuidedCalibrationResult repair, CancellationToken token = default)
    {
        if (!segment.NeedsRedo) throw new InvalidOperationException("Temiz bir bölümün yeniden kaydedilmesine gerek yok.");
        var root = Path.GetDirectoryName(original.Folder)!;
        var output = Path.Combine(root, $"phase-{original.Phase}-repaired-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(output);
        try
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in Directory.GetFiles(original.Folder, "*.jsonl"))
            {
                var name = Path.GetFileName(source); var replacement = Path.Combine(repair.Folder, name);
                if (!File.Exists(replacement)) throw new InvalidDataException($"Yeniden kayıtta {name} akışı bulunamadı.");
                var rows = ReadRows(source); var replacements = ReadRows(replacement);
                if (rows.Count == 0 || replacements.Count == 0) throw new InvalidDataException("Yeniden kayıt sensör örneği içermiyor.");
                var origin = rows.Min(x => x.Ticks); var repairOrigin = replacements.Min(x => x.Ticks);
                var startTicks = origin + (long)(segment.StartSeconds * Stopwatch.Frequency);
                var endTicks = origin + (long)(segment.EndSeconds * Stopwatch.Frequency);
                var merged = rows.Where(x => x.Ticks < startTicks || x.Ticks >= endTicks).ToList();
                foreach (var row in replacements)
                {
                    var mapped = startTicks + Math.Min(endTicks - startTicks - 1, Math.Max(0, row.Ticks - repairOrigin));
                    SetTicks(row.Json, mapped); merged.Add((mapped, row.Json));
                }
                merged.Sort((a, b) => a.Ticks.CompareTo(b.Ticks));
                var target = Path.Combine(output, name); await File.WriteAllLinesAsync(target, merged.Select(x => x.Json.ToJsonString()), token);
                counts[Path.GetFileNameWithoutExtension(name)] = merged.Count;
                await File.WriteAllTextAsync(target + ".count", merged.Count.ToString(), token);
            }
            var quality = GuidedCalibrationRecorder.AnalyzeFolder(output, original.Duration);
            var result = new GuidedCalibrationResult(original.Sensor, original.Phase, original.Duration, counts, output, quality);
            await File.WriteAllTextAsync(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
            {
                version = 2, sensor = original.Sensor, phase = original.Phase, durationSeconds = original.Duration.TotalSeconds,
                samples = counts, completedAtUtc = DateTimeOffset.UtcNow, purpose = "base-calibration", quality,
                repair = new { segment.StartSeconds, segment.EndSeconds, source = original.Folder }
            }, WriteOptions), token);
            MarkSuperseded(original.Folder, output);
            TryDeleteRepairCapture(repair.Folder);
            return result;
        }
        catch
        {
            try { if (Directory.Exists(output)) Directory.Delete(output, true); } catch (IOException) { }
            throw;
        }
    }

    private static List<(long Ticks, JsonObject Json)> ReadRows(string path)
    {
        var result = new List<(long, JsonObject)>();
        foreach (var line in File.ReadLines(path))
        {
            try
            {
                if (JsonNode.Parse(line) is not JsonObject json || !TryTicks(json, out var ticks)) continue;
                result.Add((ticks, json));
            }
            catch (JsonException) { }
        }
        return result;
    }

    private static bool TryTicks(JsonObject root, out long ticks)
    {
        ticks = 0; var timestamp = root.FirstOrDefault(x => x.Key.Equals("timestamp", StringComparison.OrdinalIgnoreCase)).Value as JsonObject;
        var node = timestamp?.FirstOrDefault(x => x.Key.Equals("monotonicTicks", StringComparison.OrdinalIgnoreCase)).Value;
        return node is not null && long.TryParse(node.ToString(), out ticks);
    }

    private static void SetTicks(JsonObject root, long ticks)
    {
        var timestamp = root.First(x => x.Key.Equals("timestamp", StringComparison.OrdinalIgnoreCase)).Value!.AsObject();
        var key = timestamp.Select(x => x.Key).First(x => x.Equals("monotonicTicks", StringComparison.OrdinalIgnoreCase)); timestamp[key] = ticks;
    }

    private static void MarkSuperseded(string folder, string replacement)
    {
        var path = Path.Combine(folder, "manifest.json"); if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject manifest) return;
        manifest["superseded"] = true; manifest["supersededBy"] = replacement;
        var temp = path + ".tmp"; File.WriteAllText(temp, manifest.ToJsonString(WriteOptions)); File.Move(temp, path, true);
    }

    private static void TryDeleteRepairCapture(string folder)
    {
        try
        {
            var full = Path.GetFullPath(folder); var root = Path.GetFullPath(NiiMotionPaths.Data) + Path.DirectorySeparatorChar;
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(full)) Directory.Delete(full, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
