using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed record GameCompatibilityIssue(string Code, string Message, string Action);
public sealed record GameCompatibilityReport(IReadOnlyList<GameCompatibilityIssue> Issues)
{
    public bool IsReady => Issues.Count == 0;
    public string UserMessage => string.Join(Environment.NewLine, Issues.Select(x => $"• {x.Message} {x.Action}"));
}

public sealed class GameLaunchCompatibilityService
{
    private readonly string? _steamExe;
    private readonly string _appBase;
    private readonly IReadOnlyList<UserGameAdapter> _steamAdapters;
    private readonly IReadOnlyList<OpenXrGameAdapter> _openXrAdapters;

    public GameLaunchCompatibilityService(string? steamExe = null, string? appBase = null,
        IReadOnlyList<UserGameAdapter>? steamAdapters = null, IReadOnlyList<OpenXrGameAdapter>? openXrAdapters = null)
    {
        _steamExe = steamExe ?? SteamInstallLocator.FindSteamExe();
        _appBase = appBase ?? AppContext.BaseDirectory;
        _steamAdapters = steamAdapters ?? new GameAdapterStore().Load();
        _openXrAdapters = openXrAdapters ?? new OpenXrGameAdapterStore().Load();
    }

    public GameCompatibilityReport Validate(InstalledGame game, bool requireNiiMotion)
    {
        var issues = new List<GameCompatibilityIssue>();
        if (game.Definition.SteamAppId is null)
            issues.Add(new("steam-id", "Bu oyun için geçerli Steam kimliği yok.", "Oyunu yeniden ekle."));
        if (!game.IsInstalled || string.IsNullOrWhiteSpace(game.InstallPath) || !Directory.Exists(game.InstallPath))
            issues.Add(new("install", "Steam oyun klasörü bulunamadı.", "Steam'den oyun dosyalarını doğrula."));
        if (string.IsNullOrWhiteSpace(_steamExe) || !File.Exists(_steamExe))
            issues.Add(new("steam", "Steam çalıştırıcısı bulunamadı.", "Steam'i kur veya kütüphane yolunu düzelt."));
        if (!requireNiiMotion) return new(issues);
        if (!game.Definition.MotionSupported)
            issues.Add(new("unsupported", "Bu oyun için NiiMotion hareket eşlemesi doğrulanmamış.", "Önce Oyun Ekleme Sihirbazı ile güvenli bir adaptör oluştur."));

        var openXr = game.Definition.Runtime.Contains("OpenXR", StringComparison.OrdinalIgnoreCase);
        if (openXr)
        {
            if (!File.Exists(Path.Combine(_appBase, "OpenXRLayer", "bin", "win64", "niirmotion_openxr.dll")))
                issues.Add(new("openxr-layer", "NiiMotion OpenXR katmanı paket içinde bulunamadı.", "Uygulamayı onar veya yeniden kur."));
            var adapter = _openXrAdapters.FirstOrDefault(x => x.Id.Equals(game.Definition.Id, StringComparison.OrdinalIgnoreCase));
            if (adapter is null) issues.Add(new("openxr-adapter", "Bu oyun için OpenXR hareket adaptörü yok.", "Oyunu yeniden ekle."));
            else
            {
                foreach (var error in OpenXrGameAdapterValidator.Validate(adapter)) issues.Add(new("openxr-adapter", error, "Adaptörü yeniden oluştur."));
                if (!string.IsNullOrWhiteSpace(game.InstallPath) && Directory.Exists(game.InstallPath) && !adapter.Executables.Any(x => ExecutableExists(game.InstallPath, x)))
                    issues.Add(new("game-executable", "Kayıtlı OpenXR oyun çalıştırıcısı bulunamadı.", "Oyunun dosyalarını doğrula veya adaptörü yeniden oluştur."));
            }
        }
        else
        {
            if (!File.Exists(Path.Combine(_appBase, "OpenVRDriver", "driver.vrdrivermanifest")))
                issues.Add(new("openvr-driver", "NiiMotion SteamVR sürücüsü paket içinde bulunamadı.", "Uygulamayı onar veya yeniden kur."));
            var adapter = _steamAdapters.FirstOrDefault(x => x.Id.Equals(game.Definition.Id, StringComparison.OrdinalIgnoreCase));
            if (adapter is not null)
                foreach (var error in GameAdapterValidator.Validate(adapter)) issues.Add(new("steamvr-adapter", error, "Adaptörü yeniden oluştur."));
            else if (game.Definition.Id.StartsWith("user-", StringComparison.OrdinalIgnoreCase))
                issues.Add(new("steamvr-adapter", "Kullanıcı oyun eşlemesi bulunamadı.", "Oyunu yeniden ekle."));
        }
        return new(issues.DistinctBy(x => (x.Code, x.Message)).ToArray());
    }

    private static bool ExecutableExists(string root, string fileName)
    {
        try { return Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).Any(); }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }
}
