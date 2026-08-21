# NiiMotion V5 Yükseltme Planı

Bu plan `NiiMotion_Codex_Master_Prompt_V5_FINAL.txt` gereksinimlerini mevcut çalışan kodla karşılaştırır. Statüler: **KEEP AS-IS**, **EXTEND**, **NEW**, **DEFER UNTIL HMD**.

## Sınıflandırma

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

**NOT IMPLEMENTED**

1. Güncel birincil kaynaklardan CECH-ZCM1E Windows HID/Bluetooth davranışını araştır.
2. Mevcut Windows HID katmanının `VID_054C/PID_03D5` collections görünürlüğünü read-only ölç.
3. Tek Move USB ile collection/path/report length/unique identity tanı çıktısı al.
4. Pairing yöntemi seçmeden önce gerçek Windows raporlarını kaydet.
5. Bu fazda locomotion veya mevcut profiller değişmez.

Kabul: cihaz görünürlüğü gerçek donanımla raporlanmış; mock başarı sayılmamış; mevcut 48 test geçiyor.

### Faz 2 — Move diagnostics, L/R ve reconnect

**NOT IMPLEMENTED**

- İki gerçek cihazı ayırt et; kullanıcı kontrollü L/R atama sakla.
- Accel/gyro ve destekleniyorsa magnetometer/battery/buttons/timing diagnostics.
- USB, Bluetooth, disconnect/reconnect ve iki eşzamanlı cihaz testi.
- Ana UI sade; ham XYZ/jitter gelişmiş görünümde.

### Faz 3–4 — Move Only kalibrasyon, eğitim ve locomotion

**NOT IMPLEMENTED**

- Uyluk yerleşim açısı kalibrasyonu ve MoveOnly kişisel veri şeması.
- En az yaklaşık 10 dk gerçek etiketli veri; kalite ve segment redo.
- Replay üzerinden tuning; sonra gerçek Move Only VR testi.
- Mevcut Joy-Con motoru kopyalanmaz; ortak feature sözleşmesi küçük adımlarla genişletilir.

### Faz 5–8 — Hybrid ve non-HMD Full Fusion

**NOT IMPLEMENTED**

- 2 Move + 2 Joy-Con saat hizalama ve gecikme ölçümü.
- Aynı bacak phase/amplitude/flexion proxy; kesin anatomik açı iddiası yok.
- Telefon ve Board opsiyonel evidence; gürültülü kaynak core'u düşürmez.
- Her topoloji için ayrı readiness, kalibrasyon ve kişisel model.

### Faz 9–10 — Sürümlü eğitim ve augmentation

**PARTIAL**

- Mevcut JSONL/replay ve 24×5 dk Joy-Con programı temel alınır.
- Ortak session manifest, bütün aktif stream'ler, quality metrics ve segment redo eklenir.
- Semantic labels V5 sözlüğüne migrate edilir.
- Eski iyi veri silinmeden yeni 5 dk eklemeleri sürümlenir ve rollback edilir.

### Faz 11–14 — Oyunlar ve non-HMD regresyon

**PARTIAL / NOT IMPLEMENTED**

- Alyx ve Arizona 2 mevcut çalışmalar **KEEP AS-IS**, sonra adaptör sözleşmesine alınır.
- Metro ve Skyrim yalnız gerçek runtime/input incelemesinden sonra eklenir.
- Zelda zinciri bulunmadan kart/adaptör oluşturulmaz.
- Kişisel hareket modeli ile oyun mapping'i kesin ayrılır.
- Tüm config değişiklikleri backup/apply/revert durumuna sahip olur.

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

V5 talimatına göre bir sonraki milestone **Faz 1** olmalıdır. Gerekli kullanıcı donanım adımı yalnız şu noktada istenir: bir adet CECH-ZCM1E PS Move'u USB ile bilgisayara bağlamak. Öncesinde güncel protokol ve Windows yaklaşımı yalnız birincil/korunabilir kaynaklardan araştırılabilir.
