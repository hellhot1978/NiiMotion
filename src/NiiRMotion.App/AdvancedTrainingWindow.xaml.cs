using System.IO;
using System.Text.Json;
using System.Windows;
using NiiRMotion.Core;
using NiiRMotion.Infrastructure;

namespace NiiRMotion.App;

public partial class AdvancedTrainingWindow : Window
{
    private readonly MotionProfile _profile;
    private readonly SensorFamily[] _sensors;
    private bool _recording;
    private string? _lastRecordingRoot;

    public AdvancedTrainingWindow(MotionProfile profile, IEnumerable<SensorFamily> sensors)
    {
        _profile = profile;
        _sensors = sensors.Distinct().ToArray();
        InitializeComponent();
        ProfileText.Text = profile.Name;
        SensorText.Text = string.Join("  +  ", _sensors.Select(DisplayName));
    }

    private async void StartClick(object sender, RoutedEventArgs e)
    {
        if (_recording || _sensors.Length == 0) return;
        _recording = true; StartButton.IsEnabled = false;
        var root = Path.Combine(NiiMotionPaths.Data, "advanced-training", _profile.Id, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(root);
        var progress = new Progress<TimeSpan>(elapsed => { Progress.Value = Math.Min(300, elapsed.TotalSeconds); TimerText.Text = $"{elapsed:mm\\:ss} / 05:00"; });
        try
        {
            using var linked = new CancellationTokenSource();
            var tasks = _sensors.Select(sensor => new GuidedCalibrationRecorder().RecordAsync(sensor, 0, TimeSpan.FromMinutes(5), progress, "advanced-combination-training", root, linked.Token)).ToArray();
            GuidedCalibrationResult[] results;
            try { results = await Task.WhenAll(tasks); }
            catch { linked.Cancel(); try { await Task.WhenAll(tasks); } catch { } throw; }
            await File.WriteAllTextAsync(Path.Combine(root, "training-manifest.json"), JsonSerializer.Serialize(new { version = 1, profile = _profile.Id, sensors = _sensors, results = results.Select(x => new { x.Sensor, x.TotalSamples, x.Folder }), completedAtUtc = DateTimeOffset.UtcNow }, new JsonSerializerOptions { WriteIndented = true }));
            await UnifiedSensorSessionWriter.WriteAsync(root, "advanced-combination-training", _profile.Id, 0, results);
            var analysis = await new OfflineCalibrationPipeline().ApplyAvailableAsync();
            _lastRecordingRoot = root;
            RejectButton.IsEnabled = true;
            InstructionText.Text = $"✓ Kayıt tamamlandı · {results.Sum(x => x.TotalSamples):N0} eşzamanlı örnek analiz edildi."
                + (analysis.UpdatedProfiles.Count > 0 ? $" Oyuna uygulanan profiller: {string.Join(", ", analysis.UpdatedProfiles)}." : " Temel üç faz tamamlandığında otomatik uygulanacak.");
            StartButton.Content = "✓ YENİ KAYIT EKLENDİ";
        }
        catch (Exception ex)
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            InstructionText.Text = "Kayıt tamamlanmadı: " + ex.GetBaseException().Message;
            StartButton.Content = "YENİDEN DENE";
        }
        finally { _recording = false; StartButton.IsEnabled = true; Progress.Value = 0; TimerText.Text = "00:00 / 05:00"; }
    }

    private static string DisplayName(SensorFamily sensor) => sensor switch { SensorFamily.JoyCon => "Joy-Con", SensorFamily.PsMove => "PS Move", SensorFamily.Phone => "Telefon", _ => "Balance Board" };
    private async void RejectClick(object sender, RoutedEventArgs e)
    {
        if (_recording || _lastRecordingRoot is null) return;
        try
        {
            var full = Path.GetFullPath(_lastRecordingRoot);
            var allowed = Path.GetFullPath(Path.Combine(NiiMotionPaths.Data, "advanced-training")) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Kayıt güvenli veri alanının dışında.");
            if (Directory.Exists(full)) Directory.Delete(full, true);
            var analysis = await new OfflineCalibrationPipeline().ApplyAvailableAsync();
            InstructionText.Text = "↶ Son ek kayıt reddedildi; kişisel model kalan doğrulanmış kayıtlardan yeniden oluşturuldu."
                + (analysis.UpdatedProfiles.Count > 0 ? $" Güncellenenler: {string.Join(", ", analysis.UpdatedProfiles)}." : "");
            _lastRecordingRoot = null; RejectButton.IsEnabled = false; StartButton.Content = "▶  YENİ 5 DK KAYIT EKLE";
        }
        catch (Exception ex) { InstructionText.Text = "Kayıt geri alınamadı: " + ex.GetBaseException().Message; }
    }
    private void CloseClick(object sender, RoutedEventArgs e) { if (!_recording) Close(); }
}
