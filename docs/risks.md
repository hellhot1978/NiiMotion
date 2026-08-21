# Riskler

| Risk | Etki | Önlem |
|---|---|---|
| Alyx virtual locomotion binding uyumsuzluğu | Yüksek | M5'te izole OpenVR driver spike ve gerçek Alyx testi; fallback kararı kanıta göre |
| Joy-Con Bluetooth jitter/kopma | Yüksek | Per-device timing, bounded queues, reconnect, smooth zero; gerçek test matrisi |
| Yanlış pozitif crouch/lean | Yüksek | Leg-motion start şartı, hysteresis, replay senaryoları |
| Private VD API değişimi | Orta | Private API yok; güvenilmez durum Unknown ve kullanıcı talimatı |
| Eski kernel dependency | Yüksek | ViGEm/HidHide default değil; kullanıcı onayı olmadan kurulum yok |
| Hardware olmadan sahte başarı | Yüksek | Mock açık etiketli; rapor: UNTESTED — HARDWARE NOT AVAILABLE |
| Kayıtların diski doldurması | Yüksek | 5 GB kota/rotasyon, proje 15 GB üst sınırı, düzenli boyut ölçümü |
