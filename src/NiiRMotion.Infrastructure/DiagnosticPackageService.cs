using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed record FriendlyDiagnostic(string Title, string Explanation, string Action, string Severity);
public sealed record DiagnosticReport(DateTimeOffset CreatedAtUtc, string AppVersion, string OperatingSystem, string Profile, IReadOnlyList<FriendlyDiagnostic> Findings);

public sealed partial class DiagnosticPackageService
{
    public async Task<DiagnosticReport> AnalyzeAsync(MotionProfile profile, CancellationToken cancellationToken = default)
    {
        var devices = await new HardwareDiscoveryService().ScanAsync(cancellationToken);
        var required = profile.Required;
        var findings = new List<FriendlyDiagnostic>();
        foreach (var device in devices.Where(x => required.Contains(x.Kind) && x.State != DeviceState.Connected))
        {
            findings.Add(new(
                device.Kind is DeviceKind.PsMoveLeft or DeviceKind.PsMoveRight ? $"{device.Name} uyuyor veya bağlantısı kesildi" : $"{device.Name} hazır değil",
                FriendlyDetail(device),
                device.Action,
                device.State == DeviceState.Missing ? "error" : "warning"));
        }
        if (findings.Count == 0) findings.Add(new("Seçili profil hazır", "Zorunlu cihazlarda belirgin bir bağlantı sorunu bulunmadı.", "VR'yi hazırlayıp başlatabilirsin.", "ok"));
        return new(DateTimeOffset.UtcNow, Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown", Environment.OSVersion.VersionString, profile.Name, findings);
    }

    public async Task<string> ExportAsync(MotionProfile profile, string? destinationFolder = null, CancellationToken cancellationToken = default)
    {
        var report = await AnalyzeAsync(profile, cancellationToken);
        destinationFolder ??= Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        Directory.CreateDirectory(destinationFolder);
        var path = Path.Combine(destinationFolder, $"NiiMotion-Tani-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(zip, "report.json", Redact(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })));
        AddRedactedTail(zip, Path.Combine(NiiMotionPaths.Logs, "events.jsonl"), "events-redacted.jsonl", 2 * 1024 * 1024);
        var steamLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "logs", "vrserver.txt");
        AddRedactedTail(zip, steamLog, "steamvr-tail-redacted.txt", 256 * 1024);
        Write(zip, "privacy.txt", "Bu paket ham sensör kayıtlarını, kişisel hareket modelini veya oyun kayıtlarını içermez. IP, Bluetooth kimliği ve kullanıcı klasörü yolları maskelenmiştir.");
        await NiiMotionEventLog.WriteAsync("diagnostics", "package-created", "Privacy-redacted diagnostic package created.", new { fileName = Path.GetFileName(path) }, cancellationToken);
        return path;
    }

    private static string FriendlyDetail(DeviceStatus device) => device.Kind switch
    {
        DeviceKind.Phone => "Telefon verisi henüz ulaşmıyor veya son paket eskidi.",
        DeviceKind.PsMoveLeft or DeviceKind.PsMoveRight => "Kontrolcü eşleştirilmiş olabilir; uyandırılmadığında sensör akışı görünmez.",
        DeviceKind.JoyConLeft or DeviceKind.JoyConRight => "Windows Bluetooth bağlantısı veya Joy-Con HID akışı bulunamadı.",
        DeviceKind.VirtualDesktop => "Streamer açık olsa bile Quest tarafında aktif PC oturumu kurulmamış olabilir.",
        DeviceKind.Quest3 => "Quest oturumu Virtual Desktop veya SteamVR üzerinden doğrulanamadı.",
        _ => device.Detail
    };

    private static void AddRedactedTail(ZipArchive zip, string source, string entryName, int maximumBytes)
    {
        if (!File.Exists(source)) return;
        using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length > maximumBytes) stream.Seek(-maximumBytes, SeekOrigin.End);
        using var reader = new StreamReader(stream);
        Write(zip, entryName, Redact(reader.ReadToEnd()));
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open()); writer.Write(content);
    }

    public static string Redact(string value)
    {
        value = Ipv4Regex().Replace(value, "[IP]");
        value = BluetoothIdRegex().Replace(value, "[DEVICE-ID]");
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(user)) value = value.Replace(user, "[USER]", StringComparison.OrdinalIgnoreCase);
        return value;
    }

    [GeneratedRegex(@"(?<!\d)(?:\d{1,3}\.){3}\d{1,3}(?::\d{1,5})?")] private static partial Regex Ipv4Regex();
    [GeneratedRegex(@"(?i)(?<![0-9a-f])[0-9a-f]{12}(?![0-9a-f])")] private static partial Regex BluetoothIdRegex();
}
