# NiiMotion V5 Tarihsel Durum Denetimi

> **TARİHSEL BELGE — GÜNCEL DURUM İÇİN KULLANMA.** Bu dosya 21 Ağustos 2026'daki Faz 0 anlık görüntüsüdür. PS Move, oyun sistemi, OpenXR, HMD, bağımsız kalibrasyon ve kurtarma hakkındaki aşağıdaki “NOT IMPLEMENTED” ifadeleri artık geçerli değildir. Güncel agent devri için `docs/OPENCODE_HANDOFF.md`, kabul sonucu için `docs/standalone-acceptance.md`, faz geçmişi için `docs/upgrade-plan-v5.md` okunmalıdır.

Tarih: 2026-08-21  
Kapsam: V5 Faz 0 — yalnız mevcut sistemi anlama, koruma sınırlarını belirleme ve regresyon tabanı oluşturma.

## Regresyon tabanı

- `NiiRMotion.slnx` Release derlemesi: **başarılı, 0 uyarı, 0 hata**.
- Otomatik doğrulama: **48/48 başarılı**.
- Son kullanıcı hedefi: `win-x64`, WPF, `.NET 10`, `SelfContained=true`.
- C: boş alan: **22,6 GB**.
- Yaklaşık klasör boyutları: data 626,6 MiB; artifacts 638,4 MiB; src 287,2 MiB; logs 44,9 MiB; native 10,3 MiB.
- Git: depo var fakat `main` dalında henüz commit yoktu; tüm proje izlenmeyen durumdaydı. Kişisel veri, günlük ve paket klasörleri Git dışında tutulmalıdır.

## Mimari

### NiiRMotion.Core

- Donanımdan bağımsız cihaz, profil ve readiness modelleri.
- Joy-Con protokolü ve fabrika IMU kalibrasyonu.
- Gait state makinesi: Idle, Starting, Walking, FastWalk, Running, Stopping.
- Kadans + yön bağımsız bacak salınım büyüklüğü + kişisel hız referansları + DeepGait öncülü.
- Telefon ve Balance Board için kişisel hareket profilleri.
- Asenkron confidence-aware sensor fusion.
- Fail-closed analog locomotion sözleşmesi ve hız yumuşatma.

### NiiRMotion.Infrastructure

- Windows HID üzerinden özgün Joy-Con L/R ve Wii Balance Board keşfi/okuması.
- owoTrack UDP alımı, bağlantı tazeliği ve telefon gövde koordinatı dönüşümü.
- Virtual Desktop/SteamVR/Quest süreç ve başlık doğrulaması.
- Canlı locomotion servisi, bounded sensör kaynakları, 100 Hz output döngüsü ve günlük kotası.
- JSONL kayıt/replay; Joy-Con, telefon ve Board replay okuyucuları.
- Kişisel yürüyüş kayıtlarını analiz edip profili atomik yazan `PersonalGaitAnalyzer`.
- Normal VR ↔ NiiMotion sürücü/oyun bağlama geçişi.

### NiiRMotion.App

- Mevcut koyu arayüz, mavi vurgu, yeşil/sarı/kırmızı durum dili ve kart düzeni korunuyor.
- Genel Bakış: aktif profil, sistem durumu, VR oturumu, gerekli cihazlar ve profil seçimi.
- Test ve Kalibrasyon: önerilen 5 dk kayıt, kişisel ilerleme, Kalibrasyonu Uygula, Joy-Con/telefon/Board kontrolleri.
- Yürüyüş, Telefon ve Balance Board laboratuvarları VR çıkışı kapalı çalışıyor.
- Kalibrasyon uygulandığında önceki ve yeni yavaş/doğal/hızlı değerleri karşılaştırma kartı hazırlanmış durumda.

### Native OpenVR sürücüsü

- Yerel `NiiRMotion.VrOutput.v1` named pipe üzerinden 12 baytlık `NMR1` paketi alır.
- İleri ve dönüş için ayrı analog eksenler yayınlar.
- Başlangıç, bağlantı kaybı, 250 ms timeout, standby, deactivation ve cleanup durumlarında güvenli sıfır.
- El rolü üstlenmeden aktif sabit pose yayınlayan treadmill kaynağıdır; Quest kontrolcüleri bağımsız kalır.

## Mevcut profiller

| Profil | Gerekli sensörler | Durum |
|---|---|---|
| Normal VR | Quest 3 | Uygulandı; NiiMotion çıkışı kapalı ve özgün bağlamalar geri yüklenir |
| Sadece Joy-Con | Quest 3 + Joy-Con L/R | Gerçek Alyx testlerinde çalışan ana profil |
| Joy-Con + Telefon | Quest 3 + Joy-Con L/R + telefon | Uygulandı; telefon destekleyici gövde kanıtı |
| Sadece Telefon | Quest 3 + telefon | Deneysel |
| Sadece Balance Board | Quest 3 + Board | Deneysel |
| Board + Joy-Con | Quest 3 + Board + Joy-Con L/R | Uygulandı; Board davranışı hâlâ deneysel |
| Board + Telefon | Quest 3 + Board + telefon | Deneysel |
| Tüm Cihazlar | Quest 3 + Joy-Con L/R + telefon + Board | Uygulandı; tam donanım deneyimi tam doğrulanmış değil |

Virtual Desktop ve el takibi profillerde isteğe bağlı bağlamdır. SteamVR, başlangıçtan önce gerekli sensör kontrolünün dışında tutulur; uygulama onu doğru sıranın son adımında başlatır.

## Güvenli VR başlangıç zinciri

1. Seçili profil için Quest dışındaki kritik sensörler kontrol edilir.
2. Virtual Desktop başlık oturumu beklenir.
3. `VirtualDesktop.Server` varlığı 5 saniye kesintisiz doğrulanır.
4. Normal VR ise özgün sistem modu; NiiMotion ise sürücü ve geri alınabilir oyun override'ları uygulanır.
5. SteamVR, Virtual Desktop Streamer üzerinden başlatılır.
6. NiiMotion modunda `vrserver` ve named-pipe sürücüsü beklenir.
7. Başlık/readiness tekrar doğrulanır; ancak sonra hareket çıkışı açılır.

Bu zincir V5 boyunca korunacak invarianttır. Error 1114 nedeniyle doğrudan SteamVR başlatma yolu eklenmemelidir.

## Kalibrasyon ve kişisel veri

- Joy-Con temel kalibrasyonu: `calibration/gait-v1.json` (kişisel; Git dışında).
- Aktif kişisel hız: `config/personal-gait-pace.json`.
- Telefon/Board kişisel profilleri: `config/personal-phone-motion.json`, `config/personal-board-motion.json`.
- Kayıtlar: `data/user-gait`, `data/user-phone`, `data/user-board`.
- 24 parçalık Joy-Con programında tamamlanan parçalar: **4/24**.
- Reddedilmiş/kopmuş kayıtlar ayrı `joycon-learning-rejected` klasöründe korunuyor.
- Ani duruş kısa testi artık `sudden_stop_fast_walk` ve `sudden_stop_stand` semantik etiketlerini yazıyor; gerçek yeni kayıt ve analiz henüz yapılmadı.
- Kişisel veri sıfırlama **uygulanmadı**. V5 uyarınca yalnız şema/migrasyon/backup tamamlandıktan ve açık kullanıcı onayından sonra yapılabilir.

## Oyun entegrasyonları

| Oyun | Depoda doğrulanan durum |
|---|---|
| Half-Life: Alyx | OpenVR treadmill binding, kontrolcü hareket override'ı, autoexec ve gerçek oyun testi mevcut |
| Arizona Sunshine 2 | OpenVR binding ve geri alınabilir Oculus Touch binding override'ı mevcut; oyun kurulu |
| Metro Awakening | Oyun kurulu; NiiMotion adaptörü/profili yok |
| Skyrim VR | Oyun kurulu; NiiMotion adaptörü/profili yok |
| Zelda BOTW / Cemu | Bu depo ve taranan oyun klasörlerinde mevcut entegrasyon doğrulanamadı |

Zelda entegrasyonu varmış gibi kabul edilmeyecek. Sahibi daha sonra gerçek konumu veya önceki çıktıyı sağlarsa yalnız o zincir incelenip korunacak.

## V5 ile gelen ancak mevcut olmayan bileşenler

- PS Move keşfi, pairing, HID rapor çözümleme, L/R kimliği, IMU, diagnostics ve profiller: **NOT IMPLEMENTED**.
- Move + Joy-Con zaman hizalama ve hybrid fusion: **NOT IMPLEMENTED**.
- HMD pose kayıt/replay ve SteamVR canlı edinim kaynağı: **SOFTWARE IMPLEMENTED / HARDWARE VALIDATION PENDING**. Canlı veri henüz locomotion kararına katılmaz.
- Oyunlar sayfası ve GameDefinition/adaptör sistemi: **NOT IMPLEMENTED**.
- Metro/Skyrim/Zelda adaptörleri: **NOT IMPLEMENTED**.
- Sürümlü birleşik çoklu-sensör session şeması ve segment bazlı redo/rollback: **PARTIAL**.
- Güvenli “Öğrenilmiş Hareket Verisini Sıfırla”: **NOT IMPLEMENTED**.

## Korunacak invariantlar

- Normal VR birinci sınıf ve NiiMotion yokmuş gibi çalışır.
- Uygulama ve kalibrasyon locomotion OFF başlar.
- Eksik kritik sensörde sentetik hareket başlamaz.
- Kopma, stop, profil geçişi, hata ve kapanışta analog çıkış nötrlenir.
- Telefon/Board gibi opsiyonel gürültülü kaynaklar çalışan Joy-Con çekirdeğini bozamamalıdır.
- Virtual Desktop/SteamVR başlangıç sırası değiştirilmeden önce regresyon testi gerekir.
- Kişisel veriler açık onay olmadan silinmez veya Git'e alınmaz.
- Mock/replay hiçbir zaman donanım doğrulaması olarak raporlanmaz.

## Faz 0 sonucu

Mevcut NiiMotion çalışan ve testli bir prototiptir; PS Move genişletmesi için uygun katmanlara sahiptir ancak ortak normalize çoklu-yerleşim sensör modeli, Move protokolü ve zaman hizalama altyapısı henüz yoktur. En güvenli sonraki adım, Faz 1'de yalnız PS Move protokol/Windows bağlantı araştırması ve tek cihaz read-only keşfidir. Locomotion motoruna Move verisi ancak gerçek iki cihaz doğrulamasından sonra bağlanmalıdır.
