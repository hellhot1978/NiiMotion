# Yol haritası ve bağımlılıklar

- **M0:** gereksinimler → araştırma → risk/dependency kararı → mimari. Tamamlandı.
- **M1:** solution → core profiles/readiness → process discovery → WPF shell → mock ayrımı → tests/build/smoke. Uygulandı.
- **M2 (tamamlandı):** Joy-Con HID spike ✓ → L/R identity ✓ → calibrated IMU timestamps ✓ → timing diagnostics ✓ → bounded source ✓ → JSONL record/replay ✓ → SPI factory calibration ✓ → UI live diagnostics ✓ → gerçek L/R cihaz testi ✓.
- **M3 (tamamlandı):** timestamped UDP v1 + session token ✓ → phone source ✓ → loss/order/timing ✓ → owoTrack native adapter ✓ → real orientation/accel/gyro ✓ → recording/replay ✓ → UI Phone Test ✓.
- **M4 (tamamlandı):** leg evidence ✓ → alternation/cadence ✓ → state/hysteresis ✓ → analog smoothing ✓ → torso/single-leg rejection ✓ → versioned calibration ✓ → gerçek leg calibration + stand/crouch/walk/stop doğrulaması ✓.
- **M5 (tamamlandı):** fail-closed analog output contract ✓ → zero-before-attach/detach lifecycle ✓ → named-pipe v1 transport ✓ → OpenVR driver ✓ → scalar component ✓ → bindings ✓ → SteamVR testi ✓ → Alyx gerçek yürüyüş testi ✓.
- **M6:** Board HID → calibration/load cells → CoP/transfer → record/replay → confidence contribution.
- **M7 (büyük ölçüde tamamlandı):** async fusion ✓ → per-source/global confidence ✓ → stale/conflict handling ✓ → replay tuning ✓ → farklı oyun doğrulaması bekliyor.
- **M8 (devam ediyor):** preflight/session orchestration ✓ → calibration validity ✓ → helper lifecycle ✓ → Joy-Con kopmasında güvenli duruş ✓ → uzun oturum testi bekliyor.
- **M9 (devam ediyor):** VD/Alyx doğrulaması ✓ → false-positive gerçek senaryolar ✓ → bağımsız tek-dosya paket ✓ → masaüstü kısayolu ✓ → latency/uzun oturum ve farklı oyun doğrulaması bekliyor.

Her milestone önceki milestone build, tests, docs ve gerçek-donanım durum raporu olmadan kapanmaz.
