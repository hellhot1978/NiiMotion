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
        Title = "NiiMotion · Başlangıç Rehberi"; Width = 650; Height = 610; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = MainWindow.Brush("#070D12"); Foreground = Brushes.White; FontFamily = new FontFamily("Segoe UI Variable Text");
        var store = new UserExperienceStore(); var prefs = store.Load(); var root = new Grid { Margin = new Thickness(28) }; root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var body = new StackPanel(); root.Children.Add(body); body.Children.Add(Text("Başlangıç Rehberi", 25, FontWeights.SemiBold, "#F4F7FA")); body.Children.Add(Text("NiiMotion'ı güvenli biçimde hazırlamak için dört kısa adım.", 11, FontWeights.Normal, "#9CB0BC", new Thickness(0, 4, 0, 18)));
        foreach (var step in FirstUseGuidance.Build(inventory, progress))
        {
            var card = new Border { Background = MainWindow.Brush("#0E171F"), BorderBrush = MainWindow.Brush(step.Complete ? "#286E5A" : "#263B49"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(15), Margin = new Thickness(0, 0, 0, 9) };
            var row = new StackPanel(); row.Children.Add(Text((step.Complete ? "✓  " : "○  ") + step.Title, 14, FontWeights.SemiBold, step.Complete ? "#58DBB5" : "#F4F7FA")); row.Children.Add(Text(step.Detail, 10, FontWeights.Normal, "#9CB0BC", new Thickness(25, 4, 0, 0))); card.Child = row; body.Children.Add(card);
        }
        body.Children.Add(Text("TERCİHLER", 10, FontWeights.Bold, "#40B9F5", new Thickness(0, 15, 0, 7)));
        var language = new ComboBox { ItemsSource = new[] { "Türkçe", "English" }, SelectedIndex = prefs.Language == "en" ? 1 : 0, Height = 34, Margin = new Thickness(0, 0, 0, 10), ToolTip = "Arayüz dili" }; body.Children.Add(language);
        var updates = new Button { Content = "↻  GÜNCELLEMELERİ KONTROL ET", HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 4) }; updates.Click += (_, _) => new UpdateWindow { Owner = this }.ShowDialog(); body.Children.Add(updates);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var later = new Button { Content = "KAPAT", Padding = new Thickness(20, 10, 20, 10), Margin = new Thickness(0, 0, 10, 0) }; later.Click += (_, _) => Close();
        var save = new Button { Content = "KAYDET VE DEVAM ET", Padding = new Thickness(20, 10, 20, 10), Background = MainWindow.Brush("#087DC4") }; save.Click += (_, _) => { store.Save(prefs with { Language = language.SelectedIndex == 1 ? "en" : "tr", OnboardingComplete = true }); if (Owner is not null) UiLocalization.Apply(Owner); DialogResult = true; Close(); };
        footer.Children.Add(later); footer.Children.Add(save); Grid.SetRow(footer, 1); root.Children.Add(footer); Content = root;
    }
    private static TextBlock Text(string value, double size, FontWeight weight, string color, Thickness? margin = null) => new() { Text = value, FontSize = size, FontWeight = weight, Foreground = MainWindow.Brush(color), TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0) };
}
