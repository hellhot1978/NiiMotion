using System.Reflection;
using System.Text.Json;

namespace NiiRMotion.Infrastructure;

public sealed record NiiMotionUpdateManifest(string Version, string InstallerUrl, string Sha256, string? ReleaseNotesUrl);
public sealed record NiiMotionUpdateStatus(bool Configured, bool UpdateAvailable, Version CurrentVersion, Version? AvailableVersion, NiiMotionUpdateManifest? Manifest, string Message);

public sealed class UpdateService(HttpClient? client = null)
{
    private readonly HttpClient _client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<NiiMotionUpdateStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        var current = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);
        var endpoint = Environment.GetEnvironmentVariable("NIIRMOTION_UPDATE_MANIFEST_URL")?.Trim();
        if (string.IsNullOrWhiteSpace(endpoint))
            return new(false, false, current, null, null, "Güncelleme kanalı henüz yapılandırılmadı.");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return new(true, false, current, null, null, "Güncelleme adresi güvenli bir HTTPS adresi değil.");

        var json = await _client.GetStringAsync(uri, cancellationToken);
        var manifest = JsonSerializer.Deserialize<NiiMotionUpdateManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Güncelleme bildirimi okunamadı.");
        if (!Version.TryParse(manifest.Version, out var available)) throw new InvalidDataException("Güncelleme sürümü geçersiz.");
        if (!Uri.TryCreate(manifest.InstallerUrl, UriKind.Absolute, out var installer) || installer.Scheme != Uri.UriSchemeHttps || manifest.Sha256.Length != 64)
            throw new InvalidDataException("Güncelleme paketi güvenlik bilgisi geçersiz.");
        return new(true, available > current, current, available, manifest, available > current ? $"NiiMotion {available} hazır." : "NiiMotion güncel.");
    }
}
