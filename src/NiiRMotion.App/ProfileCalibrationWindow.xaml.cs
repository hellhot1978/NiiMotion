using System.IO;
using System.Windows;
using System.Windows.Controls;
using NiiRMotion.Core;
using NiiRMotion.Infrastructure;

namespace NiiRMotion.App;

public partial class ProfileCalibrationWindow : Window
{
    private static readonly TimeSpan PhaseDuration = TimeSpan.FromMinutes(2);
    private readonly MotionProfile _profile;
    private readonly SensorFamily[] _sensors;
    private readonly UserSetupStore _store = new();
    private int _completed;
    private bool _recording;
    private bool _paused;

    public ProfileCalibrationWindow(MotionProfile profile, IEnumerable<SensorFamily> sensors)
    {
        _profile = profile; _sensors = sensors.Distinct().ToArray(); InitializeComponent();
        ProfileText.Text = profile.Name; SensorText.Text = "Aktif sensörler\n" + string.Join("  +  ", _sensors.Select(DisplayName));
        Loaded += async (_, _) => { var document = await _store.LoadCalibrationAsync(); _completed = document.Profiles?.FirstOrDefault(x => x.ProfileId == _profile.Id)?.CompletedPhases ?? 0; Refresh(); };
    }

    private async void PhaseClick(object sender, RoutedEventArgs e)
    {
        if (_recording || sender is not Button button || !int.TryParse(button.Tag?.ToString(), out var phase) || phase != _completed + 1) return;
        _recording = true; Refresh(); InstructionText.Text = Instruction(phase);
        var sessionRoot = Path.Combine(NiiMotionPaths.Data, "profile-calibration", _profile.Id, $"phase-{phase}-{DateTime.Now:yyyyMMdd-HHmmss}"); Directory.CreateDirectory(sessionRoot);
        var progress = new Progress<TimeSpan>(elapsed => { Progress.Value = Math.Min(PhaseDuration.TotalSeconds, elapsed.TotalSeconds); Timer.Text = $"{elapsed:mm\\:ss} / 02:00"; });
        try
        {
            using var linked = new CancellationTokenSource();
            var tasks = _sensors.Select(sensor => new GuidedCalibrationRecorder().RecordAsync(sensor, phase, PhaseDuration, progress, "profile-walking-calibration", sessionRoot, linked.Token, () => _paused)).ToArray();
            GuidedCalibrationResult[] results;
            try { results = await Task.WhenAll(tasks); }
            catch { linked.Cancel(); try { await Task.WhenAll(tasks); } catch { } throw; }
            await File.WriteAllTextAsync(Path.Combine(sessionRoot, "profile-manifest.json"), System.Text.Json.JsonSerializer.Serialize(new { version = 1, profile = _profile.Id, phase, sensors = _sensors, results = results.Select(x => new { x.Sensor, x.TotalSamples, x.Folder }), completedAtUtc = DateTimeOffset.UtcNow }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            await UnifiedSensorSessionWriter.WriteAsync(sessionRoot, "profile-walking-calibration", _profile.Id, phase, results);
            _completed = phase; await SaveAsync(); InstructionText.Text = $"✓ Faz {phase} tamamlandı · {results.Sum(x => x.TotalSamples):N0} eşzamanlı örnek.";
            var analysis = await new OfflineCalibrationPipeline().ApplyAvailableAsync();
            if (analysis.UpdatedProfiles.Count > 0) InstructionText.Text += $" Profiller yenilendi: {string.Join(", ", analysis.UpdatedProfiles)}.";
            if (_completed >= 3 && new ProfileFusionModelStore().Load(_profile.Id) is { } model) InstructionText.Text = ModelSummary(model);
        }
        catch (Exception ex)
        {
            try
            {
                var full = Path.GetFullPath(sessionRoot); var root = Path.GetFullPath(NiiMotionPaths.Data) + Path.DirectorySeparatorChar;
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(full)) Directory.Delete(full, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            InstructionText.Text = "Faz tamamlanmadı: " + ex.GetBaseException().Message;
        }
        finally { _recording = false; _paused = false; Progress.Value = 0; Timer.Text = "00:00 / 02:00"; Refresh(); }
    }

    private async Task SaveAsync()
    {
        var document = await _store.LoadCalibrationAsync();
        var profiles = (document.Profiles ?? Array.Empty<ProfileCalibrationProgress>()).Where(x => x.ProfileId != _profile.Id).Append(new(_profile.Id, _completed, DateTimeOffset.UtcNow)).ToArray();
        await _store.SaveCalibrationAsync(document with { Profiles = profiles });
    }

    private void Refresh()
    {
        var buttons = new[] { Phase1, Phase2, Phase3 };
        var resets = new[] { Reset1, Reset2, Reset3 };
        for (var i = 0; i < 3; i++) { var n = i + 1; buttons[i].Content = _completed >= n ? $"✓ FAZ {n} TAMAMLANDI" : _completed + 1 == n ? $"▶ FAZ {n}'Ü BAŞLAT · 2 DK" : $"○ FAZ {n} · 2 DK"; buttons[i].IsEnabled = !_recording && _completed + 1 == n; }
        for (var i = 0; i < 3; i++) resets[i].IsEnabled = !_recording && _completed >= i + 1;
        PauseButton.Visibility = _recording ? Visibility.Visible : Visibility.Collapsed; PauseButton.Content = _paused ? "▶  DEVAM ET" : "Ⅱ  DURAKLAT";
        RestoreButton.IsEnabled = !_recording && new ProfileFusionModelStore().HistoryCount(_profile.Id) > 0;
        if (_completed >= 3) InstructionText.Text = new ProfileFusionModelStore().Load(_profile.Id) is { } model ? ModelSummary(model) : "✓ Kayıtlar tamamlandı · ortak model yerel olarak hazırlanıyor.";
    }

    private void PauseClick(object sender, RoutedEventArgs e) { if (!_recording) return; _paused = !_paused; PauseButton.Content = _paused ? "▶  DEVAM ET" : "Ⅱ  DURAKLAT"; InstructionText.Text = _paused ? "Kayıt duraklatıldı; bu süre modele eklenmeyecek." : "Kayıt devam ediyor."; }
    private async void ResetPhaseClick(object sender, RoutedEventArgs e)
    {
        if (_recording || sender is not Button button || !int.TryParse(button.Tag?.ToString(), out var phase) || _completed < phase) return;
        if (UiLocalization.ShowMessage(this, $"Faz {phase} ve sonraki ortak fazlar silinip yeniden kayda açılsın mı?", "Ortak fazı yeniden çek", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var root = Path.GetFullPath(Path.Combine(NiiMotionPaths.Data, "profile-calibration", _profile.Id)); var data = Path.GetFullPath(NiiMotionPaths.Data) + Path.DirectorySeparatorChar;
        if (root.StartsWith(data, StringComparison.OrdinalIgnoreCase) && Directory.Exists(root)) foreach (var folder in Directory.GetDirectories(root, "phase-*")) { var name = Path.GetFileName(folder); if (int.TryParse(name.Split('-').ElementAtOrDefault(1), out var found) && found >= phase) Directory.Delete(folder, true); }
        _completed = phase - 1; new ProfileFusionModelStore().Invalidate(_profile.Id); await SaveAsync(); Refresh(); InstructionText.Text = $"Faz {phase} yeniden kayda açıldı.";
    }
    private async void RestoreClick(object sender, RoutedEventArgs e)
    {
        var store = new ProfileFusionModelStore(); if (!store.RestorePrevious(_profile.Id)) return; await Task.Yield(); Refresh(); InstructionText.Text = "✓ Önceki ortak model geri yüklendi. Tekil cihaz modelleri değiştirilmedi.";
    }
    private static string ModelSummary(ProfileFusionModel model) => $"✓ Ortak model hazır · kalite %{model.CaptureQuality * 100:0} · {model.AcceptedSamples:N0} örnek · ritim toleransı {model.CadenceToleranceHz:0.00} Hz · kesinti payı {model.DisagreementGraceMs} ms";

    private static string Instruction(int phase) => phase switch { 1 => "Rahat doğal yürüyüş · düzenli ritim ve kısa duruşlar.", 2 => "Yavaş, doğal ve hızlı tempo · kontrollü hız geçişleri.", _ => "Dönüş, eğilme, çömelme, tek bacak ve ani duruş · yanlış hareket ayrımı." };
    private static string DisplayName(SensorFamily sensor) => sensor switch { SensorFamily.JoyCon => "Joy-Con", SensorFamily.PsMove => "PS Move", SensorFamily.Phone => "Telefon", _ => "Balance Board" };
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
