namespace NiiRMotion.Core;

public static class HybridGaitFusion
{
    public static FusionSnapshot Combine(FusionSnapshot primary, GaitSnapshot secondary)
    {
        var primaryActive = primary.TargetSpeed > 0;
        var secondaryActive = secondary.TargetSpeed > 0;
        var target = primaryActive && secondaryActive
            ? primary.TargetSpeed * .52 + secondary.TargetSpeed * .48
            : primaryActive ? primary.TargetSpeed * .65
            : secondaryActive ? secondary.TargetSpeed * .65
            : 0;
        var confidence = primaryActive && secondaryActive
            ? Math.Clamp((primary.GlobalConfidence + secondary.Confidence) * .58, 0, 1)
            : Math.Max(primary.GlobalConfidence, secondary.Confidence) * .65;
        return primary with
        {
            TargetSpeed = target,
            GlobalConfidence = confidence,
            Gait = target > 0 && secondary.Confidence > primary.Gait.Confidence ? secondary : primary.Gait
        };
    }
}

public sealed class HybridGaitAgreementGate(TimeSpan? disagreementGrace = null, double cadenceToleranceHz = 1.20)
{
    private readonly long _graceTicks = (long)((disagreementGrace ?? TimeSpan.FromMilliseconds(360)).TotalSeconds * System.Diagnostics.Stopwatch.Frequency);
    private bool _established;
    private long _lastAgreementTicks;
    private readonly double _cadenceToleranceHz = Math.Clamp(cadenceToleranceHz, .35, 1.5);

    public FusionSnapshot Combine(FusionSnapshot primary, GaitSnapshot secondary, long nowTicks)
    {
        var primaryActive = primary.TargetSpeed > 0;
        var secondaryActive = secondary.TargetSpeed > 0;
        var cadenceAgrees = Math.Abs(primary.Gait.CadenceHz - secondary.CadenceHz) <= _cadenceToleranceHz;
        if (primaryActive && secondaryActive && cadenceAgrees)
        {
            _established = true;
            _lastAgreementTicks = nowTicks;
            return HybridGaitFusion.Combine(primary, secondary);
        }
        if (!primaryActive && !secondaryActive)
        {
            _established = false;
            return HybridGaitFusion.Combine(primary, secondary);
        }
        if (_established && nowTicks >= _lastAgreementTicks && nowTicks - _lastAgreementTicks <= _graceTicks)
            return HybridGaitFusion.Combine(primary, secondary);

        _established = false;
        var source = primaryActive ? primary.Gait : secondary;
        return primary with
        {
            Gait = source with { State = GaitState.Idle, Confidence = 0, TargetSpeed = 0 },
            GlobalConfidence = 0,
            TargetSpeed = 0
        };
    }
}
