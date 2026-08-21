using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NiiRMotion.Core;
using NiiRMotion.Infrastructure;

namespace NiiRMotion.App;
public partial class PhoneLabWindow : Window
{
    public event EventHandler<string>? PhoneConnected;
    private readonly DispatcherTimer _guide = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly System.Diagnostics.Stopwatch _clock = new(); private readonly object _writeLock = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true };
    private CancellationTokenSource? _lifetime; private StreamWriter? _writer; private long _samples, _phaseStarted; private int _phase; private string _label = "natural";
    public PhoneLabWindow() { InitializeComponent(); _guide.Tick += GuideTick; Closed += async (_, _) => await StopAsync(); }
    private async void ConnectClick(object sender, RoutedEventArgs e) { if (_lifetime is not null) return; _lifetime = new(); ConnectButton.IsEnabled = false; StatusText.Text = "owoTrack verisi bekleniyor…"; _clock.Restart(); _ = PumpAsync(_lifetime.Token); await Task.CompletedTask; }
    private async Task PumpAsync(CancellationToken token)
    {
        try { await using var source = new OwoTrackSensorSource(); await source.StartAsync(token); var announced = false; await foreach (var sample in source.Samples.ReadAllAsync(token)) { var body = PhoneMounting.ToBodyFrame(sample); await Dispatcher.InvokeAsync(() => { var endpoint = source.PhoneEndpoint?.ToString() ?? "telefon"; ConnectionText.Text = "✓ TELEFON BAĞLI"; ConnectionText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(114,225,194)); RecordButton.IsEnabled = true; ConnectButton.Content = "✓ BAĞLI"; InstructionText.Text = _writer is null ? "Kayıt türünü seç ve başlat" : InstructionText.Text; StatusText.Text = $"Canlı veri alınıyor: {endpoint}"; GyroValue.Text = body.AngularVelocityRadps.Length().ToString("0.00"); AccelValue.Text = body.AccelerationMps2.Length().ToString("0.00"); if (!announced) { announced = true; PhoneConnected?.Invoke(this, endpoint); } }); lock (_writeLock) if (_writer is not null) { _writer.WriteLine(JsonSerializer.Serialize(new { mounting = PhoneMounting.Id, bodyAccelerationMps2 = body.AccelerationMps2, bodyAngularVelocityRadps = body.AngularVelocityRadps, sample }, JsonOptions)); _samples++; if (_samples % 90 == 0) _writer.Flush(); } } }
        catch (OperationCanceledException) { } catch (Exception ex) { await Dispatcher.InvokeAsync(() => StatusText.Text = ex.Message); }
    }
    private void RecordClick(object sender, RoutedEventArgs e)
    {
        if (_writer is not null) { FinishRecording(false); return; } _label = (LabelSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "natural";
        var folder = Path.Combine(@"C:\NiirMotion\data\user-phone", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + _label); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "session.json"), JsonSerializer.Serialize(new { label = _label, movement = "walk_in_place", sensor = "phone_only", orientation = PhoneMounting.Id, placement = "chest_center", startedAt = DateTimeOffset.Now, output = "disabled" }, new JsonSerializerOptions { WriteIndented = true }));
        _writer = new StreamWriter(Path.Combine(folder, "phone.jsonl")); _samples = 0; _phase = 0; _phaseStarted = _clock.ElapsedMilliseconds; RecordButton.Content = "■  KAYDI BİTİR"; RecordInfo.Text = $"Kaydediliyor: {_label}"; System.Media.SystemSounds.Beep.Play(); _guide.Start();
    }
    private void GuideTick(object? sender, EventArgs e)
    {
        if (_writer is null) { _guide.Stop(); return; } if (_label == "stand") { Timed("HAREKETSİZ DUR", 15, true); return; }
        var durations = _label == "stop" ? new[] { 5, 10, 5, 10, 5, 10, 5 } : new[] { 5, 30, 5 }; var walking = _phase % 2 == 1;
        var title = walking ? _label switch { "slow" => "YAVAŞ YÜRÜ", "fast" => "HIZLI YÜRÜ", "stop" => "OLDUĞUN YERDE YÜRÜ", _ => "DOĞAL YÜRÜ" } : "SABİT DUR"; Timed(title, durations[_phase], _phase == durations.Length - 1);
    }
    private void Timed(string title, int seconds, bool final) { var elapsed = (int)((_clock.ElapsedMilliseconds - _phaseStarted) / 1000); InstructionText.Text = title; CounterText.Text = Math.Max(0, seconds - elapsed).ToString(); if (elapsed < seconds) return; if (final) { FinishRecording(true); return; } _phase++; _phaseStarted = _clock.ElapsedMilliseconds; System.Media.SystemSounds.Beep.Play(); }
    private void FinishRecording(bool automatic) { _guide.Stop(); var wasRecording = _writer is not null; lock (_writeLock) { _writer?.Flush(); _writer?.Dispose(); _writer = null; } if (!wasRecording) return; InstructionText.Text = _samples > 0 ? "TAMAMLANDI" : "KAYIT BAŞARISIZ"; CounterText.Text = _samples > 0 ? "✓" : "!"; RecordButton.Content = "●  KAYDI BAŞLAT"; RecordInfo.Text = _samples > 0 ? $"KAYIT DOĞRULANDI · {_samples:N0} telefon örneği" : "Telefon verisi alınamadı."; if (automatic) System.Media.SystemSounds.Asterisk.Play(); }
    private async Task StopAsync() { FinishRecording(false); _lifetime?.Cancel(); _lifetime?.Dispose(); _lifetime = null; await Task.CompletedTask; }
    private async void CloseClick(object sender, RoutedEventArgs e) { await StopAsync(); Close(); }
}
