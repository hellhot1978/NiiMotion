# Milestone 0 araştırması

Güncelleme: 12 Ağustos 2026. Kararlar resmî dokümantasyon ve proje depolarının güncel durumuna dayanır.

## Bulgular

- **Platform:** .NET 10 LTS Kasım 2028'e kadar destekli ve Windows 11 x64 destekleniyor. WPF, yalnız Windows hedefi ve yerleşik masaüstü runtime nedeniyle uygun; sensor/fusion katmanları WPF bağımlılığı taşımamalı. Kaynaklar: [Microsoft .NET destek politikası](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support), [Windows kurulum matrisi](https://learn.microsoft.com/en-us/dotnet/core/install/windows).
- **SteamVR output:** Valve'ın OpenVR driver API'si scalar input component oluşturma/güncelleme ve uygulama özel binding sağlar. Alyx için `steam.app.546560` binding'i mümkündür. En güçlü aday, yalnız oturumda aktif olan küçük bir SteamVR driver virtual controller'dır; Milestone 5'te gerçek SteamVR smoke testinden önce kesinleştirilecektir. Kaynak: [Valve OpenVR Driver API](https://github.com/ValveSoftware/openvr/blob/master/docs/Driver_API_Documentation.md).
- **OpenXR:** uygulamaların action'larını dışarıdan genel biçimde enjekte eden taşınabilir bir uygulama API'si değildir. Alyx/SteamVR uyumluluğu için OpenVR driver yolu daha doğrudan görünmektedir; bu bir araştırma çıkarımıdır.
- **Virtual Desktop:** resmî release akışı hand/controller geçişleri ve SteamVR driver iyileştirmelerinin sürdüğünü gösteriyor. Private API kullanılmayacak; NiiRMotion yalnız süreç/health gösterecek, güvenilir biçimde okunamayan hand tracking durumu Unknown olacaktır. Kaynak: [Virtual Desktop releases](https://github.com/guygodin/VirtualDesktop/releases).
- **Telefon:** SlimeVR Server owoTrack telefonlarını, Joy-Con Wrangler'ı ve OpenVR driver'ı aktif ekosistem içinde destekliyor. İlk aday, açık SolarXR/OSC-benzeri timestamped UDP adaptörü; harici SlimeVR zorunlu bağımlılık olmayacak. Kaynak: [SlimeVR Server](https://github.com/SlimeVR/SlimeVR-Server).
- **Joy-Con:** JoyShockLibrary Windows/Bluetooth, HIDAPI ve tüm IMU alt örneklerini açma açısından güçlü referans/adaptör adayıdır. Linux `hid-nintendo` sürücüsü packet/calibration davranışı için ikinci bağımsız referanstır. Lisans ve native dağıtım Milestone 2 spike'ında doğrulanmalıdır. Kaynaklar: [JoyShockLibrary](https://github.com/JibbSmart/JoyShockLibrary), [hid-nintendo](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-nintendo.c).
- **Windows discovery:** `DeviceInformation.CreateWatcher` ekleme/güncelleme/çıkarma olaylarıyla uygun keşif API'sidir; isim kimlik olarak kullanılmamalıdır. Kaynak: [Microsoft DeviceInformation](https://learn.microsoft.com/en-us/uwp/api/windows.devices.enumeration.deviceinformation).
- **Balance Board:** güncel Windows projeleri seyrek ve çoğu eski Wiimote katmanlarına dayanıyor. Mevcut recorder proje/protokol referansı olabilir, ana dependency yapılmayacaktır. Kaynak: [Wii Balance Board Recorder](https://github.com/tomvredeveld/wii-balance-board-recorder).
- **ViGEm/HidHide:** ViGEmBus 2023'te arşivlenmiş ve retired; default değildir. HidHide kernel filtre olduğundan yalnız kanıtlanmış çift-input sorunu ve açık kullanıcı onayıyla değerlendirilebilir. Kaynaklar: [ViGEmBus](https://github.com/nefarius/ViGEmBus), [HidHide](https://github.com/nefarius/HidHide).

## Bağımlılık / lisans / aktivite

| Bileşen | Rol | Lisans | Aktivite | Karar |
|---|---|---|---|---|
| .NET 10 / WPF | App ve UI | MIT / Microsoft dağıtımı | LTS, 2028'e dek | Kullan |
| Valve OpenVR | SteamVR driver API | BSD-3-Clause | Resmî, aktif doküman | M5 adayı |
| JoyShockLibrary/HIDAPI | Joy-Con spike | MIT / BSD-benzeri | Güncel repo | M2'de doğrula |
| SlimeVR/SolarXR | Phone/protokol referansı | Apache-2.0 + MIT | Aktif | Adaptör adayı |
| WBB Recorder/Wiimote referansları | Board protokolü | MIT + üçüncü taraflar | Düşük aktivite | Kod alma, yalnız referans |
| ViGEmBus | Virtual gamepad | BSD-3-Clause | Retired/archived | Varsayılan değil |
| HidHide | Input filter | MIT, kernel driver | Bakımlı | Otomatik kurma |

## Açık doğrulamalar

- Virtual Desktop hand tracking'in güncel Quest/Streamer sürümü ve Alyx binding'i gerçek sistemde manuel doğrulanmalı.
- OpenVR driver ile yalnız locomotion scalar sağlayan cihazın Alyx controller eşleşmesine etkisi M5 prototipinde ölçülmeli.
- Joy-Con ve Board eşzamanlı Bluetooth sample rate/jitter gerçek donanımla ölçülmeli.
# Learned gait data sources (2026-08-15)

- DeepGait (`data/external/DeepGait`, GPL-3.0): eight subjects with thigh/shin/foot accelerometer and gyroscope data plus measured speed. NiiMotion uses 3,643 walking/light-running windows between 0.5 and 10 km/h. The distilled model is `models/deepgait-pace-v1.json`.
- HuGaDB (`data/external/HuGaDB`, repository dataset): 18 subjects and bilateral thigh/shin/foot IMU activity recordings. Reserved for offline gait/non-gait and bilateral-symmetry modeling; not mixed into speed regression because it has no continuous speed ground truth.
- HuGaDB gate result: 25,651 windows, 80 shallow trees, 86.4% leave-one-subject-out accuracy. The artifact is retained at `models/hugadb-activity-gate-v1.json`, but it is intentionally not allowed to veto live motion until cross-subject accuracy reaches at least 90%.
- Personalization source: `recordings/leg-balance.jsonl`; isolated lift p95 values are 157.3 dps left and 140.9 dps right. The DeepGait gyro amplitude is scaled to this device/user domain before inference.
- DeepGait leave-one-subject-out MAE for the compact cadence + thigh-gyro model: 0.72 km/h. It is blended 70% learned / 30% deterministic so model failure cannot bypass gait safety gates.
