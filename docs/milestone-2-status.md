# Milestone 2 durum raporu

Güncelleme: 12 Ağustos 2026.

## Uygulanan

- Original Nintendo VID `057E`, Joy-Con L PID `2006`, Joy-Con R PID `2007` ile güvenilir taraf kimliği.
- Windows SetupAPI üzerinden mevcut HID arabirimlerinin keşfi.
- HID output subcommand ile IMU açma (`0x40`) ve standard full report `0x30` seçme.
- Her rapordaki üç accel/gyro alt örneğini sıra numarası ve monotonic zamanla ayrıştırma.
- UI'ı bloke etmeyen asenkron read loop ve bounded sample buffer.
- Sample rate, interval, jitter ve packet age istatistikleri.
- JSONL record ve 1x/hızlandırılmış replay; fusion tarafı için ortak `ISensorSample` modeli.

## Doğrulama

- Build: 0 hata, 0 uyarı.
- Otomatik test: 8/8 başarılı; identity, parser, invalid-report ve recording round-trip dahil.
- Gerçek Joy-Con taraması: Joy-Con L `057E:2006` ve Joy-Con R `057E:2007` doğrulandı.
- HID capabilities: her iki cihaz input report 362 bayt, output report 49 bayt bildirdi.
- Gerçek IMU smoke testi: her cihazdan 300 örnek; yaklaşık 198 Hz, 1,86–1,89 ms jitter, ölçüm anında 0,1–0,5 ms packet age.
- IMU enable/report-mode komutları ve eşzamanlı olmayan L/R okuma: **PASS**.

## Kapanış için gerekenler

Milestone 2 kapanış koşulları tamamlandı:

- Her cihazın SPI `0x6020–0x6037` fabrika IMU kalibrasyonu okundu ve ölçeklemeye uygulandı.
- Her iki gerçek cihazda 300 sample JSONL recording/replay eşitliği doğrulandı.
- Ana ekrana `JOY-CON LIVE TEST` eklendi; sample rate, jitter ve sample count kullanıcıya gösteriliyor.

Milestone 2 **READY**. Milestone 3 telefon entegrasyonu için teknik önkoşullar sağlandı.
