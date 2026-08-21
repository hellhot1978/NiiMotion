# Milestone 3 durum raporu

Güncelleme: 12 Ağustos 2026.

## Uygulanan

- UDP/6969 üzerinde NiiRMotion Phone Protocol v1.
- Rastgele session token doğrulaması; yanlış token paketleri sessizce reddedilir.
- Device ID, sequence, phone send timestamp, PC monotonic receive timestamp.
- Quaternion orientation, linear acceleration ve angular velocity payload'ı.
- Packet loss, out-of-order, sample rate, jitter ve packet age ölçümü.
- Bounded channel tabanlı `PhoneSensorSource`; Joy-Con kaynaklarıyla aynı abstraction.
- Gerçek UDP socket üzerinden localhost entegrasyon testi.

## Karar

owoTrack güncel ve SlimeVR tarafından destekleniyor, ancak doğrudan NiiRMotion'a güvenli/timestamp'li bağlantı için protokol adaptörü veya küçük Android sender gerekir. Android SDK/workload kurulumu birkaç GB ekleyebileceğinden depolama bütçesi gereği otomatik kurulmadı.

## Gerçek cihaz doğrulaması

- owoTrack Android cihaz `192.168.31.117` üzerinden bağlandı.
- Native handshake, quaternion rotation, acceleration ve gyroscope paketleri doğrulandı.
- 120 orientation sample: yaklaşık 202 Hz, 3,46 ms jitter, 1 missing, 0 out-of-order.
- Gerçek telefon sample'ları JSONL recording/replay ile doğrulandı.
- owoTrack send timestamp taşımadığı için packet age receive timestamp tabanlıdır; NiiRMotion v1 protokolü send timestamp destekler.

Milestone 3 **READY**. Milestone 4 gait/locomotion engine için önkoşullar sağlandı.
