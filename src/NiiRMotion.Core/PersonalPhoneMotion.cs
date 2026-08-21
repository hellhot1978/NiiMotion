using System.Text.Json;
namespace NiiRMotion.Core;
public sealed record PersonalPhoneMotion(double RestGyroP95, double SlowGyroP95, double NaturalGyroP95, double FastGyroP95, double SlowAccelP95, double NaturalAccelP95, double FastAccelP95)
{
    public (double Agreement, double Pace) Estimate(double gyro, double accel)
    {
        var agreement = Math.Clamp((gyro - RestGyroP95) / Math.Max(.05, SlowGyroP95 - RestGyroP95), 0, 1);
        var gyroPace = Map(gyro, SlowGyroP95, NaturalGyroP95, FastGyroP95);
        var accelPace = Map(accel, SlowAccelP95, NaturalAccelP95, FastAccelP95);
        return (agreement, gyroPace * .35 + accelPace * .65);
    }
    public static async Task<PersonalPhoneMotion> LoadAsync(string path, CancellationToken token = default)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<PersonalPhoneMotion>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, token) ?? throw new InvalidDataException("Telefon hareket profili boş.");
    }
    private static double Map(double value, double slow, double natural, double fast) => value <= natural
        ? .60 + .22 * Math.Clamp((value - slow) / Math.Max(.01, natural - slow), 0, 1)
        : .82 + .18 * Math.Clamp((value - natural) / Math.Max(.01, fast - natural), 0, 1);
}
