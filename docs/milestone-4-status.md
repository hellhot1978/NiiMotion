# Milestone 4 durum raporu

Güncelleme: 14 Ağustos 2026.

## Tamamlanan yazılım

- Joy-Con angular velocity tabanlı leg-motion evidence.
- Sol/sağ alternation, cadence ve confidence.
- `IDLE`, `STARTING`, `WALKING`, `FAST_WALK`, `RUNNING`, `STOPPING`, `TRACKING_DEGRADED` durum modeli.
- Start/stop hysteresis ve analog acceleration/deceleration smoothing.
- Telefon ritmi yalnız confidence bağlamıdır; tek başına step oluşturamaz.
- Torso-only ve tekrarlı tek-bacak hareketi locomotion başlatmaz.
- Version 1 kalibrasyon profili; rest mean/stddev, önerilen threshold ve cadence aralığı.
- Eksik kalibrasyon kaydedilmez; minimum rest/step örnekleri zorunludur.

## Doğrulama

- Build: 0 hata, 0 uyarı.
- Otomatik test: 20/20 başarılı.
- Gerçek Joy-Con ve telefon kaynakları önceki milestone'larda doğrulandı.
- Gerçek bacak montajı, baseline, çömelme, yerinde yürüyüş ve durma doğrulandı.
- Etiketli gerçek kayıt: `recordings/latest-labeled-validation.jsonl` (13.605 sample; yaklaşık 3,1 MiB).
- Son kabul sonucu: stand 0, crouch 0, walk 177 aktif örnek, stop 0 yanlış locomotion örneği.

## Sonuç

Milestone 4 **READY**. M5 analog VR output çalışması başlatılabilir. Reach ve oyun içi karma hareketler M9 gerçek Alyx test matrisinde ayrıca ölçülecek.
