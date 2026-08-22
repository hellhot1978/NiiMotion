using System.IO;
using System.Windows;
using System.Windows.Controls;
using NiiRMotion.Core;
using NiiRMotion.Infrastructure;

namespace NiiRMotion.App;

public partial class ProfileCalibrationWindow : Window
{
    private readonly MotionProfile _profile;
    private readonly SensorFamily[] _sensors;
    private readonly UserSetupStore _store = new();
    private int _completed;
    private bool _recording;

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
        var progress = new Progress<TimeSpan>(elapsed => { Progress.Value = Math.Min(300, elapsed.TotalSeconds); Timer.Text = $"{elapsed:mm\\:ss} / 05:00"; });
        try
        {
            using var linked = new CancellationTokenSource();
            var tasks = _sensors.Select(sensor => new GuidedCalibrationRecorder().RecordAsync(sensor, phase, TimeSpan.FromMinutes(5), progress, "profile-walking-calibration", sessionRoot, linked.Token)).ToArray();
            GuidedCalibrationResult[] results;
            try { results = await Task.WhenAll(tasks); }
            catch { linked.Cancel(); try { await Task.WhenAll(tasks); } catch { } throw; }
            await File.WriteAllTextAsync(Path.Combine(sessionRoot, "profile-manifest.json"), System.Text.Json.JsonSerializer.Serialize(new { version = 1, profile = _profile.Id, phase, sensors = _sensors, results = results.Select(x => new { x.Sensor, x.TotalSamples, x.Folder }), completedAtUtc = DateTimeOffset.UtcNow }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            _completed = phase; await SaveAsync(); InstructionText.Text = $"✓ Faz {phase} tamamlandı · {results.Sum(x => x.TotalSamples):N0} eşzamanlı örnek.";
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
        finally { _recording = false; Progress.Value = 0; Timer.Text = "00:00 / 05:00"; Refresh(); }
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
        for (var i = 0; i < 3; i++) { var n = i + 1; buttons[i].Content = _completed >= n ? $"✓ FAZ {n} TAMAMLANDI" : _completed + 1 == n ? $"▶ FAZ {n}'Ü BAŞLAT · 5 DK" : $"○ FAZ {n} · 5 DK"; buttons[i].IsEnabled = !_recording && _completed + 1 == n; }
        if (_completed >= 3) InstructionText.Text = "✓ Bu profil için birlikte çalışma kalibrasyonu hazır.";
    }

    private static string Instruction(int phase) => phase switch { 1 => "Rahat doğal yürüyüş · düzenli ritim ve kısa duruşlar.", 2 => "Yavaş, doğal ve hızlı tempo · kontrollü hız geçişleri.", _ => "Dönüş, eğilme, çömelme, tek bacak ve ani duruş · yanlış hareket ayrımı." };
    private static string DisplayName(SensorFamily sensor) => sensor switch { SensorFamily.JoyCon => "Joy-Con", SensorFamily.PsMove => "PS Move", SensorFamily.Phone => "Telefon", _ => "Balance Board" };
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
