using System.Text.Json;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed record UserExperiencePreferences(int SchemaVersion, string Language, double TextScale, bool HighContrast, bool ReducedMotion, bool OnboardingComplete)
{
    public static UserExperiencePreferences Default => new(1, "tr", 1, false, false, false);
}

public sealed class UserExperienceStore
{
    private readonly string _path;
    public UserExperienceStore(string? configDirectory = null) => _path = Path.Combine(configDirectory ?? NiiMotionPaths.Config, "user-experience.json");
    public UserExperiencePreferences Load() { try { return File.Exists(_path) ? Normalize(JsonSerializer.Deserialize<UserExperiencePreferences>(File.ReadAllText(_path)) ?? UserExperiencePreferences.Default) : UserExperiencePreferences.Default; } catch { return UserExperiencePreferences.Default; } }
    public void Save(UserExperiencePreferences value) { value = Normalize(value); Directory.CreateDirectory(Path.GetDirectoryName(_path)!); var temp = _path + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, _path, true); }
    private static UserExperiencePreferences Normalize(UserExperiencePreferences value) => value with { Language = value.Language is "en" ? "en" : "tr", TextScale = Math.Clamp(value.TextScale, .9, 1.3) };
}

public sealed record GuidanceStep(string Title, string Detail, bool Complete);
public static class FirstUseGuidance
{
    public static IReadOnlyList<GuidanceStep> Build(UserHardwareInventory inventory, CalibrationProgressDocument progress) =>
    [
        new("1. Cihazlarını seç", "Sahip olduğun sensörleri Cihazlarım bölümünde işaretle.", inventory.Sensors.Count > 0),
        new("2. Temel kalibrasyon", "Seçtiğin her hareket cihazının üç temel fazını tamamla.", Selected(inventory).All(sensor => progress.Devices.FirstOrDefault(x => x.Sensor == sensor)?.IsReady == true)),
        new("3. Yürüyüş profilini seç", "Genel Bakış ekranında hazır cihazlarına uygun profili seç.", inventory.Sensors.Count > 0),
        new("4. Oyunu doğrula", "Oyunlar bölümünden VR oyununu seç; NiiMotion bağlantıları sırayla kontrol etsin.", false)
    ];
    private static IEnumerable<SensorFamily> Selected(UserHardwareInventory x)
    {
        if (x.HasJoyCons) yield return SensorFamily.JoyCon; if (x.HasPsMoves) yield return SensorFamily.PsMove;
        if (x.HasPhone) yield return SensorFamily.Phone; if (x.HasBalanceBoard) yield return SensorFamily.BalanceBoard;
    }
}
