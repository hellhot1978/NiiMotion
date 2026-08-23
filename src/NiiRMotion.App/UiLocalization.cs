using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NiiRMotion.Infrastructure;

namespace NiiRMotion.App;

public static class UiLocalization
{
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Genel Bakış"]="Overview", ["Oyunlar"]="Games", ["Test ve Kalibrasyon"]="Test & Calibration", ["Cihazlarım"]="My Devices",
        ["Başlangıç Rehberi"]="Getting Started", ["Erişilebilirlik"]="Accessibility", ["VR Paneli"]="VR Panel",
        ["Profili değiştir  ▾"]="Change profile  ▾", ["VR'Yİ HAZIRLA VE BAŞLAT"]="PREPARE & START VR", ["NORMAL VR'Yİ BAŞLAT"]="START NORMAL VR",
        ["GEREKEN CİHAZLAR"]="REQUIRED DEVICES", ["SİSTEM DURUMU"]="SYSTEM STATUS", ["AKTİF PROFİL"]="ACTIVE PROFILE", ["VR OTURUMU"]="VR SESSION",
        ["OYUN HAREKETİNİ DURDUR"]="STOP GAME MOVEMENT", ["CİHAZLARI KONTROL ET"]="CHECK DEVICES", ["HAREKETİ DURDUR"]="STOP MOVEMENT",
        ["KAPAT"]="CLOSE", ["VAZGEÇ"]="CANCEL", ["KAYDET VE DEVAM ET"]="SAVE & CONTINUE", ["Yüksek kontrast"]="High contrast",
        ["Azaltılmış hareket ve animasyon"]="Reduced motion and animation", ["YENİ YEDEK"]="NEW BACKUP", ["SEÇİLİ YEDEĞİ GERİ YÜKLE"]="RESTORE SELECTED BACKUP",
        ["SİSTEM TANILAMA"]="SYSTEM DIAGNOSTICS", ["YEDEKLEME VE GERİ YÜKLEME"]="BACKUP & RESTORE", ["OYUN MODUNU BAŞLAT"]="START GAME MODE"
    };
    public static void Apply(DependencyObject root)
    {
        if (new UserExperienceStore().Load().Language != "en") return;
        Visit(root);
    }
    private static void Visit(DependencyObject value)
    {
        if (value is TextBlock text) text.Text = Translate(text.Text);
        else if (value is ContentControl content && content.Content is string label) content.Content = TranslateDecorated(label);
        var count = VisualTreeHelper.GetChildrenCount(value); for (var i = 0; i < count; i++) Visit(VisualTreeHelper.GetChild(value, i));
    }
    private static string Translate(string value) => English.TryGetValue(value, out var translated) ? translated : value;
    private static string TranslateDecorated(string value)
    {
        if (English.TryGetValue(value, out var direct)) return direct;
        foreach (var pair in English) if (value.EndsWith(pair.Key, StringComparison.Ordinal)) return value[..^pair.Key.Length] + pair.Value;
        return value;
    }
}
