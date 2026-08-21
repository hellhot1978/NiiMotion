using System.Diagnostics;
using System.Numerics;
using System.Windows;
using NiiRMotion.Core;
using NiiRMotion.Infrastructure;

namespace NiiRMotion.App;

public partial class PsMoveLabWindow : Window
{
    private const string Assignments = @"C:\NiirMotion\config\psmove-assignments.json";
    private const string FactoryCalibration = @"C:\NiirMotion\config\psmove-calibrations.json";
    private const string PlacementCalibration = @"C:\NiirMotion\config\personal-psmove-placement.json";
    private int _stage;
    private bool _busy;

    public PsMoveLabWindow() => InitializeComponent();

    private async void ActionClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true; ActionButton.IsEnabled = false;
        try
        {
            if (_stage == 0) await VerifyAsync();
            else if (_stage == 1) await CalibrateAsync();
        }
        catch (Exception ex)
        {
            StateText.Text = "KONTROL GEREKİYOR"; StateText.Foreground = System.Windows.Media.Brushes.LightPink;
            InstructionText.Text = "Kalibrasyon tamamlanamadı"; DetailText.Text = ex.Message;
        }
        finally { _busy = false; ActionButton.IsEnabled = true; }
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
