using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NiiRMotion.Infrastructure;

namespace NiiRMotion.App;

public static class UiLocalization
{
    private sealed class State { public string? OriginalText, OriginalContent, OriginalTitle, LastText, LastContent, LastTitle; public bool TextHooked, ContentHooked, TitleHooked, Updating; }
    private static readonly ConditionalWeakTable<DependencyObject, State> States = new();
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Genel Bakış"]="Overview", ["Oyunlar"]="Games", ["Test ve Kalibrasyon"]="Test & Calibration", ["Cihazlarım"]="My Devices",
        ["Başlangıç Rehberi"]="Getting Started", ["Erişilebilirlik"]="Accessibility", ["ERİŞİLEBİLİRLİK"]="ACCESSIBILITY", ["VR Paneli"]="Live Status Panel", ["Canlı Durum Paneli"]="Live Status Panel", ["Güncellemeler"]="Updates",
        ["NiiMotion Canlı Durum Paneli"]="NiiMotion Live Status Panel", ["Bu masaüstü penceresi VR panelini yansıtır. Başlıkta SteamVR menüsünü açıp NiiMotion kutucuğunu seç."]="This desktop window mirrors the VR panel. In the headset, open the SteamVR menu and select the NiiMotion tile.", ["SteamVR başlığındaki NiiMotion panelinin masaüstü görünümünü aç"]="Open the desktop mirror of the NiiMotion panel shown in SteamVR",
        ["NiiMotion'ı güvenli biçimde hazırlamak için dört kısa adım."]="Four short steps to prepare NiiMotion safely.", ["Arayüz dili"]="Interface language", ["Yazı boyutu"]="Text size",
        ["Sistem durumu ve hızlı başlangıç"]="System status and quick start", ["MENÜ"]="MENU", ["AKTİF PROFİL"]="ACTIVE PROFILE", ["SİSTEM DURUMU"]="SYSTEM STATUS", ["VR OTURUMU"]="VR SESSION",
        ["Profili değiştir  ▾"]="Change profile  ▾", ["VR'Yİ HAZIRLA VE BAŞLAT"]="PREPARE & START VR", ["NORMAL VR'Yİ BAŞLAT"]="START NORMAL VR",
        ["GEREKEN CİHAZLAR"]="REQUIRED DEVICES", ["Seçili profil için bağlantı durumu"]="Live connection status for the selected profile", ["Cihazlar hazırsa SteamVR son adımda otomatik açılır."]="SteamVR starts automatically as the final step when devices are ready.",
        ["OYUN HAREKETİNİ DURDUR"]="STOP GAME MOVEMENT", ["CİHAZLARI KONTROL ET"]="CHECK DEVICES", ["HAREKETİ DURDUR"]="STOP MOVEMENT", ["KAPALI"]="OFF", ["HAZIR"]="READY", ["KONTROL EDİLİYOR"]="CHECKING",
        ["Sadece Joy-Con"]="Joy-Con Only", ["Sadece Telefon"]="Phone Only", ["Sadece Balance Board"]="Balance Board Only", ["Tüm Cihazlar"]="All Devices", ["Board + Telefon"]="Board + Phone",
        ["Özgün kontrolcü hareketi"]="Native controller movement", ["Telefonsuz yerinde yürüme"]="Walk in place without a phone", ["Deneysel hareket modu"]="Experimental movement mode", ["Önerilen · Dengeli hareket doğrulaması"]="Recommended · Balanced motion validation",
        ["KAPAT"]="CLOSE", ["VAZGEÇ"]="CANCEL", ["İPTAL"]="CANCEL", ["KAYDET VE DEVAM ET"]="SAVE & CONTINUE", ["DEVAM ET  →"]="CONTINUE  →",
        ["Yüksek kontrast"]="High contrast", ["Azaltılmış hareket ve animasyon"]="Reduced motion and animation", ["YENİ YEDEK"]="NEW BACKUP", ["SEÇİLİ YEDEĞİ GERİ YÜKLE"]="RESTORE SELECTED BACKUP",
        ["SİSTEM TANILAMA"]="SYSTEM DIAGNOSTICS", ["YEDEKLEME VE GERİ YÜKLEME"]="BACKUP & RESTORE", ["OYUN MODUNU BAŞLAT"]="START GAME MODE",
        ["KİŞİSEL KALİBRASYON"]="PERSONAL CALIBRATION", ["KİŞİSEL MODEL"]="PERSONAL MODEL", ["İSTEĞE BAĞLI"]="OPTIONAL", ["ÖNERİLEN SONRAKİ ADIM"]="RECOMMENDED NEXT STEP",
        ["Yürüyüş kalibrasyonu"]="Walking calibration", ["Joy-Con sensör testi"]="Joy-Con sensor test", ["Telefon kalibrasyonu"]="Phone calibration", ["Board kalibrasyonu"]="Board calibration", ["PS Move kalibrasyonu"]="PS Move calibration",
        ["İki bacak sensörünü ölç"]="Measure both leg sensors", ["Bağlantıyı hızlıca doğrula"]="Quickly verify the connection", ["Göğüs sensöründen yönlendirmeli kişisel kayıt al."]="Record a guided personal sample from the chest sensor.", ["Bağlantı ve basıncı kontrol et"]="Check connection and pressure",
        ["Modeli yeni kayıtlarla geliştir"]="Improve the model with new recordings", ["SEÇİLİ SENSÖR KOMBİNASYONU"]="SELECTED SENSOR COMBINATION", ["VR çıkışı kapalıdır · kayıt yalnız kişisel modeli geliştirmek için kullanılır"]="VR output is off · this recording is used only to improve your personal model",
        ["Bu kayıt temel kalibrasyonu değiştirmez. Sensörlerin birlikte çalışmasını güçlendiren yeni bir eğitim örneği ekler."]="This recording does not change base calibration. It adds a new training sample that improves how the sensors work together.",
        ["5 dakika boyunca yavaş, doğal ve hızlı yerinde yürüyüş; kısa duruş, dönüş ve eğilme hareketleri yap."]="For 5 minutes, walk slowly, naturally and quickly in place; include short stops, turns and bends.",
        ["5 DK KOMBİNE KAYDI BAŞLAT"]="START 5-MIN COMBINED RECORDING", ["SON KAYDI REDDET"]="REJECT LAST RECORDING", ["YENİ 5 DK KAYIT"]="NEW 5-MIN RECORDING",
        ["Cihaz kalibrasyonu"]="Device calibration", ["Bağlantı ve üç temel kayıt"]="Connection and three base recordings", ["Bağlantı yönergeleri"]="Connection instructions", ["Bağlantı henüz doğrulanmadı"]="Connection has not been verified", ["BAĞLANTIYI KONTROL ET"]="CHECK CONNECTION",
        ["TEMEL KALİBRASYON"]="BASE CALIBRATION", ["3 faz · her biri 5 dakika"]="3 phases · 5 minutes each", ["Tamamlanan bir faz hatalıysa yanındaki yeniden çek düğmesini kullanabilirsin."]="If a completed phase is incorrect, use its retake button.",
        ["Önce cihaz bağlantısını doğrula."]="Verify the device connection first.", ["VR ÇIKIŞI KAPALI"]="VR OUTPUT OFF", ["MOVE'LARI YENİDEN EŞLEŞTİR"]="PAIR MOVES AGAIN", ["SORUNLU BÖLÜMÜ YENİDEN KAYDET"]="RECORD THE PROBLEM SEGMENT AGAIN",
        ["Kalibrasyon tamamlandı · cihaz bağlı değil"]="Calibration complete · device not connected", ["Bağlı · kalibrasyon tamamlandı"]="Connected · calibration complete", ["Cihaz bağlı değil"]="Device not connected", ["Cihaz bağlı"]="Device connected",
        ["ŞİMDİ YAP"]="DO THIS NOW", ["BU HAREKETİN KALAN SÜRESİ"]="TIME LEFT FOR THIS MOVEMENT", ["SONRAKİ HAREKET"]="NEXT MOVEMENT", ["Hazırlan"]="Get ready", ["Sensör akışı başlatılıyor…"]="Starting sensor stream…",
        ["DURAKLAT"]="PAUSE", ["SOL MOVE'U KIRMIZI YAK"]="LIGHT LEFT MOVE RED", ["SAĞ MOVE'U MAVİ YAK"]="LIGHT RIGHT MOVE BLUE", ["KAYIT HAZIRLANIYOR"]="PREPARING RECORDING",
        ["SABİT DUR"]="STAND STILL", ["YAVAŞ YÜRÜ"]="WALK SLOWLY", ["DOĞAL YÜRÜ"]="WALK NATURALLY", ["HIZLI YÜRÜ"]="WALK QUICKLY", ["DÖN"]="TURN", ["EĞİL"]="BEND", ["DENGE"]="BALANCE",
        ["Joy-Con Laboratuvarı"]="Joy-Con Lab", ["Yalnız sol ve sağ Joy-Con · VR çıkışı kapalı"]="Left and right Joy-Con only · VR output off", ["CANLI HAREKET"]="LIVE MOTION", ["KISA SENSÖR KAYDI"]="SHORT SENSOR RECORDING", ["CANLI TEST"]="LIVE TEST", ["KISA KAYDI BAŞLAT"]="START SHORT RECORDING",
        ["Sol bacak"]="Left leg", ["Sağ bacak"]="Right leg", ["ADIM"]="STEPS", ["SÜRE"]="DURATION", ["Yönerge burada görünecek"]="Instructions will appear here",
        ["Telefon Laboratuvarı"]="Phone Lab", ["Yalnız owoTrack verisi · VR çıkışı kapalı"]="owoTrack data only · VR output off", ["TELEFON YERLEŞİMİ"]="PHONE PLACEMENT", ["Ekran göğsüne dönük"]="Screen facing your chest", ["KAYIT TÜRÜ"]="RECORDING TYPE", ["TELEFONA BAĞLAN"]="CONNECT PHONE", ["Önce telefonu bağla"]="Connect the phone first",
        ["Balance Board Laboratuvarı"]="Balance Board Lab", ["Görsel yönlendirmeli kişisel basınç ve yürüyüş kalibrasyonu · VR çıkışı yok"]="Guided personal pressure and walking calibration · no VR output", ["ÖLÇÜM TÜRÜ"]="MEASUREMENT TYPE", ["TOPLAM YÜK"]="TOTAL LOAD", ["GEÇİŞ"]="TRANSITIONS", ["SAĞ / SOL"]="RIGHT / LEFT", ["ÖN / ARKA"]="FRONT / BACK", ["ÖLÇÜMÜ BAŞLAT"]="START MEASUREMENT",
        ["PS Move Bacak Kalibrasyonu"]="PS Move Leg Calibration", ["DOĞRU YERLEŞİM"]="CORRECT PLACEMENT", ["Her iki Move aynı yönde olmalı"]="Both Move controllers must face the same direction", ["YÖNLENDİRMELİ KALİBRASYON"]="GUIDED CALIBRATION", ["SOL BALDIR"]="LEFT CALF", ["SAĞ BALDIR"]="RIGHT CALF", ["MOVE’LARI DOĞRULA"]="VERIFY MOVE CONTROLLERS",
        ["NiiMotion'a hoş geldin"]="Welcome to NiiMotion", ["Hangi hareket cihazlarına sahipsin?"]="Which motion devices do you own?", ["Bu cihaza sahibim"]="I own this device", ["Ana başlık · tüm profillerde kullanılır"]="Main headset · used by every profile", ["Uyluk sensörleri"]="Thigh sensors", ["Baldır sensörleri"]="Calf sensors", ["Göğüs sensörü"]="Chest sensor", ["Basınç ve denge"]="Pressure and balance", ["VR el kontrolü"]="VR hand control", ["Virtual Desktop üzerinden"]="Through Virtual Desktop",
        ["Sistem Tanılama"]="System Diagnostics", ["Bağlantı sorunlarını anlaşılır biçimde açıklar · oyun hareketi göndermez"]="Explains connection issues in plain language · sends no game movement", ["Tanı paketi ham hareket verisi içermez."]="The diagnostic package contains no raw motion data.", ["GİZLİLİK KORUMALI TANI PAKETİ"]="PRIVACY-SAFE DIAGNOSTIC PACKAGE",
        ["HMD dönüş desteği hazır"]="HMD turn assistance ready", ["HMD dönüş desteği isteğe bağlı"]="HMD turn assistance optional", ["Başlık bağlı değilse mevcut yürüyüş profili değişmeden çalışır."]="If the headset is unavailable, the current walking profile continues unchanged.", ["İstersen Test ve Kalibrasyon bölümünden tek üç dakikalık doğrulama yapabilirsin."]="You can run one three-minute validation from Test & Calibration whenever you want.",
        ["NiiMotion · Balance Board Laboratuvarı"]="NiiMotion · Balance Board Lab", ["NiiMotion · Cihazlarım"]="NiiMotion · My Devices", ["NiiMotion · Model Geliştirme"]="NiiMotion · Model Improvement", ["NiiMotion · PS Move Kalibrasyonu"]="NiiMotion · PS Move Calibration", ["NiiMotion · Sistem Tanılama"]="NiiMotion · System Diagnostics", ["NiiMotion · Telefon Laboratuvarı"]="NiiMotion · Phone Lab", ["NiiMotion · Temel Kalibrasyon"]="NiiMotion · Base Calibration", ["NiiMotion · Yedekleme Merkezi"]="NiiMotion · Backup Center", ["NiiMotion · Yönlendirmeli Kalibrasyon"]="NiiMotion · Guided Calibration", ["NiiMotion · Yürüyüş Kalibrasyonu"]="NiiMotion · Walking Calibration", ["NiiMotion · Yürüyüş Laboratuvarı"]="NiiMotion · Walking Lab",
        ["Aktif süre sayılır · duraklatıldığında veri kaydı ve sayaç birlikte durur"]="Only active time counts · pausing stops both recording and the timer", ["Baldır yerleşimini ölç ve doğrula"]="Measure and verify calf placement", ["Bir cihaz seçtiğinde test sonucu burada görünecek."]="The test result will appear here after you select a device.", ["Bir kart seçtiğinde gereken cihazlar ve başlangıç ayarları otomatik hazırlanır."]="Select a card and the required devices and startup settings will be prepared automatically.", ["CİHAZ KONTROLÜ"]="DEVICE CHECK", ["Cihazlar taranıyor"]="Scanning devices", ["Denge merkezi ve ağırlık aktarımını kişiselleştir."]="Personalize balance center and weight transfer.", ["Hareketini NiiMotion'a öğret"]="Teach NiiMotion your movement", ["İlerleme hesaplanıyor"]="Calculating progress", ["KİŞİSEL PROFİL FARKI"]="PERSONAL PROFILE DIFFERENCE", ["Önceki → yeni"]="Previous → new", ["OYUN PROFİLİ SEÇ"]="SELECT GAME PROFILE", ["OYUNA HAREKET GÖNDERİLMEZ"]="NO MOVEMENT IS SENT TO THE GAME", ["Önerilen adımı tamamla; diğer araçları yalnız ihtiyaç duyduğunda kullan."]="Complete the recommended step; use other tools only when needed.",
        ["Ekrandaki yönergeleri izle. Joy-Con verilerin kaydedilir ve kişisel yürüyüş modelin geliştirilir."]="Follow the on-screen guidance. Your Joy-Con data is recorded to improve your personal gait model.", ["Quest ve kontrolcüler özgün VR davranışıyla çalışır."]="Quest and controllers use their native VR behavior.", ["NiiMotion kapalı"]="NiiMotion off", ["NiiMotion kapalı · Özgün kontrolcü hareketi"]="NiiMotion off · Native controller movement", ["owoTrack bağlantısını başlat"]="Start the owoTrack connection", ["Telefonu eşleştir"]="Pair phone", ["KALİBRASYONU AÇ  →"]="OPEN CALIBRATION  →", ["KAYIT EKRANINI AÇ  →"]="OPEN RECORDING  →", ["5 dakikalık yönlendirmeli kayıt"]="5-minute guided recording",
        ["Kalibrasyon sırasında oyun hareketi üretilmez."]="No game movement is generated during calibration.", ["Move’ları taktıktan sonra bağlantıyı doğrula"]="Verify the connection after attaching the Move controllers", ["Sol Move kırmızı · Sağ Move mavi"]="Left Move red · Right Move blue", ["Sırayla: bağlantı → 5 sn sabit duruş → kontrollü bacak kaldırma"]="Order: connection → 5 sec standing still → controlled leg lift", ["İlk adımda sensör akışı ve renkler kontrol edilir."]="The first step checks sensor streams and colors.", ["Henüz başlatılmadı"]="Not started yet", ["Move: diz altı / baldır · Joy-Con: kalça–diz arası / uyluk · VR çıkışı kapalı"]="Move: below knee / calf · Joy-Con: hip-to-knee / thigh · VR output off", ["Baldırın dış/ön tarafına, dizin hemen altına sabitle. Küre yukarı, düğmeler dışarı…"]="Secure it to the outer/front calf just below the knee. Sphere up, buttons outward…",
        ["Baldırın dış/ön tarafına, dizin hemen altına sabitle. Küre yukarı, düğmeler dışarı/öne baksın. Bandı dolaşımı bozmayacak kadar sıkı tut."]="Secure it to the outer/front calf just below the knee. Sphere up, buttons facing outward/forward. Keep the strap snug without restricting circulation.",
        ["Her ölçümden önce kart boş olmalı. İlk sesten sonra ekrandaki hareketi uygula."]="The board must be empty before each measurement. Follow the on-screen movement after the first sound.", ["Kart açılışta otomatik olarak sıfırlanır."]="The board is automatically zeroed when opened.", ["Kart boşken Başlat'a bas"]="Press Start while the board is empty", ["Göğsün ortasında · telefonun üst kenarı sola bakacak · sallanmayacak kadar sabit"]="Center of chest · top edge of phone points left · secure enough not to wobble", ["owoTrack'i başlat; sonra Bağlan'a bas."]="Start owoTrack, then press Connect.", ["Kayıt başlamadı."]="Recording has not started.", ["BAĞLANTI BEKLİYOR"]="WAITING FOR CONNECTION", ["YATAY"]="LANDSCAPE",
        ["Kesilen veya yetersiz veri üreten faz tamamlanmış sayılmaz."]="A phase with interrupted or insufficient data is not considered complete.", ["Tekil cihaz kalibrasyonlarını değiştirmez. Seçili profildeki sensörlerin ritim, hız ve duruş uyumunu birlikte ölçer."]="Does not change individual device calibration. It measures rhythm, speed and stopping agreement across the selected profile sensors.", ["Temel kalibrasyon yalnız cihazı kullanıma hazırlar. Ek eğitim kayıtları Kalibrasyon Merkezi'ndeki 'Modeli geliştir' bölümündedir."]="Base calibration only prepares the device for use. Additional training recordings are under Improve Model in Calibration Center.", ["Faz 1'i sil ve yeniden çek"]="Delete and retake Phase 1", ["Faz 2'yi sil ve yeniden çek"]="Delete and retake Phase 2", ["Faz 3'ü sil ve yeniden çek"]="Delete and retake Phase 3",
        ["Tekil cihaz kalibrasyonlarını değiştirmez. Seçili profildeki sensörlerin ritim, hız, başlangıç, duruş ve yanlış hareket uyumunu ayrı bir model olarak kaydeder."]="Does not change individual device calibration. It stores rhythm, speed, starts, stops and false-motion agreement for the selected sensors as a separate model.", ["Temel kalibrasyon yalnız cihazı kullanıma hazırlar. Ek eğitim kayıtları Kalibrasyon Merkezi'ndeki “Modeli geliştir” bölümündedir."]="Base calibration only prepares the device for use. Additional training recordings are under Improve Model in Calibration Center.",
        ["Kişisel modeller, cihaz tercihleri ve oyun ayarları · ham sensör kayıtları kopyalanmaz"]="Personal models, device preferences and game settings · raw sensor recordings are not copied", ["Geri yüklemeden önce mevcut durum otomatik yedeklenir."]="The current state is backed up automatically before restore.", ["Cihaz eşleştirmeleri, oyun ayarları ve uygulama korunur. Önce geri dönüş ZIP'i oluşturulur."]="Device pairings, game settings and the app are preserved. A recovery ZIP is created first.", ["Yalnız Joy-Con verisi kaydedilir."]="Only Joy-Con data is recorded.", ["Joy-Con hareketini tek başına doğrula"]="Validate Joy-Con movement by itself", ["Joy-Con'ları bacaklarına bağladıktan sonra Canlı Test'i başlat."]="Start Live Test after attaching the Joy-Cons to your legs.", ["Yalnız seçtiklerin için profil ve kalibrasyon adımları gösterilecek. Bunu daha sonra değiştirebilirsin."]="Profiles and calibration steps are shown only for your selected devices. You can change this later.", ["Seçimine göre en uygun profiller performans ve kullanım kolaylığına göre sıralanacak."]="Profiles will be ranked by performance and ease of use for your selected devices.",
        ["Yedekleme ve geri yükleme"]="Backup & Restore", ["ÖĞRENİLMİŞ HAREKET VERİSİ"]="LEARNED MOTION DATA", ["GÜVENLİ SIFIRLA"]="SAFE RESET",
        ["Birlikte çalışma kalibrasyonu"]="Combined operation calibration", ["Aktif profil"]="Active profile", ["3 FAZ · 15 DAKİKA"]="3 PHASES · 15 MINUTES", ["BU KAYIT NE YAPAR?"]="WHAT DOES THIS RECORDING DO?", ["Sensörleri aynı zaman çizgisinde ölçer"]="Measures sensors on the same timeline", ["Faz 1 ile başla."]="Start with Phase 1.",
        ["Profil"]="Profile", ["Oyun"]="Game", ["Durum"]="Status", ["Cihazlar"]="Devices", ["Canlı oturum"]="Live session", ["Kapalı"]="Off", ["Hazırlanıyor"]="Preparing", ["Başlıkta kullanılacak güvenli, büyük kontroller"]="Large, safe controls for monitoring a VR session", ["NiiMotion VR Panel"]="NiiMotion Live Status Panel",
        ["HMD yön doğrulaması"]="HMD direction validation", ["Başlık yön ve dönüş doğrulaması"]="Headset direction and turn validation", ["İSTEĞE BAĞLI · HMD"]="OPTIONAL · HMD", ["BUGÜN DAHA SONRA  →"]="DO LATER TODAY  →",
        ["Tek seferlik 3 dakikalık kayıt; kişisel yürüyüş modelini değiştirmez."]="One 3-minute recording; it does not change the personal gait model.", ["Son kayıt kalite kontrolünden geçmedi; güvenle yeniden alınabilir."]="The latest recording did not pass quality checks and can be safely repeated.",
        ["HMD yön ve dönüş doğrulaması"]="HMD direction and turn validation", ["Tek kayıt · kişisel yürüyüş modelini değiştirmez · oyun hareketi gönderilmez"]="One recording · does not change the personal gait model · sends no game movement",
        ["Başlık akışı bulunamadı"]="Headset stream not found", ["SteamVR ve NiiMotion VR paneli çalışırken yeniden dene. Kayıt başlamadığı için üç dakika beklenmedi."]="Try again while SteamVR and the NiiMotion VR panel are running. The app did not wait three minutes because recording never started.",
        ["Sabit dur ve doğal biçimde etrafa bak"]="Stand still and look around naturally", ["Yerinde doğal yürü"]="Walk naturally in place", ["Sola ve sağa bak"]="Look left and right", ["Vücudunla sola ve sağa dön"]="Turn your body left and right", ["Başla, yürü ve birkaç kez dur"]="Start, walk and stop several times",
        ["Ⅱ  DURAKLAT"]="Ⅱ  PAUSE", ["▶  DEVAM ET"]="▶  CONTINUE", ["SteamVR HMD akışı başlatılıyor…"]="Starting SteamVR HMD stream…", ["Kayıt ve sayaç duraklatıldı."]="Recording and timer paused.", ["Kayıt devam ediyor."]="Recording resumed.",
        ["SOL"]="LEFT", ["SAĞ"]="RIGHT", ["YAVAŞ"]="SLOW", ["DOĞAL"]="NATURAL", ["HIZLI"]="FAST", ["İVME"]="ACCELERATION", ["HAREKET"]="MOTION"
        ,["NiiMotion oyun girişine müdahale etmez"]="NiiMotion does not interfere with game input", ["KAYDI BAŞLAT"]="START RECORDING", ["KALİBRASYONU UYGULA"]="APPLY CALIBRATION", ["DAHİL"]="INCLUDED", ["BAĞLANTI KONTROLÜ"]="CONNECTION CHECK",
        ["Basınç ve bacak sensörleri"]="Pressure and leg sensors", ["Basınç ve gövde sensörü"]="Pressure and torso sensor", ["Cihaz"]="Device", ["Faz"]="Phase", ["Joy-Con çifti"]="Joy-Con pair", ["Sadece Board · Deneysel"]="Board Only · Experimental"
        ,["Joy-Con + Telefon"]="Joy-Con + Phone", ["Board + Joy-Con"]="Board + Joy-Con", ["NORMAL VR"]="NORMAL VR", ["SADECE JOY-CON"]="JOY-CON ONLY", ["SADECE TELEFON"]="PHONE ONLY", ["BALANCE BOARD"]="BALANCE BOARD", ["BOARD + TELEFON"]="BOARD + PHONE"
        ,["NiiMotion · Başlangıç Rehberi"]="NiiMotion · Getting Started", ["TERCİHLER"]="PREFERENCES", ["GÜNCELLEMELERİ KONTROL ET"]="CHECK FOR UPDATES", ["UYGULAMA GÜNCELLEMESİ"]="APP UPDATE", ["Yeni NiiMotion sürümünü denetle"]="Check for a new NiiMotion version", ["KONTROL ET"]="CHECK", ["ARAYÜZ DİLİ"]="INTERFACE LANGUAGE", ["Uygulamanın görüntüleneceği dili seç"]="Choose the language used by the app"
        ,["OYUN İÇİ HIZ UYUMU"]="IN-GAME PACE TUNING", ["DAHA HIZLI OLMALI"]="SHOULD BE FASTER", ["HIZ DOĞRU"]="PACE IS RIGHT", ["DAHA YAVAŞ OLMALI"]="SHOULD BE SLOWER", ["SON HIZ AYARINI GERİ AL"]="UNDO LAST PACE CHANGE", ["ÖĞRENİLEN HIZI SIFIRLA"]="RESET LEARNED PACE", ["TÜM AYARLARI SIFIRLA"]="RESET ALL SETTINGS"
        ,["GEREKLİ YAZILIM"]="REQUIRED SOFTWARE", ["Yazılım bilgisi"]="Software information", ["KURULUM VE YERLEŞİM"]="SETUP AND PLACEMENT", ["Ek uygulama gerekmez · Windows Bluetooth + NiiMotion Joy-Con HID desteği"]="No extra app required · Windows Bluetooth + built-in NiiMotion Joy-Con HID support", ["PSMoveAPI eşleştirme aracı · gerektiğinde NiiMotion güvenli biçimde kurar"]="PSMoveAPI pairing tool · NiiMotion installs it securely when needed", ["Android telefonda owoTrack · bilgisayarda ek telefon uygulaması gerekmez"]="owoTrack on the Android phone · no additional phone software is required on the PC", ["Ek uygulama gerekmez · Windows Bluetooth + NiiMotion Balance Board desteği"]="No extra app required · Windows Bluetooth + built-in NiiMotion Balance Board support"
        ,["BAĞLANTI YARDIMINI AÇ"]="OPEN CONNECTION HELP", ["1 / 3 · Yazılım hazır"]="1 / 3 · Software ready", ["2 / 3 · Bağlantı denetleniyor"]="2 / 3 · Checking connection", ["3 / 3 · Sensör doğrulandı"]="3 / 3 · Sensor verified", ["Bağlantı kontrolü bekleniyor"]="Waiting for connection check", ["Canlı sensör akışı bekleniyor…"]="Waiting for live sensor stream…", ["Hazır · temel kalibrasyona geçebilirsin."]="Ready · you can continue to base calibration."
        ,["Sadece PS Move"]="PS Move Only", ["Sadece Joy-Con"]="Joy-Con Only", ["Sadece Telefon"]="Phone Only", ["Sadece Board"]="Board Only",
        ["PS Move + Telefon"]="PS Move + Phone", ["Joy-Con + Telefon"]="Joy-Con + Phone", ["Telefon + Board"]="Phone + Board", ["Joy-Con + PS Move + Telefon"]="Joy-Con + PS Move + Phone", ["Joy-Con + Telefon + Board"]="Joy-Con + Phone + Board", ["PS Move + Telefon + Board"]="PS Move + Phone + Board", ["Joy-Con + PS Move + Telefon + Board"]="Joy-Con + PS Move + Phone + Board",
        ["NiiMotion kapalı · özgün VR kontrolü"]="NiiMotion off · native VR controls", ["En güçlü bacak doğrulaması"]="Strongest leg validation", ["Doğal yerinde yürüyüş"]="Natural walk-in-place", ["Basınç tabanlı deneysel hareket"]="Experimental pressure-based movement", ["Telefon tabanlı deneysel hareket"]="Experimental phone-based movement", ["DENEYSEL"]="EXPERIMENTAL", ["SANA UYGUN PROFİLLER"]="RECOMMENDED PROFILES",
        ["Yerinde yürüyüş çıkışı"]="Walk-in-place output", ["BAĞLANTI GEREKİYOR"]="CONNECTION REQUIRED", ["SİSTEM HAZIR"]="SYSTEM READY", ["DEGRADED MODE HAZIR"]="LIMITED MODE READY", ["Başlamak için aşağıdaki eksik cihazları bağla."]="Connect the missing devices shown below to begin.", ["Tüm gerekli cihazlar bağlı."]="All required devices are connected.", ["Başlatılabilir; bazı ek cihazlar bağlı değil."]="Ready to start; some optional devices are not connected.", ["EKSİK CİHAZLARI BAĞLA"]="CONNECT MISSING DEVICES", ["Son tarama"]="Last scan",
        ["NİIMOTION KAPALI"]="NIIMOTION OFF", ["NIIMOTION KAPALI"]="NIIMOTION OFF", ["NİIMOTION AKTİF"]="NIIMOTION ACTIVE", ["NIIMOTION AKTİF"]="NIIMOTION ACTIVE", ["NiiMotion etkin"]="NiiMotion active", ["NiiMotion devre dışı · Özgün VR kontrolü"]="NiiMotion disabled · Native VR controls", ["NiiMotion etkin · Bir profil seçip başlat"]="NiiMotion active · Select a profile to begin",
        ["Sol PS Move"]="Left PS Move", ["Sağ PS Move"]="Right PS Move", ["Sol Joy-Con"]="Left Joy-Con", ["Sağ Joy-Con"]="Right Joy-Con", ["Android Telefon"]="Android Phone", ["El Takibi"]="Hand Tracking",
        ["Cihazların birlikte doğrulanır"]="Devices are validated together", ["Zorunlu bir cihaz kesilirse hareket güvenle sıfırlanır."]="Movement safely resets if a required device disconnects.", ["NORMAL VR MODU SEÇİLDİ"]="NORMAL VR MODE SELECTED", ["VR HAZIRLANIYOR"]="PREPARING VR", ["VR HAZIRLANAMADI"]="VR COULD NOT BE PREPARED", ["CİHAZLAR EKSİK"]="DEVICES MISSING", ["QUEST BAĞLANTISI BEKLENİYOR"]="WAITING FOR QUEST CONNECTION", ["NORMAL VR BAŞLATILDI"]="NORMAL VR STARTED", ["NORMAL VR HAZIR"]="NORMAL VR READY", ["BEKLİYOR"]="WAITING",
        ["GÜVENLİ DURUŞ"]="SAFE STOP", ["HAREKET BAĞLANTISI KESİLDİ"]="MOTION CONNECTION LOST", ["TARANIYOR"]="SCANNING", ["SİSTEM MODU DEĞİŞTİRİLİYOR"]="CHANGING SYSTEM MODE", ["ORİJİNAL SİSTEM AKTİF"]="NATIVE SYSTEM ACTIVE", ["GEÇİŞ TAMAMLANAMADI"]="SWITCH COULD NOT BE COMPLETED", ["DAHA BASİT PROFİLİ KULLAN"]="USE A SIMPLER PROFILE", ["KULLAN"]="USE",
        ["YEREL ÇALIŞMA DENETİMİ"]="LOCAL RUNTIME CHECK", ["NiiMotion yapay zekâ veya ağ bağlantısı olmadan kullanıma hazır."]="NiiMotion is ready to use without AI or a network connection.", ["DENETLE VE ONAR"]="CHECK & REPAIR", ["EKSİK BİLEŞEN"]="MISSING COMPONENT", ["Bağımsız .NET çalışma zamanı"]="Self-contained .NET runtime", ["Yerel hareket modelleri"]="Local motion models", ["Yerel kalibrasyon tanımları"]="Local calibration definitions", ["SteamVR analog hareket sürücüsü"]="SteamVR analog motion driver", ["OpenXR hareket katmanı"]="OpenXR motion layer", ["SteamVR içi NiiMotion paneli"]="NiiMotion SteamVR panel", ["Çevrimdışı PS Move eşleştirme aracı"]="Offline PS Move pairing tool", ["Kişisel veri alanı"]="Personal data storage",
        ["KART SIFIRLANIYOR"]="ZEROING BOARD", ["KART BOŞ KALSIN"]="KEEP THE BOARD EMPTY", ["ŞİMDİ KARTA ÇIK"]="STEP ONTO THE BOARD NOW", ["KART BEKLENİYOR"]="WAITING FOR BOARD", ["TAM KALİBRASYONA HAZIRLAN"]="PREPARE FOR FULL CALIBRATION", ["DOĞAL YÜRÜYÜŞE HAZIRLAN"]="PREPARE FOR NATURAL WALKING", ["TAM KALİBRASYON"]="FULL CALIBRATION", ["ÖLÇÜLÜYOR"]="MEASURING", ["DOĞAL YERİNDE YÜRÜ"]="WALK NATURALLY IN PLACE", ["ŞİMDİ SABİT DUR"]="STAND STILL NOW", ["ŞİMDİ KARTTAN İN"]="STEP OFF THE BOARD NOW", ["TAMAMLANDI"]="COMPLETE", ["KAYDEDİLDİ"]="SAVED", ["ÖLÇÜM ALINAMADI"]="MEASUREMENT FAILED",
        ["YENİDEN DENE"]="TRY AGAIN", ["YENİ KAYIT EKLENDİ"]="NEW RECORDING ADDED", ["YENİ 5 DK KAYIT EKLE"]="ADD NEW 5-MIN RECORDING", ["Son ek kayıt reddedildi; kişisel model kalan doğrulanmış kayıtlardan yeniden oluşturuldu."]="The latest recording was rejected; the personal model was rebuilt from the remaining verified recordings.",
        ["Cihaz aranıyor…"]="Searching for device…", ["Bağlantı yardımı"]="Connection help", ["PS Move eşleştirme"]="PS Move pairing", ["Fazı yeniden çek"]="Retake phase", ["Faz yeniden kayda açıldı · canlı bağlantı kontrol ediliyor"]="Phase reopened for recording · checking live connection", ["Güvenli kurtarma"]="Safe recovery", ["NiiMotion güvenli duruş"]="NiiMotion safe stop",
        ["Hızlı yürü ve aniden dur"]="Walk quickly and stop suddenly", ["Sol ve sağ Joy-Con bağlı olmalı."]="Both left and right Joy-Con must be connected.", ["TESTİ DURDUR"]="STOP TEST", ["CANLI · VR ÇIKIŞI YOK"]="LIVE · NO VR OUTPUT", ["Hareketlerini animasyonda görebilirsin."]="You can see your movements in the animation.", ["KAYIT GEÇERSİZ"]="RECORDING INVALID", ["KAYDI BİTİR"]="FINISH RECORDING", ["Kaydediliyor"]="Recording", ["HAREKETSİZ DUR"]="REMAIN STILL", ["HIZLI YÜRÜ · DUR SESİNDE DON"]="WALK QUICKLY · FREEZE AT THE STOP SOUND",
        ["KAYIT SÜRÜYOR"]="RECORDING", ["KAYIT TAMAMLANDI"]="RECORDING COMPLETE", ["KAYIT TAMAMLANAMADI"]="RECORDING FAILED", ["Sensör akışı kesildi"]="Sensor stream interrupted", ["DURAKLATILDI"]="PAUSED", ["Sayaç ve veri kaydı durdu. Hazır olduğunda devam et."]="Timer and recording are paused. Continue when ready.", ["Kayıt kaldığı aktif süreden devam ediyor."]="Recording continues from its remaining active time.", ["Kayıt duraklatıldı."]="Recording paused.", ["Kayıt sürüyor."]="Recording in progress.", ["Kalibrasyonu iptal et"]="Cancel calibration",
        ["Doğrulama tamamlandı"]="Validation complete", ["Kayıt tekrarlanmalı"]="Recording must be repeated", ["HMD verisi alınamadı"]="HMD data could not be captured", ["Başını küçük hareketlerle yukarı, aşağı, sola ve sağa çevir."]="Move your head slightly up, down, left and right.", ["Olduğun yerden ayrılmadan rahat ritimde yürü."]="Walk in place at a comfortable pace.", ["Ayakların sabitken yalnız başını iki yöne çevir."]="Keep your feet still and turn only your head both ways.", ["İleri yürümeye başlamadan kontrollü dönüş örnekleri ver."]="Perform controlled turns without beginning to walk forward.", ["Kısa yürüyüş başlangıçları ve belirgin tam duruşlar yap."]="Make short walking starts and clear full stops.",
        ["TELEFON BAĞLI"]="PHONE CONNECTED", ["BAĞLI"]="CONNECTED", ["Kayıt türünü seç ve başlat"]="Select a recording type and start", ["KAYIT BAŞARISIZ"]="RECORDING FAILED", ["KAYIT DOĞRULANDI"]="RECORDING VERIFIED", ["Telefon verisi alınamadı."]="Phone data could not be captured.", ["owoTrack verisi bekleniyor…"]="Waiting for owoTrack data…",
        ["MODEL HAZIR"]="MODEL READY", ["Canlı Move doğrulaması hazır"]="Live Move validation ready", ["KALİBRE"]="CALIBRATE", ["KONTROL GEREKİYOR"]="CHECK REQUIRED", ["Kalibrasyon tamamlanamadı"]="Calibration could not be completed", ["İLK EŞLEŞTİRME"]="INITIAL PAIRING", ["EŞLEŞTİRİLİYOR"]="PAIRING", ["SOL BEKLENİYOR"]="WAITING FOR LEFT", ["SAĞ BEKLENİYOR"]="WAITING FOR RIGHT", ["EŞLEŞTİRME TAMAM"]="PAIRING COMPLETE", ["USB KALİBRASYONU"]="USB CALIBRATION", ["FABRİKA VERİSİ TAMAM"]="FACTORY DATA COMPLETE", ["BAĞLANTI TAMAM"]="CONNECTION COMPLETE", ["Baldır yerleşimi kalibre edildi"]="Calf placement calibrated", ["KAYIT · VR KAPALI"]="RECORDING · VR OFF", ["CANLI · VR KAPALI"]="LIVE · VR OFF", ["DOĞRULAMA TAMAM"]="VALIDATION COMPLETE", ["Canlı test tamamlandı"]="Live test complete", ["MOVE MODELİ ÇALIŞIYOR"]="MOVE MODEL RUNNING",
        ["Geri yükle"]="Restore", ["Öğrenilmiş veriyi sıfırla"]="Reset learned data", ["Sıfırlama tamamlandı"]="Reset complete", ["YENİDEN KONTROL ET"]="CHECK AGAIN", ["İNDİR VE DOĞRULA"]="DOWNLOAD & VERIFY", ["DOSYAYI GÖSTER"]="SHOW FILE"
    };

    public static bool IsEnglish => new UserExperienceStore().Load().Language == "en";
    public static string Text(string value) => IsEnglish ? Translate(value) : value;
    public static void Apply(DependencyObject root) => Visit(root);
    public static void ApplyLoaded(DependencyObject value) => VisitSelf(value);

    private static readonly (string Turkish, string English)[] DynamicFragments =
    [
        ("Quest ve Virtual Desktop oturumu bekleniyor", "Waiting for the Quest and Virtual Desktop session"),
        ("SteamVR ve NiiMotion hareket köprüsü doğrulanıyor", "Validating SteamVR and the NiiMotion motion bridge"),
        ("Gerekli sensörler canlı olarak kontrol ediliyor", "Checking the required sensors live"),
        ("Virtual Desktop bağlantısının kararlılığı doğrulanıyor", "Validating Virtual Desktop connection stability"),
        ("Virtual Desktop bağlantısı kontrol ediliyor", "Checking the Virtual Desktop connection"),
        ("Virtual Desktop bağlı. SteamVR doğru sırayla başlatılıyor", "Virtual Desktop is connected. Starting SteamVR in the correct order"),
        ("SteamVR Virtual Desktop üzerinden başlatılıyor", "Starting SteamVR through Virtual Desktop"),
        ("NiiMotion sürücüsü ve oyun eşlemesi uygulanıyor", "Applying the NiiMotion driver and game mapping"),
        ("Kişisel hareket modeli başlatılıyor", "Starting the personal motion model"),
        ("Kişisel kalibrasyon doğrulanıyor", "Validating personal calibration"),
        ("Profil doğrulanıyor", "Validating profile"),
        ("Canlı sensör akışı bekleniyor", "Waiting for the live sensor stream"),
        ("Bağlantı kontrol ediliyor", "Checking the connection"),
        ("Bağlantı hazır", "Connection ready"),
        ("Başlamak için aşağıdaki eksik cihazları bağla", "Connect the missing devices shown below to begin"),
        ("Son tarama", "Last scan"),
        ("Yerinde yürüyüş çıkışı", "Walk-in-place output"),
        ("Doğal yerinde yürüyüş", "Natural walk-in-place"),
        ("Cihazların birlikte doğrulanır", "Devices are validated together"),
        ("verileri seçili profil içinde birleştirilir", "data is combined within the selected profile"),
        ("Telefon veya board gerekmez", "No phone or board required"),
        ("Kişisel baldır profili", "Personal calf profile"),
        ("Dengeli ve önerilen profil", "Balanced recommended profile"),
        ("Deneysel hareket algılama", "Experimental motion detection"),
        ("Basınçla yürüyüş ve dönüş", "Pressure-based walking and turning"),
        ("Bacak ve basınç füzyonu", "Leg and pressure fusion"),
        ("Basınç ve gövde füzyonu", "Pressure and torso fusion"),
        ("Hareket güvenle durduruldu", "Movement stopped safely"),
        ("VR'yi Hazırla düğmesine yeniden bas", "Press Prepare VR again"),
        ("Başlık algılandı", "Headset detected"),
        ("SteamVR açıldıktan sonra otomatik doğrulanacak", "will be verified automatically after SteamVR starts"),
        ("Telefon canlı veri gönderiyor", "Phone is sending live data"),
        ("Telefon bağlı değil", "Phone not connected"),
        ("Hareket çıkışı durduruluyor ve sürücü ayarı değiştiriliyor", "Stopping motion output and changing the driver setting"),
        ("NiiMotion sürücüsü ve oyun ayarları hazır", "NiiMotion driver and game settings are ready"),
        ("NiiMotion tamamen devre dışı", "NiiMotion is fully disabled"),
        ("kendi özgün ayarlarıyla çalışır", "use their native settings"),
        ("Kişisel ölçüm, doğrulama ve veri kaydı", "Personal measurements, validation and recordings"),
        ("Kişisel hareketini oyunlara güvenle uygula", "Apply your personal movement safely to games"),
        ("Oyunu seç ve güvenle başlat", "Select and safely start a game"),
        ("Yalnız doğrulanmış VR oyunları görünür", "Only verified VR games are shown"),
        ("Kişisel yürüyüş modelin değişmez", "Your personal gait model is not changed"),
        ("oyun eşlemesi ayrı tutulur", "game mappings are kept separate"),
        ("Henüz doğrulanmış ve kurulu bir VR oyunu bulunamadı", "No verified installed VR game was found"),
        ("Kullanıcı eşlemelerini kaldır veya özgün profili geri yükle", "Remove user mappings or restore the original profile"),
        ("YÜRÜYÜŞ PROFİLİ", "GAIT PROFILE"),
        ("OYUN KÜTÜPHANESİ", "GAME LIBRARY"),
        ("SEÇİLİ VR OYUNU", "SELECTED VR GAME"),
        ("Oyun Ekleme Sihirbazı", "Add Game Wizard"),
        ("Oyunu seç", "Select game"),
        ("Girdileri tara", "Scan inputs"),
        ("Eşlemeyi doğrula", "Validate mapping"),
        ("İLERİ HAREKET GİRDİSİ", "FORWARD MOVEMENT INPUT"),
        ("KOŞMA DÜĞMESİ", "RUN BUTTON"),
        ("İSTEĞE BAĞLI", "OPTIONAL"),
        ("OYUN HIZ ÇARPANI", "GAME SPEED MULTIPLIER"),
        ("OYUN ÇALIŞTIRMA DOSYASI", "GAME EXECUTABLE"),
        ("Önce oyun girdilerini tara", "Scan game inputs first"),
        ("Oyun dosyaları değiştirilmeden", "Without modifying game files"),
        ("Önce kurulu bir Steam oyunu seç", "Select an installed Steam game first"),
        ("Oyun başlatılmadı", "Game was not launched"),
        ("Önce Genel Bakış sayfasından", "First, from the Overview page"),
        ("Yürüyüş profili gerekli", "Gait profile required"),
        ("Kalibrasyon gerekli", "Calibration required"),
        ("Cihazlar eksik", "Devices missing"),
        ("Bağlantılar doğrulandı", "Connections verified"),
        ("güvenli sırayla hazırlanıyor", "is being prepared in the safe order"),
        ("başlatılıyor", "starting"),
        ("KALİBRASYON MERKEZİ", "CALIBRATION CENTER"),
        ("Önce cihazlarını hazırla", "Prepare your devices first"),
        ("KULLANIMA HAZIR", "READY TO USE"),
        ("Ölçülüyor", "Measuring"),
        ("İki sensör de veri gönderiyor", "Both sensors are sending data"),
        ("Yalnız bir sensör bulundu", "Only one sensor was found"),
        ("Test tamamlanamadı", "Test could not be completed"),
        ("Sensörler ölçülüyor", "Measuring sensors"),
        ("İki Move bağlı", "Two Move controllers connected"),
        ("sensörler sağlıklı", "sensors healthy"),
        ("İki PS Move bağlı değil", "Two PS Move controllers are not connected"),
        ("Telefon bağlantısı için", "For the phone connection"),
        ("eşleşme penceresi açıldı", "pairing window opened"),
        ("TELEFON BEKLENİYOR", "WAITING FOR PHONE"),
        ("TELEFON BAĞLANDI", "PHONE CONNECTED"),
        ("TELEFON BULUNAMADI", "PHONE NOT FOUND"),
        ("Canlı telefon verisi alınıyor", "Receiving live phone data"),
        ("Telefon verisi bulunamadı", "Phone data not found"),
        ("Basınç sensörlerini kontrol et", "Check the pressure sensors"),
        ("model hazır", "model ready"),
        ("Tamamlanan yürüyüş kayıtları analiz ediliyor", "Analyzing completed gait recordings"),
        ("Kişisel profil uygulandı", "Personal profile applied"),
        ("Yeni değerler bir sonraki VR oturumunda kullanılacak", "New values will be used in the next VR session"),
        ("kişisel referans", "personal reference"),
        ("TEMEL KALİBRASYON GEREKİYOR", "BASE CALIBRATION REQUIRED"),
        ("PS MOVE KURULUMU GEREKİYOR", "PS MOVE SETUP REQUIRED"),
        ("SteamVR başlatılmadı", "SteamVR was not started"),
        ("SteamVR algılandı", "SteamVR detected"),
        ("kayıtlı hareket profili otomatik başlatıldı", "saved motion profile started automatically"),
        ("Demo oturumu çalışıyor", "Demo session is running"),
        ("gerçek donanım doğrulaması değildir", "this is not real hardware validation"),
        ("PS Move tabanlı profil çalışıyor", "PS Move profile is running"),
        ("Yerinde yürüyerek oyunda ilerleyebilirsin", "Walk in place to move in the game"),
        ("güvenli şekilde durduruldu", "stopped safely"),
        ("VR çıkışı kapalı", "VR output is off"),
        ("Oyun Modları", "Game Modes"),
        ("Nasıl hareket etmek istediğini seç", "Choose how you want to move"),
        ("EŞLEMELER", "MAPPINGS"),
        ("Tarama, oyunun yerel SteamVR action dosyalarını okur; hiçbir oyun dosyasını değiştirmez", "The scan reads the game's local SteamVR action files and does not modify any game files"),
        ("Bulunursa otomatik seçilir; istemiyorsan boş bırak", "Selected automatically when found; leave blank if you do not want it"),
        ("Boş bırakabilirsin", "You can leave this blank"),
        ("VR olmayan oyunlar otomatik eklenmez", "Non-VR games are not added automatically"),
        ("VR doğrulaması gerekli", "VR validation required"),
        ("OpenXR adaptörü doğrulanamadı", "OpenXR adapter could not be validated"),
        ("OpenXR adaptörü oluşturulamadı", "OpenXR adapter could not be created"),
        ("Eşleme doğrulanamadı", "Mapping could not be validated"),
        ("Eşleme oluşturulamadı", "Mapping could not be created"),
        ("Görsel ve oyun bilgisi", "Artwork and game information"),
        ("Oyun Hareket Ayarları", "Game Motion Settings"),
        ("Aktif yürüyüş profili", "Active gait profile"),
        ("Oyundaki hareket mesafesi farklı geliyorsa", "If movement distance feels different in the game"),
        ("Yaklaşık 10 doğal adım yürü", "Walk about 10 natural steps"),
        ("Ayarları sıfırla", "Reset settings"),
        ("Önce hareket profilini seç", "Select a motion profile first"),
        ("Önce temel kalibrasyonu tamamla", "Complete base calibration first"),
        ("Virtual Desktop bağlı değil", "Virtual Desktop is not connected"),
        ("Oyun eşlemesini kaldır", "Remove game mapping"),
        ("Özgün profili geri yükle", "Restore original profile"),
        ("Geri yükleme tamamlandı", "Restore complete"),
        ("Geri yükleme başarısız", "Restore failed"),
        ("Temel fazlardan sonra", "After the base phases"),
        ("Bu kombinasyonla yeni", "Add a new"),
        ("Önce bu cihazların temel kalibrasyonlarını tamamla", "Complete base calibration for these devices first"),
        ("Joy-Con sensörlerinden", "From the Joy-Con sensors"),
        ("PS Move tanıtılamadı", "PS Move could not be identified"),
        ("Dinleniyor", "Listening"),
        ("Telefon hazır", "Phone ready"),
        ("bağlantı izleniyor", "connection is being monitored"),
        ("Board bağlı ve hazır", "Board connected and ready"),
        ("İki Joy-Con bağlı", "Two Joy-Cons connected"),
        ("sensör testine hazır", "ready for sensor test"),
        ("PS Move durumu okunamadı", "PS Move status could not be read"),
        ("parça · sıradaki", "segments · next"),
        ("Move ışığı yakılamadı", "Move light could not be activated"),
        ("Tanı paketi masaüstüne kaydedildi", "Diagnostic package saved to the desktop"),
        ("Tanı paketi oluşturulamadı", "Diagnostic package could not be created"),
        ("Faz iptal edildi; tamamlanmamış veri kullanılmadı", "Phase cancelled; incomplete data was not used"),
        ("Faz tamamlanmadı", "Phase did not complete"),
        ("Bölüm yenilenemedi", "The segment could not be recorded again"),
        ("Kayıt tamamlanmadı", "Recording did not complete"),
        ("Kayıt geri alınamadı", "The recording could not be reverted"),
        ("Başlatma durduruldu", "Launch stopped"),
        ("Oyun açılmadı", "The game was not launched"),
        ("Locomotion başlatılamadı", "Locomotion could not be started"),
        ("Önce bağlantıyı doğrula", "Verify the connection first"),
        ("Fazları sırayla tamamla", "Complete the phases in order"),
        ("örnek analiz edildi", "samples analyzed"),
        ("eşzamanlı örnek", "synchronized samples"),
        ("sensör örneği alındı", "sensor samples captured"),
        ("örnek kaydedildi", "samples recorded"),
        ("adım · demo", "steps · demo")
    ];

    private static void Visit(DependencyObject value)
    {
        VisitSelf(value);
        var count = VisualTreeHelper.GetChildrenCount(value); for (var i = 0; i < count; i++) Visit(VisualTreeHelper.GetChild(value, i));
    }

    private static void VisitSelf(DependencyObject value)
    {
        var state = States.GetOrCreateValue(value);
        if (value is TextBlock text) HookText(text, state);
        if (value is ContentControl content && content.Content is string) HookContent(content, state);
        if (value is Window window) HookTitle(window, state);
        if (value is FrameworkElement element && element.ToolTip is string tip && IsEnglish) element.ToolTip = Translate(tip);
    }

    private static void HookText(TextBlock text, State state)
    {
        state.OriginalText ??= text.Text;
        if (!state.TextHooked) { state.TextHooked = true; DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock)).AddValueChanged(text, (_, _) => { if (state.Updating) return; if (text.Text != state.LastText) state.OriginalText = text.Text; ApplyText(text, state); }); }
        ApplyText(text, state);
    }

    private static void ApplyText(TextBlock text, State state)
    {
        var original = state.OriginalText ?? text.Text; var target = IsEnglish ? Translate(original) : original;
        if (text.Text == target) { state.LastText = target; return; }
        state.Updating = true; text.Text = target; state.LastText = target; state.Updating = false;
    }

    private static void HookContent(ContentControl content, State state)
    {
        state.OriginalContent ??= content.Content as string;
        if (!state.ContentHooked) { state.ContentHooked = true; DependencyPropertyDescriptor.FromProperty(ContentControl.ContentProperty, typeof(ContentControl)).AddValueChanged(content, (_, _) => { if (state.Updating || content.Content is not string current) return; if (current != state.LastContent) state.OriginalContent = current; ApplyContent(content, state); }); }
        ApplyContent(content, state);
    }

    private static void ApplyContent(ContentControl content, State state)
    {
        var original = state.OriginalContent ?? content.Content as string ?? ""; var target = IsEnglish ? TranslateDecorated(original) : original;
        if (Equals(content.Content, target)) { state.LastContent = target; return; }
        state.Updating = true; content.Content = target; state.LastContent = target; state.Updating = false;
    }

    private static void HookTitle(Window window, State state)
    {
        state.OriginalTitle ??= window.Title;
        if (!state.TitleHooked) { state.TitleHooked = true; DependencyPropertyDescriptor.FromProperty(Window.TitleProperty, typeof(Window)).AddValueChanged(window, (_, _) => { if (state.Updating) return; if (window.Title != state.LastTitle) state.OriginalTitle = window.Title; ApplyTitle(window, state); }); }
        ApplyTitle(window, state);
    }

    private static void ApplyTitle(Window window, State state)
    {
        var original = state.OriginalTitle ?? window.Title; var target = IsEnglish ? TranslateDecorated(original) : original;
        if (window.Title == target) { state.LastTitle = target; return; }
        state.Updating = true; window.Title = target; state.LastTitle = target; state.Updating = false;
    }

    private static string Translate(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("{Binding", StringComparison.Ordinal)) return value;
        if (English.TryGetValue(value, out var translated)) return translated;
        foreach (var pair in English.OrderByDescending(x => x.Key.Length))
            if (value.EndsWith(pair.Key, StringComparison.Ordinal))
                return value[..^pair.Key.Length] + pair.Value;
        var phase = Regex.Match(value, @"^FAZ (\d+)(.*)$", RegexOptions.CultureInvariant);
        if (phase.Success) return "PHASE " + phase.Groups[1].Value + phase.Groups[2].Value.Replace(" TAMAMLANDI", " COMPLETE").Replace("'Ü BAŞLAT · 5 DK", " · START · 5 MIN").Replace(" · 5 DK", " · 5 MIN");

        var result = value;
        foreach (var fragment in DynamicFragments)
            result = result.Replace(fragment.Turkish, fragment.English, StringComparison.OrdinalIgnoreCase);

        result = Regex.Replace(result, @"\bFaz (\d+)\b", "Phase $1", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        result = Regex.Replace(result, @"\b(\d+(?:[.,]\d+)?) saniye(?:lik)?\b", "$1 seconds", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        result = Regex.Replace(result, @"\b(\d+(?:[.,]\d+)?) sn\b", "$1 sec", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        result = Regex.Replace(result, @"\b(\d+) adım\b", "$1 steps", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        result = Regex.Replace(result, @"\bkararlılık\b", "stability", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        result = Regex.Replace(result, @"\bkayıp\b", "loss", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        result = Regex.Replace(result, @"\bivme\b", "acceleration", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        result = Regex.Replace(result, @"\bbatarya\b", "battery", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return result;
    }

    private static string TranslateDecorated(string value)
    {
        if (English.TryGetValue(value, out var direct)) return direct;
        foreach (var pair in English.OrderByDescending(x => x.Key.Length)) if (value.EndsWith(pair.Key, StringComparison.Ordinal)) return value[..^pair.Key.Length] + pair.Value;
        return Translate(value);
    }
}
