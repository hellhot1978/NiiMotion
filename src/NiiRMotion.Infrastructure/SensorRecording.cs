using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class JsonLinesSensorRecorder
{
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web) { IncludeFields = true };
    public async Task RecordAsync<T>(IAsyncEnumerable<T> samples, Stream destination, CancellationToken cancellationToken = default) where T : ISensorSample
    {
        await using var writer = new StreamWriter(destination, leaveOpen: true);
        await foreach (var sample in samples.WithCancellation(cancellationToken)) await writer.WriteLineAsync(JsonSerializer.Serialize(sample, _options));
        await writer.FlushAsync(cancellationToken);
    }
}

public sealed class JoyConReplayReader
{
    public async IAsyncEnumerable<JoyConImuSample> ReadAsync(Stream source, double speed = 1.0, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (speed <= 0) throw new ArgumentOutOfRangeException(nameof(speed));
        using var reader = new StreamReader(source, leaveOpen: true);
        long? firstTicks = null; var replayStart = Stopwatch.GetTimestamp();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var sample = JsonSerializer.Deserialize<JoyConImuSample>(line, new JsonSerializerOptions(JsonSerializerDefaults.Web) { IncludeFields = true });
            firstTicks ??= sample.Timestamp.MonotonicTicks;
            var target = (long)((sample.Timestamp.MonotonicTicks - firstTicks.Value) / speed);
            var remaining = target - (Stopwatch.GetTimestamp() - replayStart);
            if (remaining > 0) await Task.Delay(TimeSpan.FromSeconds((double)remaining / Stopwatch.Frequency), cancellationToken);
            yield return sample with { Timestamp = new SensorTimestamp(Stopwatch.GetTimestamp(), DateTimeOffset.UtcNow) };
        }
    }
}

public sealed class PhoneReplayReader
{
    public async IAsyncEnumerable<PhoneImuSample> ReadAsync(Stream source, double speed = 1.0, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (speed <= 0) throw new ArgumentOutOfRangeException(nameof(speed));
        using var reader = new StreamReader(source, leaveOpen: true); long? firstTicks = null; var replayStart = Stopwatch.GetTimestamp();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var sample = JsonSerializer.Deserialize<PhoneImuSample>(line, new JsonSerializerOptions(JsonSerializerDefaults.Web) { IncludeFields = true }); firstTicks ??= sample.Timestamp.MonotonicTicks;
            var target = (long)((sample.Timestamp.MonotonicTicks - firstTicks.Value) / speed); var remaining = target - (Stopwatch.GetTimestamp() - replayStart);
            if (remaining > 0) await Task.Delay(TimeSpan.FromSeconds((double)remaining / Stopwatch.Frequency), cancellationToken);
            yield return sample with { Timestamp = new SensorTimestamp(Stopwatch.GetTimestamp(), DateTimeOffset.UtcNow) };
        }
    }
}

public sealed class BalanceBoardReplayReader
{
    public async IAsyncEnumerable<BalanceBoardSample> ReadAsync(Stream source, double speed = 1.0, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (speed <= 0) throw new ArgumentOutOfRangeException(nameof(speed));
        using var reader = new StreamReader(source, leaveOpen: true); long? firstTicks = null; var replayStart = Stopwatch.GetTimestamp();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var sample = JsonSerializer.Deserialize<BalanceBoardSample>(line, new JsonSerializerOptions(JsonSerializerDefaults.Web) { IncludeFields = true }); firstTicks ??= sample.Timestamp.MonotonicTicks;
            var target = (long)((sample.Timestamp.MonotonicTicks - firstTicks.Value) / speed); var remaining = target - (Stopwatch.GetTimestamp() - replayStart);
            if (remaining > 0) await Task.Delay(TimeSpan.FromSeconds((double)remaining / Stopwatch.Frequency), cancellationToken);
            yield return sample with { Timestamp = new SensorTimestamp(Stopwatch.GetTimestamp(), DateTimeOffset.UtcNow) };
        }
    }
}
