# NiiMotion OpenCode devralma rehberi

Güncelleme: 26 Ağustos 2026

Depo: `C:\NiirMotion`

Ana dal: `main`

## İlk komut

OpenCode'u depo kökünde aç ve agente şunu söyle:

> Read AGENTS.md and docs/OPENCODE_HANDOFF.md completely. Run the repository verification script before editing. Preserve all user recordings, runtime config, device identities, and unrelated dirty files. Continue only from the current remaining-work list and never introduce an online AI runtime dependency.

Ardından:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/verify-development.ps1
```

Çalışan uygulama paketi de gerekiyorsa:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/verify-development.ps1 -Publish
```

Kurucu yalnız sürüm adayı hazırlanırken üretilmelidir; günlük geliştirmede çalıştırılmamalıdır.

## Güncel gerçek durum

- WPF `.NET 10` uygulaması self-contained `win-x64` yayınlanıyor.
- Joy-Con, PS Move, owoTrack telefon ve Wii Balance Board tekil/karma profilleri yazılımda mevcut.
- Her hareket cihazı için üç adet yönlendirmeli beş dakikalık temel faz, duraklatma, sorunlu segmenti yenileme ve fazı silip yeniden çekme akışı mevcut.
- Yerel `OfflineCalibrationPipeline` onaylı JSONL kayıtlarından kişisel modelleri üretir. Oyun başlatmadan önce hem ilerleme kaydı hem gerçek model dosyası doğrulanır; uygun kayıt varsa model yerel olarak yeniden oluşturulur.
- OpenVR named-pipe sürücüsü, geri alınabilir binding sistemi, OpenXR API katmanı ve SteamVR dashboard overlay pakette bulunur.
- Oyun Ekleme Sihirbazı kurulu Steam VR oyunlarını ve yerel SteamVR action/OpenXR executable verisini tarar; oyun dosyalarını değiştirmez.
- Half-Life: Alyx için doğrudan yerel telemetri; diğer oyunlar için sınırlı, sürümlü ve geri alınabilir evrensel eşleme bulunur.
- Quest/Virtual Desktop → sistem modu → SteamVR → NiiMotion köprüsü → locomotion → oyun sırası zorunludur.
- Başlangıç Rehberi yerel runtime bileşenlerini denetler. Tanılama bağlantı, model, paket ve son başlatma hatasını açıklar.
- Runtime kaynaklarında OpenAI/Gemini/Anthropic/Azure OpenAI bağımlılığı yoktur.
- Son otomatik taban: 102/102 test, Release build 0 uyarı/0 hata.

Son ilgili commitler:

- `69c4802` standalone hazırlık ve kendi kendine ilerleyen rehber
- `b078a81` kişisel model bütünlük kapısı ve yerel yeniden üretim
- `bc24440` AI gerektirmeyen yerel tanılama
- `f8a499a` standalone kabul sözleşmesi

## Kalan geliştirmeler

### P0 — Tek fiziksel kabul oturumu

Bunlar kodla taklit edilmemeli; sahibin cihazlarıyla doğrulanmalıdır:

1. Her tekil profil: Joy-Con, PS Move, telefon, Board.
2. Seçilmiş karma profiller: Joy-Con + PS Move, telefon destekli varyantlar ve Board varyantları.
3. Her profilde başlangıç, doğal/hızlı tempo, ani duruş, dönüş, eğilme/çömelme yanlış pozitifleri.
4. Sensör uyku/kopma/yeniden bağlanma ve güvenli sıfır.
5. Alyx, Arizona Sunshine 2 ve Metro/OpenXR gerçek oyun doğrulaması.
6. VR overlay düğmeleri ve masaüstü dönüşü.

Sonuçlar `hardware-verified`, `replay-tested` ve `software-only` olarak ayrı etiketlenmelidir.

### P1 — Donanım sonucuna göre son ayar

- Move Only duruş gecikmesi ve tek fiziksel adım/oyun mesafesi ince ayarı.
- Joy-Con + Move zaman hizalama ve birleşik profile ait gerçek kalibrasyon.
- Board yürüyüş başlangıcı ve ağırlıkla dönüşün gerçek kullanıcı verisiyle kararlı hale getirilmesi.
- Telefonun yatay, ekran göğse dönük ve üst kenarı sola bakan yerleşiminin bütün kayıt yönergelerinde fiziksel doğrulanması.
- HMD dönüş bastırmasının farklı oyunlarda yanlış negatif üretmediğinin kontrolü.

### P1 — Oyun kapsamı

- Arizona Sunshine 2 başlatma/mesafe hissi doğrulaması.
- Metro Awakening OpenXR action davranışının gerçek oyunda doğrulanması.
- Skyrim VR ancak gerçekten kurulu olduğunda salt okunur action/runtime incelemesiyle adaptörlenmeli.
- Yeni oyunlar sihirbazla eklenebilir; otomatik tarama sonuç uydurmamalı. Action bulunamazsa kullanıcıya SteamVR binding yolu gösterilmeli.

### P2 — Yayın adayı

- Türkçe ve İngilizce arayüzün son görsel/metin turu.
- DPI ölçekleri ve küçük ekran taşma matrisi.
- Temiz Windows kullanıcı hesabında kurulum, kaldırma, güncelleme ve sürücü geri dönüş testi.
- GitHub README, katkı rehberi, lisans ve sürüm notlarının son düzeni.
- En sonda tek kez kurucu ve checksum üretimi.

## Kullanıcıya ait ve commitlenmemesi gerekenler

- `data/`, `logs/`, `artifacts/`
- kişisel `config/*.json`, model history, çalışma oturumu ve cihaz kimlikleri
- `*.niirmotion.backup`, yerel PDB ve tanı çıktıları
- ham sensör kayıtları veya Bluetooth/IP kimlikleri

`git status` kirli olabilir. Başka bir agent bunu otomatik temizlememeli.

## Kritik kaynaklar

- Bağımsız kabul: `docs/standalone-acceptance.md`
- V5 faz geçmişi: `docs/upgrade-plan-v5.md`
- Mimari: `docs/architecture.md`
- Cihaz kurulumu: `docs/device-setup-tr.md`
- Sorun giderme: `docs/troubleshooting-tr.md`
- Hareket runtime: `src/NiiRMotion.Infrastructure/LiveLocomotionService.cs`
- Yerel kalibrasyon: `src/NiiRMotion.Infrastructure/OfflineCalibrationPipeline.cs`
- Başlatma kapıları: `src/NiiRMotion.App/MainWindow.xaml.cs`
- OpenVR/OpenXR native bileşenleri: `native/openvr-driver`, `native/openxr-layer`

## Yasak kısa yollar

- AI API eklemek veya kullanıcı kalibrasyonunu buluta taşımak.
- Cihaz bağlı görünümünü eski log/process varlığından kesin kabul etmek.
- Virtual Desktop oturumu kararlı olmadan SteamVR açmak.
- Eksik action/executable için tahmini oyun adaptörü üretmek.
- Profil veya binding'i sessiz değiştirmek.
- Kişisel veriyi temizlemek, yeniden etiketlemek veya Git'e eklemek.
