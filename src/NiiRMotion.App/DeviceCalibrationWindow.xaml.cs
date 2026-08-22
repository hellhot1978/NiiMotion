using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using NiiRMotion.Core;
using NiiRMotion.Infrastructure;

namespace NiiRMotion.App;

public partial class DeviceCalibrationWindow : Window
{
    private static readonly TimeSpan PhaseDuration = TimeSpan.FromMinutes(5);
    private readonly SensorFamily _sensor;
    private readonly UserSetupStore _store = new();
    private readonly PendingCalibrationRepairStore _repairStore = new();
    private DeviceCalibrationProgress _progress;
    private bool _connected;
    private bool _recording;
    private GuidedCalibrationResult? _pendingResult;

    public DeviceCalibrationWindow(SensorFamily sensor)
    {
        _sensor = sensor;
        _progress = new(sensor, CalibrationStage.NotConnected, 0, null);
        InitializeComponent();
        ConfigureVisuals();
        Loaded += async (_, _) => await LoadAsync();
    }

    private void ConfigureVisuals()
    {
        var (name, icon) = _sensor switch
        {
            SensorFamily.JoyCon => ("Joy-Con çifti", "device-v3-joycon-left.png"),
            SensorFamily.PsMove => ("PS Move çifti", "device-v3-psmove-left.png"),
            SensorFamily.Phone => ("Android telefon", "device-v3-phone.png"),
            _ => ("Wii Balance Board", "device-v3-board.png")
        };
        TitleText.Text = $"{name} · temel kalibrasyon"; DeviceName.Text = name;
        DeviceImage.Source = new BitmapImage(new Uri($"pack://application:,,,/NiiRMotion.App;component/Assets/{icon}"));
    }

    private async Task LoadAsync()
    {
        var document = await _store.LoadCalibrationAsync();
        _progress = document.Devices.FirstOrDefault(x => x.Sensor == _sensor) ?? _progress;
        _pendingResult = _repairStore.Load(_sensor);
        if (_pendingResult is not null) { _connected = true; RepairSegmentButton.Visibility = Visibility.Visible; ShowPendingRepair(); }
        RefreshPhaseButtons();
    }

    private async void ConnectionClick(object sender, RoutedEventArgs e)
    {
        ConnectionButton.IsEnabled = false; ConnectionText.Text = "Cihaz aranıyor…";
        try
        {
            if (_sensor == SensorFamily.Phone)
            {
                await using var phone = new OwoTrackSensorSource(); await phone.StartAsync();
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
                while (DateTime.UtcNow < deadline && phone.PhoneEndpoint is null) await Task.Delay(100);
                _connected = phone.PhoneEndpoint is not null;
            }
            else
            {
                var devices = await new HardwareDiscoveryService().ScanAsync();
                _connected = RequiredKinds().All(kind => devices.Any(x => x.Kind == kind && x.IsConnected));
            }
            if (!_connected) throw new InvalidOperationException(ConnectionHelp());
            ConnectionText.Text = "✓ Bağlantı tamamlandı"; ConnectionText.Foreground = MainWindow.Brush("#55DDB8");
            if (_progress.Stage == CalibrationStage.NotConnected) await SaveProgressAsync(0, CalibrationStage.ConnectionReady);
            InstructionText.Text = "Bağlantı hazır. Faz 1 ile başla; ekrandaki yönergeleri kayıt boyunca uygula.";
        }
        catch (Exception ex) { _connected = false; ConnectionText.Text = ex.Message; ConnectionText.Foreground = MainWindow.Brush("#FF829D"); }
        finally { ConnectionButton.IsEnabled = true; RefreshPhaseButtons(); }
    }

    private async void PhaseClick(object sender, RoutedEventArgs e)
    {
        if (_recording || sender is not Button button || !int.TryParse(button.Tag?.ToString(), out var phase)) return;
        if (!_connected) { InstructionText.Text = "Önce bağlantıyı doğrula."; return; }
        if (phase != _progress.CompletedPhases + 1) { InstructionText.Text = "Fazları sırayla tamamla."; return; }
        _recording = true; RefreshPhaseButtons();
        InstructionText.Text = PhaseInstruction(phase);
        var progress = new Progress<TimeSpan>(elapsed => { PhaseProgress.Value = Math.Min(300, elapsed.TotalSeconds); TimerText.Text = $"{elapsed:mm\\:ss} / 05:00"; });
        try
        {
            var result = await new GuidedCalibrationRecorder().RecordAsync(_sensor, phase, PhaseDuration, progress);
            await UnifiedSensorSessionWriter.WriteAsync(result.Folder, "base-calibration", null, phase, [result]);
            if (!result.Quality.IsClean)
            {
                _pendingResult = result; RepairSegmentButton.Visibility = Visibility.Visible;
                _repairStore.Save(result);
                ShowPendingRepair();
            }
            else await CompleteCleanPhaseAsync(result);
        }
        catch (Exception ex) { InstructionText.Text = $"Faz tamamlanmadı: {ex.GetBaseException().Message}"; }
        finally { _recording = false; PhaseProgress.Value = 0; TimerText.Text = "00:00 / 05:00"; RefreshPhaseButtons(); }
    }

    private async void RepairSegmentClick(object sender, RoutedEventArgs e)
    {
        if (_recording || _pendingResult is null || _pendingResult.Quality.RedoSegments.FirstOrDefault() is not { } segment) return;
        _recording = true; RepairSegmentButton.IsEnabled = false; RefreshPhaseButtons();
        var duration = TimeSpan.FromSeconds(segment.EndSeconds - segment.StartSeconds);
        InstructionText.Text = $"{segment.StartSeconds:0}-{segment.EndSeconds:0} saniyelik sorunlu bölüm yeniden kaydediliyor. Faz yönergesindeki hareketi sürdür.";
        PhaseProgress.Maximum = duration.TotalSeconds;
        var progress = new Progress<TimeSpan>(elapsed => { PhaseProgress.Value = Math.Min(duration.TotalSeconds, elapsed.TotalSeconds); TimerText.Text = $"{elapsed:mm\\:ss} / {duration:mm\\:ss}"; });
        try
        {
            var repair = await new GuidedCalibrationRecorder().RecordAsync(_sensor, _pendingResult.Phase, duration, progress, "segment-repair");
            _pendingResult = await new CalibrationSegmentRepairService().ReplaceAsync(_pendingResult, segment, repair);
            _repairStore.Save(_pendingResult);
            if (_pendingResult.Quality.IsClean) await CompleteCleanPhaseAsync(_pendingResult);
            else ShowPendingRepair();
        }
        catch (Exception ex) { InstructionText.Text = "Bölüm yenilenemedi: " + ex.GetBaseException().Message; }
        finally
        {
            _recording = false; PhaseProgress.Maximum = 300; PhaseProgress.Value = 0; TimerText.Text = "00:00 / 05:00";
            RepairSegmentButton.IsEnabled = true; RefreshPhaseButtons();
        }
    }

    private void ShowPendingRepair()
    {
        if (_pendingResult is null) return;
        var segment = _pendingResult.Quality.RedoSegments.First();
        InstructionText.Text = $"Kalite %{_pendingResult.Quality.Score * 100:0}. {segment.StartSeconds:0}-{segment.EndSeconds:0} sn bölümünde {segment.Issue.ToLowerInvariant()}. Yalnız bu {segment.EndSeconds - segment.StartSeconds:0} saniyeyi yeniden kaydet.";
        RepairSegmentButton.Content = $"↻ {segment.StartSeconds:0}-{segment.EndSeconds:0} SN BÖLÜMÜNÜ YENİDEN KAYDET";
        RepairSegmentButton.Visibility = Visibility.Visible;
    }

    private async Task CompleteCleanPhaseAsync(GuidedCalibrationResult result)
    {
        await SaveProgressAsync(result.Phase, result.Phase == 3 ? CalibrationStage.Ready : (CalibrationStage)((int)CalibrationStage.Phase1 + result.Phase - 1));
        var analysis = await new OfflineCalibrationPipeline().ApplyAvailableAsync(); var applied = analysis.UpdatedProfiles.Contains(DisplayName(_sensor));
        InstructionText.Text = applied
            ? $"✓ Faz {result.Phase} tamamlandı · {result.TotalSamples:N0} örnek · kalite %{result.Quality.Score * 100:0} · kişisel profil oyuna uygulandı."
            : $"✓ Faz {result.Phase} tamamlandı · {result.TotalSamples:N0} örnek · kalite %{result.Quality.Score * 100:0}.";
        _pendingResult = null; RepairSegmentButton.Visibility = Visibility.Collapsed;
        _repairStore.Clear();
    }

    private async Task SaveProgressAsync(int completed, CalibrationStage stage)
    {
        _progress = new(_sensor, stage, completed, DateTimeOffset.UtcNow);
        var document = await _store.LoadCalibrationAsync();
        var devices = document.Devices.Where(x => x.Sensor != _sensor).Append(_progress).OrderBy(x => x.Sensor).ToArray();
        await _store.SaveCalibrationAsync(new(1, devices, document.Profiles));
    }

    private async void ResetPhaseClick(object sender, RoutedEventArgs e)
    {
        if (_recording || sender is not Button button || !int.TryParse(button.Tag?.ToString(), out var phase) || _progress.CompletedPhases < phase) return;
        var consequence = phase < 3 ? $"Faz {phase} ve sonraki tamamlanmış fazlar silinecek." : "Yalnız Faz 3 silinecek.";
        if (MessageBox.Show($"Bu fazı yeniden kaydetmek istiyor musun?\n\n{consequence}\nEk model geliştirme kayıtların korunacak.", "Fazı yeniden çek", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _recording = true; RefreshPhaseButtons();
        try
        {
            new CalibrationDataManager().DeleteDevicePhases(_sensor, phase);
            _repairStore.Clear(); _pendingResult = null; RepairSegmentButton.Visibility = Visibility.Collapsed;
            var completed = phase - 1;
            var stage = completed switch { 0 => CalibrationStage.ConnectionReady, 1 => CalibrationStage.Phase1, _ => CalibrationStage.Phase2 };
            _progress = new(_sensor, stage, completed, DateTimeOffset.UtcNow);
            var document = await _store.LoadCalibrationAsync();
            var devices = document.Devices.Where(x => x.Sensor != _sensor).Append(_progress).OrderBy(x => x.Sensor).ToArray();
            await _store.SaveCalibrationAsync(new(1, devices, Array.Empty<ProfileCalibrationProgress>()));
            await new OfflineCalibrationPipeline().ApplyAvailableAsync();
            InstructionText.Text = $"↻ Faz {phase} yeniden kayda açıldı. Bu cihazı kullanan birlikte-kalibrasyonlar güvenlik için sıfırlandı.";
            ConnectionText.Text = "✓ Bağlantı hazır · yeniden kayıt bekleniyor"; ConnectionText.Foreground = MainWindow.Brush("#F1C566"); _connected = true;
        }
        catch (Exception ex) { InstructionText.Text = "Faz silinemedi: " + ex.GetBaseException().Message; }
        finally { _recording = false; RefreshPhaseButtons(); }
    }

    private void RefreshPhaseButtons()
    {
        var buttons = new[] { Phase1Button, Phase2Button, Phase3Button };
        var resetButtons = new[] { Phase1ResetButton, Phase2ResetButton, Phase3ResetButton };
        for (var i = 0; i < buttons.Length; i++)
        {
            var phase = i + 1; var done = _progress.CompletedPhases >= phase; var next = _connected && _progress.CompletedPhases + 1 == phase;
            buttons[i].Content = done ? $"✓  FAZ {phase} TAMAMLANDI" : next ? $"▶  FAZ {phase}'Ü BAŞLAT · 5 DK" : $"○  FAZ {phase} · 5 DK";
            buttons[i].IsEnabled = !_recording && next;
            buttons[i].Background = MainWindow.Brush(done ? "#143A31" : next ? "#0F77B6" : "#101820");
            resetButtons[i].Visibility = done ? Visibility.Visible : Visibility.Hidden;
            resetButtons[i].IsEnabled = !_recording && done;
            resetButtons[i].Background = MainWindow.Brush(done ? "#39242A" : "#101820");
            resetButtons[i].Foreground = MainWindow.Brush("#FF9AAF");
        }
        if (_progress.IsReady) { ConnectionText.Text = "✓ Kalibre edildi ve kullanıma hazır"; ConnectionText.Foreground = MainWindow.Brush("#55DDB8"); _connected = true; }
    }

    private IReadOnlyList<DeviceKind> RequiredKinds() => _sensor switch
    {
        SensorFamily.JoyCon => [DeviceKind.JoyConLeft, DeviceKind.JoyConRight],
        SensorFamily.PsMove => [DeviceKind.PsMoveLeft, DeviceKind.PsMoveRight],
        SensorFamily.Phone => [DeviceKind.Phone],
        _ => [DeviceKind.BalanceBoard]
    };

    private string ConnectionHelp() => _sensor switch
    {
        SensorFamily.JoyCon => "İki Joy-Con'u Windows Bluetooth'a bağla.",
        SensorFamily.PsMove => "İki atanmış PS Move'u Bluetooth ile bağla.",
        SensorFamily.Phone => "Telefonda owoTrack'i başlat ve aynı ağa bağlan.",
        _ => "Balance Board'u Windows Bluetooth'a bağla ve kartı boş bırak."
    };

    private string PhaseInstruction(int phase) => (phase, _sensor) switch
    {
        (1, SensorFamily.BalanceBoard) => "FAZ 1 · Kart boşken bekle, ardından üzerine çıkıp ortada doğal ve sabit dur.",
        (1, _) => "FAZ 1 · Sensörü doğru konumda sabitle; önce sakin dur, sonra rahat ve yavaş yerinde yürü.",
        (2, _) => "FAZ 2 · Doğal hızda yerinde yürü; kısa duruşlar ve yeniden başlangıçlar yap.",
        _ => "FAZ 3 · Yavaş, doğal ve hızlı yürüyüşü; dönüş, eğilme ve sabit duruşları sırayla uygula."
    };

    private static string DisplayName(SensorFamily sensor) => sensor switch { SensorFamily.JoyCon => "Joy-Con", SensorFamily.PsMove => "PS Move", SensorFamily.Phone => "Telefon", _ => "Balance Board" };

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
