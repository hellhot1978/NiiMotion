namespace NiiRMotion.Infrastructure;

public sealed class ActiveMotionProfileStore(string? path = null)
{
    private readonly string _path = path ?? Path.Combine(NiiMotionPaths.Config, "active-motion-profile.txt");
    public string? Load() { try { return File.Exists(_path) ? File.ReadAllText(_path).Trim() : null; } catch { return null; } }
    public void Save(string profileId)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path + ".tmp", profileId.Trim());
        File.Move(_path + ".tmp", _path, true);
    }
}
