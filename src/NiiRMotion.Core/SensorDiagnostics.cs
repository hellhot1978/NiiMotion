using System.Diagnostics;

namespace NiiRMotion.Core;

public sealed record SensorTimingSnapshot(long SampleCount, double SampleRateHz, double MeanIntervalMs, double JitterMs, double PacketAgeMs);
public sealed class SensorTimingDiagnostics
{
    private long _count; private long _first; private long _last; private double _mean; private double _m2;
    public void Observe(long monotonicTicks)
    {
        if (_count == 0) { _first = _last = monotonicTicks; _count = 1; return; }
        var intervalMs = (monotonicTicks - _last) * 1000d / Stopwatch.Frequency; _last = monotonicTicks; _count++;
        var n = _count - 1; var delta = intervalMs - _mean; _mean += delta / n; _m2 += delta * (intervalMs - _mean);
    }
    public SensorTimingSnapshot Snapshot(long nowTicks)
    {
        var duration = (_last - _first) / (double)Stopwatch.Frequency;
        var rate = _count > 1 && duration > 0 ? (_count - 1) / duration : 0;
        var jitter = _count > 2 ? Math.Sqrt(_m2 / (_count - 2)) : 0;
        var age = _count > 0 ? (nowTicks - _last) * 1000d / Stopwatch.Frequency : double.PositiveInfinity;
        return new(_count, rate, _mean, jitter, age);
    }
}
