using System.Windows;
using NiiRMotion.Infrastructure;

namespace NiiRMotion.App;

public partial class RecoveryCenterWindow : Window
{
    private sealed record Row(RecoverySnapshot Snapshot) { public string Display => $"{Snapshot.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm:ss}   ·   {Snapshot.Files} dosya   ·   {Snapshot.Bytes / 1024d:0} KB   ·   {Snapshot.Id}"; }
    private readonly RecoveryCenterService _service = new();
    public RecoveryCenterWindow() { InitializeComponent(); Refresh(); }
    private void Refresh() { Snapshots.ItemsSource = _service.List().Select(x => new Row(x)).ToArray(); Snapshots.SelectedIndex = Snapshots.Items.Count > 0 ? 0 : -1; }
    private void CreateClick(object sender, RoutedEventArgs e) { _service.Create(); Refresh(); }
    private void RestoreClick(object sender, RoutedEventArgs e)
    {
        if (Snapshots.SelectedItem is not Row row) return;
        if (MessageBox.Show(this, "Seçili yedek geri yüklensin mi? Mevcut durum önce ayrıca saklanacak.", "Geri yükle", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _service.Restore(row.Snapshot.Id); DialogResult = true; Close();
    }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
