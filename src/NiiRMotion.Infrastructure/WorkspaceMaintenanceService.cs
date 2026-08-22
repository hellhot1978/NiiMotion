namespace NiiRMotion.Infrastructure;

public sealed record WorkspaceMaintenanceReport(long LogsBytes, long MetadataBytes, int TemporaryFilesRemoved, long FreeDiskBytes);

public sealed class WorkspaceMaintenanceService
{
    public WorkspaceMaintenanceReport Run()
    {
        var removed = 0;
        removed += DeleteOldTemporaryFiles(NiiMotionPaths.Config, TimeSpan.FromDays(1));
        removed += DeleteOldTemporaryFiles(NiiMotionPaths.Data, TimeSpan.FromDays(1));
        var logs = EnforceTreeBudget(NiiMotionPaths.Logs, 512L * 1024 * 1024);
        var metadata = EnforceTreeBudget(Path.Combine(NiiMotionPaths.Config, "game-metadata"), 100L * 1024 * 1024);
        var root = Path.GetPathRoot(NiiMotionPaths.Root)!; var drive = new DriveInfo(root);
        return new(logs, metadata, removed, drive.AvailableFreeSpace);
    }

    public static long EnforceTreeBudget(string directory, long budgetBytes)
    {
        if (!Directory.Exists(directory)) return 0;
        var files = new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories).OrderByDescending(x => x.LastWriteTimeUtc).ToArray();
        var total = files.Sum(x => x.Length);
        foreach (var file in files.Reverse())
        {
            if (total <= budgetBytes) break;
            try { var length = file.Length; file.Delete(); total -= length; } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return total;
    }

    private static int DeleteOldTemporaryFiles(string directory, TimeSpan age)
    {
        if (!Directory.Exists(directory)) return 0; var removed = 0; var threshold = DateTime.UtcNow - age;
        foreach (var file in Directory.EnumerateFiles(directory, "*.tmp", SearchOption.AllDirectories))
        {
            try { if (File.GetLastWriteTimeUtc(file) < threshold) { File.Delete(file); removed++; } } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return removed;
    }
}
