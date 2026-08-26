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
    internal const string ExecutableSha256 = "FEDE4BBE0675CE9A78FDAF6E5D99985D1ED18674F539DA2F1F4428458390D42D";
    internal const string LibrarySha256 = "B0CF9D566D35D7ADF4CDD08829D1BA89B2A8462CF695E3A7456A56D90B2427B7";
    internal const string LicenseSha256 = "1A5007D3E29F1E89DFCB6471BB6EE1353D82DBD7071A5789EA28A64F5A27EB5F";
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

        await NiiMotionEventLog.WriteAsync("psmove", "pair-started", "USB assisted pairing started.", cancellationToken: cancellationToken);
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
            PsMovePairingResult result = process.ExitCode == 0
                ? new(true, "Bluetooth eşleştirme bilgisi kontrolcüye yazıldı.")
                : new(false, $"PS Move eşleştirme aracı hata kodu {process.ExitCode} döndürdü.");
            await NiiMotionEventLog.WriteAsync("psmove", result.Success ? "pair-completed" : "pair-failed", result.Message, new { exitCode = process.ExitCode }, cancellationToken);
            return result;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            await NiiMotionEventLog.WriteAsync("psmove", "pair-cancelled", "Administrator permission was cancelled.", cancellationToken: cancellationToken);
            return new(false, "Yönetici izni iptal edildi; eşleştirme yapılmadı.");
        }
    }

    internal async Task EnsureToolAsync(CancellationToken cancellationToken = default)
    {
        var executable = Path.Combine(_toolDirectory, "psmove.exe");
        var library = Path.Combine(_toolDirectory, "psmoveapi.dll");
        var license = Path.Combine(_toolDirectory, "COPYING");
        if (IsVerified(executable, ExecutableSha256) && IsVerified(library, LibrarySha256) && IsVerified(license, LicenseSha256)) return;

        Directory.CreateDirectory(_toolDirectory);
        var bundled = Path.Combine(AppContext.BaseDirectory, "Tools", "PSMoveAPI", Version);
        var bundledExecutable = Path.Combine(bundled, "psmove.exe");
        var bundledLibrary = Path.Combine(bundled, "psmoveapi.dll");
        var bundledLicense = Path.Combine(bundled, "COPYING");
        if (IsVerified(bundledExecutable, ExecutableSha256) && IsVerified(bundledLibrary, LibrarySha256) && IsVerified(bundledLicense, LicenseSha256))
        {
            File.Copy(bundledExecutable, executable, true); File.Copy(bundledLibrary, library, true); File.Copy(bundledLicense, license, true);
            await NiiMotionEventLog.WriteAsync("psmove", "pair-tool-installed", $"Bundled PSMoveAPI {Version} pairing components installed.", new { source = "offline-bundle" }, cancellationToken);
            return;
        }

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
            if (!IsVerified(executable, ExecutableSha256) || !IsVerified(library, LibrarySha256) || !IsVerified(license, LicenseSha256))
                throw new InvalidDataException("PS Move eşleştirme bileşenlerinin bütünlüğü doğrulanamadı.");
            await NiiMotionEventLog.WriteAsync("psmove", "pair-tool-installed", $"Official PSMoveAPI {Version} pairing components installed.", new { sha256 = ArchiveSha256 }, cancellationToken);
        }
        finally
        {
            if (File.Exists(archive)) File.Delete(archive);
        }
    }

    private static bool IsVerified(string path, string expectedSha256)
    {
        if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static void Extract(ZipArchive zip, string entryName, string destination)
    {
        var entry = zip.GetEntry(entryName) ?? throw new InvalidDataException($"Resmî pakette {entryName} bulunamadı.");
        var temporary = destination + ".tmp";
        entry.ExtractToFile(temporary, true);
        File.Move(temporary, destination, true);
    }
}
