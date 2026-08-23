using System.Text.Json;
using System.Threading.Channels;
using System.Diagnostics;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed record GuidedCalibrationResult(SensorFamily Sensor, int Phase, TimeSpan Duration, IReadOnlyDictionary<string, int> Samples, string Folder, CalibrationQualityReport Quality)
{
    public int TotalSamples => Samples.Values.Sum();
}

public sealed class GuidedCalibrationRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { IncludeFields = true };

    public async Task<GuidedCalibrationResult> RecordAsync(
        SensorFamily sensor,
        int phase,
        TimeSpan duration,
        IProgress<TimeSpan>? elapsedProgress = null,
        string purpose = "base-calibration",
        string? sessionRoot = null,
        CancellationToken cancellationToken = default,
        Func<bool>? isPaused = null)
    {
        // Phase 0 is reserved for optional post-calibration combination training.
        if (phase is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(phase));
        var folder = sessionRoot is null
            ? Path.Combine(NiiMotionPaths.Data, "calibration", sensor.ToString().ToLowerInvariant(), $"phase-{phase}-{DateTime.Now:yyyyMMdd-HHmmss}")
            : Path.Combine(sessionRoot, sensor.ToString().ToLowerInvariant());
        Directory.CreateDirectory(folder);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pauseTimeline = new PauseTimeline();
        var progressTask = MonitorActiveDurationAsync(duration, elapsedProgress, isPaused, pauseTimeline, timeout);
        Dictionary<string, int> counts;
        try
        {
            counts = sensor switch
            {
                SensorFamily.JoyCon => await RecordJoyConsAsync(folder, isPaused, timeout.Token),
                SensorFamily.PsMove => await RecordPsMovesAsync(folder, isPaused, timeout.Token),
                SensorFamily.Phone => await RecordPhoneAsync(folder, isPaused, timeout.Token),
                SensorFamily.BalanceBoard => await RecordBoardAsync(folder, isPaused, timeout.Token),
                _ => throw new ArgumentOutOfRangeException(nameof(sensor))
            };
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            counts = ReadCounts(folder);
        }
        catch
        {
            TryDeleteIncomplete(folder);
            throw;
        }
        finally
        {
            try { await progressTask; } catch (OperationCanceledException) { }
        }

        try { Validate(sensor, counts); }
        catch { TryDeleteIncomplete(folder); throw; }
        var quality = AnalyzeFolder(folder, duration, pauseTimeline.Completed());
        var result = new GuidedCalibrationResult(sensor, phase, duration, counts, folder, quality);
        await File.WriteAllTextAsync(Path.Combine(folder, "manifest.json"), JsonSerializer.Serialize(new
        {
            version = 1, sensor, phase, durationSeconds = duration.TotalSeconds, samples = counts,
            completedAtUtc = DateTimeOffset.UtcNow, purpose, quality
        }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        return result;
    }

    private static async Task<Dictionary<string, int>> RecordJoyConsAsync(string folder, Func<bool>? isPaused, CancellationToken token)
    {
        var devices = HidDeviceEnumerator.FindJoyCons().GroupBy(x => x.Side).Select(x => x.First()).ToArray();
        var leftDevice = devices.FirstOrDefault(x => x.Side == JoyConSide.Left) ?? throw new InvalidOperationException("Sol Joy-Con bağlı değil.");
        var rightDevice = devices.FirstOrDefault(x => x.Side == JoyConSide.Right) ?? throw new InvalidOperationException("Sağ Joy-Con bağlı değil.");
        await using var left = new JoyConSensorSource(leftDevice); await using var right = new JoyConSensorSource(rightDevice);
        await left.StartAsync(token); await right.StartAsync(token);
        var values = await Task.WhenAll(RecordChannelAsync(left.Samples, Path.Combine(folder, "left.jsonl"), isPaused, token), RecordChannelAsync(right.Samples, Path.Combine(folder, "right.jsonl"), isPaused, token));
        return new() { ["left"] = values[0], ["right"] = values[1] };
    }

    private static async Task<Dictionary<string, int>> RecordPsMovesAsync(string folder, Func<bool>? isPaused, CancellationToken token)
    {
        await using var source = new PsMoveSensorSource(NiiMotionPaths.PsMoveAssignments, NiiMotionPaths.PsMoveFactoryCalibration);
        await source.StartAsync(token);
        var count = await RecordChannelAsync(source.Samples, Path.Combine(folder, "moves.jsonl"), isPaused, token);
        return new() { ["pair"] = count };
    }

    private static async Task<Dictionary<string, int>> RecordPhoneAsync(string folder, Func<bool>? isPaused, CancellationToken token)
    {
        await using var source = new OwoTrackSensorSource(); await source.StartAsync(token);
        var count = await RecordChannelAsync(source.Samples, Path.Combine(folder, "phone.jsonl"), isPaused, token);
        return new() { ["phone"] = count };
    }

    private static async Task<Dictionary<string, int>> RecordBoardAsync(string folder, Func<bool>? isPaused, CancellationToken token)
    {
        await using var source = new BalanceBoardSensorSource(); await source.StartAsync(token);
        var count = await RecordChannelAsync(source.Samples, Path.Combine(folder, "board.jsonl"), isPaused, token);
        return new() { ["board"] = count };
    }

    private static async Task<int> RecordChannelAsync<T>(ChannelReader<T> reader, string path, Func<bool>? isPaused, CancellationToken token) where T : ISensorSample
    {
        var count = 0;
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream);
        try
        {
            await foreach (var sample in reader.ReadAllAsync(token))
            {
                if (isPaused?.Invoke() == true) continue;
                await writer.WriteLineAsync(JsonSerializer.Serialize(sample, JsonOptions));
                count++;
            }
        }
        finally
        {
            await writer.FlushAsync(CancellationToken.None);
            await File.WriteAllTextAsync(path + ".count", count.ToString(), CancellationToken.None);
        }
        return count;
    }

    private static Dictionary<string, int> ReadCounts(string folder) => Directory.GetFiles(folder, "*.count")
        .ToDictionary(x => Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(x)), x => int.TryParse(File.ReadAllText(x), out var count) ? count : 0);

    public static CalibrationQualityReport AnalyzeFolder(string folder, TimeSpan duration) => AnalyzeFolder(folder, duration, []);
    private static CalibrationQualityReport AnalyzeFolder(string folder, TimeSpan duration, IReadOnlyList<(long Start, long End)> pauses)
    {
        var points = new List<CalibrationStreamPoint>(); long? origin = null;
        foreach (var file in Directory.GetFiles(folder, "*.jsonl"))
        {
            var stream = Path.GetFileNameWithoutExtension(file);
            foreach (var line in File.ReadLines(file))
            {
                try
                {
                    using var json = JsonDocument.Parse(line); var root = json.RootElement;
                    if (!TryProperty(root, "sequence", out var sequence) || !sequence.TryGetInt64(out var seq)) continue;
                    if (!TryProperty(root, "timestamp", out var timestamp) || !TryProperty(timestamp, "monotonicTicks", out var ticksElement) || !ticksElement.TryGetInt64(out var ticks)) continue;
                    origin = origin is null ? ticks : Math.Min(origin.Value, ticks);
                    var adjusted = ticks - pauses.Sum(p => Math.Max(0, Math.Min(ticks, p.End) - p.Start));
                    points.Add(new(stream, seq, adjusted / (double)Stopwatch.Frequency));
                }
                catch (JsonException) { }
            }
        }
        if (origin is null) return CalibrationQualityAnalyzer.Analyze([], duration.TotalSeconds);
        var originSeconds = origin.Value / (double)System.Diagnostics.Stopwatch.Frequency;
        return CalibrationQualityAnalyzer.Analyze(points.Select(x => x with { Seconds = x.Seconds - originSeconds }), duration.TotalSeconds);
    }

    private static bool TryProperty(JsonElement value, string name, out JsonElement property)
    {
        if (value.TryGetProperty(name, out property)) return true;
        foreach (var candidate in value.EnumerateObject()) if (candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { property = candidate.Value; return true; }
        property = default; return false;
    }

    private static void Validate(SensorFamily sensor, IReadOnlyDictionary<string, int> counts)
    {
        var minimum = sensor is SensorFamily.Phone or SensorFamily.BalanceBoard ? 100 : 1000;
        if (counts.Count == 0 || counts.Values.Any(x => x < minimum))
            throw new InvalidDataException("Kalibrasyon için yeterli kesintisiz sensör verisi alınamadı; bu faz tamamlanmış sayılmadı.");
    }

    private static void TryDeleteIncomplete(string folder)
    {
        try
        {
            var full = Path.GetFullPath(folder);
            var dataRoot = Path.GetFullPath(NiiMotionPaths.Data) + Path.DirectorySeparatorChar;
            if (full.StartsWith(dataRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(full)) Directory.Delete(full, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static async Task MonitorActiveDurationAsync(TimeSpan duration, IProgress<TimeSpan>? progress, Func<bool>? isPaused, PauseTimeline pauses, CancellationTokenSource timeout)
    {
        var active = TimeSpan.Zero; var previous = DateTime.UtcNow;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
            while (await timer.WaitForNextTickAsync(timeout.Token))
            {
                var now = DateTime.UtcNow; var paused = isPaused?.Invoke() == true; pauses.Observe(paused); if (!paused) active += now - previous; previous = now; progress?.Report(active);
                if (active >= duration) { timeout.Cancel(); break; }
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
        finally { pauses.Complete(); }
    }

    private sealed class PauseTimeline
    {
        private readonly object _gate = new(); private readonly List<(long Start, long End)> _completed = []; private long? _started;
        public void Observe(bool paused) { lock (_gate) { var now = Stopwatch.GetTimestamp(); if (paused && _started is null) _started = now; else if (!paused && _started is long start) { _completed.Add((start, now)); _started = null; } } }
        public void Complete() { lock (_gate) { if (_started is long start) { _completed.Add((start, Stopwatch.GetTimestamp())); _started = null; } } }
        public IReadOnlyList<(long Start, long End)> Completed() { lock (_gate) return _completed.ToArray(); }
    }
}
