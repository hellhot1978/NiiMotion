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
    private void ResetLearnedDataClick(object sender, RoutedEventArgs e)
    {
        var first = MessageBox.Show(this, "Kişisel hareket modelleri, temel kalibrasyon ilerlemesi ve ham eğitim kayıtları sıfırlanacak. PS Move/Joy-Con eşleştirmeleri, cihaz tercihleri ve oyun ayarları korunacak. Devam edilsin mi?", "Öğrenilmiş veriyi sıfırla", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (first != MessageBoxResult.Yes) return;
        var second = MessageBox.Show(this, "İşlemden önce tam geri dönüş ZIP'i oluşturulacak. Sıfırlamayı onaylıyor musun?", "Son onay", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (second != MessageBoxResult.Yes) return;
        var result = new LearnedMotionDataService().Reset();
        MessageBox.Show(this, $"Öğrenilmiş hareket verileri sıfırlandı.\n\nGeri dönüş arşivi:\n{result.BackupPath}", "Sıfırlama tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = true; Close();
    }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
