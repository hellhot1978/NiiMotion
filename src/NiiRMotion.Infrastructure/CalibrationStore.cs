using System.Text.Json;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class CalibrationStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public async Task SaveAsync(GaitCalibrationProfile profile, Stream destination, CancellationToken cancellationToken = default) => await JsonSerializer.SerializeAsync(destination, profile, Options, cancellationToken);
    public async Task<GaitCalibrationProfile> LoadAsync(Stream source, CancellationToken cancellationToken = default)
    {
        var profile = await JsonSerializer.DeserializeAsync<GaitCalibrationProfile>(source, Options, cancellationToken) ?? throw new InvalidDataException("Calibration file is empty.");
        if (profile.Version != 1) throw new InvalidDataException($"Unsupported calibration version {profile.Version}."); return profile;
    }
}
