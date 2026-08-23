using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NiiRMotion.Infrastructure;

namespace NiiRMotion.App;

public sealed class UpdateWindow : Window
{
    private readonly TextBlock _status; private readonly ProgressBar _progress; private readonly Button _action; private NiiMotionUpdateManifest? _manifest; private string? _staged;
    public UpdateWindow()
    {
        Title = "NiiMotion · Güncellemeler"; Width = 620; Height = 390; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = MainWindow.Brush("#070D12"); Foreground = Brushes.White;
        var root = new Grid { Margin = new Thickness(28) }; root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); var body = new StackPanel(); root.Children.Add(body);
        body.Children.Add(new TextBlock { Text = "Güvenli Güncelleme", FontSize = 24, FontWeight = FontWeights.SemiBold }); body.Children.Add(new TextBlock { Text = "Paket çalıştırılmadan önce HTTPS, boyut ve SHA-256 bütünlüğü doğrulanır.", Foreground = MainWindow.Brush("#9AAEBB"), Margin = new Thickness(0, 5, 0, 20), TextWrapping = TextWrapping.Wrap });
        var card = new Border { Background = MainWindow.Brush("#0E171F"), BorderBrush = MainWindow.Brush("#29404D"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(18) }; var stack = new StackPanel(); card.Child = stack;
        _status = new TextBlock { Text = "Güncelleme kanalı kontrol edilmeyi bekliyor.", FontSize = 14, TextWrapping = TextWrapping.Wrap }; stack.Children.Add(_status); _progress = new ProgressBar { Height = 7, IsIndeterminate = false, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 18, 0, 0) }; stack.Children.Add(_progress); body.Children.Add(card);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) }; var close = new Button { Content = "KAPAT", Padding = new Thickness(20, 10, 20, 10), Margin = new Thickness(0, 0, 10, 0) }; close.Click += (_, _) => Close();
        _action = new Button { Content = "GÜNCELLEMEYİ KONTROL ET", Padding = new Thickness(20, 10, 20, 10), Background = MainWindow.Brush("#087DC4") }; _action.Click += ActionClick; footer.Children.Add(close); footer.Children.Add(_action); Grid.SetRow(footer, 1); root.Children.Add(footer); Content = root;
    }
    private async void ActionClick(object sender, RoutedEventArgs e)
    {
        _action.IsEnabled = false; _progress.Visibility = Visibility.Visible; _progress.IsIndeterminate = true;
        try
        {
            if (_staged is not null) { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_staged}\"") { UseShellExecute = true }); return; }
            var service = new UpdateService();
            if (_manifest is null)
            {
                var result = await service.CheckAsync(); _status.Text = result.Message; _manifest = result.UpdateAvailable ? result.Manifest : null;
                _action.Content = _manifest is null ? "YENİDEN KONTROL ET" : "İNDİR VE DOĞRULA"; return;
            }
            _staged = await service.DownloadVerifiedAsync(_manifest); _status.Text = "✓ Güncelleme indirildi ve bütünlüğü doğrulandı. Kurulum yalnız sen onayladığında başlayacak."; _action.Content = "DOSYAYI GÖSTER";
        }
        catch (Exception ex) { _status.Text = "Güncelleme hazırlanamadı: " + ex.Message; _status.Foreground = MainWindow.Brush("#FF91A8"); }
        finally { _progress.IsIndeterminate = false; _progress.Visibility = Visibility.Collapsed; _action.IsEnabled = true; }
    }
}
