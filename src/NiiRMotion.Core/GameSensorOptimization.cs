namespace NiiRMotion.Core;

public sealed record GameSensorOptimization(
    int SchemaVersion,
    string GameId,
    string MotionProfileId,
    double DistanceScale,
    double PreviousDistanceScale,
    string Source,
    double Confidence,
    DateTimeOffset UpdatedAt)
{
    public GameSensorOptimization Safe() => this with
    {
        DistanceScale = Math.Clamp(DistanceScale, .15, 2.5),
        PreviousDistanceScale = Math.Clamp(PreviousDistanceScale, .15, 2.5),
        Confidence = Math.Clamp(Confidence, 0, 1)
    };
}

public sealed record StrideTelemetryMeasurement(
    int PhysicalSteps,
    double AvatarDistanceMeters,
    double TargetMetersPerStep,
    double SampleDurationSeconds,
    bool HadTeleport,
    bool HadLargeTurn);

public static class AutomaticStrideOptimizer
{
    public static bool TryOptimize(double currentScale, StrideTelemetryMeasurement measurement, out double proposedScale, out string reason)
    {
        proposedScale = currentScale;
        if (measurement.PhysicalSteps < 6) { reason = "En az 6 güvenilir fiziksel adım gerekli."; return false; }
        if (measurement.SampleDurationSeconds is < 2 or > 90) { reason = "Ölçüm süresi güvenilir aralığın dışında."; return false; }
        if (measurement.HadTeleport) { reason = "Işınlanma algılandığı için ölçüm kullanılmadı."; return false; }
        if (measurement.HadLargeTurn) { reason = "Keskin dönüş algılandığı için ölçüm kullanılmadı."; return false; }
        if (measurement.AvatarDistanceMeters <= .25 || measurement.TargetMetersPerStep is < .25 or > 1.25)
        { reason = "Oyun içi mesafe güvenilir biçimde ölçülemedi."; return false; }

        var expected = measurement.PhysicalSteps * measurement.TargetMetersPerStep;
        var ratio = Math.Clamp(expected / measurement.AvatarDistanceMeters, .70, 1.30);
        // Never let one noisy in-game capture rewrite the model. Apply only half
        // of the bounded correction; later clean captures converge smoothly.
        proposedScale = Math.Clamp(currentScale * (1 + ((ratio - 1) * .5)), .15, 2.5);
        reason = "Oyun içi mesafe ile fiziksel adımlar güvenle eşleştirildi.";
        return true;
    }
}
