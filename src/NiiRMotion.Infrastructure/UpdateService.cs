using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace NiiRMotion.Infrastructure;

public sealed record NiiMotionUpdateManifest(string Version, string InstallerUrl, string Sha256, string? ReleaseNotesUrl)
{
    public long? SizeBytes { get; init; }
}
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

    public async Task<string> DownloadVerifiedAsync(NiiMotionUpdateManifest manifest, string? stagingDirectory = null, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(manifest.InstallerUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Güncelleme paketi güvenlik bilgisi geçersiz.");
        var root = stagingDirectory ?? Path.Combine(NiiMotionPaths.Root, "updates", "staged"); Directory.CreateDirectory(root);
        var final = Path.Combine(root, $"NiiMotion-{Version.Parse(manifest.Version)}.exe"); var temp = final + ".download";
        using var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken); response.EnsureSuccessStatusCode();
        var announced = response.Content.Headers.ContentLength; const long maximum = 512L * 1024 * 1024;
        if (announced > maximum || manifest.SizeBytes is > 0 && announced is not null && announced != manifest.SizeBytes) throw new InvalidDataException("Güncelleme paketi boyutu bildirimle uyuşmuyor.");
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken)) await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920]; long total = 0; int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0) { total += read; if (total > maximum) throw new InvalidDataException("Güncelleme paketi güvenli boyut sınırını aşıyor."); await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken); }
            if (manifest.SizeBytes is > 0 && total != manifest.SizeBytes) throw new InvalidDataException("Güncelleme paketi eksik indirildi.");
        }
        string actual;
        await using (var file = File.OpenRead(temp)) actual = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
        if (!actual.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase)) { File.Delete(temp); throw new InvalidDataException("Güncelleme doğrulaması başarısız; paket çalıştırılmadı."); }
        File.Move(temp, final, true); return final;
    }
}
