# NiiMotion OpenXR adaptör katmanı

NiiMotion, OpenXR oyun dosyalarını değiştirmeden Khronos API layer zincirine eklenen `XR_APILAYER_NIIRMOTION_locomotion` katmanını kullanır. Windows kaydı yalnız geçerli kullanıcı altında tutulur. Katman yalnız seçili desteklenen OpenXR oyun profili ve NiiMotion modu birlikte etkin olduğunda açılır; Normal VR veya SteamVR/OpenVR oyunu seçildiğinde registry değeri devre dışı bırakılır.

Masaüstü hareket motoru analog vektörü yerel paylaşımlı belleğe yazar. Paket monoton sıra numarası, iki izinli oyun süreç kimliği ve 250 ms heartbeat taşır. API layer yalnız izinli süreçte adı `move`, `locomotion` veya `walk` içeren OpenXR vector2/float action'larını değiştirir. Diğer action'lar, eller, düğmeler ve başlık pozu çalışma zamanından aynen geçer. Paket eskirse çıkış aynı sorguda sıfırlanır.

İlk adaptör Metro Awakening içindir. Kurulu yürütülebilirleri `Impact.exe` ve `Impact-Win64-Shipping.exe` ile süreç kapsamı sınırlandırılmıştır. Kod ve protokol test edilmiştir; action adlarının gerçek oyun oturumunda eşleşmesi fiziksel oyun içi doğrulama kapısıdır.

Yeni OpenXR oyun desteği aynı katmanı kullanabilir ancak yürütülebilir süreç adları ve gerçek OpenXR action adları salt okunur inceleme veya tanı kaydıyla doğrulanmadan etkinleştirilmez.
