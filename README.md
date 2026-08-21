# NiiRMotion

Windows 11 üzerinde Quest 3, SteamVR, Joy-Con, Android telefon ve Wii Balance Board verilerini düşük gecikmeli, güvenli VR locomotion çıktısına dönüştürmeyi hedefleyen masaüstü uygulaması.

## Mevcut durum

Joy-Con ve isteğe bağlı owoTrack telefon verisiyle yerinde yürüyüş, NiiMotion OpenVR sürücüsü üzerinden Half-Life: Alyx içinde doğrulandı. Portal; Normal VR, Joy-Con, Joy-Con + Telefon, deneysel Telefon ve ileride Balance Board eklenebilecek tam sistem modlarını sunar. Balance Board dışındaki ana geliştirme hattı son dayanıklılık ve farklı oyun doğrulaması aşamasındadır.

## Hızlı kullanım

1. Joy-Con'ları bacaklara bağlayıp Windows Bluetooth üzerinden eşleştirin.
2. İsterseniz telefonda owoTrack'i başlatıp portalda **TELEFONU BAĞLA** düğmesine basın.
3. Kullanacağınız modu seçin ve **OYUN MODUNU BAŞLAT** düğmesine basın.
4. SteamVR açıldıktan sonra oyunda yerinizde yürüyün. **NORMAL VR** seçimi NiiMotion'ı tamamen devre dışı bırakır.

Joy-Con bağlantısı oyun sırasında kesilirse hareket güvenli biçimde sıfırlanır. Telefon isteğe bağlı modlarda koparsa Joy-Con yürüyüşü devam eder.

## Yerel çalıştırma

Son kullanıcı uygulaması: `C:\NiirMotion\artifacts\app\NiiRMotion.App.exe`

Uygulama .NET kurulumu gerektirmeyen bağımsız tek dosya olarak yayımlanır.

Depolama bütçesi: proje 15 GB'ı aşmamalı; C: sürücüsünde en az 10 GB boş alan korunmalıdır. Kayıtlar varsayılan olarak kotalı tasarlanacaktır.
