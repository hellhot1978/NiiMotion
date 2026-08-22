using System.Text.Json;

namespace NiiRMotion.Infrastructure;

public sealed record GameCapabilityReport(string AppId, string Name, bool Installed, string Runtime, int ActionCount, string Recommendation, DateTimeOffset InspectedAt);

public sealed class GameCapabilityReportService
{
    public IReadOnlyList<GameCapabilityReport> InspectKnownGames()
    {
        var discovery = new SteamActionDiscovery();
        return new SteamGameCatalog().Detect()
            .Where(x => x.Definition.SteamAppId is "2669410" or "611670")
            .Select(x =>
            {
                if (!x.IsInstalled || string.IsNullOrWhiteSpace(x.InstallPath))
                    return new GameCapabilityReport(x.Definition.SteamAppId!, x.Definition.Name, false, "Bulunamadı", 0, "Oyun kurulduktan sonra salt okunur inceleme yeniden çalıştırılır.", DateTimeOffset.Now);
                var result = discovery.Inspect(x.InstallPath);
                var recommendation = result.Runtime == VrInputRuntime.OpenXr
                    ? "OpenXR kullanıyor. SteamVR action eşlemesi üretilmedi; oyun dosyalarına dokunmadan özel adaptör doğrulaması gerekir."
                    : result.Actions.Count > 0 ? "SteamVR action adayları bulundu; kullanıcı onayı ve oyun içi doğrulamayla adaptör oluşturulabilir." : result.Message;
                return new GameCapabilityReport(x.Definition.SteamAppId!, x.Definition.Name, true, result.Runtime.ToString(), result.Actions.Count, recommendation, DateTimeOffset.Now);
            }).ToArray();
    }

    public string Save()
    {
        Directory.CreateDirectory(NiiMotionPaths.Logs);
        var path = Path.Combine(NiiMotionPaths.Logs, "game-capabilities.json");
        File.WriteAllText(path, JsonSerializer.Serialize(InspectKnownGames(), new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }
}
