namespace NiiRMotion.Infrastructure;

public sealed record RecoverySnapshot(string Id, DateTimeOffset CreatedAt, int Files, long Bytes, string Folder);

public sealed class RecoveryCenterService
{
    private static readonly string[] Patterns = ["personal-*.json", "game-*.json", "calibration-*.json", "user-hardware.json", "psmove-*.json", "active-game.txt", "pending-calibration-repair.json"];
    private string Root => Path.Combine(NiiMotionPaths.Config, "recovery-snapshots");

    public IReadOnlyList<RecoverySnapshot> List()
    {
        if (!Directory.Exists(Root)) return [];
        return new DirectoryInfo(Root).EnumerateDirectories().OrderByDescending(x => x.Name).Select(x =>
        {
            var files = x.GetFiles("*", SearchOption.TopDirectoryOnly); return new RecoverySnapshot(x.Name, x.CreationTimeUtc, files.Length, files.Sum(f => f.Length), x.FullName);
        }).ToArray();
    }

    public RecoverySnapshot Create(string reason = "manual")
    {
        Directory.CreateDirectory(Root); var id = $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Safe(reason)}"; var folder = Path.Combine(Root, id); Directory.CreateDirectory(folder);
        foreach (var file in SourceFiles()) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), true);
        Trim(20); return List().First(x => x.Id == id);
    }

    public void Restore(string id)
    {
        var snapshot = List().FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("Yedek bulunamadı.");
        Create("before-restore");
        foreach (var file in Directory.GetFiles(snapshot.Folder))
        {
            var target = Path.Combine(NiiMotionPaths.Config, Path.GetFileName(file)); var temp = target + ".tmp";
            File.Copy(file, temp, true); File.Move(temp, target, true);
        }
    }

    private IEnumerable<string> SourceFiles() => Patterns.SelectMany(pattern => Directory.GetFiles(NiiMotionPaths.Config, pattern, SearchOption.TopDirectoryOnly)).Distinct(StringComparer.OrdinalIgnoreCase);
    private void Trim(int keep) { foreach (var item in new DirectoryInfo(Root).EnumerateDirectories().OrderByDescending(x => x.Name).Skip(keep)) try { item.Delete(true); } catch (IOException) { } }
    private static string Safe(string value) => new string(value.ToLowerInvariant().Where(x => char.IsLetterOrDigit(x) || x == '-').Take(24).ToArray()) is { Length: > 0 } safe ? safe : "snapshot";
}
