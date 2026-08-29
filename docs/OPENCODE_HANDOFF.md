# NiiMotion OpenCode devralma rehberi

Güncelleme: 29 Ağustos 2026

Depo: `C:\NiirMotion`

Ana dal: `main`

## İlk komut

OpenCode'u depo kökünde aç ve agente şunu söyle:

> Read `AGENTS.md`, `docs/AI_AGENT_HANDOFF.md`, `docs/OPENCODE_HANDOFF.md`, `docs/standalone-acceptance.md`, and `docs/upgrade-plan-v5.md` completely. Then follow `docs/OPENCODE_START_PROMPT.md` as the exact continuation contract. Run the canonical verification before editing. Preserve every user-owned recording, runtime configuration, device identity, model history, backup, and unrelated dirty file. Never introduce an online AI runtime dependency.

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
- Son otomatik taban: 111/111 test, Release build 0 uyarı/0 hata. İngilizce kaynak denetimi, yayın sözleşmesi ve UI smoke ayrı kapılardır.

Son ilgili commitler:

- `4eb9d51` 0.6.1 beta hazırlığı, ortak faz kontrolleri, model geçmişi ve sağlık ekranı
- `ab17b75` ortak fazların iki dakikaya indirilmesi
- `02cf75f` her çoklu kombinasyon için ayrı yerel birleşim modeli
- `be46540` Alyx için açık manuel hız ayarı
- `bf4b041` karma sensörlerde uzlaşma kapısı

Yayımlanmış beta: `v0.6.1-beta.1` — <https://github.com/hellhot1978/NiiMotion/releases/tag/v0.6.1-beta.1>

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

Makinece okunabilir profil, senaryo ve oyun listesi `docs/hardware-acceptance-matrix.json` dosyasındadır. Yeni agent bu matrisi tahminle tamamlanmış saymamalıdır.

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

### P2 — Yayın ve temiz makine kabulü

- Türkçe/İngilizce görsel duman testi; Genel Bakış 1100×700/1200×760, Başlangıç Rehberi, cihaz seçimi, Joy-Con/PS Move temel kalibrasyonu, yönlendirmeli kayıt ve Board laboratuvarı dahil 16 render senaryosunu doğruluyor (`scripts/verify-ui.ps1`, geliştirme doğrulamasında `-UiSmoke`).
- Başlangıç Rehberi kaydırmasız 2×2 düzene geçirildi; dinamik profil, cihaz ve kalibrasyon metinlerinin İngilizce karşılıkları görsel olarak denetlendi. Windows %125/%150 gerçek DPI ile kalan ikincil diyalogların taşma matrisi yayın adayı öncesinde tamamlanmalı.
- Temiz Windows kullanıcı hesabında kurulum, kaldırma, güncelleme ve sürücü geri dönüş testi.
- Kurucu sözleşmesi otomatik doğrulanıyor: standart kullanıcı kurulumu, self-contained paket, masaüstü kısayolu, OpenVR/OpenXR kaydı kaldırma ve kişisel veriyi koruma. Gerçek temiz Windows kabulü yine gereklidir.
- GitHub README, kullanıcı rehberleri, lisans ve sürüm notları beta için yayımlandı.
- 0.6.1 kurucu, checksum, bileşen envanteri ve commit-bağlı sürüm manifesti `v0.6.1-beta.1` altında yayımlandı.
- Kalan yayın kapıları: gerçek kod imzası ve temiz standart Windows hesabında %100/%125/%150 DPI ile fiziksel yükseltme/geri dönüş kabulü.

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
