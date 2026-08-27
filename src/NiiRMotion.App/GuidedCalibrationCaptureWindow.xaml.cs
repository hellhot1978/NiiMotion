using System.Windows;
using NiiRMotion.Core;
using NiiRMotion.Infrastructure;

namespace NiiRMotion.App;

public partial class GuidedCalibrationCaptureWindow : Window
{
    private readonly SensorFamily _sensor; private readonly int _phase; private readonly TimeSpan _duration; private readonly CancellationTokenSource _cancel = new(); private volatile bool _paused; private TimeSpan _elapsed;
    public GuidedCalibrationResult? Result { get; private set; }
    public GuidedCalibrationCaptureWindow(SensorFamily sensor, int phase, TimeSpan duration)
    {
        _sensor = sensor; _phase = phase; _duration = duration; InitializeComponent(); HeaderText.Text = $"{Display(sensor)} · Faz {phase}"; OverallProgress.Maximum = duration.TotalSeconds; MoveLightPanel.Visibility = sensor == SensorFamily.PsMove ? Visibility.Visible : Visibility.Collapsed; UpdateGuidance();
        Loaded += async (_, _) => await BeginAsync(); Closing += (_, _) => { if (Result is null) _cancel.Cancel(); };
    }
    private async Task BeginAsync()
    {
        var progress = new Progress<TimeSpan>(elapsed => { _elapsed = elapsed; UpdateGuidance(); });
        try
        {
            RecordStateText.Text = "● KAYIT SÜRÜYOR"; StatusText.Text = "Sensör verisi güvenli kayıt klasörüne yazılıyor.";
            Result = await new GuidedCalibrationRecorder().RecordAsync(_sensor, _phase, _duration, progress, isPaused: () => _paused, cancellationToken: _cancel.Token);
            RecordStateText.Text = "✓ KAYIT TAMAMLANDI"; InstructionText.Text = "Faz tamamlandı"; InstructionDetail.Text = $"{Result.TotalSamples:N0} sensör örneği alındı. Kalite kontrolü uygulanacak."; PauseButton.Visibility = Visibility.Collapsed; CancelButton.Content = "TAMAM"; StatusText.Text = "Bu pencereyi kapatarak kalite sonucuna dönebilirsin.";
        }
        catch (OperationCanceledException) { if (IsVisible) Close(); }
        catch (Exception ex) { RecordStateText.Text = "! KAYIT TAMAMLANAMADI"; RecordStateText.Foreground = MainWindow.Brush("#FF8AA5"); InstructionText.Text = "Sensör akışı kesildi"; InstructionDetail.Text = ex.GetBaseException().Message; PauseButton.Visibility = Visibility.Collapsed; CancelButton.Content = "KAPAT"; }
    }
    private void PauseClick(object sender, RoutedEventArgs e) { _paused = !_paused; PauseButton.Content = _paused ? "▶  DEVAM ET" : "Ⅱ  DURAKLAT"; RecordStateText.Text = _paused ? "Ⅱ DURAKLATILDI" : "● KAYIT SÜRÜYOR"; StatusText.Text = _paused ? "Sayaç ve veri kaydı durdu. Hazır olduğunda devam et." : "Kayıt kaldığı aktif süreden devam ediyor."; }
    private async void LeftMoveLightClick(object sender, RoutedEventArgs e) => await IdentifyMoveAsync(LegSide.Left, LeftMoveLightButton);
    private async void RightMoveLightClick(object sender, RoutedEventArgs e) => await IdentifyMoveAsync(LegSide.Right, RightMoveLightButton);
    private async Task IdentifyMoveAsync(LegSide side, System.Windows.Controls.Button button)
    {
        button.IsEnabled = false;
        try
        {
            var assignments = await new PsMoveAssignmentStore(NiiMotionPaths.PsMoveAssignments).LoadAsync();
            if (assignments is not { IsComplete: true }) throw new InvalidOperationException("Önce PS Move sol/sağ eşleştirmesini tamamla.");
            StatusText.Text = side == LegSide.Left ? "Sol Move 8 saniye kırmızı yanıyor." : "Sağ Move 8 saniye mavi yanıyor.";
            await new PsMoveDiagnosticsService().ShowAssignedControllerColorAsync(assignments, side, TimeSpan.FromSeconds(8));
            StatusText.Text = _paused ? "Kayıt duraklatıldı." : "Kayıt sürüyor.";
        }
        catch (Exception ex) { StatusText.Text = "Move ışığı yakılamadı: " + ex.GetBaseException().Message; }
        finally { button.IsEnabled = true; }
    }
    private void CancelClick(object sender, RoutedEventArgs e) { if (Result is not null) { DialogResult = true; Close(); return; } if (UiLocalization.ShowMessage(this, "Bu faz iptal edilsin mi? Tamamlanmamış kayıt kullanılmayacak.", "Kalibrasyonu iptal et", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) { _cancel.Cancel(); Close(); } }
    private void UpdateGuidance()
    {
        var steps = Schedule(_sensor, _phase); var seconds = Math.Min(_duration.TotalSeconds, _elapsed.TotalSeconds); var step = steps.FirstOrDefault(x => seconds >= x.Start && seconds < x.End) ?? steps[^1]; var remaining = TimeSpan.FromSeconds(Math.Max(0, step.End - seconds));
        InstructionText.Text = UiLocalization.Text(step.Title); InstructionDetail.Text = UiLocalization.Text(step.Detail); PoseLabel.Text = UiLocalization.Text(step.Pose); StepRemainingText.Text = remaining.ToString(@"mm\:ss"); NextStepText.Text = UiLocalization.Text(steps.SkipWhile(x => x != step).Skip(1).FirstOrDefault()?.Title ?? "Faz tamamlanacak"); OverallProgress.Value = seconds; ElapsedText.Text = _elapsed.ToString(@"mm\:ss"); AnimatePose(step.Pose, seconds);
    }
    private void AnimatePose(string pose, double seconds)
    {
        var swing = Math.Sin(seconds * (pose == "HIZLI YÜRÜ" ? 7 : pose.Contains("YÜRÜ") ? 4 : 0)) * (pose.Contains("YÜRÜ") ? 42 : 0); LeftLeg.X2 = 125 + swing; RightLeg.X2 = 275 - swing; LeftArm.X2 = 80 - swing * .45; RightArm.X2 = 320 + swing * .45;
        Body.RenderTransform = pose == "EĞİL" ? new System.Windows.Media.RotateTransform(14, 60, 80) : System.Windows.Media.Transform.Identity;
    }
    private static Step[] Schedule(SensorFamily sensor, int phase)
    {
        if (sensor == SensorFamily.BalanceBoard) return phase switch
        {
            1 => [new(0,30,"Kartı tamamen boş bırak","Henüz karta çıkma; boş ağırlık tabanı ölçülüyor.","KART BOŞ"),new(30,45,"Şimdi kartın üzerine çık","Ayaklarını sağ ve sol işaretlere yerleştir.","KARTA ÇIK"),new(45,150,"Ortada doğal dur","Ağırlığını iki ayağa dengeli ver.","ORTADA DUR"),new(150,190,"Ağırlığını hafifçe sola ver","Ayağını kaldırmadan kontrollü biçimde sola yaslan.","SOLA EĞİL"),new(190,230,"Ağırlığını hafifçe sağa ver","Ayağını kaldırmadan kontrollü biçimde sağa yaslan.","SAĞA EĞİL"),new(230,270,"Tekrar ortala","Rahat ve dengeli dur.","ORTADA DUR"),new(270,300,"Karttan in","Kartı tekrar tamamen boş bırak.","KART BOŞ")],
            2 => [new(0,20,"Kart boş","Başlangıç sıfırı alınıyor.","KART BOŞ"),new(20,35,"Karta çık ve ortala","İki ayağını yerleştir.","KARTA ÇIK"),new(35,120,"Doğal yerinde yürü","Kartın üzerinden inmeden küçük alternatif adımlar yap.","DOĞAL YÜRÜ"),new(120,145,"Hemen dur ve ortala","İki ayağınla sabit kal.","ORTADA DUR"),new(145,230,"Yeniden doğal yürü","Küçük ve düzenli adımlara dön.","DOĞAL YÜRÜ"),new(230,260,"Öne ve arkaya hafif ağırlık ver","Ayağını kaldırmadan kontrollü hareket et.","DENGE"),new(260,285,"Ortada dur","Tamamen sabit kal.","ORTADA DUR"),new(285,300,"Karttan in","Faz kart boşken bitecek.","KART BOŞ")],
            _ => [new(0,20,"Kart boş","Başlangıç sıfırı.","KART BOŞ"),new(20,35,"Karta çık","Ortada dengelen.","KARTA ÇIK"),new(35,90,"Yavaş yerinde yürü","Küçük alternatif basınç adımları.","YAVAŞ YÜRÜ"),new(90,150,"Doğal yerinde yürü","Rahat oyun ritmin.","DOĞAL YÜRÜ"),new(150,200,"Hızlı yerinde yürü","Güvenli ve belirgin hızlı adımlar.","HIZLI YÜRÜ"),new(200,225,"Hemen dur","Ortada sabit kal.","ORTADA DUR"),new(225,255,"Sola ve sağa ağırlık ver","Dönüş niyeti için kontrollü yaslan.","DENGE"),new(255,285,"Ortada son duruş","Tamamen sabit kal.","ORTADA DUR"),new(285,300,"Karttan in","Kart boşken faz tamamlanacak.","KART BOŞ")]
        };
        if (sensor == SensorFamily.Phone && phase == 3) return [new(0,25,"Sabit ve dik dur","Telefon göğüste yatay; ekran sana, üst kenar sola bakmalı.","SABİT DUR"),new(25,95,"Doğal yerinde yürü","Gövdeni gereksiz sallamadan rahat yürü.","DOĞAL YÜRÜ"),new(95,125,"Hemen dur","Gövdeni de sabitle.","SABİT DUR"),new(125,180,"Hızlı yerinde yürü","Güvenli hızlı ritim.","HIZLI YÜRÜ"),new(180,210,"Hemen dur","Telefonla birlikte tamamen sabit kal.","SABİT DUR"),new(210,245,"Gövdeni hafifçe öne ve geriye eğ","Belden küçük, kontrollü hareket yap.","EĞİL"),new(245,275,"Gövdeni sağa ve sola çevir","Ayakların yerinde; yalnız dönüş örneği ver.","DÖN"),new(275,300,"Son sabit duruş","Rahat ve dik kal.","SABİT DUR")];
        return phase switch
    {
        1 => [new(0,30,"Sabit ve rahat dur","Ayakların yerde; dizlerin kilitli olmasın.","SABİT DUR"),new(30,150,"Yavaşça yerinde yürü","Olduğun yerden ayrılmadan küçük, düzenli adımlar at.","YAVAŞ YÜRÜ"),new(150,180,"Dur ve sabit kal","Ayaklarını yere koy; tamamen hareketsiz kal.","SABİT DUR"),new(180,270,"Yavaş yürüyüşe devam et","Aynı doğal küçük adımları sürdür.","YAVAŞ YÜRÜ"),new(270,300,"Dur ve rahatla","Faz bitene kadar sabit kal.","SABİT DUR")],
        2 => [new(0,20,"Sabit dur","Başlangıç tabanı ölçülüyor.","SABİT DUR"),new(20,110,"Doğal hızda yerinde yürü","Oyunda kullanacağın rahat ritimle yürü.","DOĞAL YÜRÜ"),new(110,130,"Hemen dur","İki ayağını yere koy ve bekle.","SABİT DUR"),new(130,220,"Yeniden doğal yürü","Aynı rahat ritme geri dön.","DOĞAL YÜRÜ"),new(220,240,"Hemen dur","Tamamen sabit kal.","SABİT DUR"),new(240,290,"Doğal yürüyüş","Rahat ritmini koru.","DOĞAL YÜRÜ"),new(290,300,"Son duruş","Faz bitene kadar bekle.","SABİT DUR")],
        _ => [new(0,20,"Sabit dur","Başlangıç ölçümü.","SABİT DUR"),new(20,80,"Yavaş yerinde yürü","Küçük ve kontrollü adımlar.","YAVAŞ YÜRÜ"),new(80,140,"Doğal hızda yürü","Rahat oyun ritmin.","DOĞAL YÜRÜ"),new(140,200,"Hızlı yerinde yürü","Koşmadan, belirgin hızlı adımlar.","HIZLI YÜRÜ"),new(200,220,"Hemen dur","Tamamen sabit kal.","SABİT DUR"),new(220,245,"Dizlerini bük ve doğrul","Yürümeden kontrollü eğil.","EĞİL"),new(245,275,"Yerinde sağa ve sola dön","Adım yürüyüşüne başlamadan gövdenle dön.","DÖN"),new(275,300,"Son duruş","Rahat ve sabit kal.","SABİT DUR")]
    };
    }
    private static string Display(SensorFamily sensor) => sensor switch { SensorFamily.JoyCon => "Joy-Con", SensorFamily.PsMove => "PS Move", SensorFamily.Phone => "Telefon", _ => "Balance Board" };
    private sealed record Step(double Start, double End, string Title, string Detail, string Pose);
}
