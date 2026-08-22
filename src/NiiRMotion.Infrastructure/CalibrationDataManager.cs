using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class CalibrationDataManager
{
    public int DeleteDevicePhases(SensorFamily sensor, int fromPhase)
    {
        if (fromPhase is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(fromPhase));
        var sensorRoot = Path.GetFullPath(Path.Combine(NiiMotionPaths.Data, "calibration", sensor.ToString().ToLowerInvariant()));
        var dataRoot = Path.GetFullPath(NiiMotionPaths.Data) + Path.DirectorySeparatorChar;
        if (!sensorRoot.StartsWith(dataRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Kalibrasyon yolu güvenli veri alanının dışında.");
        var deleted = 0;
        if (Directory.Exists(sensorRoot))
        {
            foreach (var directory in new DirectoryInfo(sensorRoot).EnumerateDirectories("phase-*-*"))
            {
                var part = directory.Name.Split('-', StringSplitOptions.RemoveEmptyEntries);
                if (part.Length < 2 || !int.TryParse(part[1], out var phase) || phase < fromPhase) continue;
                directory.Delete(true); deleted++;
            }
        }
        var profile = sensor switch
        {
            SensorFamily.JoyCon => Path.Combine(NiiMotionPaths.Config, "personal-gait-pace.json"),
            SensorFamily.PsMove => NiiMotionPaths.PsMoveTrainingProfile,
            SensorFamily.Phone => Path.Combine(NiiMotionPaths.Config, "personal-phone-motion.json"),
            _ => Path.Combine(NiiMotionPaths.Config, "personal-board-motion.json")
        };
        try { if (File.Exists(profile)) File.Delete(profile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return deleted;
    }
}
