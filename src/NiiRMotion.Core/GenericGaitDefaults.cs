namespace NiiRMotion.Core;

/// <summary>
/// Literatürdeki açık kaynak yürüyüş verilerine (gaitmap, NONAN GaitPrint, DUO-Gait, Gait120)
/// dayalı genel varsayılan profiller. Kalibrasyon yapılmadığında kullanılır.
/// Değerler ortalama sağlıklı yetişkin verilerinden türetilmiştir.
/// </summary>
public static class GenericGaitDefaults
{
    // Joy-Con (uyluk yerleşimi) için genel yürüyüş eşiği dps cinsinden.
    // Kaynaklar: gaitmap stride algorithms, NONAN GaitPrint IMU verileri.
    public static PersonalGaitPace DefaultJoyConPace => new(
        SlowP95Dps: 65,      // Yavaş yürüyüş: ~60-80 dps
        NaturalP95Dps: 125,  // Doğal yürüyüş: ~100-150 dps
        FastP95Dps: 210      // Hızlı yürüyüş: ~180-250 dps
    );

    // Telefon (göğüs/karın yerleşimi) için genel hareket profili.
    // Kaynaklar: DeepWalking CNN, waist-worn IMU çalışmaları.
    public static PersonalPhoneMotion DefaultPhoneMotion => new(
        RestGyroP95: 3.0,
        SlowGyroP95: 15.0,
        NaturalGyroP95: 35.0,
        FastGyroP95: 65.0,
        SlowAccelP95: 0.4,
        NaturalAccelP95: 0.9,
        FastAccelP95: 1.8
    );

    // Balance Board için genel eşik ve kadans değerleri.
    // Kaynaklar: Gait120 pressure data, Tripod treadmill walking dataset.
    public static PersonalBoardMotion DefaultBoardMotion => new(
        LeftStepThreshold: 2.5,
        RightStepThreshold: 2.5,
        SlowCadenceHz: 0.9,
        NaturalCadenceHz: 1.7,
        FastCadenceHz: 2.4,
        TurnCopYThreshold: 3.5
    );
}
