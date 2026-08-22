using System.Windows;
using NiiRMotion.Core;
using NiiRMotion.Infrastructure;

namespace NiiRMotion.App;

public partial class DiagnosticsWindow : Window
{
    private readonly MotionProfile _profile;
    public DiagnosticsWindow(MotionProfile profile)
    {
        _profile = profile; InitializeComponent();
        Loaded += async (_, _) => Findings.ItemsSource = (await new DiagnosticPackageService().AnalyzeAsync(_profile)).Findings;
    }
    private async void ExportClick(object sender, RoutedEventArgs e)
    {
        try { var path = await new DiagnosticPackageService().ExportAsync(_profile); StatusText.Text = "✓ Tanı paketi masaüstüne kaydedildi: " + System.IO.Path.GetFileName(path); }
        catch (Exception ex) { StatusText.Text = "Tanı paketi oluşturulamadı: " + ex.GetBaseException().Message; }
    }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
