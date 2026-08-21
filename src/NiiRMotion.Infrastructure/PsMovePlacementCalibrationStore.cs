using System.Text.Json;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class PsMovePlacementCalibrationStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, IncludeFields = true };

    public async Task SaveAsync(PsMovePlacementCalibration calibration, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(calibration, Options), cancellationToken);
        File.Move(temporary, path, true);
    }

    public async Task<PsMovePlacementCalibration?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<PsMovePlacementCalibration>(await File.ReadAllTextAsync(path, cancellationToken), Options);
    }
}
