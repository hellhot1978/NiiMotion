using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NiiRMotion.Core;
using NiiRMotion.Infrastructure;

namespace NiiRMotion.App;

public partial class BoardLabWindow : Window
{
    private sealed record CalibrationPhase(double EndSecond, string Key, string Instruction);
    private sealed record CalibrationFrame(double ElapsedSeconds, string Phase, BalanceBoardSample Sample);
    private static readonly CalibrationPhase[] FullCalibrationPhases =
    [
        new(10, "standing", "ORTADA SABİT DUR"),
        new(25, "slow", "YAVAŞ YERİNDE YÜRÜ"),
        new(40, "natural", "DOĞAL YERİNDE YÜRÜ"),
        new(55, "fast", "HIZLI YERİNDE YÜRÜ"),
        new(62, "stop-1", "ŞİMDİ SABİT DUR"),
        new(77, "restart", "DOĞAL YÜRÜYÜŞE TEKRAR BAŞLA"),
        new(90, "turn-right", "AYAKLARINI OYNATARAK SAĞA DÖN"),
        new(103, "turn-left", "AYAKLARINI OYNATARAK SOLA DÖN"),
        new(113, "pace-change", "YAVAŞTAN HIZLIYA TEMPOYU ARTIR"),
        new(118, "stop-2", "ŞİMDİ SABİT DUR"),
        new(120, "exit", "ŞİMDİ KARTTAN İN")
    ];
    private CancellationTokenSource? _capture;
    public BoardLabWindow() => InitializeComponent();

    private async void StartClick(object sender, RoutedEventArgs e)
    {
        if (_capture is not null) return;
        var item = (ComboBoxItem)StageSelector.SelectedItem;
        var label = item.Tag?.ToString() ?? "standing-center";
        var fullCalibration = label == "full-calibration";
        _capture = new CancellationTokenSource(TimeSpan.FromSeconds(fullCalibration ? 155 : 25)); StartButton.IsEnabled = false; StageSelector.IsEnabled = false;
        StateText.Text = "KART SIFIRLANIYOR"; InstructionText.Text = "KART BOŞ KALSIN"; CounterText.Text = "…";
        try
        {
            await using var source = new BalanceBoardSensorSource(); await source.StartAsync(_capture.Token);
            _ = await source.Samples.ReadAsync(_capture.Token);
            if (label is "stop-exit" or "full-calibration")
            {
                System.Media.SystemSounds.Beep.Play();
                InstructionText.Text = "ŞİMDİ KARTA ÇIK";
                StateText.Text = "KART BEKLENİYOR";
                CounterText.Text = "↑";
                while (true)
                {
                    var contact = await source.Samples.ReadAsync(_capture.Token);
                    UpdateLive(contact, []);
                    if (contact.HasStableContact()) break;
                }
                InstructionText.Text = fullCalibration ? "TAM KALİBRASYONA HAZIRLAN" : "DOĞAL YÜRÜYÜŞE HAZIRLAN";
            }
            else
            {
                System.Media.SystemSounds.Beep.Play();
                InstructionText.Text = Instruction(label);
            }
            StateText.Text = "HAZIRLAN";
            for (var i = 3; i > 0; i--) { CounterText.Text = i.ToString(); await Task.Delay(1000, _capture.Token); }
            while (source.Samples.TryRead(out _)) { }
            var samples = new List<BalanceBoardSample>();
            var frames = new List<CalibrationFrame>();
            var captureStarted = Stopwatch.GetTimestamp();
            var durationSeconds = fullCalibration ? 120 : 8;
            var end = captureStarted + durationSeconds * Stopwatch.Frequency;
            var previousPhase = "";
            StateText.Text = fullCalibration ? "TAM KALİBRASYON" : "ÖLÇÜLÜYOR";
            while (Stopwatch.GetTimestamp() < end)
            {
                var sample = await source.Samples.ReadAsync(_capture.Token); samples.Add(sample); UpdateLive(sample, samples);
                var now = Stopwatch.GetTimestamp(); var remaining = Math.Max(0, (int)Math.Ceiling((end - now) / (double)Stopwatch.Frequency)); CounterText.Text = remaining.ToString();
                var elapsed = (now - captureStarted) / (double)Stopwatch.Frequency;
                if (fullCalibration)
                {
                    var phase = FullCalibrationPhases.FirstOrDefault(x => elapsed < x.EndSecond) ?? FullCalibrationPhases[^1];
                    InstructionText.Text = phase.Instruction;
                    frames.Add(new CalibrationFrame(elapsed, phase.Key, sample));
                    if (phase.Key != previousPhase)
                    {
                        if (previousPhase.Length > 0) System.Media.SystemSounds.Beep.Play();
                        previousPhase = phase.Key;
                    }
                }
                else if (label == "stop-exit")
                {
                    InstructionText.Text = elapsed < 4 ? "DOĞAL YERİNDE YÜRÜ" : elapsed < 6 ? "ŞİMDİ SABİT DUR" : "ŞİMDİ KARTTAN İN";
                }
            }
            var folder = NiiMotionPaths.UserBoardData;
            var path = Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + label + ".json");
            var payload = fullCalibration
                ? JsonSerializer.Serialize(new { type = "full-board-calibration-v1", durationSeconds, phases = FullCalibrationPhases, frames }, new JsonSerializerOptions { WriteIndented = false, IncludeFields = true })
                : JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = false, IncludeFields = true });
            File.WriteAllText(path, payload);
            InstructionText.Text = "TAMAMLANDI"; CounterText.Text = "✓"; StateText.Text = "KAYDEDİLDİ"; ResultText.Text = $"{samples.Count:N0} örnek kaydedildi · {Path.GetFileName(path)}"; System.Media.SystemSounds.Asterisk.Play();
        }
        catch (Exception ex) { StateText.Text = "HATA"; InstructionText.Text = "ÖLÇÜM ALINAMADI"; ResultText.Text = ex.Message; }
        finally { _capture?.Dispose(); _capture = null; StartButton.IsEnabled = true; StageSelector.IsEnabled = true; }
    }

    private void UpdateLive(BalanceBoardSample sample, List<BalanceBoardSample> samples)
    {
        if (!sample.HasStableContact())
        {
            WeightText.Text = "0.0 kg"; CopXText.Text = "0.00"; CopYText.Text = "0.00";
            TransitionText.Text = "0"; BodyShift.X = 0; PressureDot.Visibility = Visibility.Hidden;
            BoardLeftLeg.Opacity = .45; BoardRightLeg.Opacity = .45;
            return;
        }
        PressureDot.Visibility = Visibility.Visible;
        WeightText.Text = $"{sample.TotalKg:0.0} kg"; CopXText.Text = sample.CenterOfPressureX.ToString("+0.00;-0.00;0.00"); CopYText.Text = sample.CenterOfPressureY.ToString("+0.00;-0.00;0.00");
        var transitions = 0; for (var i = 1; i < samples.Count; i++) if (samples[i].HasStableContact() && Math.Sign(samples[i - 1].CenterOfPressureX) != Math.Sign(samples[i].CenterOfPressureX)) transitions++;
        TransitionText.Text = transitions.ToString(); BodyShift.X = sample.CenterOfPressureX * 28; PressureDot.RenderTransform = new TranslateTransform(sample.CenterOfPressureX * 125, -sample.CenterOfPressureY * 16);
        BoardLeftLeg.Opacity = Math.Clamp(.45 + sample.LeftKg / Math.Max(1, sample.TotalKg), .45, 1); BoardRightLeg.Opacity = Math.Clamp(.45 + sample.RightKg / Math.Max(1, sample.TotalKg), .45, 1);
    }
    private static string Instruction(string label) => label switch { "standing-center" => "ORTADA SABİT DUR", "slow-march" => "YAVAŞ YERİNDE YÜRÜ", "natural-march" => "DOĞAL YERİNDE YÜRÜ", "fast-march" => "HIZLI YERİNDE YÜRÜ", "turning" => "SIRAYLA SAĞA VE SOLA DÖN", _ => "KARTA ÇIK VE HAZIRLAN" };
    private void CloseClick(object sender, RoutedEventArgs e) { _capture?.Cancel(); Close(); }
}
