using System.Text.Json;

namespace NiiRMotion.Core;

public enum SensorFamily { JoyCon, PsMove, Phone, BalanceBoard }

public sealed record UserHardwareInventory(
    int Version,
    bool HasJoyCons,
    bool HasPsMoves,
    bool HasPhone,
    bool HasBalanceBoard,
    bool UsesHandTracking,
    DateTimeOffset UpdatedAt)
{
    public static UserHardwareInventory Empty { get; } = new(1, false, false, false, false, false, DateTimeOffset.UtcNow);

    public IReadOnlySet<SensorFamily> Sensors
    {
        get
        {
            var result = new HashSet<SensorFamily>();
            if (HasJoyCons) result.Add(SensorFamily.JoyCon);
            if (HasPsMoves) result.Add(SensorFamily.PsMove);
            if (HasPhone) result.Add(SensorFamily.Phone);
            if (HasBalanceBoard) result.Add(SensorFamily.BalanceBoard);
            return result;
        }
    }
}

public sealed record ProfileRecommendation(MotionProfile Profile, int PerformanceScore, int EaseScore, string Summary, bool Experimental)
{
    public int SortScore => PerformanceScore * 2 + EaseScore;
}

public static class MotionProfileCatalog
{
    public static IReadOnlyList<ProfileRecommendation> For(UserHardwareInventory inventory)
    {
        var result = new List<ProfileRecommendation>
        {
            new(MotionProfile.ClassicVr, 100, 100, "NiiMotion kapalı · özgün VR kontrolü", false)
        };

        var available = inventory.Sensors.ToArray();
        for (var mask = 1; mask < (1 << available.Length); mask++)
        {
            var sensors = available.Where((_, index) => (mask & (1 << index)) != 0).ToHashSet();
            result.Add(Create(sensors, inventory.UsesHandTracking));
        }

        return result
            .OrderByDescending(x => x.EaseScore)
            .ThenBy(x => x.Experimental)
            .ThenByDescending(x => x.PerformanceScore)
            .ThenBy(x => x.Profile.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static ProfileRecommendation Create(IReadOnlySet<SensorFamily> sensors, bool handTracking)
    {
        var required = new HashSet<DeviceKind> { DeviceKind.Quest3 };
        if (sensors.Contains(SensorFamily.JoyCon)) { required.Add(DeviceKind.JoyConLeft); required.Add(DeviceKind.JoyConRight); }
        if (sensors.Contains(SensorFamily.PsMove)) { required.Add(DeviceKind.PsMoveLeft); required.Add(DeviceKind.PsMoveRight); }
        if (sensors.Contains(SensorFamily.Phone)) required.Add(DeviceKind.Phone);
        if (sensors.Contains(SensorFamily.BalanceBoard)) required.Add(DeviceKind.BalanceBoard);
        var optional = new HashSet<DeviceKind> { DeviceKind.VirtualDesktop };
        if (handTracking) optional.Add(DeviceKind.HandTracking);

        var names = new List<string>();
        if (sensors.Contains(SensorFamily.JoyCon)) names.Add("Joy-Con");
        if (sensors.Contains(SensorFamily.PsMove)) names.Add("PS Move");
        if (sensors.Contains(SensorFamily.Phone)) names.Add("Telefon");
        if (sensors.Contains(SensorFamily.BalanceBoard)) names.Add("Board");
        var id = string.Join('-', names.Select(x => x.Replace("PS ", "ps-").Replace("Joy-", "joy").ToLowerInvariant()));

        var hasLeg = sensors.Contains(SensorFamily.JoyCon) || sensors.Contains(SensorFamily.PsMove);
        var dualLeg = sensors.Contains(SensorFamily.JoyCon) && sensors.Contains(SensorFamily.PsMove);
        var experimental = !hasLeg;
        var performance = dualLeg ? 98 : sensors.Contains(SensorFamily.PsMove) ? 88 : sensors.Contains(SensorFamily.JoyCon) ? 84 : sensors.Contains(SensorFamily.BalanceBoard) ? 52 : 42;
        if (hasLeg && sensors.Contains(SensorFamily.Phone)) performance += 4;
        if (hasLeg && sensors.Contains(SensorFamily.BalanceBoard)) performance += 5;
        performance = Math.Min(100, performance);
        var ease = Math.Max(35, 96 - sensors.Count * 13 - (sensors.Contains(SensorFamily.BalanceBoard) ? 8 : 0));
        var summary = dualLeg ? "En güçlü bacak doğrulaması" : hasLeg ? "Doğal yerinde yürüyüş" : sensors.Contains(SensorFamily.BalanceBoard) ? "Basınç tabanlı deneysel hareket" : "Telefon tabanlı deneysel hareket";
        var profileName = names.Count == 1 ? "Sadece " + names[0] : string.Join(" + ", names);
        return new(new MotionProfile(id, profileName, required, optional, true), performance, ease, summary, experimental);
    }
}

public enum CalibrationStage { NotConnected, ConnectionReady, Phase1, Phase2, Phase3, Ready }

public sealed record DeviceCalibrationProgress(SensorFamily Sensor, CalibrationStage Stage, int CompletedPhases, DateTimeOffset? UpdatedAt)
{
    public bool IsReady => Stage == CalibrationStage.Ready;
}

public sealed record ProfileCalibrationProgress(string ProfileId, int CompletedPhases, DateTimeOffset? UpdatedAt)
{
    public bool IsReady => CompletedPhases >= 3;
}

public sealed record CalibrationProgressDocument(int Version, IReadOnlyList<DeviceCalibrationProgress> Devices, IReadOnlyList<ProfileCalibrationProgress>? Profiles = null);
