using System.IO.Compression;
using System.Text.Json;

namespace NiiRMotion.Infrastructure;

public sealed record LearnedDataResetResult(string BackupPath, int RemovedFiles, long RemovedBytes);

public sealed class LearnedMotionDataService(string? root = null)
{
    private readonly string _root = Path.GetFullPath(root ?? NiiMotionPaths.Root);
    private string Config => Path.Combine(_root, "config");
    private string Data => Path.Combine(_root, "data");
    private string Backups => Path.Combine(_root, "learned-data-backups");

    public LearnedDataResetResult Reset()
    {
        var files = LearnedFiles().Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists).ToArray();
        Directory.CreateDirectory(Backups);
        var backup = Path.Combine(Backups, $"learned-motion-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.zip");
        using (var zip = ZipFile.Open(backup, ZipArchiveMode.Create))
        {
            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(_root, file);
                zip.CreateEntryFromFile(file, relative, CompressionLevel.Fastest);
            }
            var manifest = zip.CreateEntry("reset-manifest.json");
            using var writer = new StreamWriter(manifest.Open());
            writer.Write(JsonSerializer.Serialize(new { schemaVersion = 1, createdAtUtc = DateTimeOffset.UtcNow, files = files.Select(x => Path.GetRelativePath(_root, x)).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
        }

        var bytes = files.Sum(x => new FileInfo(x).Length);
        foreach (var file in files) File.Delete(Guard(file));
        RemoveEmptyDirectories(Data);
        TrimBackups(5);
        return new(backup, files.Length, bytes);
    }

    private IEnumerable<string> LearnedFiles()
    {
        if (Directory.Exists(Config))
        {
            foreach (var pattern in new[] { "personal-*.json", "calibration-progress.json", "pending-calibration-repair.json" })
                foreach (var file in Directory.EnumerateFiles(Config, pattern, SearchOption.TopDirectoryOnly)) yield return Guard(file);
        }
        if (Directory.Exists(Data))
            foreach (var file in Directory.EnumerateFiles(Data, "*", SearchOption.AllDirectories)) yield return Guard(file);
    }

    private string Guard(string path)
    {
        var full = Path.GetFullPath(path);
        var prefix = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Öğrenilmiş veri yolu NiiMotion çalışma alanının dışında.");
        return full;
    }

    private static void RemoveEmptyDirectories(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }

    private void TrimBackups(int keep)
    {
        foreach (var file in new DirectoryInfo(Backups).EnumerateFiles("learned-motion-*.zip").OrderByDescending(x => x.CreationTimeUtc).Skip(keep))
            file.Delete();
    }
}
