using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NiiRMotion.Core;
using NiiRMotion.Infrastructure;

namespace NiiRMotion.App;

public sealed class ProfileFusionHealthWindow : Window
{
    public ProfileFusionHealthWindow(IEnumerable<MotionProfile> profiles, CalibrationProgressDocument progress)
    {
        Title = "NiiMotion · Ortak Model Sağlığı"; Owner = Application.Current.MainWindow; Width = 760; Height = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = MainWindow.Brush("#070D12"); Foreground = Brushes.White;
        var root = new DockPanel { Margin = new Thickness(24) }; var title = new StackPanel { Margin = new Thickness(0, 0, 0, 15) }; title.Children.Add(MainWindow.Label("Ortak model sağlığı", "#F5F8FA", 24, FontWeights.SemiBold)); title.Children.Add(MainWindow.Label("Her cihaz kombinasyonunun kayıt, model ve geçmiş durumunu gösterir.", "#93A6B2", 10, FontWeights.Normal, new Thickness(0, 4, 0, 0))); DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var close = new Button { Content = "KAPAT", Padding = new Thickness(20, 10, 20, 10), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) }; close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close);
        var list = new StackPanel(); var store = new ProfileFusionModelStore();
        foreach (var profile in profiles)
        {
            var phases = progress.Profiles?.FirstOrDefault(x => x.ProfileId == profile.Id)?.CompletedPhases ?? 0; var model = store.Load(profile.Id); var ready = phases >= 3 && model is not null;
            var row = new Border { Background = MainWindow.Brush("#0D1821"), BorderBrush = MainWindow.Brush(ready ? "#286D59" : "#354653"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(14, 11, 14, 11), Margin = new Thickness(0, 0, 0, 8) };
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            var name = new StackPanel(); name.Children.Add(MainWindow.Label(profile.Name, "#F4F7FA", 13, FontWeights.SemiBold)); name.Children.Add(MainWindow.Label(model is null ? $"Ortak faz {phases}/3" : $"Kalite %{model.CaptureQuality * 100:0} · {model.AcceptedSamples:N0} örnek", ready ? "#55DDB8" : "#F1C566", 9, FontWeights.Normal, new Thickness(0, 3, 0, 0))); grid.Children.Add(name);
            var status = MainWindow.Label(ready ? $"HAZIR · {store.HistoryCount(profile.Id)} YEDEK" : phases >= 3 ? "MODEL EKSİK" : "KALİBRASYON GEREKİYOR", ready ? "#55DDB8" : "#F1C566", 9, FontWeights.Bold); status.HorizontalAlignment = HorizontalAlignment.Right; status.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(status, 1); grid.Children.Add(status); row.Child = grid; list.Children.Add(row);
        }
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = root; Loaded += (_, _) => UiLocalization.Apply(this);
    }
}
