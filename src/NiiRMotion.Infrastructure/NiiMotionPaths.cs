namespace NiiRMotion.Infrastructure;

public static class NiiMotionPaths
{
    private static readonly string DevelopmentRoot = @"C:\NiirMotion";
    public static string Root { get; } = Directory.Exists(Path.Combine(DevelopmentRoot, ".git"))
        ? DevelopmentRoot
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NiiMotion");
    public static string Config => Ensure("config");
    public static string Data => Ensure("data");
    public static string Logs => Ensure("logs");
    public static string PsMoveData => Ensure(Path.Combine("data", "psmove"));
    public static string PsMoveAssignments => Path.Combine(Config, "psmove-assignments.json");
    public static string PsMoveFactoryCalibration => Path.Combine(Config, "psmove-calibrations.json");
    public static string PsMovePlacementCalibration => Path.Combine(Config, "personal-psmove-placement.json");
    public static string PsMoveTrainingProfile => Path.Combine(Config, "personal-psmove-training.json");
    public static string HardwareInventory => Path.Combine(Config, "user-hardware.json");
    public static string CalibrationProgress => Path.Combine(Config, "calibration-progress.json");

    public static void Initialize()
    {
        Directory.CreateDirectory(Root); _ = Config; _ = Data; _ = Logs;
    }

    private static string Ensure(string relative)
    {
        var path = Path.Combine(Root, relative); Directory.CreateDirectory(path); return path;
    }
}
