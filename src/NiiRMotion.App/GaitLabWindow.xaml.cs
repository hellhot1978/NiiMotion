using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NiiRMotion.Core;
using NiiRMotion.Infrastructure;

namespace NiiRMotion.App;

public partial class GaitLabWindow : Window
{
    private CancellationTokenSource? _liveCts;
    private readonly object _fileLock = new();
    private StreamWriter? _writer;
    private StreamWriter? _phoneWriter;
    private readonly Stopwatch _clock = new();
    private double _left, _right;
    private bool _leftHigh, _rightHigh;
    private JoyConSide? _lastStep;
    private int _steps;
    private long _lastStepMs = -1000;
    private string? _recordFolder;
    private long _recordSamples;
    private long _phoneRecordSamples;
    private bool _phoneConnected;
    private readonly object _fusionLock = new();
    private SensorFusionEngine? _previewFusion;
    private StreamWriter? _diagnosticWriter;
    private readonly DispatcherTimer _guideTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private int _guidePhase, _guideStartSteps;
    private long _guidePhaseStartedMs;
    private string _guideKind = "";
    private string _recordLabel = "natural";
    private bool _learningSession;
    private bool _learningCompleted;
    private int _learningPart;
    private int _learningPhase;
    private long _learningPhaseStartedMs;
    private string _activityTag = "natural_walk";
    private sealed record LearningPhase(int Seconds, string Tag, string Instruction, bool Walking);
    private static readonly LearningPhase[][] FifteenMinutePlan =
    [
        [new(60,"stand","1 DAKİKA SABİT DUR",false),new(240,"natural_walk","4 DAKİKA DOĞAL YÜRÜ",true),new(60,"stand","1 DAKİKA SABİT DUR",false),new(240,"natural_walk","4 DAKİKA DOĞAL YÜRÜ",true),new(60,"stand","1 DAKİKA SABİT DUR",false),new(240,"natural_walk","4 DAKİKA DOĞAL YÜRÜ",true)],
        [new(60,"stand","SABİT DUR",false),new(180,"slow_walk","YAVAŞ YÜRÜ",true),new(60,"stand","SABİT DUR",false),new(180,"natural_walk","DOĞAL YÜRÜ",true),new(60,"stand","SABİT DUR",false),new(180,"fast_walk","HIZLI YÜRÜ",true),new(180,"pace_changes","HIZINI KADEMELİ DEĞİŞTİR",true)],
        [new(900,"start_stop","10 SN YÜRÜ · 5 SN DUR · TEKRARLA",true)],
        [new(120,"stand","SABİT DUR",false),new(180,"bend_no_walk","DİZLERİ BÜK · YÜRÜME",false),new(180,"crouch_no_walk","ÇÖMELİP DOĞRUL · YÜRÜME",false),new(120,"single_leg_hold","TEK BACAĞI KALDIRIP BEKLE",false),new(120,"reach_no_walk","EĞİLİP YERDEN EŞYA ALIR GİBİ YAP",false),new(180,"natural_walk","DOĞAL YÜRÜ",true)],
        [new(120,"stand","SABİT DUR",false),new(180,"turn_no_walk","OLDUĞUN YERDE SAĞA-SOLA DÖN",false),new(180,"side_lean_no_walk","SAĞA-SOLA EĞİL · YÜRÜME",false),new(180,"look_reach_no_walk","BAK · UZAN · EĞİL · YÜRÜME",false),new(240,"natural_walk","DOĞAL YÜRÜ",true)],
        [new(120,"stand","SABİT DUR",false),new(180,"combat_stance_no_walk","SAVUNMA/EĞİLME HAREKETLERİ · YÜRÜME",false),new(180,"pickup_no_walk","YERDEN EŞYA ALMA HAREKETLERİ",false),new(180,"interact_no_walk","SABİT OYUN ETKİLEŞİMLERİ",false),new(240,"natural_walk","DOĞAL YÜRÜ",true)],
        [new(900,"mixed_labeled","EKRANDAKİ 10 SN YÜRÜ · 5 SN DUR DÖNGÜSÜNÜ UYGULA",true)],
        [new(900,"validation_free_play","DOĞAL OYUN HAREKETLERİNİ YAP · DOĞRULAMA",true)]
    ];
    private static readonly LearningPhase[][] LearningPlan = SplitIntoFiveMinuteParts(FifteenMinutePlan);
    private static readonly JsonSerializerOptions RecordingJson = new() { IncludeFields = true };

    private static LearningPhase[][] SplitIntoFiveMinuteParts(IEnumerable<LearningPhase[]> sessions)
    {
        var result = new List<LearningPhase[]>();
        foreach (var session in sessions)
        {
            var chunk = new List<LearningPhase>(); var room = 300;
            foreach (var phase in session)
            {
                var left = phase.Seconds;
                while (left > 0)
                {
                    var seconds = Math.Min(left, room); chunk.Add(phase with { Seconds = seconds });
                    left -= seconds; room -= seconds;
                    if (room == 0) { result.Add(chunk.ToArray()); chunk = []; room = 300; }
                }
            }
            if (chunk.Count > 0) result.Add(chunk.ToArray());
        }
        return result.ToArray();
    }

    public GaitLabWindow()
    {
        InitializeComponent();
        LearningButton.Content = "★  5 DK'LIK PARÇAYI BAŞLAT";
        foreach (var option in LabelSelector.Items.OfType<ComboBoxItem>())
            if (string.Equals(option.Tag?.ToString(), "stop", StringComparison.OrdinalIgnoreCase)) option.Content = "Hızlı yürü ve aniden dur";
        _guideTimer.Tick += GuideTick;
        Closed += async (_, _) => await StopAsync();
        UpdateLearningProgress();
    }

    private async void LiveClick(object sender, RoutedEventArgs e)
    {
        if (_liveCts is not null) { await StopAsync(); return; }
        var devices = HidDeviceEnumerator.FindJoyCons().GroupBy(x => x.Side).Select(x => x.First()).ToArray();
        if (!devices.Any(x => x.Side == JoyConSide.Left) || !devices.Any(x => x.Side == JoyConSide.Right)) { HintText.Text = "Sol ve sağ Joy-Con bağlı olmalı."; return; }
        var gaitPace = await PersonalGaitPace.LoadAsync(@"C:\NiirMotion\config\personal-gait-pace.json");
        var phonePace = await PersonalPhoneMotion.LoadAsync(@"C:\NiirMotion\config\personal-phone-motion.json");
        _previewFusion = new SensorFusionEngine(56, personalPace: gaitPace, phoneProfile: phonePace);
        StartDiagnosticLog();
        _liveCts = new(); _clock.Restart(); _steps = 0; _lastStep = null; _lastStepMs = -1000; _leftHigh = _rightHigh = false; LiveButton.Content = "■  TESTİ DURDUR"; LabState.Text = "CANLI · VR ÇIKIŞI YOK"; HintText.Text = "Hareketlerini animasyonda görebilirsin.";
        foreach (var device in devices) _ = PumpAsync(device, _liveCts.Token);
        _ = PumpPhoneAsync(_liveCts.Token);
        _ = ClockAsync(_liveCts.Token);
        await Task.CompletedTask;
    }

    private async Task PumpPhoneAsync(CancellationToken token)
    {
        try
        {
            await using var source = new OwoTrackSensorSource(); await source.StartAsync(token);
            await foreach (var sample in source.Samples.ReadAllAsync(token))
            {
                var body = PhoneMounting.ToBodyFrame(sample);
                _phoneConnected = true;
                lock (_fusionLock) _previewFusion?.ObservePhoneMotion(body.AngularVelocityRadps.Length(), body.AccelerationMps2.Length(), sample.Timestamp.MonotonicTicks, body.VerticalTurnRadps);
                lock (_fileLock) if (_phoneWriter is not null) { _phoneWriter.WriteLine(JsonSerializer.Serialize(new { elapsedMs = _clock.ElapsedMilliseconds, mounting = PhoneMounting.Id, bodyAccelerationMps2 = body.AccelerationMps2, bodyAngularVelocityRadps = body.AngularVelocityRadps, sample }, RecordingJson)); _phoneRecordSamples++; if (_phoneRecordSamples % 90 == 0) _phoneWriter.Flush(); }
                await Dispatcher.InvokeAsync(() => { PhoneFusionText.Text = $"Telefon bağlı · {source.PhoneEndpoint}"; PhoneFusionText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(114,225,194)); });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await Dispatcher.InvokeAsync(() => { PhoneFusionText.Text = $"Telefon yok · {ex.Message}"; PhoneFusionText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255,155,168)); }); }
    }

    private async Task PumpAsync(JoyConDeviceDescriptor device, CancellationToken token)
    {
        try
        {
            await using var source = new JoyConSensorSource(device); await source.StartAsync(token);
            await foreach (var sample in source.Samples.ReadAllAsync(token))
            {
                var strength = sample.AngularVelocityDps.Length();
                if (device.Side == JoyConSide.Left) _left = _left * .82 + strength * .18; else _right = _right * .82 + strength * .18;
                CountStep(device.Side, device.Side == JoyConSide.Left ? _left : _right);
                lock (_fusionLock) _previewFusion?.ObserveLeg(device.Side == JoyConSide.Left ? LegSide.Left : LegSide.Right, strength, sample.Timestamp.MonotonicTicks);
                WriteSample(device.Side, sample);
                await Dispatcher.InvokeAsync(UpdateVisuals);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            var failure = Dispatcher.InvokeAsync(() => FailJoyConAsync(device.Side, ex.Message));
            await failure.Task.Unwrap();
        }
    }

    private async Task FailJoyConAsync(JoyConSide side, string detail)
    {
        await StopAsync();
        var name = side == JoyConSide.Left ? "Sol" : "Sağ";
        InstructionText.Text = $"{name.ToUpperInvariant()} JOY-CON BAĞLANTISI KOPTU";
        InstructionCounter.Text = "KAYIT GEÇERSİZ";
        RecordInfo.Text = "Bu parça tamamlanmadı; aynı parça yeniden başlayacak.";
        HintText.Text = $"{name} Joy-Con'u bir düğmeyle uyandırıp yeniden bağlayın. {detail}";
        System.Media.SystemSounds.Hand.Play();
    }

    private void CountStep(JoyConSide side, double value)
    {
        ref var high = ref (side == JoyConSide.Left ? ref _leftHigh : ref _rightHigh);
        // Count one deliberate swing, not every IMU sub-sample or strap vibration.
        // Alternation + hysteresis + a 280 ms refractory period caps implausible cadence.
        var now = _clock.ElapsedMilliseconds;
        var slowMovement = _recordLabel == "slow" || _activityTag == "slow_walk";
        var startThreshold = slowMovement ? 72 : 130;
        var releaseThreshold = slowMovement ? 34 : 62;
        var refractoryMs = slowMovement ? 340 : 280;
        if (!high && value > startThreshold && _lastStep != side && now - _lastStepMs >= refractoryMs)
        {
            high = true; _lastStep = side; _lastStepMs = now; Interlocked.Increment(ref _steps);
        }
        else if (value < releaseThreshold) high = false;
    }

    private void UpdateVisuals()
    {
        LeftValue.Text = _left.ToString("0"); RightValue.Text = _right.ToString("0"); LeftBar.Value = Math.Min(250, _left); RightBar.Value = Math.Min(250, _right);
        var leftLift = Math.Clamp((_left - 18) / 175, 0, 1); var rightLift = Math.Clamp((_right - 18) / 175, 0, 1);
        SetLeg(LeftThigh, LeftShin, LeftShoe, 155, 143, 125, leftLift, 1);
        SetLeg(RightThigh, RightShin, RightShoe, 205, 217, 235, rightLift, -1);
        StepValue.Text = _steps.ToString();
    }

    private static void SetLeg(System.Windows.Shapes.Line thigh, System.Windows.Shapes.Line shin, FrameworkElement shoe, double hipX, double restKneeX, double restAnkleX, double lift, int direction)
    {
        var kneeX = restKneeX + direction * lift * 30; var kneeY = 282 - lift * 30;
        var ankleX = restAnkleX + direction * lift * 62; var ankleY = 348 - lift * 58;
        thigh.X2 = kneeX; thigh.Y2 = kneeY; shin.X1 = kneeX; shin.Y1 = kneeY; shin.X2 = ankleX; shin.Y2 = ankleY;
        Canvas.SetLeft(shoe, ankleX - (direction > 0 ? 34 : 28)); Canvas.SetTop(shoe, ankleY - 6);
    }

    private async Task ClockAsync(CancellationToken token)
    {
        try { while (!token.IsCancellationRequested) { await Task.Delay(100, token); FusionSnapshot? snapshot; lock (_fusionLock) snapshot = _previewFusion?.Update(Stopwatch.GetTimestamp()); if (snapshot is not null) { lock (_fileLock) { _diagnosticWriter?.WriteLine($"{_clock.ElapsedMilliseconds},{snapshot.Gait.State},{snapshot.TargetSpeed:0.000},{snapshot.GlobalConfidence:0.000},{snapshot.Gait.CadenceHz:0.000},{snapshot.Gait.StepCount},{_phoneConnected}"); if (_clock.ElapsedMilliseconds % 1000 < 110) _diagnosticWriter?.Flush(); } } await Dispatcher.InvokeAsync(() => { TimeValue.Text = _clock.Elapsed.ToString(@"mm\:ss"); FusionPreviewText.Text = $"HIZ {snapshot?.TargetSpeed ?? 0:0.00}"; }); } } catch (OperationCanceledException) { }
    }

    private void RecordClick(object sender, RoutedEventArgs e)
    {
        if (_liveCts is null) { HintText.Text = "Önce Canlı Test'i başlat."; return; }
        if (_writer is not null) { StopRecording(); return; }
        var label = (LabelSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "natural";
        _recordLabel = label;
        var folder = Path.Combine(@"C:\NiirMotion\data\user-gait", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + label); Directory.CreateDirectory(folder);
        _recordFolder = folder; _recordSamples = 0; _phoneRecordSamples = 0;
        _writer = new StreamWriter(Path.Combine(folder, "joycons.jsonl"));
        _phoneWriter = new StreamWriter(Path.Combine(folder, "phone.jsonl"));
        File.WriteAllText(Path.Combine(folder, "session.json"), JsonSerializer.Serialize(new { label, movement = "walk_in_place", sensors = "joycons_plus_phone", phoneConnectedAtStart = _phoneConnected, startedAt = DateTimeOffset.Now, output = "disabled", joyConPlacement = "outer_thighs", phonePlacement = "chest_center", phoneOrientation = PhoneMounting.Id }, new JsonSerializerOptions { WriteIndented = true }));
        RecordButton.Content = "■  KAYDI BİTİR"; RecordButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(113,48,68)); RecordInfo.Text = $"Kaydediliyor: {label}";
        if (label == "stop") StartGuidedStopTest();
        else if (label is "slow" or "natural" or "fast") StartGuidedPaceTest(label);
        else StartGuidedStandTest();
    }

    private void LearningClick(object sender, RoutedEventArgs e)
    {
        if (_liveCts is null) { HintText.Text = "Önce Canlı Test'i başlat."; return; }
        if (_writer is not null) { StopRecording(); return; }
        _learningPart = NextLearningPart();
        if (_learningPart >= LearningPlan.Length) { InstructionText.Text = "24 PARÇA TAMAMLANDI"; InstructionCounter.Text = "✓"; return; }
        _learningSession = true; _learningCompleted = false; _learningPhase = 0; _recordLabel = $"learning-{_learningPart + 1}";
        var folder = Path.Combine(@"C:\NiirMotion\data\user-gait\joycon-learning", $"part-{_learningPart + 1}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(folder); _recordFolder = folder; _recordSamples = 0; _phoneRecordSamples = 0;
        _writer = new StreamWriter(Path.Combine(folder, "joycons.jsonl"));
        File.WriteAllText(Path.Combine(folder, "session.json"), JsonSerializer.Serialize(new { programVersion = 2, part = _learningPart + 1, totalParts = 24, durationMinutes = 5, sensors = "joycons_only", purpose = _learningPart >= 21 ? "validation" : "training", startedAt = DateTimeOffset.Now, output = "disabled", joyConPlacement = "outer_thighs" }, new JsonSerializerOptions { WriteIndented = true }));
        LearningButton.Content = "■  BU PARÇAYI BİTİR"; RecordButton.IsEnabled = false; LabelSelector.IsEnabled = false;
        _learningPhaseStartedMs = _clock.ElapsedMilliseconds; ApplyLearningPhase(); _guideTimer.Start(); System.Media.SystemSounds.Beep.Play();
    }

    private void WriteSample(JoyConSide side, JoyConImuSample sample)
    {
        lock (_fileLock) if (_writer is not null) { _writer.WriteLine(JsonSerializer.Serialize(new { side = side.ToString(), elapsedMs = _clock.ElapsedMilliseconds, activity = _activityTag, sample }, RecordingJson)); _recordSamples++; if (sample.Sequence % 90 == 0) _writer.Flush(); }
    }

    private void StopRecording()
    {
        var wasRecording = _writer is not null;
        _guideTimer.Stop();
        lock (_fileLock) { _writer?.Flush(); _writer?.Dispose(); _writer = null; _phoneWriter?.Flush(); _phoneWriter?.Dispose(); _phoneWriter = null; }
        RecordButton.Content = "●  KAYDI BAŞLAT";
        if (_learningSession && _learningCompleted && wasRecording && _recordSamples > 0) MarkLearningPartComplete();
        _learningSession = false; LearningButton.Content = "★  5 DK'LIK PARÇAYI BAŞLAT"; RecordButton.IsEnabled = true; LabelSelector.IsEnabled = true; UpdateLearningProgress();
        if (wasRecording) RecordInfo.Text = _recordSamples > 0
            ? $"KAYIT DOĞRULANDI · Joy-Con {_recordSamples:N0} · Telefon {_phoneRecordSamples:N0}"
            : "KAYIT BAŞARISIZ · Joy-Con örneği alınamadı";
    }

    private void StartGuidedStopTest()
    {
        _guideKind = "stop"; _guidePhase = 0; _guideStartSteps = _steps; _guidePhaseStartedMs = _clock.ElapsedMilliseconds;
        _activityTag = "sudden_stop_stand"; InstructionText.Text = "SABİT DUR"; InstructionCounter.Text = "5";
        System.Media.SystemSounds.Beep.Play(); _guideTimer.Start();
    }

    private void StartGuidedPaceTest(string pace)
    {
        _guideKind = pace; _guidePhase = 0; _guideStartSteps = _steps; _guidePhaseStartedMs = _clock.ElapsedMilliseconds;
        _activityTag = "stand"; InstructionText.Text = "SABİT DUR"; InstructionCounter.Text = "5";
        System.Media.SystemSounds.Beep.Play(); _guideTimer.Start();
    }

    private void StartGuidedStandTest()
    {
        _guideKind = "stand"; _guidePhase = 0; _guidePhaseStartedMs = _clock.ElapsedMilliseconds;
        InstructionText.Text = "HAREKETSİZ DUR"; InstructionCounter.Text = "15";
        System.Media.SystemSounds.Beep.Play(); _guideTimer.Start();
    }

    private void GuideTick(object? sender, EventArgs e)
    {
        if (_writer is null) { _guideTimer.Stop(); return; }
        if (_learningSession) { LearningTick(); return; }
        if (_guideKind == "stand")
        {
            var remaining = 15 - (int)((_clock.ElapsedMilliseconds - _guidePhaseStartedMs) / 1000);
            InstructionText.Text = "HAREKETSİZ DUR"; InstructionCounter.Text = Math.Max(0, remaining).ToString();
            if (remaining <= 0) CompleteGuidedRecording();
            return;
        }
        if (_guideKind != "stop")
        {
            GuidePaceTick();
            return;
        }
        if (_guidePhase % 2 == 0)
        {
            _activityTag = "sudden_stop_stand";
            var remaining = 5 - (int)((_clock.ElapsedMilliseconds - _guidePhaseStartedMs) / 1000);
            InstructionText.Text = "SABİT DUR"; InstructionCounter.Text = Math.Max(0, remaining).ToString();
            if (remaining <= 0)
            {
                if (_guidePhase == 6) { CompleteGuidedRecording(); return; }
                _guidePhase++; _guideStartSteps = _steps; _activityTag = "sudden_stop_fast_walk"; System.Media.SystemSounds.Beep.Play();
            }
        }
        else
        {
            var count = Math.Max(0, _steps - _guideStartSteps);
            _activityTag = "sudden_stop_fast_walk"; InstructionText.Text = "HIZLI YÜRÜ · DUR SESİNDE DON"; InstructionCounter.Text = $"{Math.Min(10, count)} / 10";
            if (count >= 10) { _guidePhase++; _guidePhaseStartedMs = _clock.ElapsedMilliseconds; _activityTag = "sudden_stop_stand"; System.Media.SystemSounds.Beep.Play(); }
        }
    }

    private void LearningTick()
    {
        var phases = LearningPlan[_learningPart];
        var phase = phases[_learningPhase];
        var elapsed = (int)((_clock.ElapsedMilliseconds - _learningPhaseStartedMs) / 1000);
        var remaining = Math.Max(0, phase.Seconds - elapsed);
        var totalElapsed = phases.Take(_learningPhase).Sum(x => x.Seconds) + elapsed;
        InstructionText.Text = phase.Instruction;
        InstructionCounter.Text = $"{remaining / 60:00}:{remaining % 60:00}  ·  Toplam {totalElapsed / 60:00}:{totalElapsed % 60:00}";
        if (remaining > 0) return;
        _learningPhase++;
        if (_learningPhase >= phases.Length) { _learningCompleted = true; CompleteGuidedRecording(); return; }
        _learningPhaseStartedMs = _clock.ElapsedMilliseconds; ApplyLearningPhase(); System.Media.SystemSounds.Beep.Play();
    }

    private void ApplyLearningPhase()
    {
        var phase = LearningPlan[_learningPart][_learningPhase]; _activityTag = phase.Tag;
        InstructionText.Text = phase.Instruction;
    }

    private static string LearningProgressPath => @"C:\NiirMotion\data\user-gait\joycon-learning\progress-v2.json";
    private int NextLearningPart()
    {
        try { if (File.Exists(LearningProgressPath)) return Math.Clamp(JsonSerializer.Deserialize<int[]>(File.ReadAllText(LearningProgressPath))?.Length ?? 0, 0, 24); } catch { }
        return 0;
    }
    private void MarkLearningPartComplete()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LearningProgressPath)!);
        int[] completed = []; try { if (File.Exists(LearningProgressPath)) completed = JsonSerializer.Deserialize<int[]>(File.ReadAllText(LearningProgressPath)) ?? []; } catch { }
        if (!completed.Contains(_learningPart + 1)) File.WriteAllText(LearningProgressPath, JsonSerializer.Serialize(completed.Append(_learningPart + 1).Order().ToArray()));
    }
    private void UpdateLearningProgress()
    {
        var next = NextLearningPart(); LearningProgress.Text = next >= 24 ? "24/24 tamamlandı · son analiz hazır" : next >= 21 ? $"Sonraki: {next + 1}/24 · doğrulama · 5 dk" : $"Sonraki: {next + 1}/24 · 5 dk · sonra analiz ve dinlenme";
    }

    private void GuidePaceTick()
    {
        if (_guidePhase is 0 or 2)
        {
            _activityTag = "stand";
            var remaining = 5 - (int)((_clock.ElapsedMilliseconds - _guidePhaseStartedMs) / 1000);
            InstructionText.Text = "SABİT DUR"; InstructionCounter.Text = Math.Max(0, remaining).ToString();
            if (remaining <= 0)
            {
                if (_guidePhase == 2) { CompleteGuidedRecording(); return; }
                _guidePhase = 1; _guideStartSteps = _steps; _activityTag = _guideKind + "_walk"; System.Media.SystemSounds.Beep.Play();
            }
            return;
        }
        _activityTag = _guideKind + "_walk"; var count = Math.Max(0, _steps - _guideStartSteps);
        InstructionText.Text = _guideKind switch { "slow" => "YAVAŞ YÜRÜ", "fast" => "HIZLI YÜRÜ", _ => "DOĞAL YÜRÜ" };
        InstructionCounter.Text = $"{Math.Min(30, count)} / 30";
        if (count >= 30) { _guidePhase = 2; _guidePhaseStartedMs = _clock.ElapsedMilliseconds; System.Media.SystemSounds.Beep.Play(); }
    }

    private void CompleteGuidedRecording()
    {
        InstructionText.Text = "TAMAMLANDI"; InstructionCounter.Text = "✓";
        System.Media.SystemSounds.Asterisk.Play(); StopRecording();
    }
    private void StartDiagnosticLog()
    {
        var folder = @"C:\NiirMotion\logs\gait-lab"; Directory.CreateDirectory(folder);
        foreach (var old in new DirectoryInfo(folder).GetFiles("*.csv").OrderByDescending(x => x.LastWriteTimeUtc).Skip(19)) try { old.Delete(); } catch { }
        _diagnosticWriter = new StreamWriter(Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv"));
        _diagnosticWriter.WriteLine("elapsed_ms,state,target_speed,confidence,cadence_hz,steps,phone_connected");
    }
    private async Task StopAsync() { StopRecording(); _liveCts?.Cancel(); _liveCts?.Dispose(); _liveCts = null; lock (_fileLock) { _diagnosticWriter?.Flush(); _diagnosticWriter?.Dispose(); _diagnosticWriter = null; } _previewFusion = null; _clock.Stop(); FusionPreviewText.Text = "HIZ 0.00"; LiveButton.Content = "▶  CANLI TEST"; LabState.Text = "HAZIR"; await Task.CompletedTask; }
    private async void CloseClick(object sender, RoutedEventArgs e) { await StopAsync(); Close(); }
    private void OpenBoardLabClick(object sender, RoutedEventArgs e) => new BoardLabWindow { Owner = this }.ShowDialog();
}
