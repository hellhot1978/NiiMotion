using System.Runtime.CompilerServices;
using System.Text.Json;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed record UnifiedSensorStream(SensorFamily Sensor, string Name, string RelativePath, int Samples);
public sealed record UnifiedSensorSessionManifest(int Version, string SessionId, string Purpose, string? ProfileId, int Phase, DateTimeOffset CompletedAtUtc, IReadOnlyList<UnifiedSensorStream> Streams);
public sealed record UnifiedReplaySample(SensorFamily Sensor, string Stream, long Sequence, long MonotonicTicks, JsonElement Sample);

public static class UnifiedSensorSessionWriter
{
    public static async Task<string> WriteAsync(string sessionRoot, string purpose, string? profileId, int phase, IEnumerable<GuidedCalibrationResult> results, CancellationToken token = default)
    {
        var fullRoot = Path.GetFullPath(sessionRoot); Directory.CreateDirectory(fullRoot);
        var streams = new List<UnifiedSensorStream>();
        foreach (var result in results)
        {
            foreach (var (name, samples) in result.Samples)
            {
                var file = FileName(result.Sensor, name);
                var absolute = Path.Combine(result.Folder, file);
                if (!File.Exists(absolute)) continue;
                var relative = Path.GetRelativePath(fullRoot, absolute);
                if (relative.StartsWith("..", StringComparison.Ordinal)) throw new InvalidDataException("Sensör akışı oturum klasörünün dışında.");
                streams.Add(new(result.Sensor, name, relative, samples));
            }
        }
        var manifest = new UnifiedSensorSessionManifest(1, Path.GetFileName(fullRoot), purpose, profileId, phase, DateTimeOffset.UtcNow, streams);
        var path = Path.Combine(fullRoot, "unified-session.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), token);
        return path;
    }

    private static string FileName(SensorFamily sensor, string stream) => sensor switch
    {
        SensorFamily.JoyCon when stream.Equals("left", StringComparison.OrdinalIgnoreCase) => "left.jsonl",
        SensorFamily.JoyCon => "right.jsonl",
        SensorFamily.PsMove => "moves.jsonl",
        SensorFamily.Phone => "phone.jsonl",
        _ => "board.jsonl"
    };
}

public sealed class UnifiedSensorSessionReplay
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public async IAsyncEnumerable<UnifiedReplaySample> ReadAsync(string manifestPath, [EnumeratorCancellation] CancellationToken token = default)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var manifest = JsonSerializer.Deserialize<UnifiedSensorSessionManifest>(await File.ReadAllTextAsync(manifestPath, token), Options) ?? throw new InvalidDataException("Birleşik oturum manifesti boş.");
        if (manifest.Version != 1) throw new InvalidDataException($"Desteklenmeyen oturum sürümü: {manifest.Version}");
        var readers = new List<ReplayCursor>();
        try
        {
            foreach (var stream in manifest.Streams)
            {
                var path = Path.GetFullPath(Path.Combine(root, stream.RelativePath));
                if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Replay akışı oturum klasörünün dışında.");
                var cursor = new ReplayCursor(stream, new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)));
                if (await cursor.MoveNextAsync(token)) readers.Add(cursor); else cursor.Dispose();
            }
            var queue = new PriorityQueue<ReplayCursor, long>();
            foreach (var cursor in readers) queue.Enqueue(cursor, cursor.Current!.MonotonicTicks);
            while (queue.TryDequeue(out var cursor, out _))
            {
                token.ThrowIfCancellationRequested();
                yield return cursor.Current!;
                if (await cursor.MoveNextAsync(token)) queue.Enqueue(cursor, cursor.Current!.MonotonicTicks);
            }
        }
        finally { foreach (var reader in readers) reader.Dispose(); }
    }

    private sealed class ReplayCursor(UnifiedSensorStream stream, StreamReader reader) : IDisposable
    {
        public UnifiedReplaySample? Current { get; private set; }
        public async Task<bool> MoveNextAsync(CancellationToken token)
        {
            while (await reader.ReadLineAsync(token) is { } line)
            {
                try
                {
                    using var document = JsonDocument.Parse(line); var sample = document.RootElement;
                    if (!TryProperty(sample, "sequence", out var sequence) || !sequence.TryGetInt64(out var seq)) continue;
                    if (!TryProperty(sample, "timestamp", out var timestamp) || !TryProperty(timestamp, "monotonicTicks", out var ticks) || !ticks.TryGetInt64(out var monotonic)) continue;
                    Current = new(stream.Sensor, stream.Name, seq, monotonic, sample.Clone()); return true;
                }
                catch (JsonException) { }
            }
            Current = null; return false;
        }
        public void Dispose() => reader.Dispose();
        private static bool TryProperty(JsonElement value, string name, out JsonElement property)
        {
            if (value.TryGetProperty(name, out property)) return true;
            foreach (var candidate in value.EnumerateObject()) if (candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { property = candidate.Value; return true; }
            property = default; return false;
        }
    }
}
