using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NiiRMotion.Infrastructure;

namespace NiiRMotion.App;

public sealed class HmdValidationWindow : Window
{
    private readonly TimeSpan _duration = TimeSpan.FromMinutes(3); private readonly CancellationTokenSource _cancel = new(); private bool _paused; private TimeSpan _elapsed;
    private readonly TextBlock _instruction = new(), _detail = new(), _time = new(), _status = new(); private readonly ProgressBar _progress = new(); private readonly Button _pause = new();
    public HmdValidationWindow()
    {
        Title = "NiiMotion · " + UiLocalization.Text("HMD yön doğrulaması"); Owner = Application.Current.MainWindow; Width = 760; Height = 510; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = MainWindow.Brush("#060A0F"); Foreground = Brushes.White; FontFamily = new FontFamily("Segoe UI Variable Text");
        var root = new Grid { Margin = new Thickness(30) }; root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel(); header.Children.Add(new TextBlock { Text = "HMD yön ve dönüş doğrulaması", FontSize = 25, FontWeight = FontWeights.SemiBold }); header.Children.Add(new TextBlock { Text = "Tek kayıt · kişisel yürüyüş modelini değiştirmez · oyun hareketi gönderilmez", Foreground = MainWindow.Brush("#8FA4B1"), Margin = new Thickness(0,5,0,0) }); root.Children.Add(header);
        var card = new Border { Background = MainWindow.Brush("#0D1720"), BorderBrush = MainWindow.Brush("#29404D"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(25), Margin = new Thickness(0,22,0,18) }; Grid.SetRow(card,1);
        var body = new StackPanel(); body.Children.Add(new TextBlock { Text = "ŞİMDİ YAP", Foreground = MainWindow.Brush("#37B8F3"), FontSize = 10, FontWeight = FontWeights.Bold }); _instruction.FontSize=25; _instruction.FontWeight=FontWeights.SemiBold; _instruction.Margin=new Thickness(0,10,0,7); body.Children.Add(_instruction); _detail.Foreground=MainWindow.Brush("#A9BBC6"); _detail.FontSize=13; _detail.TextWrapping=TextWrapping.Wrap; body.Children.Add(_detail); _time.FontSize=48; _time.FontWeight=FontWeights.Light; _time.Margin=new Thickness(0,28,0,8); body.Children.Add(_time); _progress.Maximum=_duration.TotalSeconds; _progress.Height=8; _progress.Foreground=MainWindow.Brush("#159BE8"); _progress.Background=MainWindow.Brush("#1A2A35"); body.Children.Add(_progress); _status.Foreground=MainWindow.Brush("#8FA4B1"); _status.Margin=new Thickness(0,16,0,0); _status.Text="SteamVR HMD akışı başlatılıyor…"; body.Children.Add(_status); card.Child=body; root.Children.Add(card);
        var footer = new Grid(); footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(10) }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); Grid.SetRow(footer,2); _pause.Content="Ⅱ  DURAKLAT"; _pause.Padding=new Thickness(18,10,18,10); _pause.Click += (_,_) => { _paused=!_paused; _pause.Content=_paused?"▶  DEVAM ET":"Ⅱ  DURAKLAT"; _status.Text=_paused?"Kayıt ve sayaç duraklatıldı.":"Kayıt devam ediyor."; }; Grid.SetColumn(_pause,1); footer.Children.Add(_pause); var cancel=new Button { Content="İPTAL", Padding=new Thickness(18,10,18,10) }; cancel.Click += (_,_)=>Close(); Grid.SetColumn(cancel,3); footer.Children.Add(cancel); root.Children.Add(footer); Content=root;
        Loaded += async (_,_) => await RunAsync(); Closing += (_,_) => _cancel.Cancel(); UpdateGuide();
    }
    private async Task RunAsync()
    {
        try { var progress=new Progress<TimeSpan>(x=>{_elapsed=x; UpdateGuide();}); var result=await new HmdValidationCaptureService().CaptureAsync(_duration,()=>_paused,progress,_cancel.Token); _pause.Visibility=Visibility.Collapsed; _instruction.Text=result.Passed?"Doğrulama tamamlandı":result.Samples==0?"Başlık akışı bulunamadı":"Kayıt tekrarlanmalı"; _detail.Text=result.Samples==0?"SteamVR ve NiiMotion VR paneli çalışırken yeniden dene. Kayıt başlamadığı için üç dakika beklenmedi.":result.Message; _status.Text=$"{result.Samples:N0} örnek · {result.TrackedRatio*100:0}% takip · {result.SampleRateHz:0.0} Hz · {result.YawRangeDegrees:0}° yön aralığı"; }
        catch(OperationCanceledException) { }
        catch(Exception ex) { _instruction.Text="HMD verisi alınamadı"; _detail.Text=ex.GetBaseException().Message; _pause.Visibility=Visibility.Collapsed; }
    }
    private void UpdateGuide()
    {
        var s=_elapsed.TotalSeconds; (_instruction.Text,_detail.Text)=s<30?("Sabit dur ve doğal biçimde etrafa bak","Başını küçük hareketlerle yukarı, aşağı, sola ve sağa çevir."):s<75?("Yerinde doğal yürü","Olduğun yerden ayrılmadan rahat ritimde yürü."):s<115?("Sola ve sağa bak","Ayakların sabitken yalnız başını iki yöne çevir."):s<155?("Vücudunla sola ve sağa dön","İleri yürümeye başlamadan kontrollü dönüş örnekleri ver."):("Başla, yürü ve birkaç kez dur","Kısa yürüyüş başlangıçları ve belirgin tam duruşlar yap."); _time.Text=(_duration-_elapsed).ToString(@"mm\:ss"); _progress.Value=Math.Min(_elapsed.TotalSeconds,_duration.TotalSeconds);
    }
}
