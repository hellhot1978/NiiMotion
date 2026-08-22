namespace NiiRMotion.Core;

public sealed record UserGameAdapter(
    string Id,
    string Name,
    string SteamAppId,
    string ActionSet,
    string MovementAction,
    string? ActivationAction,
    double SpeedMultiplier,
    DateTimeOffset CreatedAt);

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
        return errors;
    }

    private static bool ValidAction(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.StartsWith("/actions/", StringComparison.OrdinalIgnoreCase)
        && !value.Any(char.IsWhiteSpace);
}
