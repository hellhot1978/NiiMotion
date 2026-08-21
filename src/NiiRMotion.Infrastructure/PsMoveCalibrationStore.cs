using System.Security.Cryptography;
using System.Text.Json;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed record StoredPsMoveCalibration(
    int SchemaVersion,
    string StableId,
    string Side,
    string BlobBase64,
    string BlobSha256,
    DateTimeOffset CapturedAtUtc)
{
    public PsMoveZcm1FactoryCalibration Parse() => PsMoveZcm1FactoryCalibration.Parse(Convert.FromBase64String(BlobBase64));
}

public sealed class PsMoveCalibrationStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<IReadOnlyList<StoredPsMoveCalibration>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return Array.Empty<StoredPsMoveCalibration>();
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<StoredPsMoveCalibration[]>(stream, JsonOptions, cancellationToken) ?? [];
    }

    public async Task SaveAsync(string stableId, string side, byte[] blob, CancellationToken cancellationToken = default)
    {
        _ = PsMoveZcm1FactoryCalibration.Parse(blob);
        var normalizedId = new string(stableId.Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());
        var normalizedSide = side.Equals("left", StringComparison.OrdinalIgnoreCase) ? "Left"
            : side.Equals("right", StringComparison.OrdinalIgnoreCase) ? "Right"
            : throw new ArgumentException("Side must be Left or Right.", nameof(side));
        var item = new StoredPsMoveCalibration(1, normalizedId, normalizedSide, Convert.ToBase64String(blob), Convert.ToHexString(SHA256.HashData(blob)), DateTimeOffset.UtcNow);
        var items = (await LoadAsync(cancellationToken)).Where(x => !x.StableId.Equals(normalizedId, StringComparison.OrdinalIgnoreCase)).Append(item).OrderBy(x => x.Side).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(items, JsonOptions), cancellationToken);
        File.Move(temporary, path, true);
    }
}
