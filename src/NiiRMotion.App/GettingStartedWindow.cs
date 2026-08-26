using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NiiRMotion.Core;
using NiiRMotion.Infrastructure;

namespace NiiRMotion.App;

public sealed class GettingStartedWindow : Window
{
    public GettingStartedWindow(UserHardwareInventory inventory, CalibrationProgressDocument progress)
    {
        Title = "NiiMotion · Başlangıç Rehberi"; Width = 650; Height = 700; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = MainWindow.Brush("#070D12"); Foreground = Brushes.White; FontFamily = new FontFamily("Segoe UI Variable Text");
        var store = new UserExperienceStore(); var prefs = store.Load(); var root = new Grid { Margin = new Thickness(28) }; root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var body = new StackPanel { Margin = new Thickness(0, 0, 8, 0) }; root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); body.Children.Add(Text("Başlangıç Rehberi", 25, FontWeights.SemiBold, "#F4F7FA")); body.Children.Add(Text("NiiMotion'ı güvenli biçimde hazırlamak için dört kısa adım.", 11, FontWeights.Normal, "#9CB0BC", new Thickness(0, 4, 0, 12)));
        var standaloneService = new StandaloneReadinessService(); var standalone = standaloneService.Inspect();
        var standaloneCard = new Border { Background = MainWindow.Brush(standalone.IsReady ? "#0B251F" : "#2A2111"), BorderBrush = MainWindow.Brush(standalone.IsReady ? "#28705C" : "#876523"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12) };
        var standaloneRow = new Grid(); standaloneRow.ColumnDefinitions.Add(new ColumnDefinition()); standaloneRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var standaloneText = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; standaloneText.Children.Add(Text("YEREL ÇALIŞMA DENETİMİ", 10, FontWeights.Bold, standalone.IsReady ? "#58DBB5" : "#F0C76A"));
        var standaloneStatus = Text(standalone.Summary, 11, FontWeights.SemiBold, "#F4F7FA", new Thickness(0, 3, 12, 0)); standaloneText.Children.Add(standaloneStatus); standaloneRow.Children.Add(standaloneText);
        var standaloneCheck = new Button { Content = standalone.IsReady ? "✓  HAZIR" : "↻  DENETLE VE ONAR", Padding = new Thickness(16, 9, 16, 9), IsEnabled = !standalone.IsReady, Background = MainWindow.Brush(standalone.IsReady ? "#1D624F" : "#9A6A16") };
        standaloneCheck.Click += (_, _) => { var report = standaloneService.RepairLocalState(); standaloneStatus.Text = report.Summary; standaloneCheck.Content = report.IsReady ? "✓  HAZIR" : "!  EKSİK BİLEŞEN"; standaloneCheck.IsEnabled = !report.IsReady; standaloneCard.Background = MainWindow.Brush(report.IsReady ? "#0B251F" : "#2A2111"); standaloneCard.BorderBrush = MainWindow.Brush(report.IsReady ? "#28705C" : "#876523"); };
        Grid.SetColumn(standaloneCheck, 1); standaloneRow.Children.Add(standaloneCheck); standaloneCard.Child = standaloneRow; body.Children.Add(standaloneCard);
        var updateCard = new Border { Background = MainWindow.Brush("#0A2231"), BorderBrush = MainWindow.Brush("#1D739E"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12) };
        var updateRow = new Grid(); updateRow.ColumnDefinitions.Add(new ColumnDefinition()); updateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var updateText = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; updateText.Children.Add(Text("UYGULAMA GÜNCELLEMESİ", 10, FontWeights.Bold, "#40B9F5")); updateText.Children.Add(Text("Yeni NiiMotion sürümünü denetle", 12, FontWeights.SemiBold, "#F4F7FA", new Thickness(0, 3, 12, 0))); updateRow.Children.Add(updateText);
        var updates = new Button { Content = "↻  KONTROL ET", Padding = new Thickness(16, 9, 16, 9), Background = MainWindow.Brush("#087DC4") }; updates.Click += (_, _) => new UpdateWindow { Owner = this }.ShowDialog(); Grid.SetColumn(updates, 1); updateRow.Children.Add(updates); updateCard.Child = updateRow; body.Children.Add(updateCard);
        var preferenceRow = new Grid { Margin = new Thickness(0, 0, 0, 12) }; preferenceRow.ColumnDefinitions.Add(new ColumnDefinition()); preferenceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        var preferenceText = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; preferenceText.Children.Add(Text("ARAYÜZ DİLİ", 10, FontWeights.Bold, "#40B9F5")); preferenceText.Children.Add(Text("Uygulamanın görüntüleneceği dili seç", 11, FontWeights.Normal, "#9CB0BC", new Thickness(0, 3, 12, 0))); preferenceRow.Children.Add(preferenceText);
        var language = new ComboBox { ItemsSource = new[] { "Türkçe", "English" }, SelectedIndex = prefs.Language == "en" ? 1 : 0, Height = 36, ToolTip = "Arayüz dili", VerticalContentAlignment = VerticalAlignment.Center }; Grid.SetColumn(language, 1); preferenceRow.Children.Add(language); body.Children.Add(preferenceRow);
        var gameValidated = new GameValidationReceiptStore().Load() is not null;
        var selectedSensors = FirstUseGuidance.Selected(inventory).ToArray();
        var unavailableModels = new CalibrationModelReadinessService().FindUnavailableAsync(selectedSensors, progress, repairFromLocalCaptures: true).GetAwaiter().GetResult();
        foreach (var step in FirstUseGuidance.Build(inventory, progress, gameValidated, unavailableModels.Count == 0))
        {
            var card = new Border { Background = MainWindow.Brush("#0E171F"), BorderBrush = MainWindow.Brush(step.Complete ? "#286E5A" : "#263B49"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(15), Margin = new Thickness(0, 0, 0, 9) };
            var row = new StackPanel(); row.Children.Add(Text((step.Complete ? "✓  " : "○  ") + step.Title, 14, FontWeights.SemiBold, step.Complete ? "#58DBB5" : "#F4F7FA")); row.Children.Add(Text(step.Detail, 10, FontWeights.Normal, "#9CB0BC", new Thickness(25, 4, 0, 0))); card.Child = row; body.Children.Add(card);
        }
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        var later = new Button { Content = "KAPAT", Padding = new Thickness(20, 10, 20, 10), Margin = new Thickness(0, 0, 10, 0) }; later.Click += (_, _) => Close();
        var save = new Button { Content = "KAYDET VE DEVAM ET", Padding = new Thickness(20, 10, 20, 10), Background = MainWindow.Brush("#087DC4") }; save.Click += (_, _) => { store.Save(prefs with { Language = language.SelectedIndex == 1 ? "en" : "tr", OnboardingComplete = true }); if (Owner is not null) UiLocalization.Apply(Owner); DialogResult = true; Close(); };
        footer.Children.Add(later); footer.Children.Add(save); Grid.SetRow(footer, 1); root.Children.Add(footer); Content = root;
    }
    private static TextBlock Text(string value, double size, FontWeight weight, string color, Thickness? margin = null) => new() { Text = value, FontSize = size, FontWeight = weight, Foreground = MainWindow.Brush(color), TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0) };
}
