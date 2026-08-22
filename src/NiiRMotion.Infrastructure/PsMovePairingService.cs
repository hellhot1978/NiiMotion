using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace NiiRMotion.Infrastructure;

public sealed record PsMovePairingResult(bool Success, string Message);

/// <summary>
/// Installs the pinned official PS Move API pairing utility on first use and
/// writes the computer's Bluetooth host address to the single USB-connected
/// controller. Camera/tracker components are deliberately not installed.
/// </summary>
public sealed class PsMovePairingService
{
    internal const string Version = "4.0.12";
    internal const string ArchiveSha256 = "78993822FE76A3F102CA24A66C19C51637E398549081EA6FECC9D6B2C3399236";
    internal const string ArchiveUrl = "https://github.com/thp/psmoveapi/releases/download/4.0.12/psmoveapi-4.0.12-windows-msvc2017-x64.zip";
    private const string PackageRoot = "psmoveapi-4.0.12-windows-msvc2017-x64/";

    private readonly string _toolDirectory;

    public PsMovePairingService(string? toolDirectory = null)
    {
        _toolDirectory = toolDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NiiMotion", "Tools", "PSMoveAPI", Version);
    }

    public async Task<PsMovePairingResult> PairSingleUsbControllerAsync(CancellationToken cancellationToken = default)
    {
        var usb = new PsMoveDiagnosticsService().Discover()
            .Where(x => x.Device.Transport == Core.PsMoveTransport.Usb)
            .DistinctBy(x => x.Device.DevicePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (usb.Length == 0) return new(false, "USB ile bağlı PS Move bulunamadı.");
        if (usb.Length > 1) return new(false, "Güvenli sağ/sol ataması için USB'de yalnız bir PS Move bırak.");

        await EnsureToolAsync(cancellationToken);
        var executable = Path.Combine(_toolDirectory, "psmove.exe");
        var start = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = "pair",
            WorkingDirectory = _toolDirectory,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("PS Move eşleştirme aracı başlatılamadı.");
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0
                ? new(true, "Bluetooth eşleştirme bilgisi kontrolcüye yazıldı.")
                : new(false, $"PS Move eşleştirme aracı hata kodu {process.ExitCode} döndürdü.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new(false, "Yönetici izni iptal edildi; eşleştirme yapılmadı.");
        }
    }

    internal async Task EnsureToolAsync(CancellationToken cancellationToken = default)
    {
        var executable = Path.Combine(_toolDirectory, "psmove.exe");
        var library = Path.Combine(_toolDirectory, "psmoveapi.dll");
        var license = Path.Combine(_toolDirectory, "COPYING");
        if (File.Exists(executable) && File.Exists(library) && File.Exists(license)) return;

        Directory.CreateDirectory(_toolDirectory);
        var archive = Path.Combine(_toolDirectory, $"psmoveapi-{Version}.zip.download");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            await using (var source = await client.GetStreamAsync(ArchiveUrl, cancellationToken))
            await using (var destination = new FileStream(archive, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                await source.CopyToAsync(destination, cancellationToken);

            await using (var stream = File.OpenRead(archive))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                if (!hash.Equals(ArchiveSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("İndirilen PS Move eşleştirme aracının güvenlik özeti doğrulanamadı.");
            }

            using var zip = ZipFile.OpenRead(archive);
            Extract(zip, PackageRoot + "bin/psmove.exe", executable);
            Extract(zip, PackageRoot + "lib/psmoveapi.dll", library);
            Extract(zip, PackageRoot + "COPYING", license);
        }
        finally
        {
            if (File.Exists(archive)) File.Delete(archive);
        }
    }

    private static void Extract(ZipArchive zip, string entryName, string destination)
    {
        var entry = zip.GetEntry(entryName) ?? throw new InvalidDataException($"Resmî pakette {entryName} bulunamadı.");
        var temporary = destination + ".tmp";
        entry.ExtractToFile(temporary, true);
        File.Move(temporary, destination, true);
    }
}
