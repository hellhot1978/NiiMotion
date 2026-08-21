using System.Text.Json;

namespace NiiRMotion.Core;

public sealed record PersonalBoardMotion(
    double LeftStepThreshold,
    double RightStepThreshold,
    double SlowCadenceHz,
    double NaturalCadenceHz,
    double FastCadenceHz,
    double TurnCopYThreshold,
    double ContactKg = 10,
    double TurnLeftThreshold = -0.45,
    double TurnRightThreshold = 0.45,
    double TurnHoldSeconds = 0.40,
    double TurnSpeed = 0.65)
{
    public double SpeedFor(double cadenceHz)
    {
        if (cadenceHz <= SlowCadenceHz) return Math.Clamp(.62 * cadenceHz / SlowCadenceHz, .52, .62);
        if (cadenceHz <= NaturalCadenceHz) return Lerp(.62, .82, (cadenceHz - SlowCadenceHz) / (NaturalCadenceHz - SlowCadenceHz));
        return Lerp(.82, 1, Math.Clamp((cadenceHz - NaturalCadenceHz) / (FastCadenceHz - NaturalCadenceHz), 0, 1));
    }

    public static async Task<PersonalBoardMotion> LoadAsync(string path, CancellationToken token = default)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<PersonalBoardMotion>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, token)
            ?? throw new InvalidDataException("Balance Board hareket profili boş.");
    }

    private static double Lerp(double from, double to, double amount) => from + (to - from) * amount;
}
