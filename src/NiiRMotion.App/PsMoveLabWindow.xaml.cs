using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows;
using NiiRMotion.Core;
using NiiRMotion.Infrastructure;

namespace NiiRMotion.App;

public partial class PsMoveLabWindow : Window
{
    private static string Assignments => NiiMotionPaths.PsMoveAssignments;
    private static string FactoryCalibration => NiiMotionPaths.PsMoveFactoryCalibration;
    private static string PlacementCalibration => NiiMotionPaths.PsMovePlacementCalibration;
    private static string TrainingProfile => NiiMotionPaths.PsMoveTrainingProfile;
    private int _stage;
    private bool _busy;
    private string? _pendingLeftId;
    private byte[]? _pendingFactoryCalibration;
    private sealed record RecordingPhase(int Seconds, string Label, string Instruction);
    private static readonly RecordingPhase[] FoundationPlan =
    [
        new(30, "stand", "Sabit ve rahat dur"),
        new(60, "slow_walk", "Olduğun yerde yavaş yürü"),
        new(20, "stand", "Dur ve sabit kal"),
        new(100, "natural_walk", "Olduğun yerde doğal hızda yürü"),
        new(20, "stand", "Dur ve sabit kal"),
        new(50, "fast_walk", "Olduğun yerde hızlı yürü; koşma"),
        new(20, "stand", "Dur ve sabit kal")
    ];
    private static readonly RecordingPhase[] DiscriminationPlan = BuildDiscriminationPlan();

    private static RecordingPhase[] BuildDiscriminationPlan()
    {
        var phases = new List<RecordingPhase> { new(30, "stand", "Sabit ve rahat dur") };
        for (var i = 0; i < 6; i++)
        {
            phases.Add(new(8, "natural_walk", "Doğal yürü; ses gelince hemen dur"));
            phases.Add(new(4, "stand", "Hemen dur ve kıpırdama"));
        }
        phases.AddRange([
            new(40, "bend_no_walk", "Yürümeden dizlerini bük ve doğrul"),
            new(40, "single_leg_no_walk", "Yürümeden sırayla bir bacağını kaldırıp bekle"),
            new(40, "turn_no_walk", "Yürümeden olduğun yerde sağa ve sola dön"),
            new(40, "crouch_reach_no_walk", "Yürümeden çömel, doğrul ve uzan"),
            new(38, "natural_walk", "Son doğrulama: doğal hızda yerinde yürü")
        ]);
        return phases.ToArray();
    }

    public PsMoveLabWindow(bool forcePairing = false)
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (forcePairing) { _stage = -12; ShowPairingInstruction(LegSide.Left); return; }
            var onboarding = await new PsMoveOnboardingService().GetStatusAsync();
            if (onboarding.NextStep == PsMoveOnboardingStep.AssignControllers)
            {
                _stage = -12; ShowPairingInstruction(LegSide.Left); return;
            }
            if (onboarding.NextStep == PsMoveOnboardingStep.ReadFactoryCalibration)
            {
                var assignments = await new PsMoveAssignmentStore(Assignments).LoadAsync();
                var stored = await new PsMoveCalibrationStore(FactoryCalibration).LoadAsync();
                var leftMissing = assignments is { IsComplete: true } && !stored.Any(x => x.StableId == assignments.LeftStableId);
                _stage = leftMissing ? -8 : -7; ShowUsbInstruction(leftMissing ? "sol/kırmızı" : "sağ/mavi"); return;
            }
            if (await new PsMovePlacementCalibrationStore(PlacementCalibration).LoadAsync() is null) return;
            _stage = 2;
            var foundationExists = Directory.EnumerateDirectories(NiiMotionPaths.PsMoveData, "*-foundation").Any();
            var discriminationExists = Directory.EnumerateDirectories(NiiMotionPaths.PsMoveData, "*-discrimination").Any();
            if (discriminationExists && File.Exists(TrainingProfile))
            {
                _stage = 3; StateText.Text = "MODEL HAZIR"; InstructionText.Text = "Canlı Move doğrulaması hazır";
                DetailText.Text = "45 saniyelik test oyun hareketi göndermez; yürüyüş ve yanlış hareket ayrımını canlı gösterir.";
                CountdownText.Text = "5 · CANLI MODEL DOĞRULAMA"; ProgressText.Text = "Sabit → doğal yürü → dur → diz bük → dön";
                ActionButton.Content = "▶  45 SN CANLI TEST"; return;
            }
            StateText.Text = "KALİBRE"; InstructionText.Text = foundationExists ? "Başla–dur ve yanlış hareket kaydı hazır" : "İlk Move yürüyüş kaydı hazır";
            DetailText.Text = "5 dakika boyunca ekrandaki yönergeleri uygula. Olduğun yerde yürü; ileri gitme.";
            CountdownText.Text = foundationExists ? "4 · HAREKET AYRIŞTIRMA KAYDI" : "3 · ETİKETLİ TEMEL HAREKET KAYDI";
            ProgressText.Text = foundationExists ? "Başla–dur · diz bükme · tek bacak · dönme · çömelme" : "Sabit · yavaş · doğal · hızlı · duruş geçişleri";
            ActionButton.Content = "▶  5 DK KAYDI BAŞLAT";
        };
    }

    private async void ActionClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true; ActionButton.IsEnabled = false;
        try
        {
            if (_stage == 0) await VerifyAsync();
            else if (_stage == -12) await PairUsbAsync(LegSide.Left);
            else if (_stage == -11) await AssignLeftAsync();
            else if (_stage == -10) await PairUsbAsync(LegSide.Right);
            else if (_stage == -9) await AssignRightAsync();
            else if (_stage is -8 or -7) await ReadFactoryCalibrationAsync(_stage == -8 ? LegSide.Left : LegSide.Right);
            else if (_stage == 1) await CalibrateAsync();
            else if (_stage == 2) await RecordFoundationAsync();
            else if (_stage == 3) await ValidateLiveAsync();
        }
        catch (Exception ex)
        {
            StateText.Text = "KONTROL GEREKİYOR"; StateText.Foreground = System.Windows.Media.Brushes.LightPink;
            InstructionText.Text = "Kalibrasyon tamamlanamadı"; DetailText.Text = ex.Message;
        }
        finally { _busy = false; ActionButton.IsEnabled = true; }
    }

    private void ShowPairingInstruction(LegSide side)
    {
        var left = side == LegSide.Left;
        StateText.Text = "İLK EŞLEŞTİRME";
        InstructionText.Text = $"{(left ? "Sol" : "Sağ")} yapacağın Move'u USB ile bağla";
        DetailText.Text = "Diğer Move'un USB kablosunu çıkar. Devam ettiğinde NiiMotion Bluetooth eşleştirmesini güvenli biçimde yapacak; yönetici onayı istenebilir.";
        CountdownText.Text = left ? "1 · SOL MOVE'U EŞLEŞTİR" : "2 · SAĞ MOVE'U EŞLEŞTİR";
        ProgressText.Text = left ? "İlk kontrolcü otomatik kırmızı atanır" : "İkinci kontrolcü otomatik mavi atanır";
        ActionButton.Content = "▶  USB'DEN EŞLEŞTİR";
    }

    private async Task PairUsbAsync(LegSide side)
    {
        var diagnostics = new PsMoveDiagnosticsService();
        var usb = diagnostics.Discover().Where(x => x.Device.Transport == PsMoveTransport.Usb).ToArray();
        if (usb.Length != 1) throw new InvalidOperationException(usb.Length == 0
            ? "USB ile bağlı PS Move bulunamadı. Yalnızca bu tarafın Move'unu kabloyla bağla."
            : "USB'de birden fazla Move var. Güvenli sağ/sol ataması için yalnız birini bağlı bırak.");
        _pendingFactoryCalibration = diagnostics.ReadUsbFactoryCalibration()?.Blob
            ?? throw new InvalidOperationException("PS Move fabrika sensör verisi USB üzerinden okunamadı.");

        StateText.Text = "EŞLEŞTİRİLİYOR"; InstructionText.Text = "Bluetooth bilgisi Move'a yazılıyor";
        DetailText.Text = "Açılan yönetici onayını kabul et. Kamera kurulumu yapılmaz ve kamera uyarısı bu işlemle ilgili değildir.";
        var result = await new PsMovePairingService().PairSingleUsbControllerAsync();
        if (!result.Success) throw new InvalidOperationException(result.Message);

        _stage = side == LegSide.Left ? -11 : -9;
        StateText.Text = "USB TAMAM"; InstructionText.Text = "USB kablosunu çıkar ve büyük Move düğmesine bas";
        DetailText.Text = $"Kontrolcü Bluetooth ile bağlanınca {(side == LegSide.Left ? "kırmızı" : "mavi")} yanacak ve tarafı otomatik kaydedilecek.";
        ProgressText.Text = "Kabloyu çıkardıktan sonra Move düğmesine bir kez bas";
        ActionButton.Content = side == LegSide.Left ? "▶  SOL MOVE'U DİNLE" : "▶  SAĞ MOVE'U DİNLE";
        System.Media.SystemSounds.Asterisk.Play();
    }

    private async Task AssignLeftAsync()
    {
        StateText.Text = "SOL BEKLENİYOR"; InstructionText.Text = "Sol Move'un büyük Move düğmesine bas";
        var device = await new PsMoveDiagnosticsService().WaitForButtonAsync(1u << 19, TimeSpan.FromSeconds(25));
        _pendingLeftId = device?.StableId ?? throw new InvalidOperationException("Sol Move düğme basışı alınamadı. Bluetooth bağlantısını kontrol et.");
        if (_pendingFactoryCalibration is null) throw new InvalidOperationException("Sol Move USB kalibrasyon verisi bulunamadı.");
        await new PsMoveCalibrationStore(FactoryCalibration).SaveAsync(_pendingLeftId, "left", _pendingFactoryCalibration);
        await new PsMoveDiagnosticsService().ShowControllerColorAsync(_pendingLeftId, LegSide.Left, TimeSpan.FromSeconds(3));
        _pendingFactoryCalibration = null;
        _stage = -10; ShowPairingInstruction(LegSide.Right); System.Media.SystemSounds.Asterisk.Play();
    }

    private async Task AssignRightAsync()
    {
        StateText.Text = "SAĞ BEKLENİYOR"; InstructionText.Text = "Sağ Move'un büyük Move düğmesine bas";
        var device = await new PsMoveDiagnosticsService().WaitForButtonAsync(1u << 19, TimeSpan.FromSeconds(25));
        var rightId = device?.StableId ?? throw new InvalidOperationException("Sağ Move düğme basışı alınamadı. Bluetooth bağlantısını kontrol et.");
        if (rightId.Equals(_pendingLeftId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Aynı kontrolcü iki kez seçildi. Sağ için diğer Move'u kullan.");
        if (_pendingFactoryCalibration is null) throw new InvalidOperationException("Sağ Move USB kalibrasyon verisi bulunamadı.");
        await new PsMoveAssignmentStore(Assignments).SaveAsync(_pendingLeftId!, rightId);
        await new PsMoveCalibrationStore(FactoryCalibration).SaveAsync(rightId, "right", _pendingFactoryCalibration);
        _pendingFactoryCalibration = null;
        var assignments = await new PsMoveAssignmentStore(Assignments).LoadAsync();
        if (assignments is not null) await new PsMoveDiagnosticsService().ShowAssignmentColorsAsync(assignments, TimeSpan.FromSeconds(4));
        _stage = 0; StateText.Text = "EŞLEŞTİRME TAMAM"; InstructionText.Text = "Sol kırmızı · sağ mavi olarak kaydedildi";
        DetailText.Text = "İki kontrolcünün USB kablosunu çıkar. Bluetooth bağlantısı hazırsa sensör doğrulamasına geç.";
        CountdownText.Text = "3 · BAĞLANTI KONTROLÜ"; ProgressText.Text = "Renkler ve fabrika kalibrasyonu otomatik kaydedildi";
        ActionButton.Content = "▶  MOVE'LARI DOĞRULA"; System.Media.SystemSounds.Asterisk.Play();
    }

    private void ShowUsbInstruction(string side)
    {
        StateText.Text = "USB KALİBRASYONU"; InstructionText.Text = $"Yalnız {side} Move'u USB ile bağla";
        DetailText.Text = "Diğer Move'un USB kablosunu çıkar. Bluetooth açık kalabilir. Hazır olunca fabrika sensör verisi otomatik okunacak.";
        CountdownText.Text = side.StartsWith("sol") ? "3 · SOL FABRİKA KALİBRASYONU" : "4 · SAĞ FABRİKA KALİBRASYONU";
        ProgressText.Text = "Bu işlem birkaç saniye sürer"; ActionButton.Content = "▶  USB VERİSİNİ OKU";
    }

    private async Task ReadFactoryCalibrationAsync(LegSide side)
    {
        var assignments = await new PsMoveAssignmentStore(Assignments).LoadAsync() ?? throw new InvalidOperationException("Move atamaları bulunamadı.");
        var capture = new PsMoveDiagnosticsService().ReadUsbFactoryCalibration() ?? throw new InvalidOperationException("USB ile bağlı PS Move bulunamadı.");
        var stableId = side == LegSide.Left ? assignments.LeftStableId : assignments.RightStableId;
        await new PsMoveCalibrationStore(FactoryCalibration).SaveAsync(stableId, side.ToString().ToLowerInvariant(), capture.Blob);
        if (side == LegSide.Left) { _stage = -7; ShowUsbInstruction("sağ/mavi"); }
        else
        {
            _stage = 0; StateText.Text = "FABRİKA VERİSİ TAMAM"; InstructionText.Text = "İki Move'u Bluetooth ile bağla";
            DetailText.Text = "USB kablosunu çıkar. İki Move bağlı olduğunda renk ve sensör kontrolüne geç."; CountdownText.Text = "5 · BAĞLANTI KONTROLÜ";
            ProgressText.Text = "Sol kırmızı · sağ mavi"; ActionButton.Content = "▶  MOVE'LARI DOĞRULA";
        }
        System.Media.SystemSounds.Asterisk.Play();
    }

    private async Task VerifyAsync()
    {
        StateText.Text = "KONTROL EDİLİYOR";
        var assignment = await new PsMoveAssignmentStore(Assignments).LoadAsync();
        if (assignment is not { IsComplete: true }) throw new InvalidOperationException("Sol ve sağ PS Move ataması bulunamadı.");
        await new PsMoveDiagnosticsService().ShowAssignmentColorsAsync(assignment, TimeSpan.FromSeconds(3));
        await using var source = new PsMoveSensorSource(Assignments, FactoryCalibration);
        await source.StartAsync();
        var seen = new HashSet<LegSide>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await foreach (var sample in source.Samples.ReadAllAsync(timeout.Token))
        {
            seen.Add(sample.Side); Update(sample);
            if (seen.Count == 2) break;
        }
        if (seen.Count != 2) throw new InvalidOperationException("İki Move’dan aynı anda sensör verisi alınamadı.");
        _stage = 1; StateText.Text = "BAĞLANTI TAMAM"; InstructionText.Text = "Move’ları baldırlarına tak";
        DetailText.Text = "İkisi de aynı yönde: küre yukarı, düğmeler dışarı/öne. Hazır olunca 5 saniye kıpırdamadan dur.";
        CountdownText.Text = "2 · NÖTR DURUŞ + BACAK KALDIRMA"; ProgressText.Text = "Önce 5 sn sabit duruş, ardından sırayla 6 sol + 6 sağ kaldırış";
        ActionButton.Content = "▶  KALİBRASYONU BAŞLAT";
    }

    private async Task CalibrateAsync()
    {
        var neutral = new Dictionary<LegSide, List<Vector3>> { [LegSide.Left] = [], [LegSide.Right] = [] };
        var movement = new Dictionary<LegSide, List<double>> { [LegSide.Left] = [], [LegSide.Right] = [] };
        await using var source = new PsMoveSensorSource(Assignments, FactoryCalibration);
        await source.StartAsync();
        var clock = Stopwatch.StartNew(); StateText.Text = "SABİT DUR"; InstructionText.Text = "5 saniye kıpırdamadan dur";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(38));
        await foreach (var sample in source.Samples.ReadAllAsync(timeout.Token))
        {
            Update(sample); var seconds = clock.Elapsed.TotalSeconds;
            if (seconds < 5)
            {
                neutral[sample.Side].Add(sample.AccelerationG); CountdownText.Text = $"SABİT DUR · {Math.Max(0, 5 - (int)seconds)}";
            }
            else
            {
                if (seconds < 20) { StateText.Text = "SOL BACAK"; InstructionText.Text = "Sol dizini 6 kez doğal biçimde kaldır"; }
                else { StateText.Text = "SAĞ BACAK"; InstructionText.Text = "Sağ dizini 6 kez doğal biçimde kaldır"; }
                var requested = seconds < 20 ? LegSide.Left : LegSide.Right;
                if (sample.Side == requested) movement[sample.Side].Add(sample.AngularVelocityRadps.Length());
                CountdownText.Text = $"HAREKET ÖLÇÜMÜ · {Math.Max(0, 35 - (int)seconds)} sn";
                if (seconds >= 35) break;
            }
        }
        if (neutral.Values.Any(x => x.Count < 100) || movement.Values.Any(x => x.Count < 100)) throw new InvalidOperationException("Yeterli sensör örneği alınamadı; bağlantıyı kontrol et.");
        var result = new PsMovePlacementCalibration(1, DateTimeOffset.UtcNow, SensorPlacement.CalfLowerLeg,
            Mean(neutral[LegSide.Left]), Mean(neutral[LegSide.Right]), Noise(neutral[LegSide.Left]), Noise(neutral[LegSide.Right]),
            Percentile(movement[LegSide.Left], .95), Percentile(movement[LegSide.Right], .95), neutral.Values.Sum(x => x.Count), movement.Values.Sum(x => x.Count));
        await new PsMovePlacementCalibrationStore(PlacementCalibration).SaveAsync(result);
        _stage = 2; StateText.Text = "TAMAMLANDI"; InstructionText.Text = "Baldır yerleşimi kalibre edildi";
        DetailText.Text = $"Sol tepe {result.LeftLiftPeak:0.00} rad/sn · Sağ tepe {result.RightLiftPeak:0.00} rad/sn. Sonraki aşamada etiketli Move yürüyüş kayıtları alınacak.";
        CountdownText.Text = "✓ MONTAJ AÇISI KAYDEDİLDİ"; ProgressText.Text = $"{result.NeutralSamples + result.MovementSamples:N0} kalibre edilmiş örnek";
        ActionButton.Content = "▶  5 DK KAYDI BAŞLAT";
        System.Media.SystemSounds.Asterisk.Play();
    }

    private async Task RecordFoundationAsync()
    {
        var root = NiiMotionPaths.PsMoveData;
        var isFoundation = !Directory.Exists(root) || !Directory.EnumerateDirectories(root, "*-foundation").Any();
        var plan = isFoundation ? FoundationPlan : DiscriminationPlan;
        var kind = isFoundation ? "foundation" : "discrimination";
        var folder = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + kind);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "manifest.json"), JsonSerializer.Serialize(new
        {
            version = 1, sensor = "PS Move CECH-ZCM1E pair", placement = "calf_lower_leg", locomotionOutput = false,
            plan, startedAtUtc = DateTimeOffset.UtcNow
        }, new JsonSerializerOptions { WriteIndented = true }));
        await using var writer = new StreamWriter(Path.Combine(folder, "samples.jsonl"), false, new System.Text.UTF8Encoding(false));
        await using var source = new PsMoveSensorSource(Assignments, FactoryCalibration);
        await source.StartAsync();
        var clock = Stopwatch.StartNew(); var phaseStart = 0.0; var phaseIndex = 0; long samples = 0;
        StateText.Text = "KAYIT · VR KAPALI"; System.Media.SystemSounds.Asterisk.Play();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(310));
        await foreach (var sample in source.Samples.ReadAllAsync(timeout.Token))
        {
            var phase = plan[phaseIndex]; var phaseElapsed = clock.Elapsed.TotalSeconds - phaseStart;
            Update(sample); InstructionText.Text = phase.Instruction;
            CountdownText.Text = $"{phase.Label.Replace('_', ' ').ToUpperInvariant()} · {Math.Max(0, phase.Seconds - (int)phaseElapsed)} sn";
            ProgressText.Text = $"Toplam {Math.Min(300, (int)clock.Elapsed.TotalSeconds)} / 300 sn · {samples:N0} örnek";
            await writer.WriteLineAsync(JsonSerializer.Serialize(new { elapsedMs = clock.ElapsedMilliseconds, label = phase.Label, placement = "calf_lower_leg", sample }, new JsonSerializerOptions { IncludeFields = true }));
            samples++;
            if (samples % 200 == 0) await writer.FlushAsync();
            if (phaseElapsed < phase.Seconds) continue;
            phaseStart += phase.Seconds; phaseIndex++; System.Media.SystemSounds.Asterisk.Play();
            if (phaseIndex >= plan.Length) break;
        }
        await writer.FlushAsync();
        if (!isFoundation)
        {
            var learned = await new PsMoveTrainingAnalyzer().AnalyzeAsync(root);
            await PsMoveTrainingAnalyzer.SaveAsync(learned, TrainingProfile);
        }
        _stage = 3; StateText.Text = "KAYIT TAMAMLANDI"; InstructionText.Text = isFoundation ? "5 dakikalık temel Move kaydı alındı" : "Move eğitim seti 10 dakikaya ulaştı";
        DetailText.Text = isFoundation ? "Sabit, yavaş, doğal ve hızlı yerinde yürüyüş tek zaman çizelgesinde etiketlendi." : "Başla–dur geçişleri ve yürüyüş olmayan bacak hareketleri ayrı etiketlerle kaydedildi.";
        CountdownText.Text = isFoundation ? "✓ TEMEL VERİ KAYDEDİLDİ" : "✓ AYRIŞTIRMA VERİSİ KAYDEDİLDİ"; ProgressText.Text = $"{samples:N0} örnek · {Path.GetFileName(folder)}";
        ActionButton.Content = "TAMAMLANDI"; ActionButton.IsEnabled = false;
        System.Media.SystemSounds.Asterisk.Play();
    }

    private async Task ValidateLiveAsync()
    {
        var profile = JsonSerializer.Deserialize<PsMoveTrainingProfile>(await File.ReadAllTextAsync(TrainingProfile))
            ?? throw new InvalidDataException("Kişisel Move profili okunamadı.");
        var engine = new PsMoveGaitEngine(profile); await using var source = new PsMoveSensorSource(Assignments, FactoryCalibration);
        await source.StartAsync(); var clock = Stopwatch.StartNew(); long active = 0, samples = 0;
        StateText.Text = "CANLI · VR KAPALI";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(48));
        await foreach (var sample in source.Samples.ReadAllAsync(timeout.Token))
        {
            engine.Observe(sample); var gait = engine.Update(sample.Timestamp.MonotonicTicks); Update(sample); samples++; if (gait.TargetSpeed > 0) active++;
            var s = clock.Elapsed.TotalSeconds;
            (InstructionText.Text, DetailText.Text) = s switch
            {
                < 8 => ("Sabit dur", "Hareket göstergesi KAPALI kalmalı"),
                < 23 => ("Doğal hızda yerinde yürü", "Hareket göstergesi AÇIK olmalı"),
                < 30 => ("Hemen dur", "Gösterge kısa sürede KAPALI olmalı"),
                < 38 => ("Yürümeden dizlerini bük", "Gösterge KAPALI kalmalı"),
                _ => ("Yürümeden sağa ve sola dön", "Gösterge KAPALI kalmalı")
            };
            CountdownText.Text = $"{(gait.TargetSpeed > 0 ? "● HAREKET AÇIK" : "○ HAREKET KAPALI")} · {Math.Max(0, 45 - (int)s)} sn";
            ProgressText.Text = $"Durum {gait.State} · hız {gait.TargetSpeed:0.00} · güven %{gait.Confidence * 100:0}";
            if (s >= 45) break;
        }
        _stage = 4; StateText.Text = "DOĞRULAMA TAMAM"; InstructionText.Text = "Canlı test tamamlandı";
        DetailText.Text = "VR çıkışı test boyunca kapalı kaldı. Sonuçlar bir sonraki entegrasyon kapısında kullanılacak.";
        CountdownText.Text = "✓ MOVE MODELİ ÇALIŞIYOR"; ProgressText.Text = $"{samples:N0} örnek · etkin örnek %{active * 100d / Math.Max(1, samples):0.0}";
        ActionButton.Content = "TAMAMLANDI"; ActionButton.IsEnabled = false;
        System.Media.SystemSounds.Asterisk.Play();
    }

    private void Update(PsMoveImuSample sample)
    {
        Dispatcher.Invoke(() =>
        {
            var value = sample.AngularVelocityRadps.Length();
            if (sample.Side == LegSide.Left) { LeftValue.Text = $"{value:0.00} rad/sn"; LeftBar.Value = Math.Min(4, value); }
            else { RightValue.Text = $"{value:0.00} rad/sn"; RightBar.Value = Math.Min(4, value); }
        });
    }

    private static Vector3 Mean(List<Vector3> values) => values.Aggregate(Vector3.Zero, (sum, value) => sum + value) / values.Count;
    private static double Noise(List<Vector3> values) { var mean = Mean(values); return Math.Sqrt(values.Average(x => Vector3.DistanceSquared(x, mean))); }
    private static double Percentile(List<double> values, double fraction) { values.Sort(); return values[(int)Math.Clamp(Math.Round((values.Count - 1) * fraction), 0, values.Count - 1)]; }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
