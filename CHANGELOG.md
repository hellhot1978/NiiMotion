# Changelog

## 0.7.0-dev

- Added a deliberate release-candidate pipeline with installer lifecycle verification, component inventory, source-commit provenance and checksums; hardware acceptance and signing remain explicit external gates.
- Removed the legacy developer-specific gait calibration from public packaging; clean installations now rely only on locally validated phase recordings and generated personal runtime models.
- Isolated installer smoke tests from the owner's desktop shortcut so test uninstall can no longer remove an existing NiiMotion shortcut.
- Added a privacy-safe local session-health summary to diagnostic packages without copying raw sensor payloads.
- Added Windows CI, release-readiness contracts, a machine-readable hardware acceptance matrix, a security threat model, and a closed-beta acceptance plan.
- Updated architecture, roadmap, standalone acceptance, and release documentation to match the implemented V5 system.
- Added model-independent handoff entry points for Codex/OpenCode, Claude Code, Gemini CLI and other coding agents, plus a reusable continuation prompt.
- Expanded the automated regression suite to 108 passing checks and kept localization/UI smoke as separate release gates.
- Added an automated installer safety contract covering per-user installation, self-contained packaging, VR component unregister, personal-data preservation, and a single application/installer version source.
- Added bilingual compact/standard UI smoke rendering and made it part of optional development acceptance.
- Added standalone/runtime readiness, local model integrity repair, privacy-safe diagnostics, and OpenCode handoff documentation.
- Expanded the automated regression suite to 102 passing checks.
- Added crash recovery, safe-zero startup, and versioned configuration migration.
- Added generic process-scoped OpenXR game adapters and engine-aware discovery.
- Added guided onboarding, accessibility preferences, Turkish/English UI localization foundations, and a live VR panel viewer with emergency-stop commands.
- Added verified update staging, release integrity manifests, privacy-safe diagnostics, and expanded recovery tooling.
- Expanded the automated regression suite to 82 passing checks, including a four-hour accelerated endurance simulation.

## 2026-08-15 - Balance Board doğal yürüyüş ve ağırlıkla dönüş

- Board adım motorunda geçersiz geçişlerin çıkışı yeniden açıp kapatmasına yol açan durum ayrıldı.
- Board içeren profillere, yürüyüş durduktan sonra ağırlığı sağda/ solda tutarak güvenli dönüş eklendi.
- Dönüş sırasında ileri çıkış sıfırlanıyor; merkeze dönünce dönüş hemen bırakılıyor.
- Board kuralları yalnızca Board seçilen profillerde çalışıyor.
- Alyx sağ çubuk kesintisiz dönüş ölü bölgesi düşürüldü ve fiziksel kontrolcü bağlaması korunuyor.
- Canlı günlükler ileri hız ile dönüş hedefini ayrı kaydediyor.

## 2026-08-15 — Normal VR geri dönüşü

- Normal VR kutusu artık yalnız profil seçmek yerine NiiMotion çıkışını durdurur, sürücüyü kaldırır ve oyunların özgün kontrol bağlamalarını anında geri yükler.
- BodyWalkVR bağımlılığı ve SteamVR ayar anahtarı NiiMotion yaşam döngüsünden tamamen çıkarıldı.

## 2026-08-15 — Dayanıklılık ve ürünleştirme

- Son başarılı yürüyüş davranışı; düz eksen, 200 ms içinde kullanılabilir başlangıç hızı ve 100 ms içinde çıkış sıfırlama sözleşmeleriyle kilitlendi.
- Kritik Joy-Con veri kaybında VR çıkışını güvenli durdurup kullanıcıya yeniden bağlama yönlendirmesi eklendi.
- İsteğe bağlı telefon kaybı Joy-Con yürüyüşünü durdurmadan degraded çalışmaya devam eder.
- Canlı tanı günlüklerine 256 MB otomatik kota ve en yeni kayıtları koruyan rotasyon eklendi.
- Gizli başlatıcı, debug/.NET bağımlılığı yerine bağımsız son kullanıcı uygulamasını açacak şekilde düzeltildi.
- Otomatik doğrulama paketi 41/41 başarılı.

## 2026-08-15 — Son kullanıcı VR portalı

- Ana ekran teknik kontrol panelinden büyük düğmeli, tek ekranlık VR giriş portalına dönüştürüldü.
- Portal, renk bloklu oyun başlatıcı görünümüne geçirildi; Joy-Con, telefon, Balance Board, el takibi, Quest ve SteamVR için ayırt edilebilir ikonlar eklendi.
- Sekiz cihazın tamamı kaydırma gerektirmeyen iki sütunlu kompakt kartlarda gösteriliyor.
- Normal VR, Sadece Joy-Con, Joy-Con + Telefon, Tüm Cihazlar ve deneysel Sadece Telefon seçimleri eklendi.
- SteamVR'ı doğrudan açma, bağlı cihazları canlı görme ve isteğe bağlı el takibi seçimi aynı ekranda toplandı.
- Normal VR seçimi NiiMotion çıkışını ve sürücüsünü kapatarak özgün sisteme döner.
- Telefon tek başına modunda tek hareket örneği yürüyüş başlatmaz; sürekli hareket doğrulaması ve stale-stop koruması eklendi.
- Balance Board entegrasyonu kullanıcının isteğiyle son aşamaya taşındı.
- Otomatik doğrulama paketi 39/39 başarılı.

## 2026-08-15 — Desktop control dashboard

- Rebuilt the WPF desktop UI as a compact dark dashboard with profile selection, readiness overview, per-device guidance, quick diagnostics, safe START/STOP controls, and live fusion telemetry.
- Refined the dashboard into a dense blue-accent control-center layout with a persistent system/sidebar summary, four-column device overview, and clearer separation between session controls and fusion telemetry.
- Added Full Fusion, Joy-Con Only, and non-invasive Classic VR profiles.
- Added a clearly separated demo mode that animates telemetry without sending output to SteamVR.
- Added automatic UI screenshot capture for repeatable visual verification.
- Verified the complete solution with a clean 0-warning build and 34/34 passing tests.

## 2026-08-14 — Device-free fusion/output hardening

- Added four-load-cell Balance Board sample model, normalized CoP, factory 0/17/34 kg calibration parser, extension payload parser, and replay reader.
- Added asynchronous confidence fusion where phone and board context can confirm/degrade but never create gait without Joy-Con leg evidence.
- Added `VrLocomotionSession` to connect fused gait speed to smooth fail-closed analog VR output.
- Replaced the WPF START/STOP placeholder with `LiveLocomotionService`: required dual Joy-Con acquisition, optional owoTrack degradation, 100 Hz fusion/output loop, cancellation, and deterministic cleanup.
- Updated the native treadmill to publish a valid stationary pose every SteamVR frame so the action source can remain active without taking a hand role.
- Built the next native driver and expanded the automated suite to 34/34 passing tests.
- Kept BodyWalk only as a temporary comparison/fallback; torso tilt locomotion is not accepted as NiiRMotion's final behavior.
- Removed reproducible Zig caches, a stale native PDB, and the BodyWalk crash dump; project size reduced from about 1.77 GB to 1.58 GB.

## 2026-08-14

- M4 gerçek uyluk montajıyla kalibre edildi; stand/crouch/walk/stop doğrulaması yanlış locomotion olmadan geçti.
- Çevrimdışı ayar için etiketli gerçek Joy-Con hareket kaydı eklendi.
- M5 fail-closed analog VR output controller ve sürümlü named-pipe transport eklendi.
- Output OFF koruması, clamp, zero-on-start/stop ve hata halinde detach testleri eklendi; toplam 24/24 test başarılı.
- OpenVR SDK 2.15.6 tabanlı x64 native locomotion driver DLL'i, joystick input profili ve 250 ms fail-safe eklendi.
- Native export ve gerçek named-pipe protokol testleriyle toplam 26/26 test başarılı.
- Normal kullanıcıdan SteamVR native driver'a gerçek pipe smoke testi geçti; başlangıç ve bitiş sıfırlandı.
- BodyWalk sürücüsü devre dışı bırakıldı; NiiRMotion el rollerinden ayrılarak `GenericTracker` + Alyx `/user/treadmill` binding'ine taşındı.

## 0.1.0-dev — 2026-08-12

- Milestone 0 araştırma, gereksinim, mimari, risk ve task belgeleri eklendi.
- .NET 10/WPF solution ve katmanlı proje yapısı oluşturuldu.
- SteamVR/Virtual Desktop süreç keşfi ve tüm hedef cihazlar için dürüst durum/talimat UI'ı eklendi.
- Required/optional readiness, TEST MODE ayrımı ve locomotion OFF yaşam döngüsü iskeleti eklendi.
- Dış test paketi gerektirmeyen çekirdek doğrulama çalıştırıcısı eklendi.
- 15 GB proje üst sınırı ve 10 GB asgari boş disk koruma gereksinimi belgelendi.
- Milestone 2 başladı: original Joy-Con L/R Nintendo VID/PID keşfi eklendi.
- Joy-Con `0x30` raporundan üç IMU alt örneği, monotonic timestamp ve sıra numarası çıkarımı eklendi.
- Asenkron HID read loop, IMU/report-mode başlangıç komutları ve sample-rate/jitter tanılaması eklendi.
- JSONL sensor recording ve hızlı/gerçek-zamanlı replay altyapısı eklendi.
- Windows Bluetooth HID'in bildirdiği report boyutlarını dinamik okuma eklendi.
- Gerçek Joy-Con L/R ile yaklaşık 198 Hz IMU akışı ve ~1,9 ms jitter doğrulandı.
- Joy-Con SPI factory IMU calibration okuma ve cihaz başına ölçekleme eklendi.
- Gerçek L/R verisiyle 300/300 recording/replay doğrulandı.
- Ana ekrana Joy-Con Live Test ölçümü eklendi; Milestone 2 READY durumuna getirildi.
- Milestone 3 başladı: token doğrulamalı UDP phone protocol ve `PhoneSensorSource` eklendi.
- Telefon orientation/accel/gyro, send/receive timestamp, loss/order/jitter ölçümü eklendi.
- Gerçek UDP socket üzerinden yanlış-token reddi ve packet round-trip testi eklendi.
- Native owoTrack handshake ve big-endian rotation/gyro/acceleration adaptörü eklendi.
- Gerçek Android telefonla ~202 Hz orientation akışı ve record/replay doğrulandı.
- Ana ekrana Phone Test eklendi; Milestone 3 READY durumuna getirildi.
- Milestone 4 başladı: leg-evidence gait engine, cadence/alternation confidence, state hysteresis ve analog speed smoothing eklendi.
- Telefon/torso hareketinin tek başına locomotion başlatmaması otomatik testle güvenceye alındı.
- Tekrarlı tek-bacak hareketinin walking durumuna geçmesi engellendi.
- Versioned gait calibration profili, rest noise istatistikleri ve threshold önerisi eklendi.
- Milestone 4 gerçek uyluk montajı bulunana kadar hardware-waiting durumuna getirildi.
# 2026-08-15 — Learned gait pace prior

- Added a compact pace model trained from 3,643 two-second DeepGait windows across eight subjects (0.5–10 km/h).
- Combined cadence and thigh angular-velocity amplitude with the existing deterministic safety gait engine.
- Personalized the public-data amplitude scale using NiiMotion's isolated left/right Joy-Con capture.
- Added safe fallback to the deterministic model when the learned model file is unavailable.
- Added head-following virtual-controller pose to remove world-axis locomotion drift.
- Added HuGaDB as an offline bilateral/activity reference without expanding its 421 MB archive.
- Trained an 80-tree shallow HuGaDB activity gate on 25,651 bilateral windows. Cross-subject accuracy reached 86.4%, below the 90% live-veto threshold, so it remains offline rather than risking false gait rejection.
- The live session status now explicitly reports `DEEPGAIT PACE` or its safe heuristic fallback.
- Test suite: 37/37 passing.
# 2026-08-15 — Sistem modu ve seçim görünürlüğü

- Seçili oyun profiline kalın beyaz çerçeve ve `✓ SEÇİLİ` rozeti eklendi.
- Etkin sistem modu `✓ NORMAL VR` / `✓ NIIMOTION` olarak belirginleştirildi.
- Zaten etkin olan sistem moduna yeniden basılması artık SteamVR'ı yeniden başlatmıyor.
- Normal VR seçiliyken gereksiz `BAĞLANTI GEREKİYOR` uyarısı kaldırıldı.
- Uygulama mevcut sistem modu Normal VR ise doğru profille açılıyor.
- Düğme gibi görünen `GERÇEK CİHAZLAR` öğesi, açıklayıcı `CANLI CİHAZ DURUMU` etiketine dönüştürüldü.
# 2026-08-15 — Gerçek Wii Balance Board bağlantısı

- Windows 11 üzerinde `Nintendo RVL-WBC-01` PIN'siz eşleştirildi ve `057E:0306` Bluetooth HID kanalı doğrulandı.
- Donanım keşfi artık eşleştirme kaydı yerine aktif Balance Board HID arabirimini gösteriyor.
- Canlı karttan boş/yüklü/boşa dönüş olmak üzere üç ölçümde 100'er örnek alındı.
- Yaklaşık -5 kg sabit boş-kart sapması otomatik dört-köşe darasıyla giderildi.
- `Tüm Cihazlar` profili Balance Board kaynağını açıyor ve ağırlık aktarımını fusion güvenine ekliyor.
# 2026-08-15 — Görsel Balance Board laboratuvarı

- `Sadece Board`, `Board + Joy-Con` ve `Board + Telefon` profilleri eklendi.
- Board ölçümleri terminal yerine uygulama içindeki yönlendirmeli laboratuvara taşındı.
- Sabit, yavaş, doğal, hızlı, dönüş ve dur/in aşamaları; geri sayım, canlı yük, basınç merkezi ve geçiş sayacı eklendi.
- Eski çizgi karakter; gövdeli, eklemli, ayakkabılı ve Board üzerindeki basınç noktasını gösteren vektör karakterle değiştirildi.
- Board bağlantısı uygulama boyunca paylaşılarak eski Wii sürücüsünün kapanış çökmesi engellendi.
- Açılış darası 200 örnekli köşe medyanına geçirildi; boş kartta ortalama 0,057 kg ve sıfır yanlış geçiş doğrulandı.
