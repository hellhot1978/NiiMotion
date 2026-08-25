using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NiiRMotion.Infrastructure;

namespace NiiRMotion.App;

public sealed class VrPanelWindow : Window
{
    private readonly VrPanelStateReader _reader = new(); private readonly VrPanelCommandChannel _commands = new(); private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly TextBlock _profile; private readonly TextBlock _game; private readonly TextBlock _state; private readonly TextBlock _devices; private readonly ProgressBar _speed; private readonly TextBlock _message;
    public VrPanelWindow()
    {
        Title = "NiiMotion · VR Panel"; Width = 520; Height = 360; Topmost = true; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = MainWindow.Brush("#050A0F"); Foreground = Brushes.White; FontFamily = new FontFamily("Segoe UI Variable Text");
        var root = new Grid { Margin = new Thickness(20) }; root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var body = new StackPanel(); root.Children.Add(body); body.Children.Add(new TextBlock { Text = "NiiMotion Canlı Durum Paneli", FontSize = 22, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = "Bu masaüstü penceresi VR panelini yansıtır. Başlıkta SteamVR menüsünü açıp NiiMotion kutucuğunu seç.", TextWrapping = TextWrapping.Wrap, Foreground = MainWindow.Brush("#8FA6B5"), Margin = new Thickness(0, 3, 0, 16) });
        var card = new Border { Background = MainWindow.Brush("#0D1720"), BorderBrush = MainWindow.Brush("#294150"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(16) }; var stack = new StackPanel(); card.Child = stack;
        _profile = Line(stack, "Profil"); _game = Line(stack, "Oyun"); _state = Line(stack, "Durum"); _devices = Line(stack, "Cihazlar"); _speed = new ProgressBar { Minimum = 0, Maximum = 1, Height = 8, Margin = new Thickness(0, 10, 0, 8) }; stack.Children.Add(_speed); _message = new TextBlock { Foreground = MainWindow.Brush("#AFC0CA"), TextWrapping = TextWrapping.Wrap }; stack.Children.Add(_message); body.Children.Add(card);
        var footer = new Grid { Margin = new Thickness(0, 16, 0, 0) }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); footer.ColumnDefinitions.Add(new ColumnDefinition());
        var rescan = new Button { Content = "↻  CİHAZLARI KONTROL ET", Padding = new Thickness(14, 12, 14, 12) }; rescan.Click += (_, _) => _commands.Send(VrPanelCommand.Rescan); footer.Children.Add(rescan);
        var stop = new Button { Content = "■  HAREKETİ DURDUR", Padding = new Thickness(14, 12, 14, 12), Background = MainWindow.Brush("#5C1E31"), BorderBrush = MainWindow.Brush("#FF7599") }; stop.Click += (_, _) => _commands.Send(VrPanelCommand.EmergencyStop); Grid.SetColumn(stop, 2); footer.Children.Add(stop); Grid.SetRow(footer, 1); root.Children.Add(footer); Content = root;
        _timer.Tick += (_, _) => Refresh(); Loaded += (_, _) => { _timer.Start(); Refresh(); }; Closed += (_, _) => { _timer.Stop(); _reader.Dispose(); _commands.Dispose(); };
    }
    private static TextBlock Line(Panel panel, string label) { var text = new TextBlock { Text = label + ": —", FontSize = 14, Margin = new Thickness(0, 0, 0, 7) }; panel.Children.Add(text); return text; }
    private void Refresh() { var value = _reader.Read(); if (value is null) return; _profile.Text = UiLocalization.Text("Profil") + ": " + value.Profile; _game.Text = UiLocalization.Text("Oyun") + ": " + value.Game; _state.Text = UiLocalization.Text("Durum") + ": " + value.Locomotion; _devices.Text = UiLocalization.Text("Cihazlar") + ": " + value.DeviceSummary; _speed.Value = Math.Clamp(value.Speed, 0, 1); _message.Text = value.Message; }
}
