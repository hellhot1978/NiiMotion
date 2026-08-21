using System.Text.Json;

namespace NiiRMotion.Infrastructure;

public sealed record PsMoveAssignments(int SchemaVersion, string LeftStableId, string RightStableId, DateTimeOffset UpdatedAtUtc)
{
    public bool IsComplete => !string.IsNullOrWhiteSpace(LeftStableId)
        && !string.IsNullOrWhiteSpace(RightStableId)
        && !string.Equals(LeftStableId, RightStableId, StringComparison.OrdinalIgnoreCase);
}

public sealed class PsMoveAssignmentStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<PsMoveAssignments?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<PsMoveAssignments>(stream, JsonOptions, cancellationToken);
    }

    public async Task SaveAsync(string leftStableId, string rightStableId, CancellationToken cancellationToken = default)
    {
        var assignments = new PsMoveAssignments(1, Normalize(leftStableId), Normalize(rightStableId), DateTimeOffset.UtcNow);
        if (!assignments.IsComplete) throw new ArgumentException("PS Move left and right identities must be present and different.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(assignments, JsonOptions), cancellationToken);
        File.Move(temporary, path, true);
    }

    private static string Normalize(string value)
        => new(value.Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());
}
