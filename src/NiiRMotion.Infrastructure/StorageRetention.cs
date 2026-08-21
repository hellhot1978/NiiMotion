namespace NiiRMotion.Infrastructure;

public static class StorageRetention
{
    public const long DefaultLiveLogBudgetBytes = 256L * 1024 * 1024;

    public static long EnforceDirectoryBudget(string directory, long budgetBytes = DefaultLiveLogBudgetBytes, int keepNewest = 3)
    {
        if (budgetBytes < 0) throw new ArgumentOutOfRangeException(nameof(budgetBytes));
        if (keepNewest < 0) throw new ArgumentOutOfRangeException(nameof(keepNewest));
        if (!Directory.Exists(directory)) return 0;

        var files = new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var total = files.Sum(file => file.Length);

        foreach (var file in files.Skip(keepNewest).Reverse())
        {
            if (total <= budgetBytes) break;
            try
            {
                var length = file.Length;
                file.Delete();
                total -= length;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return total;
    }
}
