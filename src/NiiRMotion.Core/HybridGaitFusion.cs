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
