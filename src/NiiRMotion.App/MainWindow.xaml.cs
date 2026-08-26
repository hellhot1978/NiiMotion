using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private readonly DispatcherTimer _scanTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer _vrCommandTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly VrPanelStatePublisher _vrPanel = new();
    private readonly VrPanelCommandChannel _vrPanelCommands = new();
    private readonly VrOverlayProcessManager _vrOverlay = new();
    private bool _autoScanBusy;
    private bool _vrCommandBusy;
    private bool _phonePairing;
    private bool _moveIdentifyBusy;
    private bool _suppressAutomaticLocomotion;
    private OwoTrackSensorSource? _phoneMonitor;
    private SessionReadiness? _readiness;
    private MotionProfile _profile = MotionProfile.AlyxFullFusion;
    private UserHardwareInventory _inventory = UserHardwareInventory.Empty;
    private IReadOnlyList<ProfileRecommendation> _profileRecommendations = Array.Empty<ProfileRecommendation>();
    private string _selectedGameId = new GameSelectionStore().Load();
    private bool _gameNiiMotionEnabled = true;
    private bool _launchNormalVrOverride;
    private string? _pendingGameAppId;
    private TextBlock? _gameLaunchStatus;
    private readonly GameLaunchJournalStore _gameLaunchJournal = new();
    private IGameTelemetrySession? _gameTelemetry;
    private InstalledGame? _pendingGame;
    private double _demoPhase;
    private long _demoSteps;
    public MainWindow()
    {
        InitializeComponent();
        var userExperience = new UserExperienceStore().Load(); ApplyUserExperience(userExperience);
        var profileBorder = (Border)ProfilePopup.Child;
        profileBorder.Child = new ScrollViewer { Content = profileBorder.Child, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        DevicesList.PreviewMouseLeftButtonUp += DeviceCardClick;
        DevicesList.Cursor = System.Windows.Input.Cursors.Hand;
        TelemetryMode.TextAlignment = TextAlignment.Right;
        _locomotion.CriticalSensorLost += LocomotionCriticalSensorLost;
        _demoTimer.Tick += DemoTick;
        _scanTimer.Tick += AutoScanTick;
        _scanTimer.Tick += (_, _) => _vrOverlay.EnsureRunning();
        _vrCommandTimer.Tick += async (_, _) =>
        {
            if (_vrCommandBusy) return;
            var command = _vrPanelCommands.Receive();
            if (command == VrPanelCommand.None) return;
            _vrCommandBusy = true;
            try
            {
                if (command == VrPanelCommand.EmergencyStop) StopClick(this, new RoutedEventArgs());
                else if (command == VrPanelCommand.Rescan) await ScanAsync();
                else if (command == VrPanelCommand.StartLocomotion) { _suppressAutomaticLocomotion = false; await StartLocomotionAsync(); }
                else if (command == VrPanelCommand.ShowDesktop) { if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Show(); Activate(); Topmost = true; Topmost = false; }
            }
            finally { _vrCommandBusy = false; }
        };
        Loaded += async (_, _) =>
        {
            var arguments = Environment.GetCommandLineArgs();
            await EnsureHardwareInventoryAsync();
            if (!userExperience.OnboardingComplete && !arguments.Any(x => x.StartsWith("--screenshot=", StringComparison.OrdinalIgnoreCase)))
            {
                var onboarding = new GettingStartedWindow(_inventory, await new UserSetupStore().LoadCalibrationAsync()) { Owner = this };
                onboarding.ShowDialog(); ApplyUserExperience(new UserExperienceStore().Load());
            }
            HandTrackingToggle.IsChecked = _inventory.UsesHandTracking;
            RebuildProfileMenu();
            ShowPage(OverviewPage, "Genel Bakış", "Sistem durumu ve hızlı başlangıç", OverviewNav);
            RefreshSystemMode();
            if (arguments.Contains("--normal", StringComparer.OrdinalIgnoreCase))
            {
                SelectProfile(MotionProfile.ClassicVr); await SwitchSystemModeAsync(SystemMode.Original);
            }
            else
            {
                var requestedProfileId = arguments.FirstOrDefault(x => x.StartsWith("--profile=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1];
                var storedProfileId = new ActiveMotionProfileStore().Load();
                var requestedProfile = _profileRecommendations.FirstOrDefault(x => string.Equals(x.Profile.Id, requestedProfileId ?? storedProfileId, StringComparison.OrdinalIgnoreCase))?.Profile;
                var recommended = requestedProfile ?? _profileRecommendations.FirstOrDefault(x => x.Profile.LocomotionAllowed && !x.Experimental)?.Profile ?? MotionProfile.ClassicVr;
                SelectProfile(_systemMode.CurrentMode == SystemMode.Original ? MotionProfile.ClassicVr : recommended); await ScanAsync();
                if (arguments.Contains("--autostart", StringComparer.OrdinalIgnoreCase)) await StartLocomotionAsync();
            }
            if (arguments.Contains("--calibration-page", StringComparer.OrdinalIgnoreCase)) ToolsNavClick(this, new RoutedEventArgs());
            if (arguments.Contains("--games-page", StringComparer.OrdinalIgnoreCase)) GamesNavClick(this, new RoutedEventArgs());
            if (arguments.Contains("--game-wizard", StringComparer.OrdinalIgnoreCase)) { GamesNavClick(this, new RoutedEventArgs()); OpenGameAdapterWizard(); }
            if (arguments.Contains("--game-tuning", StringComparer.OrdinalIgnoreCase)) { GamesNavClick(this, new RoutedEventArgs()); var selectedGame = new SteamGameCatalog().Detect().FirstOrDefault(x => x.IsInstalled && x.State == GameIntegrationState.Ready && x.Definition.Id == _selectedGameId); if (selectedGame is not null) OpenGameTuningWindow(selectedGame); }
            _vrOverlay.EnsureRunning(); _scanTimer.Start();
        };
        _vrCommandTimer.Start();
        Closed += async (_, _) => { _demoTimer.Stop(); _scanTimer.Stop(); _vrOverlay.Dispose(); _vrPanel.Dispose(); _vrPanelCommands.Dispose(); if (_gameTelemetry is not null) await _gameTelemetry.DisposeAsync(); await StopPhoneMonitorAsync(); await _locomotion.DisposeAsync(); };
    }
    private async void AutoScanTick(object? sender, EventArgs e)
    {
        if (_autoScanBusy) return;
        _autoScanBusy = true;
        try { await ScanAsync(); await EnsureAutomaticLocomotionAsync(); }
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
            var enabled = _inventory.UsesHandTracking;
            devices[handsIndex] = new DeviceStatus(DeviceKind.HandTracking, "VR El Kontrolü",
                enabled ? DeviceState.Configured : DeviceState.Missing,
                enabled ? "Kullanıma açık; gerçek el izleme durumu Quest ve Virtual Desktop tarafından yönetilir." : "Bu kontrol yöntemi kullanılmıyor.",
                enabled ? "Quest'te el takibini, Virtual Desktop'ta elden kontrolcü emülasyonunu açık tut." : "Kullanmak için Cihazlarım ekranından etkinleştir.");
        }
        RefreshHandTrackingVisual();
        RefreshPhoneVisual(devices);
        RefreshCalibrationStatus(devices);
        await RefreshPsMoveStatusAsync();
        if (!_moveIdentifyBusy)
        {
            var moveAssignments = await new PsMoveAssignmentStore(NiiMotionPaths.PsMoveAssignments).LoadAsync();
            if (moveAssignments is { IsComplete: true })
                await new PsMoveDiagnosticsService().KeepAssignedControllersAwakeAsync(moveAssignments);
        }
        var visibleKinds = _profile.Required.Concat(_profile.Optional)
            .Where(x => x != DeviceKind.SteamVr).ToHashSet();
        DevicesList.ItemsSource = devices.Where(x => visibleKinds.Contains(x.Kind)).ToList();
        PublishVrPanel("Hazırlanıyor", devices);
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
    private async void GuideNavClick(object sender, RoutedEventArgs e) => new GettingStartedWindow(_inventory, await new UserSetupStore().LoadCalibrationAsync()) { Owner = this }.ShowDialog();
    private void ApplyUserExperience(UserExperiencePreferences preferences)
    {
        FontSize = 13 * preferences.TextScale;
        if (preferences.HighContrast)
        {
            Resources["Panel"] = Brush("#020406"); Resources["Card"] = Brush("#080E13"); Resources["Line"] = Brush("#6B8292"); Resources["Muted"] = Brush("#D1DCE3"); Background = Brush("#000000");
        }
        else
        {
            Resources["Panel"] = Brush("#0B1118"); Resources["Card"] = Brush("#0E151D"); Resources["Line"] = Brush("#202D38"); Resources["Muted"] = Brush("#94A1AD"); Background = Brush("#060A0F");
        }
        _demoTimer.Interval = preferences.ReducedMotion ? TimeSpan.FromMilliseconds(160) : TimeSpan.FromMilliseconds(80);
    }
    private void ProfileMenuClick(object sender, RoutedEventArgs e)
    {
        ProfilePopup.IsOpen = !ProfilePopup.IsOpen;
    }
    private void ModesNavClick(object sender, RoutedEventArgs e) => ShowPage(ModesPage, "Oyun Modları", "Nasıl hareket etmek istediğini seç", ModesNav);
    private async void DevicesNavClick(object sender, RoutedEventArgs e)
    {
        var setup = new HardwareSetupWindow(_inventory) { Owner = this };
        if (setup.ShowDialog() != true) return;
        _inventory = setup.Inventory;
        await new UserSetupStore().SaveInventoryAsync(_inventory);
        HandTrackingToggle.IsChecked = _inventory.UsesHandTracking;
        RebuildProfileMenu();
        if (!_profileRecommendations.Any(x => x.Profile.Id == _profile.Id)) SelectProfile(MotionProfile.ClassicVr);
        await ScanAsync();
    }

    private async Task EnsureHardwareInventoryAsync()
    {
        var store = new UserSetupStore();
        var loaded = await store.LoadInventoryAsync();
        if (loaded is not null) { _inventory = loaded; return; }
        var setup = new HardwareSetupWindow { Owner = this };
        if (setup.ShowDialog() == true) _inventory = setup.Inventory;
        await store.SaveInventoryAsync(_inventory);
    }

    private void RebuildProfileMenu()
    {
        _profileRecommendations = MotionProfileCatalog.For(_inventory);
        var panel = (StackPanel)((ScrollViewer)((Border)ProfilePopup.Child).Child).Content;
        panel.Children.Clear();
        panel.Children.Add(new TextBlock { Text = "SANA UYGUN PROFİLLER", Foreground = Brush("#6F8493"), FontSize = 9, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 5, 8, 7) });
        foreach (var recommendation in _profileRecommendations)
        {
            var button = new Button { Style = (Style)FindResource("ProfileOption"), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = recommendation.Profile.Name, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = recommendation.Summary, Foreground = Brush("#8EA0AD"), FontSize = 9, Margin = new Thickness(0, 2, 0, 0) });
            grid.Children.Add(text);
            var score = new TextBlock { Text = recommendation.Experimental ? "DENEYSEL" : $"{recommendation.PerformanceScore}/100", Foreground = Brush(recommendation.Experimental ? "#E6B85A" : "#55DDB8"), FontSize = 9, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(score, 1); grid.Children.Add(score); button.Content = grid;
            var profile = recommendation.Profile; button.Click += async (_, _) => { SelectProfile(profile); await ScanAsync(); };
            panel.Children.Add(button);
        }
    }
    private void ToolsNavClick(object sender, RoutedEventArgs e)
    {
        RefreshCalibrationProgress();
        _ = BuildCalibrationCenterAsync();
        ShowPage(ToolsPage, "Test ve Kalibrasyon", "Kişisel ölçüm, doğrulama ve veri kaydı", ToolsNav);
    }
    private void GamesNavClick(object sender, RoutedEventArgs e)
    {
        _gameNiiMotionEnabled = _profile.LocomotionAllowed;
        BuildGamesPage();
        ShowPage(GamesPage, "Oyunlar", "Kişisel hareketini oyunlara güvenle uygula", GamesNav);
    }

    private void BuildGamesPage()
    {
        GamesPanel.Children.Clear();
        GamesPanel.Children.Add(SectionHeader("OYUN KÜTÜPHANESİ", "Oyunu seç ve güvenle başlat", "Yalnız doğrulanmış VR oyunları görünür. Kişisel yürüyüş modelin değişmez; oyun eşlemesi ayrı tutulur."));
        var available = new SteamGameCatalog().Detect().Where(x => x.IsInstalled && x.State == GameIntegrationState.Ready).ToArray();
        var selected = available.FirstOrDefault(x => x.Definition.Id == _selectedGameId) ?? available.FirstOrDefault();
        if (selected is null) { GamesPanel.Children.Add(new Border { Child = Label("Henüz doğrulanmış ve kurulu bir VR oyunu bulunamadı. VR Oyunu Ekle ile yeni bir eşleme oluşturabilirsin.", "#F1C566", 12, FontWeights.SemiBold), Padding = new Thickness(18), Background = Brush("#151B1E"), CornerRadius = new CornerRadius(8) }); return; }
        _selectedGameId = selected.Definition.Id; new GameSelectionStore().Save(_selectedGameId);

        var selector = new Grid { Margin = new Thickness(0, 0, 0, 14) }; selector.ColumnDefinitions.Add(new ColumnDefinition()); selector.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); selector.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); selector.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) }); selector.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var picker = new ComboBox { ItemsSource = available, SelectedItem = selected, Height = 38, FontSize = 12, Padding = new Thickness(10, 6, 10, 6), DisplayMemberPath = "Definition.Name" };
        picker.SelectionChanged += (_, _) => { if (picker.SelectedItem is InstalledGame choice && choice.Definition.Id != _selectedGameId) { _selectedGameId = choice.Definition.Id; new GameSelectionStore().Save(_selectedGameId); BuildGamesPage(); } }; selector.Children.Add(picker);
        var add = new Button { Content = "+ VR OYUNU EKLE", Padding = new Thickness(21, 10, 21, 10), MinWidth = 150 }; add.Click += (_, _) => OpenGameAdapterWizard(); Grid.SetColumn(add, 2); selector.Children.Add(add); GamesPanel.Children.Add(selector);
        var adapterStore = new GameAdapterStore(); var userAdapters = adapterStore.Load(); var selectedAdapter = userAdapters.FirstOrDefault(x => x.Id == selected.Definition.Id);
        var manage = new Button { Content = "EŞLEMELER  ▾", FontSize = 11, MinWidth = 116, Padding = new Thickness(15, 9, 15, 9), ToolTip = "Kullanıcı eşlemelerini kaldır veya özgün profili geri yükle", IsEnabled = userAdapters.Count > 0 || adapterStore.HasOriginalProfileBackup }; Grid.SetColumn(manage, 4); selector.Children.Add(manage);
        var menu = new ContextMenu { Background = Brush("#0D151D"), Foreground = Brush("#F4F7FA") };
        var remove = new MenuItem { Header = "Seçili kullanıcı eşlemesini kaldır", IsEnabled = selectedAdapter is not null }; remove.Click += async (_, _) => { if (selectedAdapter is not null) await RemoveGameAdapterAsync(selectedAdapter); }; menu.Items.Add(remove);
        var restore = new MenuItem { Header = "Özgün sürücü profilini geri yükle", IsEnabled = adapterStore.HasOriginalProfileBackup }; restore.Click += async (_, _) => await RestoreOriginalGameProfileAsync(); menu.Items.Add(restore);
        manage.Click += (_, _) => { menu.PlacementTarget = manage; menu.IsOpen = true; };

        var hero = new Border { Height = 300, Background = Brush("#0D151D"), BorderBrush = Brush("#273945"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), ClipToBounds = true, Margin = new Thickness(0, 0, 0, 14) };
        var heroGrid = new Grid(); heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) }); heroGrid.ColumnDefinitions.Add(new ColumnDefinition());
        var cover = new Border { Background = Brush("#070C11"), BorderBrush = Brush("#29404D"), BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(8) }; var coverImage = new Image { Stretch = Stretch.Uniform }; cover.Child = coverImage; heroGrid.Children.Add(cover);
        var content = new StackPanel { Margin = new Thickness(28, 25, 28, 24) }; Grid.SetColumn(content, 1);
        content.Children.Add(Label("SEÇİLİ VR OYUNU", "#4ABCF4", 9, FontWeights.Bold)); content.Children.Add(Label(selected.Definition.Name, "#F5F8FA", 25, FontWeights.SemiBold, new Thickness(0, 7, 0, 3))); content.Children.Add(Label(selected.Definition.Runtime, "#55DDB8", 10, FontWeights.SemiBold));
        var gameSummary = Label(selected.Definition.Summary, "#A5B4BE", 11, FontWeights.Normal, new Thickness(0, 12, 0, 16)); gameSummary.MaxHeight = 48; content.Children.Add(gameSummary); _ = LoadGameCoverAsync(selected, coverImage, gameSummary);
        var profileLine = new Border { Background = Brush("#101D26"), BorderBrush = Brush("#29404D"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(13, 10, 13, 10) }; profileLine.Child = Label($"YÜRÜYÜŞ PROFİLİ  ·  {_profile.Name}", _profile.LocomotionAllowed ? "#55DDB8" : "#F1C566", 10, FontWeights.Bold); content.Children.Add(profileLine);
        var controls = new Grid { Margin = new Thickness(0, 14, 0, 0) }; controls.ColumnDefinitions.Add(new ColumnDefinition()); controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) }); controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); controls.ColumnDefinitions.Add(new ColumnDefinition());
        var toggle = new CheckBox { Content = "NIIMOTION YÜRÜYÜŞÜ", IsChecked = _gameNiiMotionEnabled, Foreground = Brush(_gameNiiMotionEnabled ? "#55DDB8" : "#A5B4BE"), VerticalContentAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Padding = new Thickness(12), BorderBrush = Brush("#38505E"), BorderThickness = new Thickness(1) }; toggle.Checked += (_, _) => { _gameNiiMotionEnabled = true; toggle.Foreground = Brush("#55DDB8"); }; toggle.Unchecked += (_, _) => { _gameNiiMotionEnabled = false; toggle.Foreground = Brush("#A5B4BE"); }; controls.Children.Add(toggle);
        var tune = new Button { Content = "⚙  AYARLAR", Padding = new Thickness(10, 12, 10, 12) }; tune.Click += (_, _) => OpenGameTuningWindow(selected); Grid.SetColumn(tune, 2); controls.Children.Add(tune);
        var launch = new Button { Content = "▶  DOĞRULA VE OYUNU BAŞLAT", Padding = new Thickness(20, 12, 20, 12) }; launch.Click += async (_, _) => await ValidateAndLaunchGameAsync(selected, launch); Grid.SetColumn(launch, 4); controls.Children.Add(launch); content.Children.Add(controls);
        heroGrid.Children.Add(content); hero.Child = heroGrid; GamesPanel.Children.Add(hero);
        var previousLaunch = _gameLaunchJournal.Load();
        if (previousLaunch?.Stage == GameLaunchStage.Running && !IsGameRunning(selected.InstallPath ?? "")) { _gameLaunchJournal.Complete("Önceki oyun oturumu kapandı."); previousLaunch = _gameLaunchJournal.Load(); }
        var launchText = previousLaunch is not null && previousLaunch.GameId == selected.Definition.Id && previousLaunch.Stage != GameLaunchStage.Idle
            ? $"{LaunchStageLabel(previousLaunch.Stage)}  ·  {previousLaunch.Message}"
            : "Başlatma sırası: profil ve kalibrasyon → hareket cihazları → Quest / Virtual Desktop → SteamVR → oyun. Bir adım doğrulanmazsa oyun açılmaz.";
        var launchColor = previousLaunch?.Stage == GameLaunchStage.Failed ? "#FF8AA5" : previousLaunch?.Stage == GameLaunchStage.Running ? "#55DDB8" : "#93A7B3";
        var note = new Border { Background = Brush("#09121A"), BorderBrush = Brush("#1F303C"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(14) }; _gameLaunchStatus = Label(launchText, launchColor, 10, FontWeights.Normal); note.Child = _gameLaunchStatus; GamesPanel.Children.Add(note);
    }

    private static string LaunchStageLabel(GameLaunchStage stage) => stage switch
    {
        GameLaunchStage.ValidatingProfile => "1/8 PROFİL",
        GameLaunchStage.ValidatingCalibration => "2/8 KALİBRASYON",
        GameLaunchStage.ValidatingSensors => "3/8 SENSÖRLER",
        GameLaunchStage.WaitingForVirtualDesktop => "4/8 QUEST / VD",
        GameLaunchStage.ApplyingVrMode => "5/8 VR MODU",
        GameLaunchStage.StartingSteamVr or GameLaunchStage.WaitingForMotionBridge => "6/8 STEAMVR",
        GameLaunchStage.StartingLocomotion => "7/8 HAREKET",
        GameLaunchStage.StartingGame => "8/8 OYUN",
        GameLaunchStage.Running => "✓ OTURUM HAZIR",
        GameLaunchStage.Failed => "! BAŞLATMA DURDU",
        _ => "HAZIR"
    };

    private void OpenGameAdapterWizard()
    {
        var candidates = new SteamGameCatalog().DetectAdapterCandidates();
        var window = new Window { Title = "NiiMotion · Oyun Ekleme Sihirbazı", Owner = this, Width = 720, Height = 660, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = Brush("#070D12"), Foreground = Brushes.White };
        var root = new Grid { Margin = new Thickness(28) }; root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var body = new StackPanel();
        body.Children.Add(Label("Oyun Ekleme Sihirbazı", "#F5F8FA", 25, FontWeights.SemiBold));
        body.Children.Add(Label("1  Oyunu seç   →   2  Girdileri tara   →   3  Eşlemeyi doğrula", "#55DDB8", 11, FontWeights.SemiBold, new Thickness(0, 5, 0, 18)));
        body.Children.Add(Label("KURULU STEAM OYUNU", "#4ABCF4", 9, FontWeights.Bold));
        var game = new ComboBox { ItemsSource = candidates, SelectedIndex = candidates.Count > 0 ? 0 : -1, Height = 38, Margin = new Thickness(0, 6, 0, 15) }; body.Children.Add(game);
        var scan = new Button { Content = "OYUN GİRDİLERİNİ TARA", Height = 40, Margin = new Thickness(0, 0, 0, 17) }; body.Children.Add(scan);
        var movementTitle = Label("İLERİ HAREKET GİRDİSİ", "#4ABCF4", 9, FontWeights.Bold); body.Children.Add(movementTitle);
        var movement = new ComboBox { IsEditable = true, Height = 38, Margin = new Thickness(0, 6, 0, 4) }; body.Children.Add(movement);
        var discoveryStatus = Label("Tarama, oyunun yerel SteamVR action dosyalarını okur; hiçbir oyun dosyasını değiştirmez.", "#8FA1AD", 9, FontWeights.Normal, new Thickness(0, 0, 0, 14)); body.Children.Add(discoveryStatus);
        body.Children.Add(Label("KOŞMA DÜĞMESİ  ·  İSTEĞE BAĞLI", "#4ABCF4", 9, FontWeights.Bold));
        var activation = new ComboBox { IsEditable = true, Height = 38, Margin = new Thickness(0, 6, 0, 3), ToolTip = "Bulunursa otomatik seçilir; istemiyorsan boş bırak." }; body.Children.Add(activation);
        body.Children.Add(Label("Boş bırakabilirsin. Oyun ayrı bir koşma girdisi kullanıyorsa tarama bunu otomatik önerecek.", "#8FA1AD", 9, FontWeights.Normal, new Thickness(0, 0, 0, 12)));
        body.Children.Add(Label("OYUN HIZ ÇARPANI", "#4ABCF4", 9, FontWeights.Bold));
        var speedLine = new Grid { Margin = new Thickness(0, 6, 0, 14) }; speedLine.ColumnDefinitions.Add(new ColumnDefinition()); speedLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        var speed = new Slider { Minimum = .25, Maximum = 3, Value = 1, TickFrequency = .05, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        var speedValue = Label("1,00×", "#55DDB8", 13, FontWeights.Bold); speed.ValueChanged += (_, _) => speedValue.Text = $"{speed.Value:0.00}×"; speedLine.Children.Add(speed); Grid.SetColumn(speedValue, 1); speedLine.Children.Add(speedValue); body.Children.Add(speedLine);
        var vrConfirmed = new CheckBox { Content = "Bu oyun VR olarak çalışıyor (yerel SteamVR/OpenXR veya çalışan bir VR modu)", Foreground = Brush("#E8F0F5"), Margin = new Thickness(0, 0, 0, 12), FontWeight = FontWeights.SemiBold }; body.Children.Add(vrConfirmed);
        var safety = new Border { Background = Brush("#0D1820"), BorderBrush = Brush("#29404D"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(13) };
        safety.Child = Label("GÜVENLİ KURULUM · Oyun dosyaları değiştirilmez. İlk oyunda yaklaşık 10 adım yürüdükten sonra yalnızca yavaş, doğru veya hızlı seçerek bu oyunun hızını tamamlayabilirsin.", "#A9BBC5", 10, FontWeights.Normal); body.Children.Add(safety); root.Children.Add(body);
        var footer = new Grid { Margin = new Thickness(0, 20, 0, 0) }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var cancel = new Button { Content = "VAZGEÇ", Padding = new Thickness(20, 10, 20, 10) }; cancel.Click += (_, _) => window.Close(); Grid.SetColumn(cancel, 1); footer.Children.Add(cancel);
        var save = new Button { Content = "EŞLEMEYİ OLUŞTUR", IsEnabled = false, Padding = new Thickness(22, 10, 22, 10) }; Grid.SetColumn(save, 3); footer.Children.Add(save); Grid.SetRow(footer, 1); root.Children.Add(footer);
        var openXrMode = false;
        game.SelectionChanged += (_, _) => { openXrMode = false; movementTitle.Text = "İLERİ HAREKET GİRDİSİ"; movement.ItemsSource = null; movement.Text = ""; activation.ItemsSource = null; activation.Text = ""; activation.IsEnabled = true; save.IsEnabled = false; discoveryStatus.Text = "Önce oyun girdilerini tara."; discoveryStatus.Foreground = Brush("#8FA1AD"); };
        scan.Click += (_, _) =>
        {
            if (game.SelectedItem is not SteamAppCandidate selected) return;
            var inspection = new SteamActionDiscovery().Inspect(selected.InstallPath); var actions = inspection.Actions; openXrMode = inspection.Runtime == VrInputRuntime.OpenXr;
            movementTitle.Text = openXrMode ? "OYUN ÇALIŞTIRMA DOSYASI" : "İLERİ HAREKET GİRDİSİ";
            movement.ItemsSource = openXrMode ? OpenXrGameAdapterStore.FindCandidateExecutables(selected.InstallPath) : actions; movement.SelectedIndex = movement.Items.Count > 0 ? 0 : -1;
            var runActions = actions.Where(x => new[] { "run", "sprint", "walk", "press", "click" }.Any(term => x.Path.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray(); activation.ItemsSource = runActions; activation.SelectedIndex = runActions.Length > 0 ? 0 : -1;
            activation.IsEnabled = !openXrMode;
            discoveryStatus.Text = openXrMode ? $"OpenXR algılandı · {OpenXrGameAdapterStore.DetectEngine(selected.InstallPath)}. Oyun dosyaları değiştirilmeden yalnız seçilen çalıştırma dosyasına güvenli hareket katmanı uygulanır." : inspection.Message; discoveryStatus.Foreground = Brush(actions.Count > 0 || openXrMode ? "#55DDB8" : "#FF8AA5");
            save.IsEnabled = openXrMode ? movement.Items.Count > 0 : true;
        };
        save.Click += async (_, _) =>
        {
            if (game.SelectedItem is not SteamAppCandidate selected) { MessageBox.Show(window, "Önce kurulu bir Steam oyunu seç.", "NiiMotion", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            if (vrConfirmed.IsChecked != true) { MessageBox.Show(window, "VR olmayan oyunlar otomatik eklenmez. Oyun VR destekliyorsa veya çalışan bir VR modu kuruluysa kutuyu işaretle.", "VR doğrulaması gerekli", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var path = movement.Text.Trim();
            if (openXrMode)
            {
                var openXrAdapter = new OpenXrGameAdapter($"user-openxr-{selected.AppId}", selected.Name, selected.AppId, [Path.GetFileName(path)], speed.Value, DateTimeOffset.Now);
                var openXrErrors = OpenXrGameAdapterValidator.Validate(openXrAdapter); if (openXrErrors.Count > 0) { MessageBox.Show(window, string.Join(Environment.NewLine, openXrErrors), "OpenXR adaptörü doğrulanamadı", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                try { new OpenXrGameAdapterStore().Save(openXrAdapter, selected.InstallPath); await new GameMetadataService().GetAsync(openXrAdapter.SteamAppId, openXrAdapter.Name); _selectedGameId = openXrAdapter.Id; new GameSelectionStore().Save(openXrAdapter.Id); window.DialogResult = true; window.Close(); BuildGamesPage(); }
                catch (Exception ex) { MessageBox.Show(window, ex.Message, "OpenXR adaptörü oluşturulamadı", MessageBoxButton.OK, MessageBoxImage.Error); }
                return;
            }
            var marker = path.IndexOf("/in/", StringComparison.OrdinalIgnoreCase); var actionSet = marker > 0 ? path[..marker] : "";
            var adapter = new UserGameAdapter($"user-steam-{selected.AppId}", selected.Name, selected.AppId, actionSet, path, string.IsNullOrWhiteSpace(activation.Text) ? null : activation.Text.Trim(), speed.Value, DateTimeOffset.Now);
            var errors = GameAdapterValidator.Validate(adapter); if (errors.Count > 0) { MessageBox.Show(window, string.Join(Environment.NewLine, errors), "Eşleme doğrulanamadı", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            try { new GameAdapterStore().SaveAndInstall(adapter); await new GameMetadataService().GetAsync(adapter.SteamAppId, adapter.Name); _selectedGameId = adapter.Id; new GameSelectionStore().Save(adapter.Id); window.DialogResult = true; window.Close(); BuildGamesPage(); }
            catch (Exception ex) { MessageBox.Show(window, ex.Message, "Eşleme oluşturulamadı", MessageBoxButton.OK, MessageBoxImage.Error); }
        };
        window.Content = root; window.ShowDialog();
    }

    private async Task LoadGameCoverAsync(InstalledGame game, Image target, TextBlock summary)
    {
        if (game.Definition.SteamAppId is null) return;
        try
        {
            var result = await new GameMetadataService().GetAsync(game.Definition.SteamAppId, game.Definition.Name);
            if (result.CoverPath is null || !File.Exists(result.CoverPath)) return;
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(result.CoverPath); bitmap.EndInit(); bitmap.Freeze(); target.Source = bitmap;
            target.ToolTip = $"Görsel ve oyun bilgisi: {result.Metadata.Source}";
            if (!string.IsNullOrWhiteSpace(result.Metadata.Summary)) summary.Text = result.Metadata.Summary;
        }
        catch { }
    }

    private void OpenGameTuningWindow(InstalledGame game)
    {
        var store = new GameMotionProfileStore(); var profile = store.Load(game.Definition.Id);
        var window = new Window { Title = $"NiiMotion · {game.Definition.Name} Hareket Ayarları", Owner = this, Width = 680, Height = 790, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = Brush("#070D12"), Foreground = Brush("#F4F7FA") };
        var root = new Grid { Margin = new Thickness(28) }; root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); var body = new StackPanel(); root.Children.Add(body);
        body.Children.Add(Label("Oyun Hareket Ayarları", "#F5F8FA", 24, FontWeights.SemiBold)); body.Children.Add(Label(game.Definition.Name, "#4ABCF4", 11, FontWeights.SemiBold, new Thickness(0, 4, 0, 5)));
        body.Children.Add(Label("Bu ayarlar yalnız oyuna gönderilen analog hareketi değiştirir. Kişisel yürüyüş kayıtların ve kalibrasyonun değiştirilmez.", "#9FB0BA", 10, FontWeights.Normal, new Thickness(0, 0, 0, 18)));

        var speed = AddTuningSlider(body, "GENEL HIZ", "Oyundaki bütün yürüyüş hızlarını birlikte değiştirir.", .25, 3, profile.SpeedMultiplier, .05, "0.00×");
        var maximum = AddTuningSlider(body, "AZAMİ HIZ", "En hızlı fiziksel yürüyüşte oyuna gönderilecek üst sınır.", .2, 1, profile.MaximumOutput, .05, "0%");
        var deadzone = AddTuningSlider(body, "KÜÇÜK HAREKET FİLTRESİ", "Çok küçük hareketlerin yanlış yürüyüşe dönüşmesini engeller.", 0, .2, profile.Deadzone, .01, "0%");
        var acceleration = AddTuningSlider(body, "BAŞLAMA TEPKİSİ", "Yürümeye başlayınca oyun hızının ne kadar çabuk yükseldiği.", .5, 12, profile.AccelerationPerSecond, .5, "0.0");
        var deceleration = AddTuningSlider(body, "DURMA TEPKİSİ", "Durduğunda analog hareketin ne kadar hızlı sıfırlandığı.", 2, 30, profile.DecelerationPerSecond, 1, "0");
        var motionProfileId = new ActiveMotionProfileStore().Load() ?? "joycon-only";
        var optimizationStore = new GameSensorOptimizationStore();
        var optimization = optimizationStore.Load(game.Definition.Id, motionProfileId);
        var telemetryCapability = GameTelemetryProviderFactory.Create(game.Definition.Id, game.Definition.SteamAppId).Capability;
        var optimizationCard = new Border { Background = Brush("#0A1B24"), BorderBrush = Brush("#285165"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(13), Margin = new Thickness(0, 4, 0, 8) };
        var optimizationBody = new StackPanel();
        optimizationBody.Children.Add(Label("OYUN İÇİ HIZ UYUMU", "#55DDB8", 9, FontWeights.Bold));
        optimizationBody.Children.Add(Label($"Aktif yürüyüş profili: {_profile.Name}", "#DCEAF1", 10, FontWeights.SemiBold, new Thickness(0, 3, 0, 2)));
        optimizationBody.Children.Add(Label("Oyundaki hareket mesafesi farklı geliyorsa kısa bir yürüyüşten sonra aşağıdan düzelt.", "#91A7B4", 9, FontWeights.Normal));
        if (telemetryCapability.Mode == GameTelemetryMode.Guided)
        {
            optimizationBody.Children.Add(Label("Yaklaşık 10 doğal adım yürü. Oyundaki mesafe nasıl hissettirdi?", "#DCEAF1", 9, FontWeights.SemiBold, new Thickness(0, 9, 0, 5)));
            var feedback = new UniformGrid { Columns = 3 };
            Button FeedbackButton(string text, GamePaceFeedback answer)
            {
                var button = new Button { Content = text, Foreground = Brush("#F4F7FA"), Background = Brush("#163044"), BorderBrush = Brush("#39718B"), BorderThickness = new Thickness(1), Padding = new Thickness(9, 7, 9, 7), Margin = new Thickness(0, 0, 6, 0), FontWeight = FontWeights.SemiBold };
                button.Click += (_, _) => { optimizationStore.ApplyFeedback(game.Definition.Id, motionProfileId, answer); window.DialogResult = true; window.Close(); OpenGameTuningWindow(game); };
                return button;
            }
            feedback.Children.Add(FeedbackButton("DAHA HIZLI OLMALI", GamePaceFeedback.TooSlow));
            feedback.Children.Add(FeedbackButton("HIZ DOĞRU", GamePaceFeedback.Correct));
            feedback.Children.Add(FeedbackButton("DAHA YAVAŞ OLMALI", GamePaceFeedback.TooFast));
            optimizationBody.Children.Add(feedback);
        }
        var optimizationActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        Button OptimizationButton(string text) => new() { Content = text, Foreground = Brush("#F4F7FA"), Background = Brush("#172A37"), BorderBrush = Brush("#315066"), BorderThickness = new Thickness(1), Padding = new Thickness(12, 7, 12, 7), FontWeight = FontWeights.SemiBold };
        var undoOptimization = OptimizationButton("SON HIZ AYARINI GERİ AL"); undoOptimization.IsEnabled = optimization.UpdatedAt != DateTimeOffset.MinValue;
        undoOptimization.Visibility = undoOptimization.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        undoOptimization.Click += (_, _) => { optimizationStore.RestorePrevious(game.Definition.Id, motionProfileId); window.DialogResult = true; window.Close(); OpenGameTuningWindow(game); };
        var resetOptimization = OptimizationButton("ÖĞRENİLEN HIZI SIFIRLA"); resetOptimization.Margin = new Thickness(8, 0, 0, 0);
        resetOptimization.Click += (_, _) => { optimizationStore.Reset(game.Definition.Id, motionProfileId); window.DialogResult = true; window.Close(); OpenGameTuningWindow(game); };
        optimizationActions.Children.Add(undoOptimization); optimizationActions.Children.Add(resetOptimization); optimizationBody.Children.Add(optimizationActions); optimizationCard.Child = optimizationBody; body.Children.Add(optimizationCard);

        var footer = new Grid { Margin = new Thickness(0, 18, 0, 0) }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); Grid.SetRow(footer, 1); root.Children.Add(footer);
        Button ActionButton(string text, string background) => new() { Content = text, Foreground = Brush("#F4F7FA"), Background = Brush(background), BorderBrush = Brush("#315066"), BorderThickness = new Thickness(1), Padding = new Thickness(18, 10, 18, 10), FontWeight = FontWeights.SemiBold };
        var reset = ActionButton("TÜM AYARLARI SIFIRLA", "#173044"); reset.BorderBrush = Brush("#37627A"); reset.Click += (_, _) => { if (MessageBox.Show(window, "Bu oyunun kişisel hareket ayarları kaldırılıp güvenli varsayılanlara dönülsün mü?", "Ayarları sıfırla", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) { store.Reset(game.Definition.Id); optimizationStore.Reset(game.Definition.Id, motionProfileId); window.DialogResult = true; window.Close(); BuildGamesPage(); } }; footer.Children.Add(reset);
        var cancel = ActionButton("VAZGEÇ", "#101923"); cancel.Click += (_, _) => window.Close(); Grid.SetColumn(cancel, 3); footer.Children.Add(cancel);
        var save = ActionButton("AYARLARI KAYDET", "#087DC4"); save.Click += (_, _) => { store.Save(profile with { SpeedMultiplier = speed.Value, MaximumOutput = maximum.Value, Deadzone = deadzone.Value, AccelerationPerSecond = acceleration.Value, DecelerationPerSecond = deceleration.Value }); window.DialogResult = true; window.Close(); BuildGamesPage(); }; Grid.SetColumn(save, 5); footer.Children.Add(save);
        window.Content = root; window.ShowDialog();
    }

    private static Slider AddTuningSlider(Panel parent, string title, string detail, double minimum, double maximum, double value, double tick, string format)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 9) }; var heading = new Grid(); heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(Label(title, "#4ABCF4", 9, FontWeights.Bold)); var valueText = Label(FormatTuningValue(value, format), "#55DDB8", 11, FontWeights.Bold); Grid.SetColumn(valueText, 1); heading.Children.Add(valueText); panel.Children.Add(heading);
        panel.Children.Add(Label(detail, "#8FA1AD", 9, FontWeights.Normal, new Thickness(0, 2, 0, 4))); var slider = new Slider { Minimum = minimum, Maximum = maximum, Value = value, TickFrequency = tick, SmallChange = tick, LargeChange = tick, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true, Height = 22 };
        slider.ValueChanged += (_, _) => valueText.Text = FormatTuningValue(slider.Value, format); panel.Children.Add(slider); parent.Children.Add(panel); return slider;
    }

    private static string FormatTuningValue(double value, string format) => format == "0%" ? $"{value * 100:0}%" : value.ToString(format, System.Globalization.CultureInfo.CurrentCulture);

    private async Task ValidateAndLaunchGameAsync(InstalledGame game, Button launchButton)
    {
        SetGameLaunchStage(game, GameLaunchStage.ValidatingProfile, "Profil doğrulanıyor…");
        var compatibility = new GameLaunchCompatibilityService().Validate(game, _gameNiiMotionEnabled);
        if (!compatibility.IsReady)
        {
            SetGameLaunchStage(game, GameLaunchStage.Failed, "Oyun uyumluluk kontrolü tamamlanamadı.");
            if (_gameLaunchStatus is not null) { _gameLaunchStatus.Text = compatibility.UserMessage; _gameLaunchStatus.Foreground = Brush("#FF8AA5"); }
            MessageBox.Show(this, compatibility.UserMessage, "Oyun başlatılmadı", MessageBoxButton.OK, MessageBoxImage.Warning); return;
        }
        if (game.Definition.SteamAppId is null) return;
        if (_gameNiiMotionEnabled && !_profile.LocomotionAllowed)
        {
            if (_gameLaunchStatus is not null) { _gameLaunchStatus.Text = "Önce Genel Bakış sayfasından bir NiiMotion yürüyüş profili seç."; _gameLaunchStatus.Foreground = Brush("#F1C566"); }
            MessageBox.Show(this, "NiiMotion yürüyüşü açık. Önce Genel Bakış sayfasından kullanacağın hareket profilini seç.", "Yürüyüş profili gerekli", MessageBoxButton.OK, MessageBoxImage.Information);
            ShowPage(OverviewPage, "Genel Bakış", "Önce hareket profilini seç", OverviewNav); return;
        }
        if (_gameNiiMotionEnabled)
        {
            SetGameLaunchStage(game, GameLaunchStage.ValidatingCalibration, "Kişisel kalibrasyon doğrulanıyor…");
            var uncalibrated = await UncalibratedProfileSensorsAsync();
            if (uncalibrated.Count > 0) { MessageBox.Show(this, $"Önce temel kalibrasyonu tamamla: {string.Join(", ", uncalibrated.Select(SensorDisplayName))}", "Kalibrasyon gerekli", MessageBoxButton.OK, MessageBoxImage.Warning); ToolsNavClick(this, new RoutedEventArgs()); return; }
            SetGameLaunchStage(game, GameLaunchStage.ValidatingSensors, "Gerekli sensörler canlı olarak kontrol ediliyor…");
            await ScanAsync(); var devices = (DevicesList.ItemsSource as IEnumerable<DeviceStatus>)?.ToArray() ?? []; var missing = PreflightBlockingDevices(devices);
            if (missing.Count > 0) { MessageBox.Show(this, $"Oyun açılmadı. Önce bağla: {string.Join(", ", missing.Select(x => x.Name))}", "Cihazlar eksik", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        }
        else await ScanAsync();
        var currentDevices = (DevicesList.ItemsSource as IEnumerable<DeviceStatus>)?.ToArray() ?? [];
        var vdReady = currentDevices.Any(x => x.Kind == DeviceKind.VirtualDesktop && x.IsConnected);
        if (!vdReady)
        {
            if (_gameLaunchStatus is not null) { _gameLaunchStatus.Text = "Oyun açılmadı: Quest'te Virtual Desktop'ı açıp bu bilgisayara bağlan."; _gameLaunchStatus.Foreground = Brush("#FF8AA5"); }
            MessageBox.Show(this, "Önce Quest'te Virtual Desktop'ı açıp bu bilgisayara bağlan. Bağlantı doğrulanmadan SteamVR veya oyun başlatılmayacak.", "Virtual Desktop bağlı değil", MessageBoxButton.OK, MessageBoxImage.Warning); return;
        }
        _launchNormalVrOverride = !_gameNiiMotionEnabled; _pendingGame = game;
        _selectedGameId = game.Definition.Id; new GameSelectionStore().Save(_selectedGameId); _pendingGameAppId = game.Definition.SteamAppId;
        if (_gameLaunchStatus is not null) { _gameLaunchStatus.Text = "Bağlantılar doğrulandı. SteamVR güvenli sırayla hazırlanıyor…"; _gameLaunchStatus.Foreground = Brush("#55DDB8"); }
        LaunchSteamVrClick(launchButton, new RoutedEventArgs());
    }

    private void SetGameLaunchStage(InstalledGame game, GameLaunchStage stage, string message)
    {
        if (_gameLaunchStatus is not null) { _gameLaunchStatus.Text = message; _gameLaunchStatus.Foreground = Brush(stage == GameLaunchStage.Failed ? "#FF8AA5" : stage == GameLaunchStage.Running ? "#55DDB8" : "#9DD9FA"); }
        _gameLaunchJournal.Save(new(1, game.Definition.Id, game.Definition.Name, _profile.Id, _gameNiiMotionEnabled, stage, message, DateTimeOffset.UtcNow));
        PublishVrPanel(stage == GameLaunchStage.Running ? "Hazır" : "Hazırlanıyor");
    }

    private async Task LaunchPendingGameAsync()
    {
        if (string.IsNullOrWhiteSpace(_pendingGameAppId)) return;
        var appId = _pendingGameAppId; _pendingGameAppId = null;
        var game = new SteamGameCatalog().Detect().FirstOrDefault(x => x.Definition.SteamAppId == appId && x.IsInstalled);
        if (game?.InstallPath is null) throw new InvalidOperationException("Seçili oyunun Steam kurulum klasörü doğrulanamadı.");
        if (IsGameRunning(game.InstallPath)) return;
        var steam = SteamInstallLocator.FindSteamExe() ?? throw new FileNotFoundException("Steam çalıştırıcısı bulunamadı.");
        var telemetryProvider = GameTelemetryProviderFactory.Create(game.Definition.Id, appId);
        var telemetryArgument = telemetryProvider.LaunchArguments;
        SetGameLaunchStage(game, GameLaunchStage.StartingGame, $"{game.Definition.Name} Steam üzerinden başlatılıyor…");
        Process.Start(new ProcessStartInfo(steam, $"-applaunch {appId} -vr{telemetryArgument}") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(steam)! });
        if (_gameLaunchStatus is not null) { _gameLaunchStatus.Text = $"{game.Definition.Name} başlatılıyor…"; _gameLaunchStatus.Foreground = Brush("#55DDB8"); }
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(75);
        while (DateTime.UtcNow < deadline)
        {
            if (IsGameRunning(game.InstallPath))
            {
                SetGameLaunchStage(game, GameLaunchStage.Running, $"✓ {game.Definition.Name} çalışıyor · NiiMotion oturumu hazır");
                if (_locomotion.IsRunning)
                {
                    if (_gameTelemetry is not null) await _gameTelemetry.DisposeAsync();
                    _gameTelemetry = telemetryProvider.CreateSession(_locomotion, _profile.Id);
                    if (_gameTelemetry is not null) _gameTelemetry.StatusChanged += (_, status) => Dispatcher.BeginInvoke(() =>
                    {
                        if (_gameLaunchStatus is not null) { _gameLaunchStatus.Text = status; _gameLaunchStatus.Foreground = Brush("#55DDB8"); }
                        PublishVrPanel("Adım eşleme");
                    });
                    _gameTelemetry?.Start();
                }
                _pendingGame = null;
                return;
            }
            await Task.Delay(500);
        }
        throw new TimeoutException($"Steam komutu gönderildi ancak {game.Definition.Name} 75 saniye içinde başlamadı.");
    }

    private static bool IsGameRunning(string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath)) return false;
        var root = Path.GetFullPath(installPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (path is not null && Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            finally { process.Dispose(); }
        }
        return false;
    }

    private async Task RemoveGameAdapterAsync(UserGameAdapter adapter)
    {
        if (MessageBox.Show(this, $"{adapter.Name} için oluşturduğun NiiMotion eşlemesi kaldırılacak. Oyun dosyaları değişmeyecek. Devam edilsin mi?", "Oyun eşlemesini kaldır", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await _systemMode.StopSteamVrAsync(); new GameAdapterStore().Remove(adapter.SteamAppId); _selectedGameId = "half-life-alyx"; BuildGamesPage();
    }

    private async Task RestoreOriginalGameProfileAsync()
    {
        if (MessageBox.Show(this, "İlk oyun eklenmeden önceki özgün NiiMotion sürücü profili geri yüklenecek ve bütün kullanıcı oyun eşlemeleri kaldırılacak. Geri yükleme öncesi mevcut profil ayrıca saklanacak. SteamVR kapatılarak devam edilsin mi?", "Özgün profili geri yükle", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await _locomotion.StopAsync(); await _systemMode.StopSteamVrAsync(); var result = new GameAdapterStore().RestoreOriginalProfile(); _selectedGameId = "half-life-alyx"; BuildGamesPage();
            MessageBox.Show(this, $"Özgün profil geri yüklendi. {result.RemovedAdapterCount} kullanıcı eşlemesi kaldırıldı. Geri yükleme öncesi durum ayrıca saklandı.", "Geri yükleme tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Geri yükleme başarısız", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async Task BuildCalibrationCenterAsync()
    {
        if (ToolsPage.Content is not StackPanel root) return;
        root.Children.Clear();
        var progress = await new UserSetupStore().LoadCalibrationAsync();
        var selected = _inventory.Sensors.OrderBy(x => x).ToArray();

        root.Children.Add(SectionHeader("KALİBRASYON MERKEZİ", "Önce cihazlarını hazırla", "Her cihaz bağlantıdan sonra üç adet 5 dakikalık temel fazı tamamlar. Bu kayıtlar cihazı kullanıma açar."));
        var devicePanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        if (selected.Length == 0) devicePanel.Children.Add(new TextBlock { Text = "Henüz hareket cihazı seçmedin. Sol menüden Cihazlarım'ı aç.", Foreground = Brush("#F1C566"), Margin = new Thickness(4, 18, 0, 22) });
        foreach (var sensor in selected) devicePanel.Children.Add(CreateCalibrationCard(sensor, progress.Devices.FirstOrDefault(x => x.Sensor == sensor)));
        root.Children.Add(devicePanel);

        var hmdValidation = new Button
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 8),
            ToolTip = "SteamVR başlık yönü ve dönüş örneklerini oyun hareketi üretmeden doğrular"
        };
        var hmdGrid = new Grid();
        hmdGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
        hmdGrid.ColumnDefinitions.Add(new ColumnDefinition());
        hmdGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hmdGrid.Children.Add(new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/NiiRMotion.App;component/Assets/device-v3-quest3.png")), Width = 62, Height = 52, Stretch = Stretch.Uniform });
        var lastHmdValidation = HmdValidationCaptureService.LoadLatest();
        var hmdText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        hmdText.Children.Add(Label("İSTEĞE BAĞLI · HMD", "#35B8F5", 9, FontWeights.Bold));
        hmdText.Children.Add(Label("Başlık yön ve dönüş doğrulaması", "#F4F7FA", 15, FontWeights.SemiBold, new Thickness(0, 4, 0, 2)));
        hmdText.Children.Add(Label(lastHmdValidation is null ? "Tek seferlik 3 dakikalık kayıt; kişisel yürüyüş modelini değiştirmez."
            : lastHmdValidation.Passed ? $"✓ Son kayıt hazır · {lastHmdValidation.SampleRateHz:0.0} Hz · {lastHmdValidation.TrackedRatio * 100:0}% takip"
            : "Son kayıt kalite kontrolünden geçmedi; güvenle yeniden alınabilir.", lastHmdValidation?.Passed == true ? "#55DDB8" : "#94A1AD", 10, FontWeights.Normal));
        Grid.SetColumn(hmdText, 1); hmdGrid.Children.Add(hmdText);
        var hmdArrow = Label("BUGÜN DAHA SONRA  →", "#55DDB8", 9, FontWeights.Bold); hmdArrow.VerticalAlignment = VerticalAlignment.Center; hmdArrow.Margin = new Thickness(18, 0, 0, 0); Grid.SetColumn(hmdArrow, 2); hmdGrid.Children.Add(hmdArrow);
        hmdValidation.Content = hmdGrid;
        hmdValidation.Click += OpenHmdValidationClick;
        root.Children.Add(hmdValidation);

        var advanced = new Border { Background = Brush("#09121A"), BorderBrush = Brush("#1F303C"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(0, 6, 0, 0) };
        var advancedGrid = new Grid(); advancedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(245) }); advancedGrid.ColumnDefinitions.Add(new ColumnDefinition());
        var advancedText = new StackPanel(); advancedText.Children.Add(Label("İSTEĞE BAĞLI · EK KAYIT", "#8AA0AF", 9, FontWeights.Bold)); advancedText.Children.Add(Label("Modeli yeni kayıtlarla geliştir", "#F4F7FA", 17, FontWeights.SemiBold, new Thickness(0, 6, 0, 4)));
        advancedText.Children.Add(Label("Temel fazlardan sonra tek cihaz veya istediğin kombinasyonla 5 dakikalık ek kayıt yap.", "#94A1AD", 10, FontWeights.Normal)); advancedGrid.Children.Add(advancedText);
        var advancedChoices = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var combinations = _profileRecommendations
            .Where(x => x.Profile.LocomotionAllowed)
            .Select(x => new { x.Profile, Sensors = ProfileSensors(x.Profile).Distinct().OrderBy(s => s).ToArray() })
            .Where(x => x.Sensors.Length > 0 && x.Sensors.All(selected.Contains))
            .GroupBy(x => string.Join("-", x.Sensors))
            .Select(x => x.First())
            .OrderBy(x => x.Sensors.Length)
            .ThenBy(x => x.Profile.Name)
            .ToArray();
        foreach (var combination in combinations)
        {
            var ready = combination.Sensors.All(x => progress.Devices.FirstOrDefault(p => p.Sensor == x)?.IsReady == true);
            var choice = new Button { Content = string.Join(" + ", combination.Sensors.Select(SensorDisplayName)), IsEnabled = ready, ToolTip = ready ? "Bu kombinasyonla yeni 5 dakikalık kayıt ekle" : "Önce bu cihazların temel kalibrasyonlarını tamamla", Margin = new Thickness(6, 3, 0, 3), Padding = new Thickness(13, 9, 13, 9) };
            var profile = combination.Profile; var sensors = combination.Sensors;
            choice.Click += (_, _) => OpenAdvancedTraining(profile, sensors); advancedChoices.Children.Add(choice);
        }
        Grid.SetColumn(advancedChoices, 1); advancedGrid.Children.Add(advancedChoices); advanced.Child = advancedGrid; root.Children.Add(advanced);
    }

    private FrameworkElement CreateCalibrationCard(SensorFamily sensor, DeviceCalibrationProgress? progress)
    {
        var (name, detail, icon) = sensor switch
        {
            SensorFamily.JoyCon => ("Joy-Con", "İki uyluk sensörü", "device-v3-joycon-left.png"),
            SensorFamily.PsMove => ("PS Move", "İki baldır sensörü", "device-v3-psmove-left.png"),
            SensorFamily.Phone => ("Telefon", "Göğüs sensörü", "device-v3-phone.png"),
            _ => ("Balance Board", "Basınç ve denge", "device-v3-board.png")
        };
        var done = progress?.CompletedPhases ?? 0; var ready = progress?.IsReady == true;
        var button = new Button { Width = 205, Height = 174, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(13), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var image = new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri($"pack://application:,,,/NiiRMotion.App;component/Assets/{icon}")), Height = 78, Stretch = Stretch.Uniform };
        grid.Children.Add(image);
        var bottom = new Grid { Margin = new Thickness(0, 8, 0, 0) }; bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var texts = new StackPanel(); texts.Children.Add(Label(name, "#F4F7FA", 14, FontWeights.SemiBold)); texts.Children.Add(Label(detail, "#8FA0AD", 9, FontWeights.Normal, new Thickness(0, 2, 0, 0))); texts.Children.Add(Label(ready ? "✓ KULLANIMA HAZIR" : $"TEMEL FAZ  {done}/3", ready ? "#55DDB8" : "#F1C566", 9, FontWeights.Bold, new Thickness(0, 7, 0, 0))); bottom.Children.Add(texts);
        var arrow = Label("→", "#35B8F5", 20, FontWeights.SemiBold); arrow.VerticalAlignment = VerticalAlignment.Bottom; Grid.SetColumn(arrow, 1); bottom.Children.Add(arrow); Grid.SetRow(bottom, 1); grid.Children.Add(bottom); button.Content = grid;
        button.Click += async (_, _) =>
        {
            var resumePhone = sensor == SensorFamily.Phone && _phoneMonitor is not null;
            if (sensor == SensorFamily.Phone) await StopPhoneMonitorAsync();
            try { new DeviceCalibrationWindow(sensor) { Owner = this }.ShowDialog(); }
            finally
            {
                if (resumePhone || ProfileUsesPhone()) try { await EnsurePhoneMonitorAsync(); } catch { }
                await BuildCalibrationCenterAsync();
            }
        };
        return button;
    }

    private static Border SectionHeader(string eyebrow, string title, string detail)
    {
        var panel = new StackPanel(); panel.Children.Add(Label(eyebrow, "#35B8F5", 9, FontWeights.Bold)); panel.Children.Add(Label(title, "#F4F7FA", 20, FontWeights.SemiBold, new Thickness(0, 6, 0, 3))); panel.Children.Add(Label(detail, "#94A1AD", 11, FontWeights.Normal));
        return new Border { Child = panel, Background = Brush("#0B141D"), BorderBrush = Brush("#263946"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 0, 14) };
    }

    private static TextBlock Label(string text, string color, double size, FontWeight weight, Thickness? margin = null) => new() { Text = text, Foreground = Brush(color), FontSize = size, FontWeight = weight, Margin = margin ?? new Thickness(), TextWrapping = TextWrapping.Wrap };

    private async void OpenWalkingCalibrationForInventory()
    {
        var sensors = ProfileSensors(_profile).ToArray();
        var resumePhone = sensors.Contains(SensorFamily.Phone) && _phoneMonitor is not null;
        if (sensors.Contains(SensorFamily.Phone)) await StopPhoneMonitorAsync();
        try { if (sensors.Length > 0) new ProfileCalibrationWindow(_profile, sensors) { Owner = this }.ShowDialog(); }
        finally
        {
            if (resumePhone || ProfileUsesPhone()) try { await EnsurePhoneMonitorAsync(); } catch { }
            await BuildCalibrationCenterAsync();
        }
    }

    private static IEnumerable<SensorFamily> ProfileSensors(MotionProfile profile)
    {
        if (profile.Required.Contains(DeviceKind.JoyConLeft)) yield return SensorFamily.JoyCon;
        if (profile.Required.Contains(DeviceKind.PsMoveLeft)) yield return SensorFamily.PsMove;
        if (profile.Required.Contains(DeviceKind.Phone)) yield return SensorFamily.Phone;
        if (profile.Required.Contains(DeviceKind.BalanceBoard)) yield return SensorFamily.BalanceBoard;
    }

    private async void OpenAdvancedTraining(MotionProfile profile, SensorFamily[] sensors)
    {
        var resume = sensors.Contains(SensorFamily.Phone) && _phoneMonitor is not null;
        if (sensors.Contains(SensorFamily.Phone)) await StopPhoneMonitorAsync();
        try { new AdvancedTrainingWindow(profile, sensors) { Owner = this }.ShowDialog(); }
        finally { if (resume || ProfileUsesPhone()) try { await EnsurePhoneMonitorAsync(); } catch { } }
    }
    private void ShowPage(UIElement page, string title, string subtitle, Button selectedNav)
    {
        foreach (var item in new UIElement[] { OverviewPage, ModesPage, DevicesPage, GamesPage, ToolsPage }) item.Visibility = item == page ? Visibility.Visible : Visibility.Collapsed;
        foreach (var nav in new[] { OverviewNav, ModesNav, DevicesNav, GamesNav, ToolsNav })
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
            var assignments = await new PsMoveAssignmentStore(NiiMotionPaths.PsMoveAssignments).LoadAsync();
            if (assignments is not { IsComplete: true }) throw new InvalidOperationException("Önce PS Move sol/sağ atamasını tamamla.");
            await new PsMoveDiagnosticsService().ShowAssignmentColorsAsync(assignments, TimeSpan.FromSeconds(8));
            CalibrationPsMoveStatus.Text = "Sensörler ölçülüyor…";
            var stored = await new PsMoveCalibrationStore(NiiMotionPaths.PsMoveFactoryCalibration).LoadAsync();
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
            var assignments = await new PsMoveAssignmentStore(NiiMotionPaths.PsMoveAssignments).LoadAsync();
            var probes = new PsMoveDiagnosticsService().Discover().Where(x => x.SensorReportsPossible).ToArray();
            var left = assignments is { IsComplete: true } && probes.Any(x => x.Device.StableId == assignments.LeftStableId);
            var right = assignments is { IsComplete: true } && probes.Any(x => x.Device.StableId == assignments.RightStableId);
            var battery = left || right ? await new PsMoveDiagnosticsService().ReadBatteryStatusAsync() : [];
            var leftBattery = battery.FirstOrDefault(x => x.StableId.Equals(assignments?.LeftStableId, StringComparison.OrdinalIgnoreCase))?.Display;
            var rightBattery = battery.FirstOrDefault(x => x.StableId.Equals(assignments?.RightStableId, StringComparison.OrdinalIgnoreCase))?.Display;
            CalibrationPsMoveStatus.Text = left && right
                ? $"✓ Sol {leftBattery ?? "bağlı"} · Sağ {rightBattery ?? "bağlı"} · renklerle tanıt"
                : $"{(left ? $"Sol {leftBattery ?? "✓"}" : "Sol uyuyor/eksik")} · {(right ? $"Sağ {rightBattery ?? "✓"}" : "Sağ uyuyor/eksik")}";
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
        var progressPath = Path.Combine(NiiMotionPaths.UserGaitData, "joycon-learning", "progress-v2.json");
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
            var analysis = analyzer.Analyze(NiiMotionPaths.UserGaitData);
            var profilePath = Path.Combine(NiiMotionPaths.Config, "personal-gait-pace.json");
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
    private async void PsMoveProfileClick(object sender, RoutedEventArgs e) { SelectProfile(MotionProfile.PsMoveOnly); await ScanAsync(); }
    private async void JoyConPhoneProfileClick(object sender, RoutedEventArgs e) { SelectProfile(MotionProfile.JoyConPhone); await ScanAsync(); }
    private async void FullProfileClick(object sender, RoutedEventArgs e) { SelectProfile(MotionProfile.FullFusion); await ScanAsync(); }
    private async void PhoneProfileClick(object sender, RoutedEventArgs e) { SelectProfile(MotionProfile.PhoneOnly); await ScanAsync(); }
    private async void BoardOnlyProfileClick(object sender, RoutedEventArgs e) { SelectProfile(MotionProfile.BoardOnly); await ScanAsync(); }
    private void OpenBoardLabClick(object sender, RoutedEventArgs e) => new BoardLabWindow { Owner = this }.ShowDialog();
    private async void OpenHmdValidationClick(object sender, RoutedEventArgs e) { await _locomotion.StopAsync(); SetStopControl(false); new HmdValidationWindow { Owner = this }.ShowDialog(); await BuildCalibrationCenterAsync(); }
    private async void OpenRecoveryCenterClick(object sender, RoutedEventArgs e)
    {
        await _locomotion.StopAsync(); SetStopControl(false);
        if (new RecoveryCenterWindow { Owner = this }.ShowDialog() == true)
        {
            await EnsureHardwareInventoryAsync(); HandTrackingToggle.IsChecked = _inventory.UsesHandTracking; RebuildProfileMenu(); await ScanAsync();
            CalibrationLiveResult.Text = "✓ Seçili yedek geri yüklendi; cihazlar ve kişisel ayarlar yeniden okundu.";
        }
    }
    private async void OpenDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        await _locomotion.StopAsync(); SetStopControl(false);
        new DiagnosticsWindow(_profile) { Owner = this }.ShowDialog();
    }
    private async void BoardJoyConProfileClick(object sender, RoutedEventArgs e) { SelectProfile(MotionProfile.BoardJoyCon); await ScanAsync(); }
    private async void BoardPhoneProfileClick(object sender, RoutedEventArgs e) { SelectProfile(MotionProfile.BoardPhone); await ScanAsync(); }
    private async void HandTrackingChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var enabled = HandTrackingToggle.IsChecked == true;
        if (_inventory.UsesHandTracking != enabled)
        {
            _inventory = _inventory with { UsesHandTracking = enabled, UpdatedAt = DateTimeOffset.UtcNow };
            await new UserSetupStore().SaveInventoryAsync(_inventory);
            RebuildProfileMenu();
        }
        RefreshHandTrackingVisual();
        await ScanAsync();
    }
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
        else if (device.Kind is DeviceKind.PsMoveLeft or DeviceKind.PsMoveRight)
        {
            e.Handled = true;
            if (_moveIdentifyBusy) return;
            var assignments = await new PsMoveAssignmentStore(NiiMotionPaths.PsMoveAssignments).LoadAsync();
            if (device.State == DeviceState.Missing || assignments is null || !assignments.IsComplete)
            {
                ToolsNavClick(this, new RoutedEventArgs());
                CalibrationLiveResult.Text = "PS Move kurulumu eksik. Aşağıdaki PS Move kartını aç; bağlantı ekranı sol/sağ tanıtma ve USB kalibrasyonuna yönlendirecek.";
                CalibrationLiveResult.Foreground = Brush("#F1C566");
                return;
            }

            _moveIdentifyBusy = true;
            var side = device.Kind == DeviceKind.PsMoveLeft ? LegSide.Left : LegSide.Right;
            try
            {
                ReadinessTitle.Text = side == LegSide.Left ? "SOL PS MOVE · KIRMIZI" : "SAĞ PS MOVE · MAVİ";
                ReadinessMessage.Text = "Küre kısa süre yanacak. Move uyuyorsa şimdi büyük Move düğmesine bir kez bas.";
                await new PsMoveDiagnosticsService().ShowAssignedControllerColorAsync(assignments, side, TimeSpan.FromSeconds(3));
                ReadinessMessage.Text = "Işık testi tamamlandı.";
            }
            catch (Exception ex)
            {
                ReadinessTitle.Text = "PS MOVE IŞIK TESTİ BAŞARISIZ";
                ReadinessMessage.Text = ex.Message;
            }
            finally
            {
                _moveIdentifyBusy = false;
                await ScanAsync();
            }
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
        new ActiveMotionProfileStore().Save(profile.Id);
        _suppressAutomaticLocomotion = false;
        ProfilePopup.IsOpen = false;
        foreach (var tile in new[] { NativeModeTile, JoyConModeTile, JoyConPhoneModeTile, FullModeTile, PhoneModeTile, BoardOnlyModeTile, BoardJoyConModeTile, BoardPhoneModeTile })
            tile.Background = Brush("#0D151E");
        var tiles = new[] { NativeModeTile, JoyConModeTile, JoyConPhoneModeTile, FullModeTile, PhoneModeTile, BoardOnlyModeTile, BoardJoyConModeTile, BoardPhoneModeTile };
        foreach (var tile in tiles) { tile.BorderBrush = Brush("#22303B"); tile.Opacity = 1; }
        foreach (var badge in new[] { NativeSelectionBadge, JoyConSelectionBadge, JoyConPhoneSelectionBadge, FullSelectionBadge, PhoneSelectionBadge, BoardOnlySelectionBadge, BoardJoyConSelectionBadge, BoardPhoneSelectionBadge }) badge.Visibility = Visibility.Collapsed;
        var selected = profile == MotionProfile.ClassicVr ? NativeModeTile : profile == MotionProfile.JoyConOnly || profile == MotionProfile.PsMoveOnly ? JoyConModeTile : profile == MotionProfile.PhoneOnly ? PhoneModeTile : profile == MotionProfile.FullFusion ? FullModeTile : profile == MotionProfile.BoardOnly ? BoardOnlyModeTile : profile == MotionProfile.BoardJoyCon ? BoardJoyConModeTile : profile == MotionProfile.BoardPhone ? BoardPhoneModeTile : JoyConPhoneModeTile;
        var selectedBadge = profile == MotionProfile.ClassicVr ? NativeSelectionBadge : profile == MotionProfile.JoyConOnly ? JoyConSelectionBadge : profile == MotionProfile.PhoneOnly ? PhoneSelectionBadge : profile == MotionProfile.FullFusion ? FullSelectionBadge : profile == MotionProfile.BoardOnly ? BoardOnlySelectionBadge : profile == MotionProfile.BoardJoyCon ? BoardJoyConSelectionBadge : profile == MotionProfile.BoardPhone ? BoardPhoneSelectionBadge : JoyConPhoneSelectionBadge;
        selected.Background = Brush("#12283A"); selected.BorderBrush = Brush("#38A8F3"); selected.BorderThickness = new Thickness(2); selected.Opacity = 1;
        selectedBadge.Visibility = Visibility.Visible;
        foreach (var tile in tiles.Where(x => x != selected)) tile.BorderThickness = new Thickness(1);
        SidebarProfileName.Text = ActiveProfileName.Text = profile.Name;
        ActiveProfileDetail.Text = profile.LocomotionAllowed ? "Yerinde yürüyüş çıkışı" : "Özgün kontrolcü hareketi";
        UpdateProfileInformation(profile);
    }

    private void UpdateProfileInformation(MotionProfile profile)
    {
        var information = profile.Id switch
        {
            "classic-vr" => ("✓  NORMAL VR", "  ·  NiiMotion hareket üretmez", "Quest ve kontrolcüler özgün VR davranışıyla çalışır; sensör verileri oyun girişine aktarılmaz."),
            "joycon-only" => ("✓  JOY-CON YÜRÜYÜŞÜ", "  ·  Telefon veya board gerekmez", "Bacaklardaki iki Joy-Con yerinde adımlarını algılar. Bağlantı kesilirse hareket anında sıfırlanır."),
            "psmove-only" => ("✓  PS MOVE YÜRÜYÜŞÜ", "  ·  Kişisel baldır profili", "Baldırlardaki iki PS Move kişisel kalibrasyonunu kullanır. HMD dönüş kilidi yanlış ileri hareketi sürücü seviyesinde bastırır."),
            "joycon-phone" => ("✓  JOY-CON + TELEFON", "  ·  Dengeli ve önerilen profil", "Joy-Con'lar adımları, göğüsteki telefon gövde hareketini izler. Telefon kesilirse sistem güvenli biçimde durur."),
            "phone-only" => ("◇  SADECE TELEFON", "  ·  Deneysel hareket algılama", "Göğüsteki telefon yerinde yürüyüşü tahmin eder. Joy-Con gerekmez; kararlılık birleşik profillerden düşüktür."),
            "board-only" => ("◇  BALANCE BOARD", "  ·  Basınçla yürüyüş ve dönüş", "Ağırlık aktarımı hareket ve dönüşe çevrilir. Karttan inildiğinde ya da bağlantı kesildiğinde çıkış sıfırlanır."),
            "board-joycon" => ("✓  BOARD + JOY-CON", "  ·  Bacak ve basınç füzyonu", "Joy-Con'lar adımı, Balance Board ağırlık aktarımını izler. Telefon kullanmadan daha kararlı hareket sağlar."),
            "board-phone" => ("◇  BOARD + TELEFON", "  ·  Basınç ve gövde füzyonu", "Balance Board ağırlığı, telefon gövde hareketini izler. Joy-Con gerekmez; bu profil deneyseldir."),
            _ => ("✓  " + profile.Name.ToUpperInvariant(), "  ·  Cihazların birlikte doğrulanır", $"{string.Join(", ", profile.Required.Where(x => x != DeviceKind.Quest3).Select(x => new DeviceStatus(x, "", DeviceState.Unknown, "", "").IconGlyph + " " + x))} verileri seçili profil içinde birleştirilir. Zorunlu bir cihaz kesilirse hareket güvenle sıfırlanır.")
        };

        ProfileInfoTitle.Text = information.Item1;
        ProfileInfoSummary.Text = information.Item2;
        ProfileInfoDetail.Text = information.Item3;
        var experimental = _profileRecommendations.FirstOrDefault(x => x.Profile.Id == profile.Id)?.Experimental == true;
        ProfileInfoTitle.Foreground = Brush(experimental ? "#F6C86B" : "#54D4A8");
    }
    private async void LaunchSteamVrClick(object sender, RoutedEventArgs e)
    {
        var locomotionRequested = _profile.LocomotionAllowed && !_launchNormalVrOverride;
        var uncalibrated = await UncalibratedProfileSensorsAsync();
        if (locomotionRequested && uncalibrated.Count > 0)
        {
            ReadinessTitle.Text = "TEMEL KALİBRASYON GEREKİYOR";
            ReadinessMessage.Text = $"Önce tamamla: {string.Join(", ", uncalibrated.Select(SensorDisplayName))}. SteamVR başlatılmadı.";
            await BuildCalibrationCenterAsync();
            ShowPage(ToolsPage, "Test ve Kalibrasyon", "Önce temel cihaz kalibrasyonlarını tamamla", ToolsNav);
            return;
        }
        if (locomotionRequested && _profile.Required.Contains(DeviceKind.PsMoveLeft))
        {
            var onboarding = await new PsMoveOnboardingService().GetStatusAsync();
            if (!onboarding.IsReady) { ReadinessTitle.Text = "PS MOVE KURULUMU GEREKİYOR"; ReadinessMessage.Text = onboarding.Instruction; new PsMoveLabWindow { Owner = this }.ShowDialog(); await ScanAsync(); return; }
        }
        await ScanAsync();
        var latestDevices = (DevicesList.ItemsSource as IEnumerable<DeviceStatus>)?.ToArray() ?? [];
        var preflightBlocking = locomotionRequested ? PreflightBlockingDevices(latestDevices) : [];
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
            if (_pendingGame is not null) SetGameLaunchStage(_pendingGame, GameLaunchStage.WaitingForVirtualDesktop, "Quest ve Virtual Desktop oturumu bekleniyor…");
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
            if (!locomotionRequested)
            {
                if (_pendingGame is not null) SetGameLaunchStage(_pendingGame, GameLaunchStage.ApplyingVrMode, "Normal VR modu güvenle uygulanıyor…");
                if (_systemMode.CurrentMode != SystemMode.Original) await _systemMode.ApplyAsync(SystemMode.Original);
                if (_pendingGame is not null) SetGameLaunchStage(_pendingGame, GameLaunchStage.StartingSteamVr, "SteamVR Virtual Desktop üzerinden başlatılıyor…");
                LaunchSteamVrViaVirtualDesktop();
                RefreshSystemMode();
                var normalDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
                while (DateTime.UtcNow < normalDeadline && Process.GetProcessesByName("vrserver").Length == 0) await Task.Delay(500);
                await Task.Delay(1200); await ScanAsync();
                var questReady = (DevicesList.ItemsSource as IEnumerable<DeviceStatus>)?.Any(x => x.Kind == DeviceKind.Quest3 && x.IsConnected) == true;
                if (!questReady) throw new InvalidOperationException("SteamVR açıldı ancak Quest 3 bağlantısı doğrulanamadı.");
                ReadinessTitle.Text = "NORMAL VR BAŞLATILDI";
                ReadinessMessage.Text = "NiiMotion kapalı; oyunu kontrolcülerinle normal şekilde oynayabilirsin.";
                await LaunchPendingGameAsync();
                return;
            }
            if (_pendingGame is not null) SetGameLaunchStage(_pendingGame, GameLaunchStage.ApplyingVrMode, "NiiMotion sürücüsü ve oyun eşlemesi uygulanıyor…");
            if (_systemMode.CurrentMode != SystemMode.NiiMotion) await _systemMode.ApplyAsync(SystemMode.NiiMotion);
            else _systemMode.EnsureGameOverrides(SystemMode.NiiMotion);
            if (_pendingGame is not null) SetGameLaunchStage(_pendingGame, GameLaunchStage.StartingSteamVr, "SteamVR Virtual Desktop üzerinden başlatılıyor…");
            LaunchSteamVrViaVirtualDesktop();
            if (_pendingGame is not null) SetGameLaunchStage(_pendingGame, GameLaunchStage.WaitingForMotionBridge, "SteamVR ve NiiMotion hareket köprüsü doğrulanıyor…");
            await WaitForSteamVrDriverAsync(TimeSpan.FromSeconds(75));
            RefreshSystemMode(); await ScanAsync();
            if (_readiness?.State == ReadinessState.NotReady) throw new InvalidOperationException("SteamVR açıldı ancak başlık veya gerekli cihazlardan biri doğrulanamadı. Hareket çıkışı başlatılmadı.");
            if (_pendingGame is not null) SetGameLaunchStage(_pendingGame, GameLaunchStage.StartingLocomotion, "Kişisel hareket modeli başlatılıyor…");
            if (!await StartLocomotionAsync())
                throw new InvalidOperationException(ReadinessMessage.Text);
            await LaunchPendingGameAsync();
        }
        catch (Exception ex) { _pendingGameAppId = null; if (_pendingGame is not null) SetGameLaunchStage(_pendingGame, GameLaunchStage.Failed, $"Başlatma durduruldu: {ex.Message}"); _pendingGame = null; await _locomotion.StopAsync(); ReadinessTitle.Text = "VR HAZIRLANAMADI"; ReadinessMessage.Text = ex.Message; }
        finally { _launchNormalVrOverride = false; if (sender is Button prepareButton) prepareButton.IsEnabled = true; }
    }

    private async Task<IReadOnlyList<SensorFamily>> UncalibratedProfileSensorsAsync()
    {
        var required = new List<SensorFamily>();
        if (_profile.Required.Contains(DeviceKind.JoyConLeft)) required.Add(SensorFamily.JoyCon);
        if (_profile.Required.Contains(DeviceKind.PsMoveLeft)) required.Add(SensorFamily.PsMove);
        if (_profile.Required.Contains(DeviceKind.Phone)) required.Add(SensorFamily.Phone);
        if (_profile.Required.Contains(DeviceKind.BalanceBoard)) required.Add(SensorFamily.BalanceBoard);
        var progress = await new UserSetupStore().LoadCalibrationAsync();
        return required.Where(sensor => progress.Devices.FirstOrDefault(x => x.Sensor == sensor)?.IsReady != true).ToArray();
    }

    private static string SensorDisplayName(SensorFamily sensor) => sensor switch { SensorFamily.JoyCon => "Joy-Con", SensorFamily.PsMove => "PS Move", SensorFamily.Phone => "Telefon", _ => "Balance Board" };

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
    private async void StartClick(object sender, RoutedEventArgs e) { _suppressAutomaticLocomotion = false; await StartLocomotionAsync(); }

    private async Task EnsureAutomaticLocomotionAsync()
    {
        if (_suppressAutomaticLocomotion || _locomotion.IsRunning || !_profile.LocomotionAllowed) return;
        if (_systemMode.CurrentMode != SystemMode.NiiMotion || Process.GetProcessesByName("vrserver").Length == 0) return;
        var pipeReady = false;
        try { pipeReady = Directory.GetFiles(@"\\.\pipe\").Any(x => x.EndsWith("NiiRMotion.VrOutput.v1", StringComparison.OrdinalIgnoreCase)); } catch { }
        if (!pipeReady) return;
        var devices = (DevicesList.ItemsSource as IEnumerable<DeviceStatus>)?.ToArray() ?? [];
        if (PreflightBlockingDevices(devices).Count > 0 || (await UncalibratedProfileSensorsAsync()).Count > 0) return;
        if (await StartLocomotionAsync())
        {
            ReadinessTitle.Text = "NİIMOTION OTOMATİK BAĞLANDI";
            ReadinessMessage.Text = "SteamVR algılandı; kayıtlı hareket profili otomatik başlatıldı.";
        }
    }

    private async Task<bool> StartLocomotionAsync()
    {
        if (!_profile.LocomotionAllowed) { StartButton.IsEnabled = false; try { await _locomotion.StopAsync(); await _systemMode.ApplyAsync(SystemMode.Original); RefreshSystemMode(); LaunchSteamVrViaVirtualDesktop(); ReadinessTitle.Text = "NORMAL VR HAZIR"; ReadinessMessage.Text = "NiiMotion kapalı; cihazların özgün ayarları kullanılıyor."; return true; } catch (Exception ex) { ReadinessMessage.Text = ex.Message; return false; } finally { StartButton.IsEnabled = true; } }
        var currentDevices = (DevicesList.ItemsSource as IEnumerable<DeviceStatus>)?.ToArray() ?? [];
        var blockingDevices = PreflightBlockingDevices(currentDevices);
        if (blockingDevices.Count > 0)
        {
            ReadinessMessage.Text = $"Locomotion başlatılamadı. Önce bağla: {string.Join(", ", blockingDevices.Select(x => x.Name))}.";
            return false;
        }
        StartButton.IsEnabled = false;
        if (_discovery.IsTestMode) { _demoPhase = 0; _demoSteps = 0; _demoTimer.Start(); SetStopControl(true); SetRunningVisuals("DEMO OUTPUT — GERÇEK VR'A GÖNDERİLMEZ"); ReadinessMessage.Text = "Demo oturumu çalışıyor. Telemetri simülasyondur; gerçek donanım doğrulaması değildir."; return true; }
        try
        {
            var calibration = Path.Combine(NiiMotionPaths.Calibration, "gait-v1.json");
            if (!File.Exists(calibration)) calibration = Path.Combine(Environment.CurrentDirectory, "calibration", "gait-v1.json");
            if (_systemMode.CurrentMode != SystemMode.NiiMotion) await _systemMode.ApplyAsync(SystemMode.NiiMotion);
            else _systemMode.EnsureGameOverrides(SystemMode.NiiMotion);
            var usesJoyCon = _profile.Required.Contains(DeviceKind.JoyConLeft);
            var usesPsMove = _profile.Required.Contains(DeviceKind.PsMoveLeft);
            var includePhone = _profile.Required.Contains(DeviceKind.Phone);
            var includeBoard = _profile.Required.Contains(DeviceKind.BalanceBoard);
            if (usesPsMove && !usesJoyCon) { if (includePhone) await StopPhoneMonitorAsync(); await _locomotion.StartPsMoveOnlyAsync(includePhone, includeBoard); SetStopControl(true); SetRunningVisuals(_locomotion.ModeDescription); ReadinessMessage.Text = "Hazır. PS Move tabanlı profil çalışıyor."; return true; }
            if (includePhone) await StopPhoneMonitorAsync();
            await _locomotion.StartAsync(calibration, includePhone, phoneOnly: !usesJoyCon && !usesPsMove && includePhone, includeBoard: includeBoard, boardOnly: !usesJoyCon && !usesPsMove && includeBoard && !includePhone, includePsMove: usesPsMove); SetStopControl(true); SetRunningVisuals(_locomotion.ModeDescription); ReadinessMessage.Text = includeBoard ? "Board otomatik sıfırlandı. Üzerine çıkıp yerinde yürüyebilirsin." : "Hazır. Yerinde yürüyerek oyunda ilerleyebilirsin.";
            return true;
        }
        catch (Exception ex) { await _locomotion.StopAsync(); if (ProfileUsesPhone()) try { await EnsurePhoneMonitorAsync(); } catch { } SetStopControl(false); StartButton.IsEnabled = _readiness?.State != ReadinessState.NotReady; LocomotionState.Text = "OFF"; LocomotionState.Foreground = Brushes.LightPink; ReadinessMessage.Text = $"Locomotion başlatılamadı: {ex.Message}"; return false; }
    }
    private async void StopClick(object sender, RoutedEventArgs e)
    {
        _suppressAutomaticLocomotion = true;
        SetStopControl(false); _demoTimer.Stop(); await _locomotion.StopAsync();
        if (ProfileUsesPhone()) try { await EnsurePhoneMonitorAsync(); } catch { }
        LocomotionState.Text = "OFF"; LocomotionState.Foreground = Brush("#FF9BA8"); TelemetryMode.Text = "KAPALI"; ResetTelemetry();
        StartButton.IsEnabled = _profile.LocomotionAllowed && _readiness?.State != ReadinessState.NotReady; ReadinessMessage.Text = "Locomotion güvenli şekilde durduruldu ve output ayrıldı.";
        PublishVrPanel("Kapalı");
    }
    private void SetStopControl(bool running)
    {
        StopButton.IsEnabled = running;
        StopButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
    }
    private void SetRunningVisuals(string mode) { LocomotionState.Text = "ON"; LocomotionState.Foreground = Brush("#72E1C2"); TelemetryMode.Text = mode; TelemetryMode.Foreground = Brush("#72E1C2"); PublishVrPanel(mode); }
    private void PublishVrPanel(string state, IReadOnlyList<DeviceStatus>? devices = null, float speed = 0)
    {
        var game = new SteamGameCatalog().Detect().FirstOrDefault(x => x.Definition.Id == _selectedGameId)?.Definition.Name ?? _selectedGameId;
        var summary = devices is null ? UiLocalization.Text("Canlı oturum") : $"{devices.Count(x => x.IsConnected)}/{devices.Count} {(UiLocalization.IsEnglish ? "connected" : "bağlı")}";
        try { _vrPanel.Publish(new(1, _profile.Name, game, UiLocalization.Text(state), speed, summary, ReadinessMessage?.Text ?? "", DateTimeOffset.UtcNow)); } catch { }
    }
    private void DemoTick(object? sender, EventArgs e)
    {
        _demoPhase += 0.18; var cadence = 1.85 + Math.Sin(_demoPhase) * 0.22; var confidence = 82 + Math.Sin(_demoPhase * 0.55) * 9; var speed = Math.Clamp(cadence / 2.5, 0, 0.9);
        if ((int)(_demoPhase * 10) % 16 == 0) _demoSteps++;
        CadenceValue.Text = cadence.ToString("0.00"); CadenceBar.Value = cadence; ConfidenceValue.Text = confidence.ToString("0"); ConfidenceBar.Value = confidence;
        TargetSpeedValue.Text = speed.ToString("0.00"); TargetSpeedBar.Value = speed; GaitStateValue.Text = cadence > 2 ? "FAST WALK" : "WALKING"; StepCountValue.Text = $"{_demoSteps} adım · demo";
        PublishVrPanel("Demo", speed: (float)speed);
    }
    private void ResetTelemetry() { CadenceValue.Text = "0.00"; CadenceBar.Value = 0; ConfidenceValue.Text = "0"; ConfidenceBar.Value = 0; TargetSpeedValue.Text = "0.00"; TargetSpeedBar.Value = 0; GaitStateValue.Text = "BEKLİYOR"; StepCountValue.Text = "0 adım"; }
    private bool ProfileUsesPhone() => _profile.Required.Contains(DeviceKind.Phone);
    internal static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
}
