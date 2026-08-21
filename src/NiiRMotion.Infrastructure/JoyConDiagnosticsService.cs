using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed record JoyConLiveDiagnostics(JoyConSide Side, int SampleCount, SensorTimingSnapshot Timing, JoyConImuCalibration Calibration);

public sealed class JoyConDiagnosticsService
{
    public async Task<IReadOnlyList<JoyConLiveDiagnostics>> RunAsync(int sampleCount = 300, CancellationToken cancellationToken = default)
    {
        var devices = HidDeviceEnumerator.FindJoyCons().GroupBy(x => x.Side).Select(x => x.First()).ToArray();
        if (!devices.Any(x => x.Side == JoyConSide.Left) || !devices.Any(x => x.Side == JoyConSide.Right)) throw new InvalidOperationException("Sol ve sağ original Joy-Con bağlı olmalıdır.");
        return await Task.WhenAll(devices.Select(x => ReadOneAsync(x, sampleCount, cancellationToken)));
    }

    private static async Task<JoyConLiveDiagnostics> ReadOneAsync(JoyConDeviceDescriptor device, int sampleCount, CancellationToken cancellationToken)
    {
        await using var source = new JoyConSensorSource(device);
        await source.StartAsync(cancellationToken); var count = 0;
        await foreach (var _ in source.Samples.ReadAllAsync(cancellationToken)) { if (++count >= sampleCount) break; }
        return new(device.Side, count, source.Timing, source.FactoryCalibration ?? throw new InvalidOperationException("Joy-Con factory calibration missing."));
    }
}
