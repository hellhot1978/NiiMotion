using System.Text.Json;

namespace NiiRMotion.Infrastructure;

public sealed record SessionHealthReport(
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset? FirstEventUtc,
    DateTimeOffset? LastEventUtc,
    int EventsRead,
    int LaunchFailures,
    int SensorDisconnects,
    int SensorReconnects,
    int ApplicationCrashes,
    string OverallState,
    IReadOnlyList<string> Recommendations);

/// <summary>Builds a privacy-safe operational summary from the bounded event log.</summary>
public sealed class SessionHealthReportService
{
    public SessionHealthReport Analyze(string? eventLogPath = null, DateTimeOffset? now = null, TimeSpan? window = null)
    {
        eventLogPath ??= Path.Combine(NiiMotionPaths.Logs, "events.jsonl");
        var generated = now ?? DateTimeOffset.UtcNow;
        var cutoff = generated - (window ?? TimeSpan.FromHours(24));
        var events = Read(eventLogPath)
            .Where(x => x.TimestampUtc >= cutoff && x.TimestampUtc <= generated + TimeSpan.FromMinutes(1))
            .OrderBy(x => x.TimestampUtc)
            .TakeLast(5000)
            .ToArray();

        var launchFailures = events.Count(x => x.Category == "game-launch" && x.EventName.Equals("Failed", StringComparison.OrdinalIgnoreCase));
        var disconnects = events.Count(x => x.EventName is "disconnected" or "read-interrupted");
        var reconnects = events.Count(x => x.EventName == "connected");
        var crashes = events.Count(x => x.Category == "application" && x.EventName == "crash");
        var recommendations = new List<string>();
        if (crashes > 0) recommendations.Add("Review the privacy-safe diagnostic package before the next VR session.");
        if (launchFailures > 0) recommendations.Add("Run game validation again and follow the reported launch stage.");
        if (disconnects > reconnects) recommendations.Add("Check the affected controller battery and Bluetooth connection.");
        if (events.Length == 0) recommendations.Add("No recent session events are available; run a short validation session when hardware is ready.");
        if (recommendations.Count == 0) recommendations.Add("No operational warning was detected in the selected time window.");

        var state = crashes > 0 || launchFailures > 0 ? "attention" : disconnects > reconnects ? "warning" : events.Length == 0 ? "no-data" : "healthy";
        return new(generated, events.FirstOrDefault()?.TimestampUtc, events.LastOrDefault()?.TimestampUtc,
            events.Length, launchFailures, disconnects, reconnects, crashes, state, recommendations);
    }

    private static IEnumerable<HealthEvent> Read(string path)
    {
        if (!File.Exists(path)) yield break;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            HealthEvent? item = null;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("timestampUtc", out var timestamp) || !timestamp.TryGetDateTimeOffset(out var at)) continue;
                item = new(at,
                    root.TryGetProperty("category", out var category) ? category.GetString() ?? "" : "",
                    root.TryGetProperty("eventName", out var eventName) ? eventName.GetString() ?? "" : "");
            }
            catch (JsonException) { }
            if (item is not null) yield return item;
        }
    }

    private sealed record HealthEvent(DateTimeOffset TimestampUtc, string Category, string EventName);
}
