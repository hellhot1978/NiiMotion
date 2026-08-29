namespace NiiRMotion.Core;

public sealed record ProfileFusionModel(
    int Version,
    string ProfileId,
    IReadOnlyList<SensorFamily> Sensors,
    DateTimeOffset CreatedAtUtc,
    long AcceptedSamples,
    double CaptureQuality,
    double CadenceToleranceHz,
    int DisagreementGraceMs,
    double PhoneAgreementWeight,
    double BoardAgreementWeight,
    bool RequireLegAgreement,
    bool RequireBoardContact)
{
    public ProfileFusionModel Safe() => this with
    {
        CaptureQuality = Math.Clamp(CaptureQuality, 0, 1),
        CadenceToleranceHz = Math.Clamp(CadenceToleranceHz, .35, 1.5),
        DisagreementGraceMs = Math.Clamp(DisagreementGraceMs, 120, 500),
        PhoneAgreementWeight = Math.Clamp(PhoneAgreementWeight, 0, .20),
        BoardAgreementWeight = Math.Clamp(BoardAgreementWeight, 0, .20)
    };
}
