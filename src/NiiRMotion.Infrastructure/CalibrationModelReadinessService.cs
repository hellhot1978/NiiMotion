using System.Text.Json;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

/// <summary>Verifies that completed local calibration phases produced a readable runtime model.</summary>
public sealed class CalibrationModelReadinessService(string? configDirectory = null)
{
    private readonly string _config = configDirectory ?? NiiMotionPaths.Config;

    public async Task<IReadOnlyList<SensorFamily>> FindUnavailableAsync(
        IEnumerable<SensorFamily> required,
        CalibrationProgressDocument progress,
        bool repairFromLocalCaptures = true,
        CancellationToken cancellationToken = default)
    {
        var sensors = required.Distinct().ToArray();
        var unavailable = sensors.Where(sensor => progress.Devices.FirstOrDefault(x => x.Sensor == sensor)?.IsReady != true || !HasValidModel(sensor)).ToArray();
        if (unavailable.Length == 0 || !repairFromLocalCaptures || configDirectory is not null) return unavailable;

        // Re-analysis is deterministic and reads only captures already approved on this computer.
        await new OfflineCalibrationPipeline().ApplyAvailableAsync(cancellationToken);
        return sensors.Where(sensor => progress.Devices.FirstOrDefault(x => x.Sensor == sensor)?.IsReady != true || !HasValidModel(sensor)).ToArray();
    }

    public bool HasValidModel(SensorFamily sensor)
    {
        var path = PathFor(sensor);
        if (!File.Exists(path) || new FileInfo(path).Length < 2) return false;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.ValueKind == JsonValueKind.Object && sensor switch
            {
                SensorFamily.JoyCon => Positive(document, "slowP95Dps") && Positive(document, "naturalP95Dps") && Positive(document, "fastP95Dps"),
                SensorFamily.PsMove => Positive(document, "gaitActivationThresholdRadps") && Positive(document, "naturalAnchorRadps"),
                SensorFamily.Phone => Positive(document, "naturalGyroP95") && Positive(document, "naturalAccelP95"),
                SensorFamily.BalanceBoard => Positive(document, "naturalCadenceHz") && Positive(document, "contactKg"),
                _ => false
            };
        }
        catch (JsonException) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private string PathFor(SensorFamily sensor) => sensor switch
    {
        SensorFamily.JoyCon => Path.Combine(_config, "personal-gait-pace.json"),
        SensorFamily.PsMove => Path.Combine(_config, "personal-psmove-training.json"),
        SensorFamily.Phone => Path.Combine(_config, "personal-phone-motion.json"),
        _ => Path.Combine(_config, "personal-board-motion.json")
    };

    private static bool Positive(JsonDocument document, string property)
    {
        var candidate = document.RootElement.EnumerateObject().FirstOrDefault(x => x.Name.Equals(property, StringComparison.OrdinalIgnoreCase));
        return candidate.Value.ValueKind == JsonValueKind.Number && candidate.Value.TryGetDouble(out var number) && double.IsFinite(number) && number > 0;
    }
}
