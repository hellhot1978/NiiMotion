# Mimari

## Katmanlar

- `NiiRMotion.Core`: cihaz modelleri, profiller, readiness/session kuralları; platformdan bağımsız.
- `NiiRMotion.Infrastructure`: Windows süreç ve donanım keşfi; HID/UDP kaynakları, yerel kayıt/analiz, SteamVR/OpenXR adaptörleri, tanılama ve güvenli başlatma zinciri.
- `NiiRMotion.App`: WPF presentation; polling/fusion UI thread'inde çalışmayacak.
- `NiiRMotion.Tests`: dış paket gerektirmeyen deterministik çekirdek test çalıştırıcısı.

## Yaşam döngüsü

1. Uygulama locomotion OFF açılır.
2. Discovery required/optional durumlarını üretir; Unknown, Connected sayılmaz.
3. Readiness evaluator başlatmayı engeller veya degraded modu bildirir.
4. `LiveLocomotionService`, zorunlu Joy-Con kaynaklarını ve varsa owoTrack telefon kaynağını başlatır; fusion ve fail-closed VR output'u bağlar.
5. Stop/cancel önce hedef hızı sıfıra yumuşatır, output'u detach eder, kayıtları finalize eder ve kaynakları ters sırada dispose eder.

## Sensör füzyonu ve analog çıkış

- `SensorFusionEngine`, seçili profile ait Joy-Con ve/veya PS Move bacak kanıtını locomotion başlatıcısı olarak tutar.
- Telefon ritmi ve Balance Board ağırlık aktarımı yalnızca güncel olduklarında confidence'ı değiştirir; tek başlarına target speed üretemezler.
- Opsiyonel veri stale olduğunda Joy-Con-only degraded mode devam eder.
- `BalanceBoardSample` dört load-cell değerinden total load ve normalize CoP X/Y üretir.
- `BalanceBoardCalibration`, board belleğindeki sensör başına 0/17/34 kg fabrika noktalarını piecewise-linear dönüştürür.
- `VrLocomotionSession`, fusion target speed'i `LocomotionSmoother` üzerinden analog sink'e taşır; attach, stop, hata ve dispose yolları güvenli sıfırı korur.
- WPF başlatma/durdurma akışı `LiveLocomotionService` yaşam döngüsünü kullanır. Seçili profil için zorunlu sensörlerden biri eksikse başlamaz; daha basit profile geçiş yalnız açık kullanıcı onayıyla yapılır.
- Balance Board HID, fabrika kalibrasyonu, yük/CoP türetimi, kayıt/replay ve güvenli dönüş ayrımı uygulanmıştır. Board yürüyüş başlangıcı ve ağırlıkla dönüşün son ürün kabulü gerçek kullanıcı matrisi içinde tamamlanacaktır.
- HMD yalnız doğrulanmış ve taze olduğunda zayıf dönüş kaynaklı yanlış ileri hareketi bastırabilir; yürüyüşü başlatamaz.

## Depolama bütçesi

- Proje toplamı en fazla 15 GB; en az 10 GB C: boş alan korunur.
- SDK proje içinde tek kopya. `bin/obj`, eski publish ve geçici indirmeler temizlenebilir.
- Yeniden üretilebilir günlük ve önbellekler sınırlıdır. Ham kişisel kayıtlar otomatik silinmez; yedekleme, geri dönüş ve açık onaylı sıfırlama akışları kullanılır.
