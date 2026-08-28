# Yol haritası ve bağımlılıklar

- **M0:** gereksinimler → araştırma → risk/dependency kararı → mimari. Tamamlandı.
- **M1:** solution → core profiles/readiness → process discovery → WPF shell → mock ayrımı → tests/build/smoke. Uygulandı.
- **M2 (tamamlandı):** Joy-Con HID spike ✓ → L/R identity ✓ → calibrated IMU timestamps ✓ → timing diagnostics ✓ → bounded source ✓ → JSONL record/replay ✓ → SPI factory calibration ✓ → UI live diagnostics ✓ → gerçek L/R cihaz testi ✓.
- **M3 (tamamlandı):** timestamped UDP v1 + session token ✓ → phone source ✓ → loss/order/timing ✓ → owoTrack native adapter ✓ → real orientation/accel/gyro ✓ → recording/replay ✓ → UI Phone Test ✓.
- **M4 (tamamlandı):** leg evidence ✓ → alternation/cadence ✓ → state/hysteresis ✓ → analog smoothing ✓ → torso/single-leg rejection ✓ → versioned calibration ✓ → gerçek leg calibration + stand/crouch/walk/stop doğrulaması ✓.
- **M5 (tamamlandı):** fail-closed analog output contract ✓ → zero-before-attach/detach lifecycle ✓ → named-pipe v1 transport ✓ → OpenVR driver ✓ → scalar component ✓ → bindings ✓ → SteamVR testi ✓ → Alyx gerçek yürüyüş testi ✓.
- **M6 (yazılım tamamlandı):** Board HID ✓ → calibration/load cells ✓ → CoP/transfer ✓ → record/replay ✓ → confidence contribution ✓. Gerçek Board locomotion kabulü bekliyor.
- **M7 (yazılım tamamlandı):** async fusion ✓ → per-source/global confidence ✓ → stale/conflict handling ✓ → replay tuning ✓. Farklı oyunlarda fiziksel doğrulama bekliyor.
- **M8 (yazılım tamamlandı):** preflight/session orchestration ✓ → calibration validity ✓ → helper lifecycle ✓ → sensör kopmasında güvenli duruş ✓ → dört saat hızlandırılmış dayanıklılık ✓. Gerçek uzun oturum bekliyor.
- **M9 (yazılım tamamlandı):** VD/Alyx doğrulaması ✓ → false-positive gerçek senaryolar ✓ → self-contained paket ✓ → kurucu ve kısayollar ✓ → OpenXR katmanı ✓. Temiz Windows ve farklı oyun kabulü bekliyor.
- **M10 (yayın kapısı):** makinece okunabilir donanım kabul matrisi ✓ → CI doğrulaması ✓ → güvenlik tehdit modeli ✓ → gerçek cihaz/oyun matrisi → temiz Windows → imzalı sürüm adayı.

Her milestone önceki milestone build, tests, docs ve gerçek-donanım durum raporu olmadan kapanmaz.
