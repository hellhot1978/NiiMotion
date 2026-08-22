# Non-HMD otomatik regresyon sözleşmesi

Durum: **IMPLEMENTED / AUTOMATED TESTED**

`NonHmdRegressionMatrix`, Joy-Con, PS Move, telefon ve Balance Board sahipliklerinin bütün alt kümelerinden üretilebilen profilleri tarar. Her profil için:

- bütün zorunlu cihazlar hazırken oturumun açılabildiğini;
- her zorunlu cihaz tek tek eksiltildiğinde oturumun engellendiğini;
- Virtual Desktop ve VR el kontrolünün isteğe bağlı davranışını;
- el kontrolü seçiminin doğrulanmış bağlantı gibi gösterilmeden `Kullanıma açık` kalmasını;
- Normal VR profilinin hiçbir NiiMotion locomotion çıkışı üretmemesini doğrular.

Bu matris gerçek Bluetooth radyo kalitesini, sensör yerleşimini, Quest pose verisini veya oyun içi hissi kanıtlamaz. Bunlar ilgili donanım kapılarında ayrıca doğrulanacaktır.

Self-contained Release derlemesi, yerel .NET kurulumuna ihtiyaç duymadan `win-x64` olarak yayınlanır. Açılış bakımı ham kalibrasyon verisini silmez; yalnız günlük, metadata önbelleği ve terk edilmiş geçici dosyaları sınırlar.
