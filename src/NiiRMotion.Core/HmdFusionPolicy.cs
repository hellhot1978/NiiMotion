using System.Diagnostics;

namespace NiiRMotion.Core;

public sealed record HmdFusionDecision(FusionSnapshot Snapshot, bool Fresh, bool Turning, bool SuppressedFalseForward);

public static class HmdFusionPolicy
{
    public const double TurnRateThresholdRadiansPerSecond = 1.2;
    private static readonly long FreshTicks = (long)(Stopwatch.Frequency * .35);

    public static HmdFusionDecision Apply(FusionSnapshot snapshot, HmdPoseSample hmd, long nowTicks, bool enabled)
    {
        if (!enabled || !hmd.IsTracked) return new(snapshot, false, false, false);
        var age = nowTicks - hmd.Timestamp.MonotonicTicks;
        if (age < 0 || age > FreshTicks) return new(snapshot, false, false, false);

        var turning = Math.Abs(hmd.YawRateRadiansPerSecond) >= TurnRateThresholdRadiansPerSecond;
        var weakForwardEvidence = snapshot.Gait.CadenceHz < 1.1 || snapshot.GlobalConfidence < .55;
        if (!turning || !weakForwardEvidence || snapshot.TargetSpeed <= 0) return new(snapshot, true, turning, false);
        return new(snapshot with { TargetSpeed = 0 }, true, true, true);
    }
}
