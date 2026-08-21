using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Diagnostics;
using System.Text.Json;
using NiiRMotion.Core;
using NiiRMotion.Infrastructure;

namespace NiiRMotion.App;

public partial class MainWindow : Window
{
    private IHardwareDiscoveryService _discovery = new HardwareDiscoveryService();
    private readonly LiveLocomotionService _locomotion = new();
    private readonly SystemModeService _systemMode = new();
    private readonly DispatcherTimer _demoTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly DispatcherTimer _scanTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _autoScanBusy;
    private bool _phonePairing;
    private OwoTrackSensorSource? _phoneMonitor;
    private SessionReadiness? _readiness;
    private MotionProfile _profile = MotionProfile.AlyxFullFusion;
    private double _demoPhase;
    private long _demoSteps;
    public MainWindow()
    {
        InitializeComponent();
        DevicesList.PreviewMouseLeftButtonUp += DeviceCardClick;
        DevicesList.Cursor = System.Windows.Input.Cursors.Hand;
        TelemetryMode.TextAlignment = TextAlignment.Right;
        _locomotion.CriticalSensorLost += LocomotionCriticalSensorLost;
        _demoTimer.Tick += DemoTick;
        _scanTimer.Tick += AutoScanTick;
        Loaded += async (_, _) =>
        {
            ShowPage(OverviewPage, "Genel Bakış", "Sistem durumu ve hızlı başlangıç", OverviewNav);
            RefreshSystemMode();
            var arguments = Environment.GetCommandLineArgs();
            if (arguments.Contains("--normal", StringComparer.OrdinalIgnoreCase))
            {
                SelectProfile(MotionProfile.ClassicVr); await SwitchSystemModeAsync(SystemMode.Original);
            }
            else
            {
                SelectProfile(_systemMode.CurrentMode == SystemMode.Original ? MotionProfile.ClassicVr : MotionProfile.JoyConPhone); await ScanAsync();
                if (arguments.Contains("--autostart", StringComparer.OrdinalIgnoreCase)) StartClick(this, new RoutedEventArgs());
            }
            _scanTimer.Start();
        };
        Closed += async (_, _) => { _demoTimer.Stop(); _scanTimer.Stop(); await StopPhoneMonitorAsync(); await _locomotion.DisposeAsync(); };
    }
    private async void AutoScanTick(object? sender, EventArgs e)
    {
        if (_autoScanBusy) return;
        _autoScanBusy = true;
        try { await ScanAsync(); }
        catch { }
        finally { _autoScanBusy = false; }
    }
    private void LocomotionCriticalSensorLost(object? sender, string message) => Dispatcher.BeginInvoke(async () =>
    {
        await _locomotion.StopAsync();
        SetStopControl(false); StartButton.IsEnabled = _readiness?.State != ReadinessState.NotReady;
        LocomotionState.Text = "OFF"; LocomotionState.Foreground = Brush("#FF9BA8"); TelemetryMode.Text = "GÜVENLİ DURUŞ"; ResetTelemetry();
        ReadinessTitle.Text = "HAREKET BAĞLANTISI KESİLDİ";
        ReadinessMessage.Text = $"Hareket güvenle durduruldu. {message} VR'yi Hazırla düğmesine yeniden bas.";
        await ScanAsync();
    });
    private async Task ScanAsync()
    {
        if (!_autoScanBusy) { StartButton.IsEnabled = false; ReadinessTitle.Text = "TARANIYOR"; }
        var devices = (await _discovery.ScanAsync()).ToList();
        var phoneIndex = devices.FindIndex(x => x.Kind == DeviceKind.Phone);
        if (_phonePairing && phoneIndex >= 0 && !devices[phoneIndex].IsConnected)
            devices[phoneIndex] = new DeviceStatus(DeviceKind.Phone, "Android Telefon", DeviceState.Unknown,
                "owoTrack verisi bekleniyor…", "Telefonda owoTrack'i açıp bağlantıyı başlat.");
        var handsIndex = devices.FindIndex(x => x.Kind == DeviceKind.HandTracking);
        if (handsIndex >= 0)
        {
            var enabled = HandTrackingToggle.IsChecked == true;
            devices[handsIndex] = new DeviceStatus(DeviceKind.HandTracking, "El Takibi",
                enabled ? DeviceState.Connected : DeviceState.Missing,
                enabled ? "Seçili; gerçek el takibi Quest içinde doğrulanır." : "Bu oturum için kapalı.",
                enabled ? "Kapatmak için karta tıkla." : "Açmak için karta tıkla.");
        }
        RefreshHandTrackingVisual();
        RefreshPhoneVisual(devices);
        RefreshCalibrationStatus(devices);
        await RefreshPsMoveStatusAsync();
        var visibleKinds = _profile.Required.Concat(_profile.Optional)
            .Where(x => x != DeviceKind.SteamVr).ToHashSet();
        DevicesList.ItemsSource = devices.Where(x => visibleKinds.Contains(x.Kind)).ToList();
        _readiness = SessionReadinessEvaluator.Evaluate(_profile, devices.ToArray());
        var preflightDevices = PreflightBlockingDevices(devices);
        var questPending = devices.FirstOrDefault(x => x.Kind == DeviceKind.Quest3)?.State == DeviceState.Unknown;
        if (_readiness.State == ReadinessState.NotReady && preflightDevices.Count == 0 && questPending)
            _readiness = new SessionReadiness(ReadinessState.Degraded, "Başlık SteamVR açıldıktan sonra doğrulanacak.", Array.Empty<DeviceStatus>());
        if (!_profile.LocomotionAllowed)
        {
            ReadinessTitle.Text = "NORMAL VR MODU SEÇİLDİ";
            var headsetReady = devices.FirstOrDefault(x => x.Kind == DeviceKind.Quest3)?.IsConnected == true;
            ReadinessMessage.Text = headsetReady ? "Başlık algılandı. NiiMotion kapalı olarak VR'yi başlatabilirsin." : "Hazır. Quest, SteamVR açıldıktan sonra otomatik doğrulanacak.";
            var normalColor = headsetReady ? "#72E1C2" : "#F6C86B";
            ReadinessTitle.Foreground = Brush(normalColor); ReadinessGlyph.Text = headsetReady ? "✓" : "?"; ReadinessGlyph.Foreground = Brush(normalColor); ReadinessIcon.Background = Brush(headsetReady ? "#142B27" : "#30291A");
            LastScanText.Text = $"Son tarama {DateTime.Now:HH:mm:ss}";
            StartButton.Content = "✓ NORMAL VR AKTİF"; StartButton.IsEnabled = false;
            PrepareVrButton.Content = "▶  NORMAL VR'Yİ BAŞLAT";
            PrepareVrButton.IsEnabled = true;
            return;
        }
        ReadinessTitle.Text = _readiness.State switch { ReadinessState.Ready => "SİSTEM HAZIR", ReadinessState.Degraded => "DEGRADED MODE HAZIR", _ => "BAĞLANTI GEREKİYOR" };
        ReadinessMessage.Text = !_profile.LocomotionAllowed ? "VR çıkışı kapalı." : _readiness.State switch
        {
            ReadinessState.Ready => "Tüm gerekli cihazlar bağlı.",
            ReadinessState.Degraded => "Başlatılabilir; bazı ek cihazlar bağlı değil.",
            _ => "Başlamak için aşağıdaki eksik cihazları bağla."
        };
        var color = _readiness.State switch { ReadinessState.Ready => "#72E1C2", ReadinessState.Degraded => "#F6C86B", _ => "#FF9BA8" };
        ReadinessTitle.Foreground = Brush(color); ReadinessGlyph.Text = _readiness.State switch { ReadinessState.Ready => "✓", ReadinessState.Degraded => "~", _ => "!" };
        ReadinessGlyph.Foreground = Brush(color); ReadinessIcon.Background = Brush(_readiness.State switch { ReadinessState.Ready => "#142B27", ReadinessState.Degraded => "#30291A", _ => "#2C1B23" });
        LastScanText.Text = $"Son tarama {DateTime.Now:HH:mm:ss}";
        StartButton.Content = _profile.LocomotionAllowed ? "OYUN MODUNU BAŞLAT" : "NORMAL VR'I AÇ";
        StartButton.IsEnabled = !_profile.LocomotionAllowed || _readiness.State != ReadinessState.NotReady;
        var preflightBlocking = PreflightBlockingDevices(devices);
        PrepareVrButton.IsEnabled = preflightBlocking.Count == 0;
        PrepareVrButton.Content = preflightBlocking.Count == 0 ? "▶  VR'Yİ HAZIRLA VE BAŞLAT" : "EKSİK CİHAZLARI BAĞLA";
    }
    private async void RescanClick(object sender, RoutedEventArgs e) { await Task.Delay(900); await ScanAsync(); }
    private async void OpenGaitLabClick(object sender, RoutedEventArgs e)
    {
        var resumePhone = _phoneMonitor is not null;
        await StopPhoneMonitorAsync();
        try { new GaitLabWindow { Owner = this }.ShowDialog(); }
        finally
        {
            RefreshCalibrationProgress();
            if (resumePhone) try { await EnsurePhoneMonitorAsync(); } catch { }
        }
    }
    private async void OpenPhoneLabClick(object sender, RoutedEventArgs e)
    {
        var resumePhone = _phoneMonitor is not null;
        await StopPhoneMonitorAsync();
        var lab = new PhoneLabWindow { Owner = this };
        lab.PhoneConnected += PhoneLabConnected;
        try { lab.ShowDialog(); }
        finally { if (resumePhone || ProfileUsesPhone()) try { await EnsurePhoneMonitorAsync(); } catch { } }
    }
    private async void PhoneLabConnected(object? sender, string endpoint)
    {
        PhoneTestButton.Background = Brush("#237D69");
        PhoneTestButton.BorderBrush = Brush("#75E6C3");
        PhoneTestButton.BorderThickness = new Thickness(2);
        PhoneTestButton.ToolTip = $"✓ Telefon bağlı · {endpoint}";
        await ScanAsync();
    }
    private void RefreshPhoneVisual(IReadOnlyList<DeviceStatus> devices)
    {
        var connected = devices.FirstOrDefault(x => x.Kind == DeviceKind.Phone)?.IsConnected == true;
        PhoneTestButton.Background = Brush(connected ? "#237D69" : "#2B6196");
        PhoneTestButton.BorderBrush = Brush(connected ? "#75E6C3" : "#76B8E9");
        PhoneTestButton.BorderThickness = new Thickness(connected ? 2 : 1);
        PhoneTestButton.ToolTip = connected ? "✓ Telefon canlı veri gönderiyor" : "Telefon bağlı değil · owoTrack bağlantısını başlat";
    }
    private async void OriginalModeClick(object sender, RoutedEventArgs e) { SelectProfile(MotionProfile.ClassicVr); await SwitchSystemModeAsync(SystemMode.Original); }
    private async void NiiMotionModeClick(object sender, RoutedEventArgs e) { if (_profile == MotionProfile.ClassicVr) SelectProfile(MotionProfile.JoyConPhone); await SwitchSystemModeAsync(SystemMode.NiiMotion); }
    private async Task SwitchSystemModeAsync(SystemMode mode)
    {
        if (_systemMode.CurrentMode == mode) { RefreshSystemMode(); await ScanAsync(); return; }
        OriginalModeButton.IsEnabled = NiiMotionModeButton.IsEnabled = StartButton.IsEnabled = false;
        ReadinessTitle.Text = "SİSTEM MODU DEĞİŞTİRİLİYOR";
        ReadinessMessage.Text = "Hareket çıkışı durduruluyor ve sürücü ayarı değiştiriliyor…";
        try
        {
            _demoTimer.Stop(); await _locomotion.StopAsync();
            await _systemMode.ApplyAsync(mode);
            RefreshSystemMode();
            ReadinessTitle.Text = mode == SystemMode.NiiMotion ? "NIIMOTION AKTİF" : "ORİJİNAL SİSTEM AKTİF";
            ReadinessMessage.Text = mode == SystemMode.NiiMotion
                ? "NiiMotion sürücüsü ve oyun ayarları hazır. VR oturumunu ana düğmeden başlatabilirsin."
                : "NiiMotion tamamen devre dışı. SteamVR ve oyunlar kendi özgün ayarlarıyla çalışır.";
            await Task.Delay(1800); await ScanAsync();
        }
        catch (Exception ex) { ReadinessTitle.Text = "GEÇİŞ TAMAMLANAMADI"; ReadinessMessage.Text = ex.Message; }
        finally { OriginalModeButton.IsEnabled = NiiMotionModeButton.IsEnabled = true; }
    }
    private void RefreshSystemMode()
    {
        var active = _systemMode.CurrentMode == SystemMode.NiiMotion;
        SetModeButton(OriginalModeButton, !active, "NORMAL VR");
        SetModeButton(NiiMotionModeButton, active, "NIIMOTION");
        LocomotionState.Text = active ? "✓ NIIMOTION AKTİF" : "NIIMOTION KAPALI";
        LocomotionState.Foreground = Brush(active ? "#72E1C2" : "#8FC5FF");
        TelemetryMode.Text = active ? "NiiMotion etkin · Bir profil seçip başlat" : "NiiMotion devre dışı · Özgün VR kontrolü";
        TelemetryMode.Foreground = Brush("#8FA0B8");
        SidebarModeState.Text = active ? "NiiMotion etkin" : "NiiMotion kapalı";
        SidebarModeState.Foreground = Brush(active ? "#54D4A8" : "#8C99A5");
    }

    private void OverviewNavClick(object sender, RoutedEventArgs e) => ShowPage(OverviewPage, "Genel Bakış", "Sistem durumu ve hızlı başlangıç", OverviewNav);
    private void ProfileMenuClick(object sender, RoutedEventArgs e)
    {
        ProfilePopup.IsOpen = !ProfilePopup.IsOpen;
    }
    private void ModesNavClick(object sender, RoutedEventArgs e) => ShowPage(ModesPage, "Oyun Modları", "Nasıl hareket etmek istediğini seç", ModesNav);
    private void DevicesNavClick(object sender, RoutedEventArgs e) => ShowPage(DevicesPage, "Cihazlar", "Canlı bağlantı ve sensör durumu", DevicesNav);
    private void ToolsNavClick(object sender, RoutedEventArgs e)
    {
        RefreshCalibrationProgress();
        ShowPage(ToolsPage, "Test ve Kalibrasyon", "Kişisel ölçüm, doğrulama ve veri kaydı", ToolsNav);
    }
    private void ShowPage(UIElement page, string title, string subtitle, Button selectedNav)
    {
        foreach (var item in new UIElement[] { OverviewPage, ModesPage, DevicesPage, ToolsPage }) item.Visibility = item == page ? Visibility.Visible : Visibility.Collapsed;
        foreach (var nav in new[] { OverviewNav, ModesNav, DevicesNav, ToolsNav })
        {
            nav.Background = Brush(nav == selectedNav ? "#122536" : "#080D13");
            nav.BorderBrush = Brush(nav == selectedNav ? "#1E6F9F" : "#080D13");
            nav.Foreground = Brush(nav == selectedNav ? "#FFFFFF" : "#A8B2BC");
        }
        PageTitle.Text = title; PageSubtitle.Text = subtitle;
    }
    private static void SetModeButton(Button button, bool selected, string title)
    {
        button.Content = selected ? $"✓ {title}" : title;
        button.Background = Brush(selected ? "#2E6FE8" : "#0D1828");
        button.BorderBrush = Brush(selected ? "#9AC1FF" : "#2A3B56");
        button.BorderThickness = new Thickness(selected ? 2 : 1);
        button.Opacity = selected ? 1 : .62;
    }
    private async void JoyConTestClick(object sender, RoutedEventArgs e)
    {
        JoyConTestButton.IsEnabled = false;
        CalibrationJoyConStatus.Text = "Ölçülüyor… Joy-Con'ları hareket ettir";
        CalibrationJoyConStatus.Foreground = Brush("#F6C86B");
        CalibrationLiveResult.Text = "Joy-Con sensörlerinden 300 örnek alınıyor. Bu işlem yaklaşık 3 saniye sürer.";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var results = await new JoyConDiagnosticsService().RunAsync(300, timeout.Token);
            var ordered = results.OrderBy(x => x.Side).ToArray();
            CalibrationJoyConStatus.Text = ordered.Length >= 2 ? "✓ İki sensör de veri gönderiyor" : "! Yalnız bir sensör bulundu";
            CalibrationJoyConStatus.Foreground = Brush(ordered.Length >= 2 ? "#54D4A8" : "#F6C86B");
            CalibrationLiveResult.Text = string.Join("   •   ", ordered.Select(x => $"{x.Side}: {x.Timing.SampleRateHz:F1} Hz · kararlılık {x.Timing.JitterMs:F2} ms · {x.SampleCount} örnek"));
        }
        catch (Exception ex)
        {
            CalibrationJoyConStatus.Text = "✕ Test tamamlanamadı";
            CalibrationJoyConStatus.Foreground = Brush("#FF7F9B");
            CalibrationLiveResult.Text = $"Joy-Con verisi alınamadı: {ex.Message}";
        }
        finally { JoyConTestButton.IsEnabled = true; }
    }
    private async void PhoneTestClick(object sender, RoutedEventArgs e) => await BeginPhonePairingAsync();
    private async void PsMoveIdentifyClick(object sender, RoutedEventArgs e)
    {
        new PsMoveLabWindow { Owner = this }.ShowDialog();
        await ScanAsync();
        return;
#pragma warning disable CS0162
        PsMoveIdentifyButton.IsEnabled = false;
        CalibrationPsMoveStatus.Text = "Tanıtılıyor… sol kırmızı · sağ mavi";
        CalibrationPsMoveStatus.Foreground = Brush("#F6C86B");
        CalibrationLiveResult.Text = "PS Move renkleri 8 saniye gösteriliyor. Titreşim kapalıdır.";
        try
        {
            var assignments = await new PsMoveAssignmentStore(@"C:\NiirMotion\config\psmove-assignments.json").LoadAsync();
            if (assignments is not { IsComplete: true }) throw new InvalidOperationException("Önce PS Move sol/sağ atamasını tamamla.");
            await new PsMoveDiagnosticsService().ShowAssignmentColorsAsync(assignments, TimeSpan.FromSeconds(8));
            CalibrationPsMoveStatus.Text = "Sensörler ölçülüyor…";
            var stored = await new PsMoveCalibrationStore(@"C:\NiirMotion\config\psmove-calibrations.json").LoadAsync();
            var health = await new PsMoveDiagnosticsService().CaptureCalibratedHealthAsync(stored, TimeSpan.FromSeconds(3));
            if (health.Count != 2) throw new InvalidOperationException("İki kalibre edilmiş PS Move akışı bulunamadı.");
            CalibrationPsMoveStatus.Text = "✓ İki Move bağlı · sensörler sağlıklı";
            CalibrationPsMoveStatus.Foreground = Brush("#54D4A8");
            CalibrationLiveResult.Text = "✓ PS Move doğrulandı · " + string.Join("   •   ", health.OrderBy(x => x.StableId).Select(x => $"{(x.StableId == assignments.LeftStableId ? "Sol" : "Sağ")}: {x.ReportRateHz:0.0} Hz · jitter {x.JitterMs:0.00} ms · kayıp {x.MissingReports} · ivme {x.MinimumAccelerationG:0.00}–{x.MaximumAccelerationG:0.00} g · batarya {BatteryText(x.Battery)}"));
            CalibrationLiveResult.Foreground = Brush("#63DFBB");
        }
        catch (Exception ex)
        {
            CalibrationPsMoveStatus.Text = "✕ İki PS Move bağlı değil";
            CalibrationPsMoveStatus.Foreground = Brush("#FF7F9B");
            CalibrationLiveResult.Text = $"PS Move tanıtılamadı: {ex.Message}";
            CalibrationLiveResult.Foreground = Brush("#FF9BA8");
        }
        finally { PsMoveIdentifyButton.IsEnabled = true; }
#pragma warning restore CS0162
    }

    private static string BatteryText(byte value) => value switch
    {
        0x00 => "<%20", 0x01 => "%20+", 0x02 => "%40+", 0x03 => "%60+", 0x04 => "%80+", 0x05 => "tam", 0xEE => "şarj", 0xEF => "şarj tamam", _ => $"0x{value:X2}"
    };
    private async Task BeginPhonePairingAsync()
    {
        if (_phonePairing) return;
        _phonePairing = true;
        PhoneTestButton.IsEnabled = false;
        CalibrationPhoneStatus.Text = "Dinleniyor… owoTrack'i başlat";
        CalibrationPhoneStatus.Foreground = Brush("#F6C86B");
        CalibrationLiveResult.Text = "Telefon bağlantısı için 15 saniyelik eşleşme penceresi açıldı.";
        await ScanAsync();
        ReadinessTitle.Text = "TELEFON BEKLENİYOR"; ReadinessMessage.Text = "Telefonda owoTrack'i aç ve bağlantıyı başlat. 15 saniye dinliyorum…";
        try
        {
            await EnsurePhoneMonitorAsync();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            string endpoint = "";
            while (DateTime.UtcNow < deadline && !PhonePresence.TryGetFresh(out endpoint)) await Task.Delay(100);
            if (!PhonePresence.TryGetFresh(out endpoint)) throw new TimeoutException();
            ReadinessTitle.Text = "TELEFON BAĞLANDI";
            ReadinessMessage.Text = $"Telefon hazır · {endpoint} · bağlantı uygulama açık kaldığı sürece korunacak.";
            PhoneTestButton.Background = Brush("#237D69"); PhoneTestButton.BorderBrush = Brush("#75E6C3"); PhoneTestButton.BorderThickness = new Thickness(2);
            PhoneTestButton.ToolTip = $"✓ Telefon bağlı · {endpoint}";
            CalibrationPhoneStatus.Text = "✓ Canlı telefon verisi alınıyor";
            CalibrationPhoneStatus.Foreground = Brush("#54D4A8");
            CalibrationLiveResult.Text = $"Telefon doğrulandı · {endpoint} · bağlantı izleniyor.";
        }
        catch (Exception)
        {
            ReadinessTitle.Text = "TELEFON BULUNAMADI"; ReadinessMessage.Text = "owoTrack'in açık ve telefon ile bilgisayarın aynı ağda olduğundan emin ol, sonra tekrar dene.";
            CalibrationPhoneStatus.Text = "✕ Telefon verisi bulunamadı";
            CalibrationPhoneStatus.Foreground = Brush("#FF7F9B");
            CalibrationLiveResult.Text = "Telefon bulunamadı. owoTrack'i aç, bilgisayar ile aynı ağa bağlan ve tekrar dene.";
        }
        finally { _phonePairing = false; PhoneTestButton.IsEnabled = true; await ScanAsync(); }
    }

    private void RefreshCalibrationStatus(IReadOnlyList<DeviceStatus> devices)
    {
        var phone = devices.FirstOrDefault(x => x.Kind == DeviceKind.Phone);
        var board = devices.FirstOrDefault(x => x.Kind == DeviceKind.BalanceBoard);
        var left = devices.FirstOrDefault(x => x.Kind == DeviceKind.JoyConLeft);
        var right = devices.FirstOrDefault(x => x.Kind == DeviceKind.JoyConRight);
        if (!_phonePairing)
        {
            CalibrationPhoneStatus.Text = phone?.IsConnected == true ? "✓ Canlı telefon verisi alınıyor" : "owoTrack verisini dinlemeye başla";
            CalibrationPhoneStatus.Foreground = Brush(phone?.IsConnected == true ? "#54D4A8" : "#94A1AD");
        }
        CalibrationBoardStatus.Text = board?.IsConnected == true ? "✓ Board bağlı ve hazır" : "Basınç sensörlerini kontrol et";
        CalibrationBoardStatus.Foreground = Brush(board?.IsConnected == true ? "#54D4A8" : "#94A1AD");
        if (left?.IsConnected == true && right?.IsConnected == true && JoyConTestButton.IsEnabled)
        {
            CalibrationJoyConStatus.Text = "✓ İki Joy-Con bağlı · sensör testine hazır";
            CalibrationJoyConStatus.Foreground = Brush("#54D4A8");
        }
    }

    private async Task RefreshPsMoveStatusAsync()
    {
        try
        {
            var assignments = await new PsMoveAssignmentStore(@"C:\NiirMotion\config\psmove-assignments.json").LoadAsync();
            var probes = new PsMoveDiagnosticsService().Discover().Where(x => x.SensorReportsPossible).ToArray();
            var left = assignments is { IsComplete: true } && probes.Any(x => x.Device.StableId == assignments.LeftStableId);
            var right = assignments is { IsComplete: true } && probes.Any(x => x.Device.StableId == assignments.RightStableId);
            CalibrationPsMoveStatus.Text = left && right ? "✓ İki Move bağlı · renklerle tanıt" : $"{(left ? "Sol ✓" : "Sol eksik")} · {(right ? "Sağ ✓" : "Sağ eksik")}";
            CalibrationPsMoveStatus.Foreground = Brush(left && right ? "#54D4A8" : "#94A1AD");
        }
        catch
        {
            CalibrationPsMoveStatus.Text = "PS Move durumu okunamadı";
            CalibrationPsMoveStatus.Foreground = Brush("#FF7F9B");
        }
    }

    private void RefreshCalibrationProgress()
    {
        const string progressPath = @"C:\NiirMotion\data\user-gait\joycon-learning\progress-v2.json";
        var completed = 0;
        try
        {
            if (File.Exists(progressPath)) completed = Math.Clamp(JsonSerializer.Deserialize<int[]>(File.ReadAllText(progressPath))?.Distinct().Count() ?? 0, 0, 24);
        }
        catch { }
        CalibrationProgressText.Text = completed >= 24 ? "24 / 24 · model hazır" : $"{completed} / 24 parça · sıradaki {completed + 1}";
        PersonalCalibrationProgress.Value = completed;
        CalibrationProgressTitle.Foreground = Brush(completed >= 24 ? "#54D4A8" : "#35B8F5");
    }

    private async void ApplyPersonalCalibrationClick(object sender, RoutedEventArgs e)
    {
        ApplyCalibrationButton.IsEnabled = false;
        CalibrationLiveResult.Text = "Tamamlanan yürüyüş kayıtları analiz ediliyor…";
        try
        {
            var analyzer = new PersonalGaitAnalyzer();
            var analysis = analyzer.Analyze(@"C:\NiirMotion\data\user-gait");
            const string profilePath = @"C:\NiirMotion\config\personal-gait-pace.json";
            PersonalGaitPace? previous = null;
            try { if (File.Exists(profilePath)) previous = await PersonalGaitPace.LoadAsync(profilePath); } catch { }
            await analyzer.ApplyAsync(analysis, profilePath);
            var pace = analysis.Pace;
            CalibrationLiveResult.Text = $"✓ Kişisel profil uygulandı · Yavaş {pace.SlowP95Dps:0.00} · Doğal {pace.NaturalP95Dps:0.00} · Hızlı {pace.FastP95Dps:0.00} dps · {analysis.AcceptedSessions} kayıt / {analysis.AcceptedSamples:N0} örnek. Yeni değerler bir sonraki VR oturumunda kullanılacak.";
            CalibrationLiveResult.Foreground = Brush("#63DFBB");
            ApplyCalibrationButton.Content = "✓  KALİBRASYON UYGULANDI";
            ApplyCalibrationButton.Background = Brush("#174D3E");
            ApplyCalibrationButton.BorderBrush = Brush("#45B990");
            ShowCalibrationComparison(previous, pace);
        }
        catch (Exception ex)
        {
            CalibrationLiveResult.Text = $"Kalibrasyon uygulanamadı: {ex.Message}";
            CalibrationLiveResult.Foreground = Brush("#FF9BA8");
        }
        finally { ApplyCalibrationButton.IsEnabled = true; }
    }

    private void ShowCalibrationComparison(PersonalGaitPace? previous, PersonalGaitPace current)
    {
        CalibrationComparisonCard.Visibility = Visibility.Visible;
        SetComparison(SlowComparisonText, SlowComparisonDelta, previous?.SlowP95Dps, current.SlowP95Dps);
        SetComparison(NaturalComparisonText, NaturalComparisonDelta, previous?.NaturalP95Dps, current.NaturalP95Dps);
        SetComparison(FastComparisonText, FastComparisonDelta, previous?.FastP95Dps, current.FastP95Dps);
    }

    private static void SetComparison(TextBlock valueText, TextBlock deltaText, double? previous, double current)
    {
        valueText.Text = previous.HasValue ? $"{previous:0.0} → {current:0.0}" : $"Yeni: {current:0.0}";
        if (!previous.HasValue || previous.Value <= 0) { deltaText.Text = "kişisel referans"; return; }
        var percent = (current / previous.Value - 1) * 100;
        deltaText.Text = $"{percent:+0.0;-0.0;0.0}%";
        deltaText.Foreground = Brush(Math.Abs(percent) < .05 ? "#94A1AD" : percent > 0 ? "#54D4A8" : "#F6C86B");
    }
    private async Task EnsurePhoneMonitorAsync()
    {
        if (_phoneMonitor is not null) return;
        var monitor = new OwoTrackSensorSource();
        try { await monitor.StartAsync(); _phoneMonitor = monitor; }
        catch { await monitor.DisposeAsync(); throw; }
    }
    private async Task StopPhoneMonitorAsync()
    {
        var monitor = _phoneMonitor;
        _phoneMonitor = null;
        if (monitor is not null) await monitor.DisposeAsync();
    }
    private async void TestModeChanged(object sender, RoutedEventArgs e)
    {
        _discovery = TestModeToggle.IsChecked == true ? new MockHardwareDiscoveryService() : new HardwareDiscoveryService();
        await ScanAsync();
    }
    private async void NativeProfileClick(object sender, RoutedEventArgs e)
    {
        SelectProfile(MotionProfile.ClassicVr);
        await SwitchSystemModeAsync(SystemMode.Original);
    }
    private async void JoyConProfileClick(object sender, RoutedEventArgs e) { SelectProfile(MotionProfile.JoyConOnly); await ScanAsync(); }
    private async void JoyConPhoneProfileClick(object sender, RoutedEventArgs e) { SelectProfile(MotionProfile.JoyConPhone); await ScanAsync(); }
    private async void FullProfileClick(object sender, RoutedEventArgs e) { SelectProfile(MotionProfile.FullFusion); await ScanAsync(); }
    private async void PhoneProfileClick(object sender, RoutedEventArgs e) { SelectProfile(MotionProfile.PhoneOnly); await ScanAsync(); }
    private async void BoardOnlyProfileClick(object sender, RoutedEventArgs e) { SelectProfile(MotionProfile.BoardOnly); await ScanAsync(); }
    private void OpenBoardLabClick(object sender, RoutedEventArgs e) => new BoardLabWindow { Owner = this }.ShowDialog();
    private async void BoardJoyConProfileClick(object sender, RoutedEventArgs e) { SelectProfile(MotionProfile.BoardJoyCon); await ScanAsync(); }
    private async void BoardPhoneProfileClick(object sender, RoutedEventArgs e) { SelectProfile(MotionProfile.BoardPhone); await ScanAsync(); }
    private async void HandTrackingChanged(object sender, RoutedEventArgs e) { RefreshHandTrackingVisual(); if (IsLoaded) await ScanAsync(); }
    private void HandTrackingQuickClick(object sender, RoutedEventArgs e) => HandTrackingToggle.IsChecked = HandTrackingToggle.IsChecked != true;
    private void RefreshHandTrackingVisual() { }
    private async void DeviceCardClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not DeviceStatus device) return;
        if (device.Kind == DeviceKind.HandTracking)
        {
            HandTrackingToggle.IsChecked = HandTrackingToggle.IsChecked != true;
            e.Handled = true;
        }
        else if (device.Kind == DeviceKind.Phone)
        {
            e.Handled = true;
            if (!device.IsConnected) await BeginPhonePairingAsync();
        }
    }
    private IReadOnlyList<DeviceStatus> PreflightBlockingDevices(IReadOnlyCollection<DeviceStatus> devices)
    {
        var byKind = devices.ToDictionary(x => x.Kind);
        return _profile.Required
            .Where(kind => kind is not DeviceKind.Quest3 and not DeviceKind.SteamVr)
            .Where(kind => !byKind.TryGetValue(kind, out var status) || !status.IsConnected)
            .Select(kind => byKind.TryGetValue(kind, out var status) ? status : new DeviceStatus(kind, kind.ToString(), DeviceState.Missing, "Tarama sonucu yok.", "Cihazı bağla."))
            .ToArray();
    }
    private void SelectProfile(MotionProfile profile)
    {
        _profile = profile;
        if (!IsLoaded) return;
        ProfilePopup.IsOpen = false;
        foreach (var tile in new[] { NativeModeTile, JoyConModeTile, JoyConPhoneModeTile, FullModeTile, PhoneModeTile, BoardOnlyModeTile, BoardJoyConModeTile, BoardPhoneModeTile })
            tile.Background = Brush("#0D151E");
        var tiles = new[] { NativeModeTile, JoyConModeTile, JoyConPhoneModeTile, FullModeTile, PhoneModeTile, BoardOnlyModeTile, BoardJoyConModeTile, BoardPhoneModeTile };
        foreach (var tile in tiles) { tile.BorderBrush = Brush("#22303B"); tile.Opacity = 1; }
        foreach (var badge in new[] { NativeSelectionBadge, JoyConSelectionBadge, JoyConPhoneSelectionBadge, FullSelectionBadge, PhoneSelectionBadge, BoardOnlySelectionBadge, BoardJoyConSelectionBadge, BoardPhoneSelectionBadge }) badge.Visibility = Visibility.Collapsed;
        var selected = profile == MotionProfile.ClassicVr ? NativeModeTile : profile == MotionProfile.JoyConOnly ? JoyConModeTile : profile == MotionProfile.PhoneOnly ? PhoneModeTile : profile == MotionProfile.FullFusion ? FullModeTile : profile == MotionProfile.BoardOnly ? BoardOnlyModeTile : profile == MotionProfile.BoardJoyCon ? BoardJoyConModeTile : profile == MotionProfile.BoardPhone ? BoardPhoneModeTile : JoyConPhoneModeTile;
        var selectedBadge = profile == MotionProfile.ClassicVr ? NativeSelectionBadge : profile == MotionProfile.JoyConOnly ? JoyConSelectionBadge : profile == MotionProfile.PhoneOnly ? PhoneSelectionBadge : profile == MotionProfile.FullFusion ? FullSelectionBadge : profile == MotionProfile.BoardOnly ? BoardOnlySelectionBadge : profile == MotionProfile.BoardJoyCon ? BoardJoyConSelectionBadge : profile == MotionProfile.BoardPhone ? BoardPhoneSelectionBadge : JoyConPhoneSelectionBadge;
        selected.Background = Brush("#12283A"); selected.BorderBrush = Brush("#38A8F3"); selected.BorderThickness = new Thickness(2); selected.Opacity = 1;
        selectedBadge.Visibility = Visibility.Visible;
        foreach (var tile in tiles.Where(x => x != selected)) tile.BorderThickness = new Thickness(1);
        var profileName = profile == MotionProfile.ClassicVr ? "Normal VR" : profile == MotionProfile.JoyConOnly ? "Sadece Joy-Con" : profile == MotionProfile.JoyConPhone ? "Joy-Con + Telefon" : profile == MotionProfile.PhoneOnly ? "Sadece Telefon" : profile == MotionProfile.BoardOnly ? "Balance Board" : profile == MotionProfile.BoardJoyCon ? "Board + Joy-Con" : profile == MotionProfile.BoardPhone ? "Board + Telefon" : "Tüm Cihazlar";
        SidebarProfileName.Text = ActiveProfileName.Text = profileName;
        ActiveProfileDetail.Text = profile.LocomotionAllowed ? "Yerinde yürüyüş çıkışı" : "Özgün kontrolcü hareketi";
        UpdateProfileInformation(profile);
    }

    private void UpdateProfileInformation(MotionProfile profile)
    {
        var information = profile.Id switch
        {
            "classic-vr" => ("✓  NORMAL VR", "  ·  NiiMotion hareket üretmez", "Quest ve kontrolcüler özgün VR davranışıyla çalışır; sensör verileri oyun girişine aktarılmaz."),
            "joycon-only" => ("✓  JOY-CON YÜRÜYÜŞÜ", "  ·  Telefon veya board gerekmez", "Bacaklardaki iki Joy-Con yerinde adımlarını algılar. Bağlantı kesilirse hareket anında sıfırlanır."),
            "joycon-phone" => ("✓  JOY-CON + TELEFON", "  ·  Dengeli ve önerilen profil", "Joy-Con'lar adımları, göğüsteki telefon gövde hareketini izler. Telefon kesilirse sistem güvenli biçimde durur."),
            "phone-only" => ("◇  SADECE TELEFON", "  ·  Deneysel hareket algılama", "Göğüsteki telefon yerinde yürüyüşü tahmin eder. Joy-Con gerekmez; kararlılık birleşik profillerden düşüktür."),
            "board-only" => ("◇  BALANCE BOARD", "  ·  Basınçla yürüyüş ve dönüş", "Ağırlık aktarımı hareket ve dönüşe çevrilir. Karttan inildiğinde ya da bağlantı kesildiğinde çıkış sıfırlanır."),
            "board-joycon" => ("✓  BOARD + JOY-CON", "  ·  Bacak ve basınç füzyonu", "Joy-Con'lar adımı, Balance Board ağırlık aktarımını izler. Telefon kullanmadan daha kararlı hareket sağlar."),
            "board-phone" => ("◇  BOARD + TELEFON", "  ·  Basınç ve gövde füzyonu", "Balance Board ağırlığı, telefon gövde hareketini izler. Joy-Con gerekmez; bu profil deneyseldir."),
            _ => ("✓  TAM FÜZYON", "  ·  Tüm hareket sensörleri birlikte", "Joy-Con, telefon ve Balance Board verileri birleştirilir. Zorunlu bir cihaz kesilirse hareket güvenle sıfırlanır.")
        };

        ProfileInfoTitle.Text = information.Item1;
        ProfileInfoSummary.Text = information.Item2;
        ProfileInfoDetail.Text = information.Item3;
        var experimental = profile == MotionProfile.PhoneOnly || profile == MotionProfile.BoardOnly || profile == MotionProfile.BoardPhone;
        ProfileInfoTitle.Foreground = Brush(experimental ? "#F6C86B" : "#54D4A8");
    }
    private async void LaunchSteamVrClick(object sender, RoutedEventArgs e)
    {
        await ScanAsync();
        var latestDevices = (DevicesList.ItemsSource as IEnumerable<DeviceStatus>)?.ToArray() ?? [];
        var preflightBlocking = PreflightBlockingDevices(latestDevices);
        if (preflightBlocking.Count > 0)
        {
            ReadinessTitle.Text = "CİHAZLAR EKSİK";
            ReadinessMessage.Text = $"Önce bağla: {string.Join(", ", preflightBlocking.Select(x => x.Name))}. SteamVR başlatılmadı.";
            return;
        }
        if (sender is Button button) button.IsEnabled = false;
        ReadinessTitle.Text = "VR HAZIRLANIYOR"; ReadinessMessage.Text = "Virtual Desktop bağlantısı kontrol ediliyor…";
        try
        {
            _demoTimer.Stop(); await _locomotion.StopAsync();
            var virtualDesktopReady = latestDevices.Any(x => x.Kind == DeviceKind.VirtualDesktop && x.IsConnected);
            if (!virtualDesktopReady)
            {
                if (Process.GetProcessesByName("vrserver").Length > 0)
                    await _systemMode.StopSteamVrAsync();
                ReadinessTitle.Text = "QUEST BAĞLANTISI BEKLENİYOR";
                ReadinessMessage.Text = "Quest'te Virtual Desktop'ı açıp bu bilgisayara bağlan. Bağlantı gelince SteamVR otomatik başlayacak…";
            }
            else ReadinessMessage.Text = "Virtual Desktop bağlantısının kararlılığı doğrulanıyor…";
            await WaitForVirtualDesktopSessionAsync(TimeSpan.FromMinutes(2));
            await ScanAsync();
            ReadinessTitle.Text = "VR HAZIRLANIYOR";
            ReadinessMessage.Text = "Virtual Desktop bağlı. SteamVR doğru sırayla başlatılıyor…";
            if (_profile == MotionProfile.ClassicVr)
            {
                if (_systemMode.CurrentMode != SystemMode.Original) await _systemMode.ApplyAsync(SystemMode.Original);
                LaunchSteamVrViaVirtualDesktop();
                RefreshSystemMode();
                var normalDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
                while (DateTime.UtcNow < normalDeadline && Process.GetProcessesByName("vrserver").Length == 0) await Task.Delay(500);
                await Task.Delay(1200); await ScanAsync();
                var questReady = (DevicesList.ItemsSource as IEnumerable<DeviceStatus>)?.Any(x => x.Kind == DeviceKind.Quest3 && x.IsConnected) == true;
                if (!questReady) throw new InvalidOperationException("SteamVR açıldı ancak Quest 3 bağlantısı doğrulanamadı.");
                ReadinessTitle.Text = "NORMAL VR BAŞLATILDI";
                ReadinessMessage.Text = "NiiMotion kapalı; oyunu kontrolcülerinle normal şekilde oynayabilirsin.";
                return;
            }
            if (_systemMode.CurrentMode != SystemMode.NiiMotion) await _systemMode.ApplyAsync(SystemMode.NiiMotion);
            else _systemMode.EnsureGameOverrides(SystemMode.NiiMotion);
            LaunchSteamVrViaVirtualDesktop();
            await WaitForSteamVrDriverAsync(TimeSpan.FromSeconds(75));
            RefreshSystemMode(); await ScanAsync();
            if (_readiness?.State == ReadinessState.NotReady) throw new InvalidOperationException("SteamVR açıldı ancak başlık veya gerekli cihazlardan biri doğrulanamadı. Hareket çıkışı başlatılmadı.");
            StartClick(StartButton, new RoutedEventArgs());
        }
        catch (Exception ex) { ReadinessTitle.Text = "VR HAZIRLANAMADI"; ReadinessMessage.Text = ex.Message; }
        finally { if (sender is Button prepareButton) prepareButton.IsEnabled = true; }
    }

    private static async Task WaitForVirtualDesktopSessionAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        DateTime? continuouslyPresentSince = null;
        while (DateTime.UtcNow < deadline)
        {
            if (VirtualDesktopSessionPresence.IsPresent())
            {
                continuouslyPresentSince ??= DateTime.UtcNow;
                if (DateTime.UtcNow - continuouslyPresentSince >= TimeSpan.FromSeconds(5)) return;
            }
            else continuouslyPresentSince = null;
            await Task.Delay(500);
        }
        throw new TimeoutException("Virtual Desktop başlık bağlantısı iki dakika içinde kurulmadı. Quest'te Virtual Desktop'ı açıp bilgisayara bağlandıktan sonra yeniden deneyin.");
    }

    private static async Task WaitForSteamVrDriverAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var serverReady = Process.GetProcessesByName("vrserver").Length > 0;
            var pipeReady = false;
            try { pipeReady = Directory.GetFiles(@"\\.\pipe\").Any(x => x.EndsWith("NiiRMotion.VrOutput.v1", StringComparison.OrdinalIgnoreCase)); } catch { }
            if (serverReady && pipeReady) { await Task.Delay(1200); return; }
            await Task.Delay(500);
        }
        throw new TimeoutException("SteamVR NiiMotion sürücüsü 75 saniye içinde hazır olmadı.");
    }
    private static void LaunchSteamVrViaVirtualDesktop()
    {
        if (Process.GetProcessesByName("vrserver").Length > 0) return;
        if (!VirtualDesktopSessionPresence.IsStable())
            throw new InvalidOperationException("Quest ile Virtual Desktop bağlantısı henüz kararlı değil.");
        const string streamer = @"C:\Program Files\Virtual Desktop Streamer\VirtualDesktop.Streamer.exe";
        const string vrStartup = @"C:\Program Files (x86)\Steam\steamapps\common\SteamVR\bin\win64\vrstartup.exe";
        if (!File.Exists(streamer)) throw new FileNotFoundException("Virtual Desktop Streamer bulunamadı.", streamer);
        if (!File.Exists(vrStartup)) throw new FileNotFoundException("SteamVR başlangıç bileşeni bulunamadı.", vrStartup);
        Process.Start(new ProcessStartInfo(streamer, $"\"{vrStartup}\"") { UseShellExecute = true });
    }
    private async void StartClick(object sender, RoutedEventArgs e)
    {
        if (!_profile.LocomotionAllowed) { StartButton.IsEnabled = false; try { await _locomotion.StopAsync(); await _systemMode.ApplyAsync(SystemMode.Original); RefreshSystemMode(); LaunchSteamVrViaVirtualDesktop(); ReadinessTitle.Text = "NORMAL VR HAZIR"; ReadinessMessage.Text = "NiiMotion kapalı; cihazların özgün ayarları kullanılıyor."; } catch (Exception ex) { ReadinessMessage.Text = ex.Message; } finally { StartButton.IsEnabled = true; } return; }
        if (_readiness?.State == ReadinessState.NotReady) return; StartButton.IsEnabled = false;
        if (_discovery.IsTestMode) { _demoPhase = 0; _demoSteps = 0; _demoTimer.Start(); SetStopControl(true); SetRunningVisuals("DEMO OUTPUT — GERÇEK VR'A GÖNDERİLMEZ"); ReadinessMessage.Text = "Demo oturumu çalışıyor. Telemetri simülasyondur; gerçek donanım doğrulaması değildir."; return; }
        try
        {
            var calibration = Path.Combine(AppContext.BaseDirectory, "calibration", "gait-v1.json");
            if (!File.Exists(calibration)) calibration = Path.Combine(Environment.CurrentDirectory, "calibration", "gait-v1.json");
            if (_systemMode.CurrentMode != SystemMode.NiiMotion) await _systemMode.ApplyAsync(SystemMode.NiiMotion);
            else _systemMode.EnsureGameOverrides(SystemMode.NiiMotion);
            var includePhone = _profile is var p && (p == MotionProfile.JoyConPhone || p == MotionProfile.FullFusion || p == MotionProfile.PhoneOnly || p == MotionProfile.BoardPhone);
            var includeBoard = _profile == MotionProfile.FullFusion || _profile == MotionProfile.BoardOnly || _profile == MotionProfile.BoardJoyCon || _profile == MotionProfile.BoardPhone;
            if (includePhone) await StopPhoneMonitorAsync();
            await _locomotion.StartAsync(calibration, includePhone, phoneOnly: _profile == MotionProfile.PhoneOnly || _profile == MotionProfile.BoardPhone, includeBoard: includeBoard, boardOnly: _profile == MotionProfile.BoardOnly); SetStopControl(true); SetRunningVisuals(_locomotion.ModeDescription); ReadinessMessage.Text = includeBoard ? "Board otomatik sıfırlandı. Üzerine çıkıp yerinde yürüyebilirsin." : "Hazır. Yerinde yürüyerek oyunda ilerleyebilirsin.";
        }
        catch (Exception ex) { await _locomotion.StopAsync(); if (ProfileUsesPhone()) try { await EnsurePhoneMonitorAsync(); } catch { } SetStopControl(false); StartButton.IsEnabled = _readiness?.State != ReadinessState.NotReady; LocomotionState.Text = "OFF"; LocomotionState.Foreground = Brushes.LightPink; ReadinessMessage.Text = $"Locomotion başlatılamadı: {ex.Message}"; }
    }
    private async void StopClick(object sender, RoutedEventArgs e)
    {
        SetStopControl(false); _demoTimer.Stop(); await _locomotion.StopAsync();
        if (ProfileUsesPhone()) try { await EnsurePhoneMonitorAsync(); } catch { }
        LocomotionState.Text = "OFF"; LocomotionState.Foreground = Brush("#FF9BA8"); TelemetryMode.Text = "KAPALI"; ResetTelemetry();
        StartButton.IsEnabled = _profile.LocomotionAllowed && _readiness?.State != ReadinessState.NotReady; ReadinessMessage.Text = "Locomotion güvenli şekilde durduruldu ve output ayrıldı.";
    }
    private void SetStopControl(bool running)
    {
        StopButton.IsEnabled = running;
        StopButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
    }
    private void SetRunningVisuals(string mode) { LocomotionState.Text = "ON"; LocomotionState.Foreground = Brush("#72E1C2"); TelemetryMode.Text = mode; TelemetryMode.Foreground = Brush("#72E1C2"); }
    private void DemoTick(object? sender, EventArgs e)
    {
        _demoPhase += 0.18; var cadence = 1.85 + Math.Sin(_demoPhase) * 0.22; var confidence = 82 + Math.Sin(_demoPhase * 0.55) * 9; var speed = Math.Clamp(cadence / 2.5, 0, 0.9);
        if ((int)(_demoPhase * 10) % 16 == 0) _demoSteps++;
        CadenceValue.Text = cadence.ToString("0.00"); CadenceBar.Value = cadence; ConfidenceValue.Text = confidence.ToString("0"); ConfidenceBar.Value = confidence;
        TargetSpeedValue.Text = speed.ToString("0.00"); TargetSpeedBar.Value = speed; GaitStateValue.Text = cadence > 2 ? "FAST WALK" : "WALKING"; StepCountValue.Text = $"{_demoSteps} adım · demo";
    }
    private void ResetTelemetry() { CadenceValue.Text = "0.00"; CadenceBar.Value = 0; ConfidenceValue.Text = "0"; ConfidenceBar.Value = 0; TargetSpeedValue.Text = "0.00"; TargetSpeedBar.Value = 0; GaitStateValue.Text = "BEKLİYOR"; StepCountValue.Text = "0 adım"; }
    private bool ProfileUsesPhone() => _profile == MotionProfile.JoyConPhone || _profile == MotionProfile.FullFusion || _profile == MotionProfile.PhoneOnly || _profile == MotionProfile.BoardPhone;
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
}
