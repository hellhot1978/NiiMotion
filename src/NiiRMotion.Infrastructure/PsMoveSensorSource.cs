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
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _readers.Add(ReadSideWithReconnectAsync(assignments.LeftStableId, LegSide.Left, GetCalibration(assignments.LeftStableId), _lifetime.Token));
        _readers.Add(ReadSideWithReconnectAsync(assignments.RightStableId, LegSide.Right, GetCalibration(assignments.RightStableId), _lifetime.Token));

        PsMoveZcm1FactoryCalibration GetCalibration(string stableId) => calibrations.TryGetValue(stableId, out var calibration)
            ? calibration
            : throw new InvalidOperationException($"Assigned PS Move {stableId} has no factory calibration.");
    }

    private async Task ReadSideWithReconnectAsync(string stableId, LegSide side, PsMoveZcm1FactoryCalibration calibration, CancellationToken cancellationToken)
    {
        var wasConnected = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            var probe = new PsMoveDiagnosticsService().Discover()
                .Where(x => x.SensorReportsPossible)
                .SingleOrDefault(x => string.Equals(x.Device.StableId, stableId, StringComparison.OrdinalIgnoreCase));
            if (probe is null)
            {
                if (wasConnected) await NiiMotionEventLog.WriteAsync("psmove", "disconnected", $"{side} PS Move disconnected; locomotion evidence is now stale.", new { stableId }, cancellationToken);
                wasConnected = false;
                await Task.Delay(750, cancellationToken);
                continue;
            }

            try
            {
                await using var stream = new FileStream(probe.Device.DevicePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, probe.InputReportBytes, FileOptions.Asynchronous);
                if (!wasConnected) await NiiMotionEventLog.WriteAsync("psmove", "connected", $"{side} PS Move connected.", new { stableId }, cancellationToken);
                wasConnected = true;
                await ReadLoopAsync(stream, stableId, side, calibration, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                if (wasConnected) await NiiMotionEventLog.WriteAsync("psmove", "read-interrupted", $"{side} PS Move stream interrupted; automatic reconnect is active.", new { stableId, error = ex.GetBaseException().Message }, cancellationToken);
                wasConnected = false;
                await Task.Delay(750, cancellationToken);
            }
        }
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
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { throw; }
    }

    public async ValueTask DisposeAsync()
    {
        if (_lifetime is null) return;
        _lifetime.Cancel();
        try { await Task.WhenAll(_readers); } catch (OperationCanceledException) { }
        _readers.Clear();
        _lifetime.Dispose();
        _lifetime = null;
        _buffer.Complete();
    }
}
