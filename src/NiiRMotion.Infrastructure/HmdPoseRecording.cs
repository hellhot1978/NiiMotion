using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class HmdPoseRecorder
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { IncludeFields = true };
    public async Task RecordAsync(IAsyncEnumerable<HmdPoseSample> samples, Stream destination, CancellationToken token = default)
    {
        await using var writer = new StreamWriter(destination, leaveOpen: true);
        await foreach (var sample in samples.WithCancellation(token)) await writer.WriteLineAsync(JsonSerializer.Serialize(sample, Options));
        await writer.FlushAsync(token);
    }
}

public sealed class HmdPoseReplayReader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { IncludeFields = true };
    public async IAsyncEnumerable<HmdPoseSample> ReadAsync(Stream source, double speed = 1, [EnumeratorCancellation] CancellationToken token = default)
    {
        if (speed <= 0) throw new ArgumentOutOfRangeException(nameof(speed));
        using var reader = new StreamReader(source, leaveOpen: true); long? first = null; var start = Stopwatch.GetTimestamp();
        while (await reader.ReadLineAsync(token) is { } line)
        {
            var sample = JsonSerializer.Deserialize<HmdPoseSample>(line, Options); first ??= sample.Timestamp.MonotonicTicks;
            var target = (long)((sample.Timestamp.MonotonicTicks - first.Value) / speed); var remaining = target - (Stopwatch.GetTimestamp() - start);
            if (remaining > 0) await Task.Delay(TimeSpan.FromSeconds(remaining / (double)Stopwatch.Frequency), token);
            yield return sample with { Timestamp = new SensorTimestamp(Stopwatch.GetTimestamp(), DateTimeOffset.UtcNow) };
        }
    }
}
