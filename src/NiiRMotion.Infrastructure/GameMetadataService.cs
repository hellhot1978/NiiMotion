using System.Net.Http.Json;
using System.Text.Json;

namespace NiiRMotion.Infrastructure;

public sealed record GameMetadata(string Name, string Summary, string? CoverUrl, string Source);

public sealed class GameMetadataService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static string CacheDirectory => Path.Combine(NiiMotionPaths.Config, "game-metadata");

    public async Task<(GameMetadata Metadata, string? CoverPath)> GetAsync(string appId, string fallbackName, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(CacheDirectory);
        var metadataPath = Path.Combine(CacheDirectory, appId + ".json"); var coverPath = Path.Combine(CacheDirectory, appId + ".jpg");
        GameMetadata? metadata = null;
        try { if (File.Exists(metadataPath)) metadata = JsonSerializer.Deserialize<GameMetadata>(await File.ReadAllTextAsync(metadataPath, cancellationToken)); } catch { }
        if (metadata is null)
        {
            metadata = await FetchFromConfiguredIgdbProxyAsync(appId, cancellationToken) ?? await FetchSteamAsync(appId, fallbackName, cancellationToken)
                ?? new GameMetadata(fallbackName, "Yerel Steam kurulumu", null, "Yerel");
            await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        }
        if (!File.Exists(coverPath) && Uri.TryCreate(metadata.CoverUrl, UriKind.Absolute, out var coverUri))
        {
            try { await File.WriteAllBytesAsync(coverPath, await Http.GetByteArrayAsync(coverUri, cancellationToken), cancellationToken); } catch { }
        }
        return (metadata, File.Exists(coverPath) ? coverPath : null);
    }

    private static async Task<GameMetadata?> FetchFromConfiguredIgdbProxyAsync(string appId, CancellationToken token)
    {
        var proxy = Environment.GetEnvironmentVariable("NIIRMOTION_IGDB_PROXY_URL");
        if (string.IsNullOrWhiteSpace(proxy)) return null;
        try { return await Http.GetFromJsonAsync<GameMetadata>($"{proxy.TrimEnd('/')}?steamAppId={Uri.EscapeDataString(appId)}", token); } catch { return null; }
    }

    private static async Task<GameMetadata?> FetchSteamAsync(string appId, string fallbackName, CancellationToken token)
    {
        try
        {
            using var stream = await Http.GetStreamAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&l=turkish", token); using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            var entry = json.RootElement.GetProperty(appId); if (!entry.GetProperty("success").GetBoolean()) return null; var data = entry.GetProperty("data");
            return new GameMetadata(data.TryGetProperty("name", out var name) ? name.GetString() ?? fallbackName : fallbackName, data.TryGetProperty("short_description", out var summary) ? summary.GetString() ?? "" : "", data.TryGetProperty("header_image", out var cover) ? cover.GetString() : null, "Steam");
        }
        catch { return null; }
    }
}
