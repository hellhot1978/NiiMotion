using System.Text.Json;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class PendingCalibrationRepairStore
{
    private sealed record PendingDocument(int Version, SensorFamily Sensor, int Phase, double DurationSeconds, string Folder, DateTimeOffset UpdatedAtUtc);
    private string PathName => Path.Combine(NiiMotionPaths.Config, "pending-calibration-repair.json");

    public void Save(GuidedCalibrationResult result)
    {
        Directory.CreateDirectory(NiiMotionPaths.Config);
        var document = new PendingDocument(1, result.Sensor, result.Phase, result.Duration.TotalSeconds, result.Folder, DateTimeOffset.UtcNow);
        var temp = PathName + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, PathName, true);
    }

    public GuidedCalibrationResult? Load(SensorFamily sensor)
    {
        try
        {
            if (!File.Exists(PathName)) return null;
            var document = JsonSerializer.Deserialize<PendingDocument>(File.ReadAllText(PathName));
            if (document is null || document.Version != 1 || document.Sensor != sensor || document.DurationSeconds <= 0) return null;
            var folder = Path.GetFullPath(document.Folder); var data = Path.GetFullPath(NiiMotionPaths.Data) + Path.DirectorySeparatorChar;
            if (!folder.StartsWith(data, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(folder)) return null;
            var counts = Directory.GetFiles(folder, "*.count").ToDictionary(x => Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(x)), x => int.TryParse(File.ReadAllText(x), out var value) ? value : 0);
            var duration = TimeSpan.FromSeconds(document.DurationSeconds); var quality = GuidedCalibrationRecorder.AnalyzeFolder(folder, duration);
            if (quality.IsClean) { Clear(); return null; }
            return new(sensor, document.Phase, duration, counts, folder, quality);
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }

    public void Clear() { try { if (File.Exists(PathName)) File.Delete(PathName); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}
