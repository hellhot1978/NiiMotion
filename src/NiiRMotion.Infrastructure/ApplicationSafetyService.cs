using System.IO.MemoryMappedFiles;
using System.Text.Json;

namespace NiiRMotion.Infrastructure;

public sealed record PreviousRunStatus(bool WasUnclean, DateTimeOffset? StartedAtUtc, string Message);

public sealed class ApplicationSafetyService : IDisposable
{
    private readonly string _marker;
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
    private Timer? _heartbeat;
    public ApplicationSafetyService(string? configDirectory = null) => _marker = Path.Combine(configDirectory ?? NiiMotionPaths.Config, "running-session.json");

    public PreviousRunStatus Begin()
    {
        PreviousRunStatus previous = new(false, null, "Önceki oturum düzgün kapandı.");
        try
        {
            if (File.Exists(_marker))
            {
                using var json = JsonDocument.Parse(File.ReadAllText(_marker));
                DateTimeOffset? started = json.RootElement.TryGetProperty("startedAtUtc", out var value) && value.TryGetDateTimeOffset(out var parsed) ? parsed : null;
                previous = new(true, started, "Önceki NiiMotion oturumu beklenmedik biçimde kapandı. Hareket çıkışı güvenli sıfırdan başlatıldı.");
            }
            WriteMarker(); _heartbeat = new Timer(_ => { try { WriteMarker(); } catch { } }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        }
        catch { }
        ForceSafeZero();
        return previous;
    }

    public void Complete()
    {
        _heartbeat?.Dispose(); _heartbeat = null; ForceSafeZero();
        try { if (File.Exists(_marker)) File.Delete(_marker); } catch { }
    }

    public static void ForceSafeZero()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var mapping = MemoryMappedFile.CreateOrOpen(SharedMemoryOpenXrOutputSink.MappingName, 64, MemoryMappedFileAccess.ReadWrite);
            using var view = mapping.CreateViewAccessor(); view.Write(16, 0f); view.Write(20, 0f); view.Write(24, 0u); view.Write(40, 0UL); view.Flush();
        }
        catch { }
    }

    private void WriteMarker()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_marker)!);
        var json = JsonSerializer.Serialize(new { schemaVersion = 1, processId = Environment.ProcessId, startedAtUtc = _startedAtUtc, heartbeatUtc = DateTimeOffset.UtcNow });
        File.WriteAllText(_marker + ".tmp", json); File.Move(_marker + ".tmp", _marker, true);
    }
    public void Dispose() => Complete();
}
