namespace NiiRMotion.Core;

public sealed record UserGameAdapter(
    string Id,
    string Name,
    string SteamAppId,
    string ActionSet,
    string MovementAction,
    string? ActivationAction,
    double SpeedMultiplier,
    DateTimeOffset CreatedAt)
{
    public int SchemaVersion { get; init; } = 2;
    public string Runtime { get; init; } = "SteamVR";
    public string MappingVersion { get; init; } = "user-steamvr-v1";
    public bool Reversible { get; init; } = true;
}

public static class GameAdapterValidator
{
    public static IReadOnlyList<string> Validate(UserGameAdapter adapter)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(adapter.Name)) errors.Add("Oyun adı gerekli.");
        if (string.IsNullOrWhiteSpace(adapter.SteamAppId) || !adapter.SteamAppId.All(char.IsDigit)) errors.Add("Steam App ID yalnız rakamlardan oluşmalı.");
        if (!ValidAction(adapter.ActionSet)) errors.Add("Action set /actions/... biçiminde olmalı.");
        if (!ValidAction(adapter.MovementAction)) errors.Add("Analog hareket action'ı /actions/... biçiminde olmalı.");
        if (!string.IsNullOrWhiteSpace(adapter.ActivationAction) && !ValidAction(adapter.ActivationAction)) errors.Add("Koşma/yürüme action'ı /actions/... biçiminde olmalı.");
        if (adapter.SpeedMultiplier is < 0.25 or > 3.0) errors.Add("Hız çarpanı 0,25 ile 3,00 arasında olmalı.");
        if (adapter.SchemaVersion != 2) errors.Add("Desteklenmeyen oyun adaptörü sürümü.");
        if (!adapter.Runtime.Equals("SteamVR", StringComparison.OrdinalIgnoreCase)) errors.Add("Otomatik eşleme şu anda yalnız doğrulanmış SteamVR action oyunlarında kullanılabilir.");
        if (!adapter.Reversible) errors.Add("Geri alınamayan oyun eşlemesi kurulamaz.");
        return errors;
    }

    private static bool ValidAction(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.StartsWith("/actions/", StringComparison.OrdinalIgnoreCase)
        && !value.Any(char.IsWhiteSpace);
}
