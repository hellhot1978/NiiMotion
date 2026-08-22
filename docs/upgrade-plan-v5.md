# NiiMotion V5 Yükseltme Planı

Bu plan `NiiMotion_Codex_Master_Prompt_V5_FINAL.txt` gereksinimlerini mevcut çalışan kodla karşılaştırır. Statüler: **KEEP AS-IS**, **EXTEND**, **NEW**, **DEFER UNTIL HMD**.

## Sınıflandırma

## Sahip tarafından doğrulanan yerleşim değişikliği

V5 taslağındaki yerleşim, sahibin fiziksel kullanım kararıyla değiştirilmiştir. Bundan sonraki tek doğru topoloji:

- Sol/Sağ PS Move: diz altında, baldır/lower-leg.
- Sol/Sağ Joy-Con: kalça ile diz arasında, uyluk/upper-leg.
- Hybrid model Move'u alt bacak, Joy-Con'u üst bacak evidence kaynağı olarak yorumlar.
- Eski kayıtlar yeniden etiketlenmez; session manifest içindeki placement alanı model seçimini belirler.

### KEEP AS-IS

- Koyu WPF görsel dili, mevcut Genel Bakış ve Test/Kalibrasyon akışı.
- Normal VR'nin NiiMotion'ı kapatması ve özgün oyun bağlamalarını geri yüklemesi.
- Virtual Desktop → SteamVR güvenli başlatma sırası.
- Named-pipe OpenVR analog output ve fail-closed nötrleme.
- Joy-Con HID, L/R kimliği, IMU fabrika kalibrasyonu, diagnostics ve gerçek yürüyüş motoru.
- owoTrack telefon bağlantısı, belirtilmiş göğüs yerleşimi ve opsiyonel fusion davranışı.
- Wii Balance Board protokolü, laboratuvarı, kişisel profil ve mevcut deneysel modlar.
- Yürüyüş/telefon/Board laboratuvarları ve VR çıkışı kapalı kayıt ilkesi.
- DeepGait destekli fakat deterministik güvenlik kurallarıyla çevrili hız modeli.
- Alyx ve Arizona 2 için mevcut geri alınabilir bağlama yaklaşımı.

### EXTEND

- `DeviceKind`, discovery, UI cihaz kartları ve readiness: PS Move L/R eklenmesi.
- Mevcut timestamp/sample modeli: placement, health, sample age, battery ve opsiyonel magnetometer metadata.
- Test ve Kalibrasyon: Move sensör testi, L/R atama ve yerleşim kalibrasyonu.
- Yürüyüş Laboratuvarı: seçili sensör topolojisine göre Move/Hybrid kayıt.
- Kayıt/replay: sürümlü session manifest, bütün aktif akışlar, derived feature/output ve segment kalite bilgisi.
- Kişisel model: sensör topolojisi başına ayrı kalibrasyon/model sürümleri.
- 5 dk ek kayıt: sınıf toplamları, kalite kontrolü, kötü segment redo ve model rollback.
- Logging: discovery, pairing, disconnect, profile, training, model ve game apply/revert olayları.
- UI: mevcut tasarım sistemi içinde yalnız `Oyunlar` üst seviye sayfası.

### NEW

- PS Move CECH-ZCM1E USB/Bluetooth keşif ve read-only tanılama katmanı.
- Gerçek raporlara dayalı PS Move accel/gyro/magnetometer/button/battery parser'ı.
- Kalıcı L/R atama ve reconnect kimliği.
- Move Only profilleri ve kişisel model.
- Move + Joy-Con timestamp hizalama, küçük tampon ve interpolasyon/resampling.
- Hybrid üst/alt bacak feature ve confidence fusion.
- Açık, sessiz olmayan güvenli fallback seçimi.
- GameDefinition ve geri alınabilir oyun adaptör sistemi.
- Alyx dışındaki oyunlar için gerçek incelemeye dayalı mapping profilleri.
- Öğrenilmiş hareket verisi için backup + explicit reset akışı.

### DEFER UNTIL HMD

- HMD pose acquisition ve kayıt.
- HMD-relative direction, yaw/body-turn confidence ve HMD-enhanced profiller.
- HMD içeren bütün Move/Hybrid kombinasyonlarının gerçek doğrulaması.
- HMD verisine dayalı oyun yeniden ayarı.

## Faz kapıları

### Faz 0 — Denetim ve regresyon tabanı

**IMPLEMENTED / TESTED**

- Mimari, profiller, sensörler, UI, VR zinciri, oyunlar ve kalıcı veri incelendi.
- `current-state-audit.md` ve bu plan oluşturuldu.
- Release build 0 uyarı/0 hata; 48/48 test başarılı.
- Kişisel veri/log/publish klasörleri Git dışında tutulacak şekilde koruma eklendi.
- Zelda entegrasyonu doğrulanamadığı açıkça kaydedildi.

### Faz 1 — PS Move bağlantı araştırması ve tek cihaz keşfi

**IMPLEMENTED / SINGLE CONTROLLER HARDWARE TESTED**

1. CECH-ZCM1E Windows HID/Bluetooth davranışı birincil PS Move API kaynaklarından araştırıldı.
2. `VID_054C/PID_03D5` için strict descriptor ve Windows HID discovery eklendi.
3. Read-only HID capability probe ve `--psmove-discovery` tanı komutu eklendi.
4. Bir gerçek Move ile USB ve Bluetooth koleksiyonları ölçüldü; `0x01` kimlikli canlı 49 baytlık giriş raporları kaydedildi.
5. Pairing, controller write, locomotion ve mevcut profiller değiştirilmedi.

Kabul: tek gerçek kontrolcü USB/Bluetooth görünürlüğü ve canlı input ile doğrulandı; regresyon 50/50 geçti. Ayrıntı: `phase-1-psmove-status.md`.

### Faz 2 — Move diagnostics, L/R ve reconnect

**PARTIAL / DUAL HARDWARE TESTED**

- İki gerçek cihaz kalıcı Bluetooth kimliğiyle ayırt edildi; kullanıcı kontrollü L/R ataması saklandı.
- Ham accel/gyro/magnetometer/battery/buttons/timing parser ve çift canlı akış tanısı eklendi.
- USB/Bluetooth, iki eşzamanlı cihaz ve disconnect/reconnect doğrulandı.
- İki kontrolcünün ayrı 143 baytlık fabrika kalibrasyonu okundu; ham ivme ve jiroskop için fiziksel birim dönüşümü eklendi.
- Ana UI sade; ham XYZ/jitter gelişmiş görünümde.

Ayrıntı: `phase-2-psmove-status.md`.

### Faz 3–4 — Move Only kalibrasyon, eğitim ve locomotion

**IMPLEMENTED — OWNER VR VALIDATION PENDING**

- Sahibin fiziksel kullanım kararı uyarınca Move sensörleri baldır/diz altına yerleştirildi; nötr yerleşim kalibrasyonu ve kişisel MoveOnly veri şeması tamamlandı.
- 10 dakikalık etiketli gerçek veri (198.550 örnek) kalite kontrolünden geçirildi ve sürümlü kişisel profile dönüştürüldü.
- Ayrı `PsMoveGaitEngine`, kayıt replay ayarı, güvenli durma ve OpenVR analog çıkışı tamamlandı.
- İlk kurulum sihirbazı sol/sağ atama, fabrika kalibrasyonu, yerleşim, iki eğitim kaydı ve kişisel profil üretimini yapay zekâ yardımı olmadan tamamlıyor.
- Ana arayüzde Move Only profili, canlı bağlantı kartları, sol-kırmızı/sağ-mavi görsel kimlik testi ve eksik kurulum yönlendirmesi mevcut.
- Son kapı: sahibin gerçek SteamVR oyun oturumunda başlangıç, duruş, dönüş ve hız duyarlılığını doğrulaması.

### Faz 5–8 — Hybrid ve non-HMD Full Fusion

**SOFTWARE IMPLEMENTED / COMBINED HARDWARE CALIBRATION PENDING**

- 2 Move + 2 Joy-Con saat hizalama ve gecikme ölçümü.
- Aynı bacak phase/amplitude/flexion proxy; kesin anatomik açı iddiası yok.
- Telefon ve Board opsiyonel evidence; gürültülü kaynak core'u düşürmez.
- Her topoloji için ayrı readiness, kalibrasyon ve kişisel model.

### Faz 9–10 — Sürümlü eğitim ve augmentation

**SOFTWARE IMPLEMENTED / CLEAN OWNER RECORDINGS PENDING**

- Mevcut JSONL/replay ve 24×5 dk Joy-Con programı temel alınır.
- Ortak session manifest, bütün aktif stream'ler, quality metrics ve segment redo eklenir.
- Semantic labels V5 sözlüğüne migrate edilir.
- Eski iyi veri silinmeden yeni 5 dk eklemeleri sürümlenir ve rollback edilir.

### Faz 11–14 — Oyunlar ve non-HMD regresyon

**IMPLEMENTED / GAME-SPECIFIC HARDWARE VALIDATION PARTIAL**

- Alyx ve Arizona 2 seçilebilir oyun adaptörleri olarak ayrıldı; yalnız seçili oyunun geri alınabilir giriş eşlemesi uygulanıyor.
- Oyunlar sayfası gerçek Steam manifestlerini tarıyor; kurulu olma ile NiiMotion desteğini birbirine karıştırmıyor.
- Metro ve Skyrim yalnız gerçek runtime/input incelemesinden sonra eklenir.
- Zelda zinciri bulunmadan kart/adaptör oluşturulmaz.
- Kişisel hareket modeli ile oyun mapping'i kesin ayrılır.
- Tüm config değişiklikleri backup/apply/revert durumuna sahip olur.

**2026-08-22 Faz 12 başlangıcı — PARTIAL / TESTED**

- Kullanıcı oyun eşlemeleri tek tek kaldırılabiliyor; ilk oyun eklenmeden önceki sürücü profili arayüzden geri yüklenebiliyor. Geri yüklemeden önce mevcut durum ayrıca tarihli güvenlik kopyasına alınıyor.
- Kişisel hareket modelinden ayrı, şema ve mapping sürümü taşıyan `GameMotionProfile` katmanı eklendi. Hız çarpanı, azami analog çıkış, deadzone, hızlanma, yavaşlama ve yön modu oyun bazında tutuluyor.
- Alyx `alyx-openvr-v2` başlangıç profili mevcut doğrulanmış 3.0 hızlanma / 12.0 durma tepkisini değiştirmeden yeni katmana taşındı. Fiziksel oyun doğrulaması yapılmadığı için Faz 12 henüz HARDWARE VERIFIED değildir.
- Oyunlar sayfasına kişisel modelden bağımsız hareket ayarları eklendi: genel hız, azami çıkış, küçük hareket filtresi, başlama ve durma tepkisi. Değişiklikler sürümlü saklanıyor; seçili oyun tek tuşla güvenli varsayılana dönebiliyor.

### Faz 15–18 — HMD

**DEFER UNTIL HMD**

- Non-HMD regresyon tamamlanmadan başlanmaz.
- Gerekirse tek kısa 3–5 dk oturumla replayable pose kaydı alınır.
- HMD yalnız confidence/direction evidence; yürüyüşü tek başına hard-gate etmez.

### Faz 19–21 — Son regresyon, güvenli kişisel reset ve temiz eğitim

**NOT IMPLEMENTED**

- Bütün legacy ve yeni profiller için otomatik + gerçek donanım matrisi.
- Kişisel schema backup/migration tamamlandıktan sonra açık kullanıcı onayıyla reset.
- Uygulama, sürücüler, VD/SteamVR, oyunlar ve statik tanımlar korunur.
- Sahibi temiz gerçek eğitim kayıtlarına başlar.

## Bir sonraki somut çalışma

Move Only yazılım zinciri tamamlandı. Bir sonraki kapı, iki atanmış Move Bluetooth ile bağlıyken gerçek SteamVR oyun doğrulamasıdır. Bu doğrulama geçmeden Move + Joy-Con saat hizalama ve hibrit profile başlanmaz.

## 2026-08-22 üretim hazırlığı

- PS Move resmi ve hash doğrulamalı eşleştirme yardımcısı, kalıcı sol/sağ renkleri, pil görünümü, olay günlüğü, yeniden eşleştirme ve kimlik temelli otomatik yeniden bağlanma tamamlandı.
- Self-contained gerçek Windows kurucusu; masaüstü/Başlat kısayolu, kaldırma sırasında OpenVR sürücüsünü güvenle ayırma ve hash doğrulamalı HTTPS güncelleme temeli tamamlandı.
- Kişisel model yedekleme/geri dönüş yanında açık çift onaylı öğrenilmiş veri sıfırlama eklendi. Oyun ayarları, uygulama ve cihaz fabrika kalibrasyonu korunuyor.
- Kullanıcı dostu otomatik tanılama ve gizlilik filtreli destek paketi eklendi. Ham sensör verisi pakete alınmıyor.
- Metro Awakening'in kurulu OpenXR yapısı salt okunur doğrulandı; SteamVR action uydurulmadı. Skyrim VR kurulu olmadığı için adaptörü güvenli biçimde beklemede. Steam oyun yolları manifestlerden dinamik çözülüyor.
- HMD pose örnek sözleşmesi, kayıt ve replay altyapısı tamamlandı. Mevcut native sürücü baş dönüş hızını zaten güvenli dönüş bastırma kanıtı olarak okuyor; tam HMD kayıt doğrulaması fiziksel teste bırakıldı.
- Dört saat/720.000 çevrim hızlandırılmış dayanıklılık regresyonu eklendi; güvenli sıfır ve bellek bütçesi otomatik test ediliyor.
- İlk kullanım, cihaz kurulumu, sorun giderme, oyun denetimi, dayanıklılık ve üçüncü taraf bildirimleri yazıldı ve kurucuya dahil edildi.

## 2026-08-22 ilerleme güncellemesi

- Donanım envanterine göre bütün sensör alt kümeleri profil olarak üretiliyor; temel cihaz kalibrasyonu ve aktif kombinasyon kalibrasyonu birbirinden ayrıldı.
- Joy-Con, PS Move, telefon ve Balance Board için üç temel fazdan yerel kişisel profil üreten çevrimdışı analiz hattı eklendi. Oyun motoru bu profilleri sonraki VR oturumunda doğrudan okuyor.
- İsteğe bağlı 5 dakikalık kombinasyon kayıtları temel istatistikleri ezmeden sınırlı ağırlıkla modele ekleniyor.
- Kişisel model dosyaları değişiklik öncesinde en fazla 20 sürüm olacak şekilde yedekleniyor; son gelişmiş kayıt arayüzden reddedilip model kalan veriden yeniden üretilebiliyor.
- VR el kontrolü yürüyüş füzyonundan kesin olarak ayrıldı. Yalnız Virtual Desktop'ın elden kontrolcü emülasyonu için kullanıcı tercihi olarak saklanıyor; hiçbir locomotion profilinde zorunlu cihaz değil.
- Faz 9 **IMPLEMENTED / AUTOMATED TESTED**: model sürümleme, ek kayıt geri alma, sürümlü birleşik sensör manifesti ve bütün akışları monotonic zaman damgasıyla tek zaman çizelgesinde birleştiren replay okuyucusu uygulandı. Kayıtlar 10 saniyelik bölümlerde akış eksikliği, uzun sensör kesintisi ve örnek yoğunluğu açısından puanlanıyor. Arayüz yalnız sorunlu bölümü yeniden kaydettiriyor; temiz bölümleri koruyup yeni parçayı zaman çizelgesine yerleştiriyor, eski kaydı `superseded` olarak saklıyor ve model analizini yalnız onaylanan birleşik kayıttan yeniliyor. Gerçek sensör kesintisiyle kullanıcı doğrulaması ileride yapılacak.
- Oyunlar sayfası eklendi. Bu bilgisayarda Alyx, Arizona Sunshine 2 ve Metro Awakening gerçek Steam manifestlerinden algılandı; yalnız doğrulanmış Alyx ve Arizona eşlemeleri etkinleştirildi. Skyrim kurulu değil, Zelda yolu doğrulanmadı ve bu durumlar arayüzde açıkça gösteriliyor.
- Yapay zekâ gerektirmeyen Oyun Ekleme Sihirbazı eklendi: kurulu Steam oyunlarını listeliyor, yerel JSON dosyalarındaki SteamVR action yollarını salt-okunur tarıyor, kullanıcı onayından sonra ayrı bir NiiMotion binding'i ve sürücü profil kaydı üretiyor. Oyun dosyalarına dokunulmuyor; sürücü profili ilk değişiklikten önce yedekleniyor. Oyun hız çarpanı gerçek analog çıkış katmanına bağlandı.
- Oyun kütüphanesi tek seçimli açılır listeye taşındı; oyun sayısı arttıkça kart/scroll üretmiyor. Oyun yalnız profil, temel kalibrasyon, sensörler, Quest/Virtual Desktop ve SteamVR sırasıyla doğrulandıktan sonra açılıyor. Aynı ekranda NiiMotion yürüyüşü açık/kapalı seçilebiliyor. Normal oyunlar otomatik eklenmiyor; VR modlu bir oyun ancak kullanıcı VR zincirini açıkça doğrularsa ekleniyor.
- Oyun metadata katmanı kapak ve açıklamayı yerel önbelleğe alıyor. Güvenli `NIIRMOTION_IGDB_PROXY_URL` yapılandırıldığında IGDB aracısını önceliklendiriyor, aksi halde Steam mağaza verisine düşüyor; IGDB/Twitch istemci sırrı açık kaynak masaüstü paketine gömülmüyor.
- Kullanıcı oyun adaptörleri şema 2 ortak sözleşmesine taşındı. Eski düz liste otomatik migrate ediliyor; runtime, mapping sürümü ve geri alınabilirlik doğrulanıyor. OpenXR oyununa yanlış SteamVR action eşlemesi kurulması engelleniyor.
- Non-HMD regresyon matrisi bütün cihaz envanteri alt kümelerini ve üretilen profilleri; hazır, her zorunlu cihaz eksik, isteğe bağlı el kontrolü ve Normal VR hareket politikası senaryolarıyla otomatik doğruluyor.
- Bekleyen bozuk kalibrasyon segmenti uygulama kapanıp açılsa da geri geliyor. Başarılı onarım eski kaydı koruyup birleşik kaydı modele uyguluyor.
- Test ve Kalibrasyon sayfasına kişisel modelleri, cihaz tercihlerini ve oyun ayarlarını sınırlı sayıda sürümlü anlık görüntüyle yedekleyip geri yükleyen merkez eklendi. Geri yükleme öncesi mevcut durum ayrıca saklanıyor.
- Self-contained Windows paketi korunuyor. Açılış bakımı yalnız yeniden üretilebilir log/metadata önbelleğini ve eski `.tmp` dosyalarını sınırlandırıyor; ham kişisel sensör kayıtlarını silmiyor.
