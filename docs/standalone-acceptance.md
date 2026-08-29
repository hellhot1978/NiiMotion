# NiiMotion bağımsız kullanım kabulü

Güncelleme: 29 Ağustos 2026

## Sonuç

NiiMotion'ın çalışma zamanı, cihaz kurulumu, temel kalibrasyon analizi, kişisel model üretimi, profil seçimi, oyun ön kontrolü, OpenVR/OpenXR hareket çıkışı, oyun bazlı yerel ayar, tanılama ve geri dönüş zinciri çevrim içi yapay zekâ servisi kullanmaz.

## Otomatik doğrulananlar

- Self-contained Windows paketi yerel .NET kurulumu istemez.
- Kaynak kodunda OpenAI, Gemini/Google Generative Language, Anthropic veya Azure OpenAI çalışma zamanı çağrısı yoktur.
- Joy-Con, PS Move, telefon ve Balance Board temel fazları yerel JSONL kayıtlarından kişisel modele dönüştürülür.
- Eski `calibration/gait-v1.json` geliştiriciye ait kişisel kayıt dağıtım paketine girmez. Temiz kurulum, doğrulanmış faz kayıtlarından `config/personal-*.json` çalışma modellerini yerelde üretir; geçerli model oluşmadan oyun başlatma kapısı geçmez.
- Kalibrasyon ilerlemesi tek başına yeterli değildir; kişisel model dosyası da okunabilir ve geçerli olmalıdır. Uygun yerel kayıt varsa oyun başlatma öncesinde yeniden üretilir.
- Seçili oyun; adaptör, profil, kalibrasyon, canlı sensörler, Quest/Virtual Desktop ve SteamVR sırasıyla doğrulanmadan başlatılmaz.
- Yeni SteamVR/OpenXR oyun adaptörleri yerel kurulum ve action/executable taramasıyla oluşturulur. Oyun dosyaları değiştirilmez.
- Alyx doğrudan yerel telemetriyle, diğer oyunlar sınırlı ve geri alınabilir kullanıcı geri bildirimiyle oyun bazında ayarlanır.
- Tanı paketi IP, cihaz kimliği ve kullanıcı yolunu maskeler; ham sensör kayıtlarını içermez.
- Kişisel modeller sürümlenir; yedekleme, geri yükleme ve açık onaylı öğrenilmiş veri sıfırlama yereldir.
- Yayın paketi; OpenVR sürücüsü, OpenXR katmanı, VR overlay, modeller ve kalibrasyon tanımlarıyla birlikte doğrulandı.
- Otomatik regresyon paketi 111/111 geçti; dört saatlik hızlandırılmış dayanıklılık, CI, modelden bağımsız agent devri, yayın güvenliği ve güvenli kurulum/kaldırma sözleşmesi bu paketin içindedir.

## İnternet gerektirmeyen temel işlevler

Yürüme algılama, kalibrasyon, kişisel model, oyun eşleme, oyun başlatma ön kontrolü, tanılama ve geri dönüş çevrimdışı çalışır. Oyun kapak/açıklama bilgisi ve uygulama güncelleme denetimi isteğe bağlı ağ özellikleridir; erişilemez olmaları hareket işlevini engellemez.

## Fiziksel kabul kapıları

Otomasyon yazılım bütünlüğünü doğrular; Bluetooth radyo, gerçek başlık oturumu ve her yeni oyunun davranışı fiziksel donanım olmadan kesin onaylanamaz. Son ürün kabulünde tek oturumda cihaz kombinasyonları, SteamVR/OpenXR oyunları, bağlantı kopması ve yeniden bağlanma matrisi uygulanmalıdır.
