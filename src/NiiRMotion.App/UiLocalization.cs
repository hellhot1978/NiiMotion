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
    };

    public static bool IsEnglish => new UserExperienceStore().Load().Language == "en";
    public static string Text(string value) => IsEnglish ? Translate(value) : value;
    public static void Apply(DependencyObject root) => Visit(root);
    public static void ApplyLoaded(DependencyObject value) => VisitSelf(value);

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
        var phase = Regex.Match(value, @"^FAZ (\d+)(.*)$", RegexOptions.CultureInvariant);
        return phase.Success ? "PHASE " + phase.Groups[1].Value + phase.Groups[2].Value.Replace(" TAMAMLANDI", " COMPLETE").Replace("'Ü BAŞLAT · 5 DK", " · START · 5 MIN").Replace(" · 5 DK", " · 5 MIN") : value;
    }

    private static string TranslateDecorated(string value)
    {
        if (English.TryGetValue(value, out var direct)) return direct;
        foreach (var pair in English.OrderByDescending(x => x.Key.Length)) if (value.EndsWith(pair.Key, StringComparison.Ordinal)) return value[..^pair.Key.Length] + pair.Value;
        return Translate(value);
    }
}
