# NiiMotion

NiiMotion, yerinde yürüme hareketini Windows üzerinde SteamVR/OpenVR analog hareket girdisine dönüştüren, güvenli duruş öncelikli bir VR locomotion uygulamasıdır. Joy-Con, PS Move, owoTrack kullanan Android telefon ve Wii Balance Board sensörlerini ayrı veya birlikte kullanabilir. Normal VR profili seçildiğinde oyun girdisine müdahale etmez.

## Son kullanıcı kurulumu

`NiiMotion-Setup-0.6.0-x64.exe` kurucusunu çalıştırın. Paket .NET kurulumu gerektirmez; masaüstü kısayolu, kaldırma kaydı, NiiMotion OpenVR sürücüsü ve OpenXR API katmanını içerir. İlk açılışta sahip olduğunuz cihazları seçin, **Test ve Kalibrasyon** sayfasında her cihazın üç temel fazını tamamlayın, ardından Genel Bakış'tan profil ve oyunu seçin.

- [İlk kullanım](docs/first-run-guide-tr.md)
- [Cihaz kurulumu](docs/device-setup-tr.md)
- [Sorun giderme](docs/troubleshooting-tr.md)
- [Mimari](docs/architecture.md)
- [OpenCode / AI agent devralma rehberi](docs/OPENCODE_HANDOFF.md)
- [Bağımsız kullanım kabulü](docs/standalone-acceptance.md)
- [Üçüncü taraf bileşenleri](THIRD_PARTY_NOTICES.md)

## Güvenlik ve gizlilik

- Hareket çıkışı kapalı başlar; kritik sensör kesildiğinde 250 ms içinde sıfırlanır.
- Normal VR, özgün kontrolcü davranışını korur.
- Öğrenilmiş veriler sıfırlanmadan önce otomatik yedeklenir; uygulama, oyun ayarları ve cihaz fabrika kalibrasyonları korunur.
- Tanı paketi ham sensör kayıtlarını içermez; kullanıcı yolu, IP adresi ve Bluetooth kimlikleri maskelenir.
- Kişisel kayıtlar varsayılan olarak yalnız bilgisayarda tutulur.

## Geliştirme

```powershell
.\.dotnet\dotnet.exe build NiiRMotion.slnx -c Release
.\.dotnet\dotnet.exe run --project tests\NiiRMotion.Tests\NiiRMotion.Tests.csproj -c Release
.\scripts\build-installer.ps1
```

Kod devri ve günlük doğrulama için önerilen tek komut:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\verify-development.ps1
```

Depolama bütçesi: proje 15 GB'ı aşmamalı ve C: sürücüsünde en az 10 GB boş alan korunmalıdır.
