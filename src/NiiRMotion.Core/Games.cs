namespace NiiRMotion.Core;

public enum GameIntegrationState { Ready, InstalledUnsupported, NotInstalled, VerificationRequired }
public sealed record GameDefinition(string Id, string Name, string? SteamAppId, string Runtime, bool MotionSupported, string Summary);
public sealed record InstalledGame(GameDefinition Definition, bool IsInstalled, string? InstallPath)
{
    public GameIntegrationState State => Definition.Id == "zelda-botw" ? GameIntegrationState.VerificationRequired
        : !IsInstalled ? GameIntegrationState.NotInstalled
        : Definition.MotionSupported ? GameIntegrationState.Ready : GameIntegrationState.InstalledUnsupported;
}

public static class BuiltInGames
{
    public static IReadOnlyList<GameDefinition> All { get; } =
    [
        new("half-life-alyx", "Half-Life: Alyx", "546560", "SteamVR / OpenVR", true, "Analog yürüyüş eşlemesi doğrulandı"),
        new("arizona-sunshine-2", "Arizona Sunshine 2", "1540210", "SteamVR", true, "Geri alınabilir kontrolcü eşlemesi hazır"),
        new("metro-awakening", "Metro Awakening", "2669410", "OpenXR API Layer", true, "Geri alınabilir OpenXR analog köprüsü hazır · oyun içi doğrulama bekliyor"),
        new("skyrim-vr", "The Elder Scrolls V: Skyrim VR", "611670", "SteamVR", false, "Kurulum ve mod yapısı doğrulanmadan değiştirilmez"),
        new("zelda-botw", "Zelda: Breath of the Wild", null, "Cemu / mevcut zincir", false, "Önceki entegrasyon yolu bulunmadan yeniden oluşturulmaz")
    ];
}
