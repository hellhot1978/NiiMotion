using System.Text.Json;
namespace NiiRMotion.Core;
public sealed record PersonalGaitPace(double SlowP95Dps, double NaturalP95Dps, double FastP95Dps)
{
    public double EstimateAnalog(double swingDps)
    {
        if (swingDps <= SlowP95Dps) return Lerp(0.40, 0.55, SmoothStep(swingDps / Math.Max(1, SlowP95Dps)));
        if (swingDps <= NaturalP95Dps) return Lerp(0.55, 0.72, SmoothStep((swingDps - SlowP95Dps) / Math.Max(1, NaturalP95Dps - SlowP95Dps)));
        return Lerp(0.72, 1.0, SmoothStep((swingDps - NaturalP95Dps) / Math.Max(1, FastP95Dps - NaturalP95Dps)));
    }
    public static async Task<PersonalGaitPace> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var value = await JsonSerializer.DeserializeAsync<PersonalGaitPace>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken) ?? throw new InvalidDataException("Kişisel yürüyüş profili boş.");
        if (!(value.SlowP95Dps > 0 && value.NaturalP95Dps > value.SlowP95Dps && value.FastP95Dps > value.NaturalP95Dps)) throw new InvalidDataException("Kişisel yürüyüş hızları sıralı değil.");
        return value;
    }
    private static double SmoothStep(double value)
    {
        var t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }
    private static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);
}
