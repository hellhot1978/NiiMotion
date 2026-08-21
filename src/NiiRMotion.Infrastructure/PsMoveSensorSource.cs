using System.Diagnostics;
using System.Threading.Channels;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class PsMoveSensorSource(
    string assignmentPath,
    string calibrationPath,
    int bufferCapacity = 2048) : ISensorSource<PsMoveImuSample>
{
    private readonly BoundedSensorBuffer<PsMoveImuSample> _buffer = new(bufferCapacity);
    private readonly List<FileStream> _streams = [];
    private readonly List<Task> _readers = [];
    private CancellationTokenSource? _lifetime;

    public string SourceId => "psmove-pair";
    public SensorMode Mode => SensorMode.Live;
    public ChannelReader<PsMoveImuSample> Samples => _buffer.Reader;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_lifetime is not null) return;
        var assignments = await new PsMoveAssignmentStore(assignmentPath).LoadAsync(cancellationToken)
            ?? throw new InvalidOperationException("PS Move left/right assignment is missing.");
        if (!assignments.IsComplete) throw new InvalidOperationException("PS Move left/right assignment is incomplete.");
        var stored = await new PsMoveCalibrationStore(calibrationPath).LoadAsync(cancellationToken);
        var calibrations = stored.ToDictionary(x => x.StableId, x => x.Parse(), StringComparer.OrdinalIgnoreCase);
        var probes = new PsMoveDiagnosticsService().Discover()
            .Where(x => x.SensorReportsPossible && x.Device.StableId is not null)
            .DistinctBy(x => x.Device.StableId!, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var selected = new[]
        {
            Select(probes, calibrations, assignments.LeftStableId, LegSide.Left),
            Select(probes, calibrations, assignments.RightStableId, LegSide.Right)
        };

        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        foreach (var item in selected)
        {
            var stream = new FileStream(item.Probe.Device.DevicePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, item.Probe.InputReportBytes, FileOptions.Asynchronous);
            _streams.Add(stream);
            _readers.Add(ReadLoopAsync(stream, item.Probe.Device.StableId!, item.Side, item.Calibration, _lifetime.Token));
        }
    }

    private static (PsMoveHidProbe Probe, PsMoveZcm1FactoryCalibration Calibration, LegSide Side) Select(
        IReadOnlyList<PsMoveHidProbe> probes,
        IReadOnlyDictionary<string, PsMoveZcm1FactoryCalibration> calibrations,
        string stableId,
        LegSide side)
    {
        var probe = probes.SingleOrDefault(x => x.Device.StableId!.Equals(stableId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Assigned {side} PS Move is not connected.");
        if (!calibrations.TryGetValue(stableId, out var calibration))
            throw new InvalidOperationException($"Assigned {side} PS Move has no factory calibration.");
        return (probe, calibration, side);
    }

    private async Task ReadLoopAsync(FileStream stream, string stableId, LegSide side, PsMoveZcm1FactoryCalibration calibration, CancellationToken cancellationToken)
    {
        var buffer = new byte[PsMoveZcm1ReportParser.InputReportBytes];
        long sequence = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read != buffer.Length) continue;
                var report = PsMoveZcm1ReportParser.Parse(buffer);
                var ticks = Stopwatch.GetTimestamp();
                var received = DateTimeOffset.UtcNow;
                Write(report.OlderSample, 0);
                Write(report.LatestSample, 1);

                void Write(PsMoveRawSample raw, int subSample)
                {
                    _buffer.TryWrite(new(
                        stableId,
                        sequence++,
                        new(ticks, received),
                        side,
                        SensorPlacement.CalfLowerLeg,
                        calibration.CalibrateAcceleration(raw.Acceleration),
                        calibration.CalibrateGyroscope(raw.AngularVelocity),
                        report.Magnetometer,
                        report.Battery,
                        subSample));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { _buffer.Complete(ex); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_lifetime is null) return;
        _lifetime.Cancel();
        try { await Task.WhenAll(_readers); } catch (OperationCanceledException) { }
        foreach (var stream in _streams) await stream.DisposeAsync();
        _streams.Clear();
        _readers.Clear();
        _lifetime.Dispose();
        _lifetime = null;
        _buffer.Complete();
    }
}
