using System.Diagnostics;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed record BalanceBoardMeasurement(
    string Label, int Samples, double DurationSeconds, float MeanWeightKg, float MinimumWeightKg, float MaximumWeightKg,
    float MinimumCopX, float MaximumCopX, float MinimumCopY, float MaximumCopY, int SideTransitions, double EstimatedCadenceHz);

public sealed class BalanceBoardMeasurementService
{
    public async Task<BalanceBoardMeasurement> CaptureAsync(string label, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        await using var source = new BalanceBoardSensorSource();
        await source.StartAsync(cancellationToken);
        _ = await source.Samples.ReadAsync(cancellationToken); // automatic empty-board tare completed
        if (OperatingSystem.IsWindows()) Console.Beep(800, 250);
        await Task.Delay(3000, cancellationToken);
        while (source.Samples.TryRead(out _)) { }
        var samples = new List<BalanceBoardSample>();
        var started = Stopwatch.GetTimestamp();
        var deadline = started + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            var sample = await source.Samples.ReadAsync(cancellationToken);
            samples.Add(sample);
        }
        if (samples.Count == 0) throw new InvalidOperationException("Balance Board ölçümü alınamadı.");
        var transitions = 0; var previousSide = 0;
        foreach (var sample in samples)
        {
            if (!sample.HasStableContact()) continue;
            var side = Math.Abs(sample.CenterOfPressureX) < .035f ? 0 : Math.Sign(sample.CenterOfPressureX);
            if (side != 0 && previousSide != 0 && side != previousSide) transitions++;
            if (side != 0) previousSide = side;
        }
        var elapsed = (samples[^1].Timestamp.MonotonicTicks - samples[0].Timestamp.MonotonicTicks) / (double)Stopwatch.Frequency;
        return new(label, samples.Count, elapsed,
            samples.Average(x => x.TotalKg), samples.Min(x => x.TotalKg), samples.Max(x => x.TotalKg),
            samples.Min(x => x.CenterOfPressureX), samples.Max(x => x.CenterOfPressureX),
            samples.Min(x => x.CenterOfPressureY), samples.Max(x => x.CenterOfPressureY),
            transitions, elapsed > 0 ? transitions / elapsed : 0);
    }
}
