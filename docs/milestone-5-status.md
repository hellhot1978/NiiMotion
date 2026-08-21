# Milestone 5 durum raporu

Güncelleme: 15 Ağustos 2026.

## Tamamlanan ilk spike

- Oyun bağımsız, iki eksenli normalize analog locomotion sözleşmesi.
- Output başlangıçta OFF; bağlı olmayan sink hareket kabul etmez.
- Attach sonrasında ilk değer daima sıfırdır.
- Normal stop sırasında önce sıfır yazılır, sonra detach edilir.
- Yazma hatasında fail-closed sıfırlama ve detach uygulanır.
- Native helper için sürümlü local named-pipe paketi (`NMR1`, X, Y).
- Değerler `[-1, 1]` aralığına sınırlandırılır; keyboard/WASD yoktur.
- OpenVR SDK 2.15.6 ile x64 `driver_niirmotion.dll` derlendi.
- Native sürücü `/input/joystick/x` ve `/input/joystick/y` scalar bileşenlerini sunar.
- Pipe kopması, standby, deactivation, cleanup veya 250 ms veri kesintisinde değerler sıfırlanır.
- Normal kullanıcı C# istemcisi → native SteamVR pipe hattı gerçek runtime üzerinde doğrulandı.
- Tanı sürücüsünde X/Y/click güncellemeleri SteamVR tarafından hatasız (`0/0/0`) kabul edildi ve kopuşta güvenli sıfır doğrulandı.
- Alyx'in yerel `actions.json` manifestinden hareket vektörü `/actions/move/in/TeleportTurn` olarak doğrulandı.
- Alyx'in `joy_forwardthreshold=0.15` eşiği tespit edildi; eski `0.10` smoke değeri oyun tarafından yok sayılıyordu. İleri doğrulama değeri güvenli `0.35` olarak düzeltildi.
- İlk Alyx testlerinde NiiRMotion scalar güncellemeleri hatasız kabul edildi fakat oyun hareket üretmedi.
- BodyWalk geçici karşılaştırma/fallback olarak yeniden etkinleştirildi; kendi Alyx binding'i kuruldu ve gövde-eğimi çıkışının oyunda çalıştığı görüldü. Bu davranış nihai NiiRMotion gait kuralı değildir.
- BodyWalk karşılaştırması sonrasında NiiRMotion treadmill cihazı geçerli sabit pose yayınlayacak şekilde düzeltildi (`TrackingResult_Running_OK`, her frame `TrackedDevicePoseUpdated`). Yeni DLL derlendi fakat gerçek SteamVR/Alyx testi cihazsız aşamada yapılmadı.
- Gait snapshot → smoothing → analog VR output oturum bağlantısı eklendi; başlangıç/duruş güvenli sıfır test edildi.

## Doğrulama

- Otomatik test: 34/34 başarılı (native DLL export, gerçek local pipe paketi, fusion ve output yaşam döngüsü dahil).
- SteamVR kurulumu ve Valve `vrpathreg.exe` bulundu.
- NiiRMotion sürücü kaydı karşılaştırma sırasında kaldırıldı; BodyWalk geçici olarak etkin. NiiRMotion kaynakları ve yeni DLL korunuyor.
- Taşınabilir Zig 0.16.0 ile derleme yapıldı; sistem çapında build tool kurulmadı.

## Kapanış sonucu

- NiiMotion sürücüsü SteamVR ve Half-Life: Alyx içinde gerçek Quest 3 donanımıyla doğrulandı.
- Gerçek Joy-Con gait akışı oyunda ileri hareket üretti; yan eksen daima sıfır tutularak sola kayma giderildi.
- Dönüş sırasında istemsiz ilerleme engellendi, yürüyüş akışı yumuşatıldı ve duruş gecikmesi güvenli hızlı sıfıra çekildi.
- Joy-Con + telefon ve yalnız Joy-Con kullanım mimarisi korunuyor; telefon isteğe bağlı doğrulama/hız katkısıdır.
- BodyWalkVR kaldırıldı; Normal VR modu NiiMotion sürücüsünü kapatıp oyunların özgün kontrol bağlamalarını doğrudan geri yükler.
- Otomatik doğrulama 41/41 başarılıdır.

M5 **READY**. Kalan işler farklı oyun/uzun oturum doğrulaması, son dağıtım cilası ve en son Balance Board entegrasyonudur.
