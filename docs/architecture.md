# Mimari

## Katmanlar

- `NiiRMotion.Core`: cihaz modelleri, profiller, readiness/session kuralları; platformdan bağımsız.
- `NiiRMotion.Infrastructure`: Windows süreç ve donanım keşfi; ileride HID, UDP, kayıt ve SteamVR adaptörleri.
- `NiiRMotion.App`: WPF presentation; polling/fusion UI thread'inde çalışmayacak.
- `NiiRMotion.Tests`: dış paket gerektirmeyen deterministik çekirdek test çalıştırıcısı.

## Yaşam döngüsü

1. Uygulama locomotion OFF açılır.
2. Discovery required/optional durumlarını üretir; Unknown, Connected sayılmaz.
3. Readiness evaluator başlatmayı engeller veya degraded modu bildirir.
4. `LiveLocomotionService`, zorunlu Joy-Con kaynaklarını ve varsa owoTrack telefon kaynağını başlatır; fusion ve fail-closed VR output'u bağlar.
5. Stop/cancel önce hedef hızı sıfıra yumuşatır, output'u detach eder, kayıtları finalize eder ve kaynakları ters sırada dispose eder.

## Sensör füzyonu ve analog çıkış

- `SensorFusionEngine`, Joy-Con leg evidence'ı tek locomotion başlatıcısı olarak tutar.
- Telefon ritmi ve Balance Board ağırlık aktarımı yalnızca güncel olduklarında confidence'ı değiştirir; tek başlarına target speed üretemezler.
- Opsiyonel veri stale olduğunda Joy-Con-only degraded mode devam eder.
- `BalanceBoardSample` dört load-cell değerinden total load ve normalize CoP X/Y üretir.
- `BalanceBoardCalibration`, board belleğindeki sensör başına 0/17/34 kg fabrika noktalarını piecewise-linear dönüştürür.
- `VrLocomotionSession`, fusion target speed'i `LocomotionSmoother` üzerinden analog sink'e taşır; attach, stop, hata ve dispose yolları güvenli sıfırı korur.
- WPF START/STOP, `LiveLocomotionService` yaşam döngüsünü kullanır. İki Joy-Con yoksa başlamaz; telefon portu kullanılamıyorsa Joy-Con-only degraded mode'a geçer.
- Gerçek Balance Board HID bağlantısı ve rapor offset doğrulaması cihaz bağlanana kadar `UNTESTED — HARDWARE REQUIRED` durumundadır.

## Depolama bütçesi

- Proje toplamı en fazla 15 GB; en az 10 GB C: boş alan korunur.
- SDK proje içinde tek kopya. `bin/obj`, eski publish ve geçici indirmeler temizlenebilir.
- Recordings için varsayılan 5 GB toplam kota, dosya başına rotasyon ve kullanıcıya görünen kullanım planlanmıştır.
