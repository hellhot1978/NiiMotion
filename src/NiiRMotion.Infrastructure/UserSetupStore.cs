using System.Text.Json;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class UserSetupStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<UserHardwareInventory?> LoadInventoryAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(NiiMotionPaths.HardwareInventory)) return null;
        await using var stream = File.OpenRead(NiiMotionPaths.HardwareInventory);
        return await JsonSerializer.DeserializeAsync<UserHardwareInventory>(stream, cancellationToken: cancellationToken);
    }

    public async Task SaveInventoryAsync(UserHardwareInventory inventory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(NiiMotionPaths.HardwareInventory)!);
        var temporary = NiiMotionPaths.HardwareInventory + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(inventory with { Version = 1, UpdatedAt = DateTimeOffset.UtcNow }, JsonOptions), cancellationToken);
        File.Move(temporary, NiiMotionPaths.HardwareInventory, true);
    }

    public async Task<CalibrationProgressDocument> LoadCalibrationAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(NiiMotionPaths.CalibrationProgress)) return new(1, Array.Empty<DeviceCalibrationProgress>());
        await using var stream = File.OpenRead(NiiMotionPaths.CalibrationProgress);
        return await JsonSerializer.DeserializeAsync<CalibrationProgressDocument>(stream, cancellationToken: cancellationToken)
            ?? new(1, Array.Empty<DeviceCalibrationProgress>());
    }

    public async Task SaveCalibrationAsync(CalibrationProgressDocument progress, CancellationToken cancellationToken = default)
    {
        var temporary = NiiMotionPaths.CalibrationProgress + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(progress with { Version = 1 }, JsonOptions), cancellationToken);
        File.Move(temporary, NiiMotionPaths.CalibrationProgress, true);
    }
}
