using System.Text.Json;

namespace NiiRMotion.Infrastructure;

public static class NiiMotionEventLog
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task WriteAsync(string category, string eventName, string message, object? data = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var entry = JsonSerializer.Serialize(new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                category,
                eventName,
                message,
                data
            });
            await Gate.WaitAsync(cancellationToken);
            try
            {
                Directory.CreateDirectory(NiiMotionPaths.Logs);
                await File.AppendAllTextAsync(Path.Combine(NiiMotionPaths.Logs, "events.jsonl"), entry + Environment.NewLine, cancellationToken);
            }
            finally { Gate.Release(); }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch { /* Logging must never break locomotion or setup. */ }
    }
}
