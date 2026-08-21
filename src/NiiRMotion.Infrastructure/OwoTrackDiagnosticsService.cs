using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed record PhoneLiveDiagnostics(string Endpoint, int SampleCount, SensorTimingSnapshot Timing, long MissingPackets, long OutOfOrderPackets);
public sealed class OwoTrackDiagnosticsService
{
    public async Task<PhoneLiveDiagnostics> RunAsync(int sampleCount = 120, CancellationToken cancellationToken = default)
    {
        await using var source = new OwoTrackSensorSource(); await source.StartAsync(cancellationToken); var count = 0;
        await foreach (var _ in source.Samples.ReadAllAsync(cancellationToken)) { if (++count >= sampleCount) break; }
        return new(source.PhoneEndpoint?.ToString() ?? "unknown", count, source.Timing, source.MissingPackets, source.OutOfOrderPackets);
    }
}
