using System.Numerics;
using System.Threading.Channels;

namespace NiiRMotion.Core;

public enum SensorMode { Live, Replay }
public enum SensorPlacement { Unknown, ThighUpperLeg, CalfLowerLeg, Chest, BalanceBoard }
public readonly record struct SensorTimestamp(long MonotonicTicks, DateTimeOffset ReceivedAtUtc);
public interface ISensorSample { string SourceId { get; } long Sequence { get; } SensorTimestamp Timestamp { get; } }
public readonly record struct JoyConImuSample(string SourceId, long Sequence, SensorTimestamp Timestamp, Vector3 AccelerationG, Vector3 AngularVelocityDps, int SubSampleIndex) : ISensorSample;
public readonly record struct PsMoveImuSample(string SourceId, long Sequence, SensorTimestamp Timestamp, LegSide Side, SensorPlacement Placement, Vector3 AccelerationG, Vector3 AngularVelocityRadps, Vector3 MagnetometerRaw, byte Battery, int SubSampleIndex) : ISensorSample;
public readonly record struct PhoneImuSample(string SourceId, long Sequence, SensorTimestamp Timestamp, long SentAtUnixMicroseconds, Quaternion Orientation, Vector3 AccelerationMps2, Vector3 AngularVelocityRadps) : ISensorSample;
public readonly record struct BalanceBoardSample(string SourceId, long Sequence, SensorTimestamp Timestamp, float FrontLeftKg, float FrontRightKg, float BackLeftKg, float BackRightKg) : ISensorSample
{
    public float LeftKg => FrontLeftKg + BackLeftKg;
    public float RightKg => FrontRightKg + BackRightKg;
    public float FrontKg => FrontLeftKg + FrontRightKg;
    public float BackKg => BackLeftKg + BackRightKg;
    public float TotalKg => LeftKg + RightKg;
    public float CenterOfPressureX => TotalKg <= 0.001f ? 0 : Math.Clamp((RightKg - LeftKg) / TotalKg, -1, 1);
    public float CenterOfPressureY => TotalKg <= 0.001f ? 0 : Math.Clamp((FrontKg - BackKg) / TotalKg, -1, 1);
    public bool HasStableContact(float minimumTotalKg = 10) => TotalKg >= minimumTotalKg;
}

public interface ISensorSource<T> : IAsyncDisposable where T : ISensorSample
{
    string SourceId { get; }
    SensorMode Mode { get; }
    ChannelReader<T> Samples { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
}

public sealed class BoundedSensorBuffer<T> where T : ISensorSample
{
    private readonly Channel<T> _channel;
    private long _dropped;
    public BoundedSensorBuffer(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest });
    }
    public ChannelReader<T> Reader => _channel.Reader;
    public long DroppedSamples => Interlocked.Read(ref _dropped);
    public bool TryWrite(T sample)
    {
        if (_channel.Writer.TryWrite(sample)) return true;
        Interlocked.Increment(ref _dropped); return false;
    }
    public void Complete(Exception? error = null) => _channel.Writer.TryComplete(error);
}
