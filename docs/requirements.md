# Gereksinimler

## Ürün ve güvenlik

- Windows 11 x64 masaüstü ürünü; kullanıcıya görünen ad ve kod kökü `NiiRMotion`.
- Başlangıçta ve kalibrasyonda locomotion OFF. STOP güvenli sıfır, detach ve dispose yapar.
- WASD/klavye çıktısı, varsayılan ViGEmBus, otomatik HidHide veya kalıcı input interception yok.
- Gerçek donanım doğrulanmadan Connected/READY gösterilmez. Test ve replay açıkça etiketlenir.
- Required cihaz eksikse başlatma engellenir; optional cihaz eksikse degraded mode açıklanır.

## Veri hattı

`ISensorSource → timestamped bounded channels → Sensor Hub → confidence fusion → gait state → smoothing → VR output`

- Monotonic timestamp, kaynak kimliği, sıra numarası, packet age/jitter/loss ölçümü.
- Asenkron sensör katkısı; sensörler birbirini beklemez.
- Torso tilt tek başına adım değildir. Joy-Con leg-motion ana başlangıç kanıtıdır.
- Gait ve yön ayrı; ilk yön HMD-relative. Joy-Con absolute yaw ve Board yaw referansı değildir.
- Record/replay ilk gerçek sensör milestone'undan itibaren zorunlu mimari parçasıdır.

## Milestone 1 kabul ölçütleri

- Çalışan WPF kabuğu, Quest/SteamVR/VD/hand/Joy-Con L/R/phone/board durum satırları.
- Eksik cihazlarda eyleme dönük talimat ve Tekrar Tara.
- Required-device readiness testi, locomotion varsayılan OFF, açık TEST MODE.
- Donanım yokken çökmeden çalışması; build ve otomatik çekirdek testlerinin geçmesi.
