using System.Diagnostics;
using System.Text.Json;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed record HmdValidationResult(string Folder, int Samples, double ActiveSeconds, double TrackedRatio, double SampleRateHz, double YawRangeDegrees, bool Passed, string Message);

public sealed class HmdValidationCaptureService
{
    private static readonly JsonSerializerOptions SampleOptions = new(JsonSerializerDefaults.Web) { IncludeFields = true };
    private static readonly JsonSerializerOptions SummaryOptions = new(JsonSerializerDefaults.Web) { IncludeFields = true, WriteIndented = true };
    public async Task<HmdValidationResult> CaptureAsync(TimeSpan duration, Func<bool> isPaused, IProgress<TimeSpan>? progress = null, CancellationToken token = default)
    {
        var folder = Path.Combine(NiiMotionPaths.Data, "hmd", DateTime.Now.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(folder);
        var samplePath = Path.Combine(folder, "samples.jsonl"); var samples = 0; var tracked = 0; var minYaw = 0d; var maxYaw = 0d; double? previousYaw = null; var unwrappedYaw = 0d; var active = TimeSpan.Zero; var last = Stopwatch.GetTimestamp();
        await using var source = new SharedMemoryHmdPoseSource(); await source.StartAsync(token);
        await using var writer = new StreamWriter(samplePath);
        while (active < duration)
        {
            token.ThrowIfCancellationRequested(); var now = Stopwatch.GetTimestamp(); var delta = TimeSpan.FromSeconds((now - last) / (double)Stopwatch.Frequency); last = now;
            if (isPaused()) { await Task.Delay(50, token); continue; }
            active += delta; progress?.Report(active);
            while (source.Samples.TryRead(out var sample))
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(sample, SampleOptions)); samples++;
                if (sample.IsTracked)
                {
                    tracked++;
                    if (previousYaw is { } prior)
                    {
                        var deltaYaw = sample.YawRadians - prior;
                        while (deltaYaw > Math.PI) deltaYaw -= Math.PI * 2;
                        while (deltaYaw < -Math.PI) deltaYaw += Math.PI * 2;
                        unwrappedYaw += deltaYaw;
                        minYaw = Math.Min(minYaw, unwrappedYaw);
                        maxYaw = Math.Max(maxYaw, unwrappedYaw);
                    }
                    previousYaw = sample.YawRadians;
                }
            }
            await Task.Delay(20, token);
        }
        await writer.FlushAsync(token);
        var ratio = samples == 0 ? 0 : tracked / (double)samples; var rate = active.TotalSeconds <= 0 ? 0 : samples / active.TotalSeconds; var yawRange = tracked == 0 ? 0 : (maxYaw - minYaw) * 180 / Math.PI;
        var passed = samples >= duration.TotalSeconds * 5 && ratio >= .85 && yawRange >= 35;
        var message = samples == 0 ? "SteamVR HMD verisi alınamadı." : ratio < .85 ? "Başlık takibi kayıt boyunca kararlı değildi." : yawRange < 35 ? "Sol ve sağ dönüş örneği yetersiz kaldı." : passed ? "HMD yön ve dönüş kaydı kullanıma hazır." : "Örnek hızı güvenilir sınırın altında kaldı.";
        var result = new HmdValidationResult(folder, samples, active.TotalSeconds, ratio, rate, yawRange, passed, message);
        await File.WriteAllTextAsync(Path.Combine(folder, "summary.json"), JsonSerializer.Serialize(result, SummaryOptions), token); return result;
    }
}
